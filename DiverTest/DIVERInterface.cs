using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DIVERSerial;
using Newtonsoft.Json;

namespace CartActivator
{
    // todo: currently use this ladderlogic to act interaction with Medulla. 
    // todo: directly integrate into CartActivator.

    // MCU->Medulla is intervally exchanged information.
    public abstract class DIVERVehicle: CartDefinition
    {

        public abstract void SetMCUProgram(string mcu_device_url, byte[] program);
        public abstract void SendUpperData(string mcu_device_url, byte[] data);
        public virtual void NotifyLog(string mcu_device_url, string message) { 
        }
        /// ////////////////////////////// INTERFACES ////////////////////////////////////////////

        // whenever a lower io data is uploaded, call this.
        public void NotifyLowerData(string mcu_device_url, byte[] lowerIOData)
        {
            using var ms = new MemoryStream(lowerIOData);
            using var br = new BinaryReader(ms);
            if (mcu_logics.TryGetValue(mcu_device_url, out var tup))
            {
                Console.WriteLine($"recv iter {br.ReadInt32()} lowerIO data from {mcu_device_url}, operation {tup.name}", $"DIVER-{tup.name}");
                while (ms.Position < lowerIOData.Length)
                {
                    var cid = br.ReadInt16();
                    if (cid < 0 || cid > tup.fields.Length) throw new Exception("invalid Cartfield id!");
                    // if it's upperio skip, otherwise write data.
                    var typeid = br.ReadByte();
                    if (tup.fields[cid].typeid != typeid)
                        throw new Exception($"??? typeid not match for {tup.fields[cid].field}({cid}), expected {tup.fields[cid].typeid} got {typeid}");
                    
                    object value;
                    switch (tup.fields[cid].typeid)
                    {
                        case 0:
                            value = br.ReadBoolean();
                            break;
                        case 1:
                            value = br.ReadByte();
                            break;
                        case 2:
                            value = br.ReadSByte();
                            break;
                        case 3:
                            value = br.ReadChar();
                            break;
                        case 4:
                            value = br.ReadInt16();
                            break;
                        case 5:
                            value = br.ReadUInt16();
                            break;
                        case 6:
                            value = br.ReadInt32();
                            break;
                        case 7:
                            value = br.ReadUInt32();
                            break;
                        case 8:
                            value = br.ReadSingle();
                            break;
                        default:
                            throw new Exception($"Unsupported type ID: {tup.fields[cid].typeid}");
                    }
                    
                    if (tup.fields[cid].isUpper) continue;
                    tup.fields[cid].fi.SetValue(this, value);
                }

                // ok to send current data.
                using var sends = new MemoryStream();
                using var bw = new BinaryWriter(sends);
                bw.Write(tup.iterations++);
                for (var cid = 0; cid < tup.fields.Length; cid++)
                {
                    bw.Write((short)cid);
                    bw.Write((byte)tup.fields[cid].typeid);
                    var val = tup.fields[cid].fi.GetValue(this);
                    switch (tup.fields[cid].typeid)
                    {
                        case 0:
                            bw.Write((bool)val);
                            break;
                        case 1:
                            bw.Write((byte)val);
                            break;
                        case 2:
                            bw.Write((sbyte)val);
                            break;
                        case 3:
                            bw.Write((char)val);
                            break;
                        case 4:
                            bw.Write((short)val);
                            break;
                        case 5:
                            bw.Write((ushort)val);
                            break;
                        case 6:
                            bw.Write((int)val);
                            break;
                        case 7:
                            bw.Write((uint)val);
                            break;
                        case 8:
                            bw.Write((float)val);
                            break;
                    }
                }

                SendUpperData(mcu_device_url, sends.ToArray());
            }
            else
                Console.WriteLine($"warning: {mcu_device_url} received lowerIOData but not registered", $"DIVER-{tup.name}");

        }

        public void Start(Assembly asm)
        {
            var logics = asm.GetTypes()
                .Where(p => p.GetCustomAttribute<LogicRunOnMCUAttribute>() != null).ToArray();
            foreach (var logic in logics)
            {
                var attr = logic.GetCustomAttribute<LogicRunOnMCUAttribute>();
                Console.WriteLine($"Set logic {logic.Name} to run on MCU-VM @ device {attr.mcu_url}");

                byte[] ReadAllBytes(Stream stream)
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }

                var bytes = ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin"));
                var json = UTF8Encoding.UTF8.GetString(ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin.json")));
                
                // Console.WriteLine(json);

                var fields = JsonConvert.DeserializeObject<PField[]>(json);
                if (mcu_logics.ContainsKey(attr.mcu_url))
                    throw new Exception($"Already have logic for {attr.mcu_url}: LadderLogic {logic.Name}");
                mcu_logics[attr.mcu_url] = new LogicInfo() { fields = fields, name = logic.Name };
                foreach (var pField in fields)
                {
                    pField.fi = GetType().GetField(pField.field);
                    if (pField.fi == null)
                        throw new Exception($"field {pField.field} doesn't exist in cart object?");
                    pField.isUpper = pField.fi.IsDefined(typeof(AsUpperIO));
                    // todo: check type.
                }
                SetMCUProgram(attr.mcu_url, bytes);
            }
        }

        /// ///////////////////////////////// DONT CARE /////////////////////////////////////
        class PField
        {
            public string field;
            public FieldInfo fi;
            public bool isUpper;
            public int typeid, offset;
        }

        class LogicInfo
        {
            public string name;
            public PField[] fields;
            public int iterations;
        }
        private Dictionary<string, LogicInfo> mcu_logics = new();
    }

    public abstract class LocalDIVERVehicle:DIVERVehicle
    {
        private DIVERSerial.DIVERSerial _serial;

        private const byte _DefaultSlaveAddress = 0x01;
        private const int _CodeChunkSplitSize = 512; // MCU can not receive too much bytes once, need split
        private const int _DefaultSerialBaudrate = 2000000;

        private string _mcuDeviceUrl;
        public void OnReceivedLowerIO(byte[] bytes)
        {
            NotifyLowerData(_mcuDeviceUrl, bytes);
        }

        public void OnReceivedLogs(byte[] bytes)
        {
            NotifyLog(_mcuDeviceUrl, Encoding.UTF8.GetString(bytes));
        }

        public override void SetMCUProgram(string mcuDeviceUrl, byte[] program)
        {
            // todo: replace the following with your implementation.
            // ------------ Implent serial communication from here ------------
            // ------------ Copy from Yu's code ------------

            _mcuDeviceUrl = mcuDeviceUrl;

            Console.WriteLine($"Interface: Opening MCU from serial {_mcuDeviceUrl}, rate = {_DefaultSerialBaudrate}!");
            _serial = new DIVERSerial.DIVERSerial(_mcuDeviceUrl, _DefaultSerialBaudrate, OnReceivedLowerIO, OnReceivedLogs);
            if (!_serial.isOpen)
            {
                Console.WriteLine("ERROR: Can not open port!");
                return;
            }

            Console.WriteLine($"Interface: Resetting MCU {_mcuDeviceUrl}!");
            _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Reset).Serialize());
            // TODO: Check MCU status
            Thread.Sleep(1000);

            Console.WriteLine($"Interface: Configurating MCU {_mcuDeviceUrl}!");
            //_serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(_DefaultSlaveAddress, configuration, ConfigurationActionEnum.Write).Serialize());
            // TODO: Check MCU status
            Thread.Sleep(500);

            Console.WriteLine($"Interface: Sending Binary Codes to MCU {_mcuDeviceUrl}!");
            // MCU can not receive too much bytes once, need split
            static List<byte[]> SplitArrayIntoChunks(byte[] array, int chunkSize)
            {
                List<byte[]> chunks = new List<byte[]>();

                for (int i = 0; i < array.Length; i += chunkSize)
                {
                    int currentChunkSize = Math.Min(chunkSize, array.Length - i);
                    byte[] chunk = new byte[currentChunkSize];
                    Array.Copy(array, i, chunk, 0, currentChunkSize);
                    chunks.Add(chunk);
                }
                return chunks;
            }
            var codeList = SplitArrayIntoChunks(program, _CodeChunkSplitSize);
            int i = 0;
            foreach (var codePack in codeList)
            {
                var downloadCodePackage = DIVERSerialPackage.CreateBinaryCodeSectionPackage(
                    _DefaultSlaveAddress, (uint)program.Length, (uint)(_CodeChunkSplitSize * i), (ushort)(codePack.Length), codePack);
                _serial.SendMessage(downloadCodePackage.Serialize());
                i++;

                // TODO: Check MCU status
                Thread.Sleep(200);
                //while (true)
                //{
                //    var receive = cart.Embedded.GetMessage();
                //    if (receive == null) continue;
                //    if (receive[5] == 0x90 && (receive[6] == 0x05 || receive[6]==0x06)) break;
                //}
            }

            Console.WriteLine($"Interface: Call MCU Start!");
            _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Start).Serialize()); // Start

            MCUTestRunner.DebugSetMCUProgram(program, (bs) =>
            {
                NotifyLowerData(mcuDeviceUrl, bs);
                // debug output all fields of cart object.
                foreach (var field in GetType().GetFields())
                {
                    var value = field.GetValue(this);
                    Console.WriteLine($"{field.Name}: {value}");
                }

            }); // note: apply NotifyLowerData on data received.
        }

        public override void SendUpperData(string mcu_device_url, byte[] data)
        {
            //todo: this is for VM data exchange, contains upperIO/lowerIO modifications.
            // For Debug use
            // MCUTestRunner.DebugSendUpper(data);
            if (_serial.isOpen) 
                _serial.SendMessage(DIVERSerialPackage.CreateMemoryExchangeRequestPackage(_DefaultSlaveAddress, (uint)(data.Length), data).Serialize());
        }

        public override void NotifyLog(string mcu_device_url, string message) 
        {
            Console.WriteLine($"MCU Log from{mcu_device_url}\n:{message}");
        }

        unsafe class MCUTestRunner
        {

            public delegate void NotifyLowerDelegate(byte* changedStates, int length);
            [DllImport("MCURuntime.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_lowerio_cb(NotifyLowerDelegate callback);

            private static NotifyLowerDelegate DNotifyStateChanged = StateChanged;

            [DllImport("MCURuntime.dll")]
            static extern void test(byte* bin, int len);
            [DllImport("MCURuntime.dll")]
            static extern void put_upper(byte* bin, int len);

            private static Action<byte[]> lo_notifier;
            private static void StateChanged(byte* changedstates, int length)
            {
                byte[] byteArray = new byte[length];
                Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
                lo_notifier(byteArray); 
            }

            public static void DebugSendUpper(byte[] data)
            { 
                fixed (byte* ptr = data)
                {
                    put_upper(ptr, data.Length);
                }
            }
            public static void DebugSetMCUProgram(byte[] program, Action<byte[]> notifier)
            {
                lo_notifier = notifier;
                set_lowerio_cb(DNotifyStateChanged);
                new Thread(() =>
                {
                    var allb = new byte[102400]; //100K runtime
                    Array.Copy(program, allb, program.Length);
                    fixed (byte* ptr = allb)
                    {
                        test(ptr, 102400);
                    }
                }){Name="MCU-debug"}.Start();
            }
        }
    }

}
