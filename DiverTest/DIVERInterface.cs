using System.Reflection;
using System.Runtime.CompilerServices;
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
        public abstract void SetMCUProgram(string mcuUri, byte[] programAssemblyBytes);
        public abstract void SendUpperData(string mcuUri, byte[] data);
        public virtual void NotifyLog(string mcuUri, string message) { 
        }
        /// ////////////////////////////// INTERFACES ////////////////////////////////////////////

        // whenever a lower io data is uploaded, call this.
        public void NotifyLowerData(string mcuUri, byte[] lowerIOData)
        {
            using var ms = new MemoryStream(lowerIOData);
            using var br = new BinaryReader(ms);
            if (mcu_logics.TryGetValue(mcuUri, out var tup))
            {
                Console.WriteLine($"recv iter {br.ReadInt32()} lowerIO data from {mcuUri}, operation {tup.name}", $"DIVER-{tup.name}");
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

                SendUpperData(mcuUri, sends.ToArray());
            }
            else
                Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered", $"DIVER-{tup.name}");

        }

        public void Start(Assembly asm)
        {
            var logics = asm.GetTypes()
                .Where(p => p.GetCustomAttribute<LogicRunOnMCUAttribute>() != null).ToArray();
            foreach (var logic in logics)
            {
                var attr = logic.GetCustomAttribute<LogicRunOnMCUAttribute>();
                Console.WriteLine($"Set logic {logic.Name} to run on MCU-VM @ device {attr.mcuUri}");

                byte[] ReadAllBytes(Stream stream)
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }

                var asmBytes = ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin"));
                var json = UTF8Encoding.UTF8.GetString(ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin.json")));
                
                // Console.WriteLine(json);

                var fields = JsonConvert.DeserializeObject<PField[]>(json);
                if (mcu_logics.ContainsKey(attr.mcuUri))
                    throw new Exception($"Already have logic for {attr.mcuUri}: LadderLogic {logic.Name}");
                mcu_logics[attr.mcuUri] = new LogicInfo() { fields = fields, name = logic.Name };
                foreach (var pField in fields)
                {
                    pField.fi = GetType().GetField(pField.field);
                    if (pField.fi == null)
                        throw new Exception($"field {pField.field} doesn't exist in cart object?");
                    pField.isUpper = pField.fi.IsDefined(typeof(AsUpperIO));
                    // todo: check type.
                }
                SetMCUProgram(attr.mcuUri, asmBytes);
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
        private DIVERSerialListener _serial;
        private string _mcuUri;

        private volatile StateEnum _mcuState = StateEnum.Uninitialized;
        private Configuration _mcuConfiguration = null;
        private BinaryCodeSectionAckCommand _codeSectionAck = null;

        private const byte _DefaultSlaveAddress = 0x01;
        private const int _CodeChunkSplitSize = 512; // MCU can not receive too much bytes once, need split
        private const int _MaxRetryCount = 3;
        private const int _MaxWaitCount = 5;
        private const int _WaitInterval = 200;

        private void OnMCUPackage(DIVERSerialPackage basePackage)
        {
            switch (basePackage.FunctionCode)
            {
                case FunctionCodeEnum.HeartBeatAck:
                    var heartbeatPackage = basePackage.GetHeartBeatCommand();
                    if (heartbeatPackage != null) {
                        _mcuState = heartbeatPackage.State;
                        Console.WriteLine($"MCU HeartBeatAck state = {_mcuState}");
                    }
                    break;
                case FunctionCodeEnum.MemoryExchangeResponse:
                    var memExchangePackage = basePackage.GetMemoryExchangeResponseCommand();
                    if (memExchangePackage != null)
                    {
                        _mcuState = StateEnum.Running;
                        NotifyLowerData(_mcuUri, memExchangePackage.MemoryExchangeData);
                        NotifyLog(_mcuUri, Encoding.UTF8.GetString(memExchangePackage.LogData));
                    }
                    break;
                case FunctionCodeEnum.ConfigurationAck:
                    var configPackage = basePackage.GetConfiguration();
                    if (configPackage != null)
                    { 
                        // Dump configuration in string full extended
                        Console.WriteLine("MCU ConfigurationAck as follows");
                        Console.WriteLine(configPackage.ToString());
                        _mcuConfiguration = configPackage;
                    }
                    break;
                case FunctionCodeEnum.BinaryCodeSectionAck:
                    var binaryCodeSectionAckPackage = basePackage.GetBinaryCodeSectionAckCommand();
                    if (binaryCodeSectionAckPackage != null)
                    {
                        _codeSectionAck = binaryCodeSectionAckPackage;
                    }
                    break;
            }
        }

        public void WaitConditionOfMCU(Func<bool> finalCondCheck, Func<bool> notWaitableCondCheck, Func<bool> doAction)
        {
            int waitCount = 0;
            int retryCount = 0;
            while (waitCount < _MaxWaitCount)
            {
                if (finalCondCheck.Invoke())
                {
                    Console.WriteLine($"WaitCondition: OK!");
                    break;
                }
                if (notWaitableCondCheck.Invoke())
                {
                    Console.WriteLine("WaitCondition: Not waitable condition, Invoking action!");
                    doAction.Invoke();
                    Thread.Sleep(_WaitInterval);
                }
                else
                {
                    waitCount++;
                    Thread.Sleep(_WaitInterval);
                    Console.WriteLine($"WaitCondition: Waiting for {waitCount}/{_MaxWaitCount}!");
                }
                if (waitCount == _MaxWaitCount)
                {
                    retryCount++;
                    if (retryCount > _MaxRetryCount)
                    {
                        throw new Exception("WaitCondition: Can not meet condition after max retry!");
                    }
                    Console.WriteLine("WaitCondition: Invoking action!");
                    doAction.Invoke();
                    Thread.Sleep(_WaitInterval);
                }
            }
        }

        public override void SetMCUProgram(string mcuUri, byte[] programAssemblyBytes)
        {
            // todo: replace the following with your implementation.
            // ------------ Implent serial communication from here ------------
            _mcuUri = mcuUri;
            _mcuState = StateEnum.Uninitialized; // Reset MCU state to uninitialized

            Console.WriteLine($"Interface: Opening MCU from serial {mcuUri}");
            _serial = new DIVERSerialListener(_mcuUri, OnMCUPackage);
            if (!_serial.isOpen)
            {
                Console.WriteLine("ERROR: Can not open port!");
                return;
            }

            WaitConditionOfMCU(
                () => { return _mcuState == StateEnum.Initialized; },
                () => { return _mcuState != StateEnum.Uninitialized; },
                () =>
                {
                    _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Reset).Serialize());
                    Thread.Sleep(_WaitInterval);
                    _mcuState = StateEnum.Uninitialized;
                    return true;
                }
            );

            Console.WriteLine($"Interface: Reading MCU {_mcuUri} configuration!");
            _serial.SendMessage(DIVERSerialPackage.CreateConfigurationReadRequestPackage(_DefaultSlaveAddress).Serialize());
            WaitConditionOfMCU(
                () => { return _mcuConfiguration != null; },
                () => { return false; },
                () =>
                {
                    _serial.SendMessage(DIVERSerialPackage.CreateConfigurationReadRequestPackage(_DefaultSlaveAddress).Serialize());
                    return true;
                }
            );

            // TODO:
            // How to dynamic set relay / terminal 
            _mcuConfiguration.Ports = new ConfigurationPort[] {
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 1000000, BufferSize = 32},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 1000000, BufferSize = 32},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.DualDirectionSerial, BaudRate = 9600, BufferSize = 1024},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.DualDirectionSerial, BaudRate = 9600, BufferSize = 1024},
            };

            Console.WriteLine($"Interface: Writing MCU {_mcuUri} configuration!");
            _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
                _DefaultSlaveAddress, _mcuConfiguration, ConfigurationActionEnum.Write).Serialize());
            WaitConditionOfMCU(
                () => { return _mcuState == StateEnum.Configurated; },
                () => { return false; },
                () =>
                {
                    _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
                        _DefaultSlaveAddress, _mcuConfiguration, ConfigurationActionEnum.Write).Serialize());
                    return true;
                }
            );

            Console.WriteLine($"Interface: Sending Binary Codes to MCU {_mcuUri}!");
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

            var codeList = SplitArrayIntoChunks(programAssemblyBytes, _CodeChunkSplitSize);          
            for (int i = 0; i < codeList.Count; i++)
            {
                var doSendCodeSplitPack = () => {
                    var codePack = codeList[i];
                    var downloadCodePackage = DIVERSerialPackage.CreateBinaryCodeSectionPackage(
                        _DefaultSlaveAddress,
                        programAssemblyBytes.Length,
                        _CodeChunkSplitSize * i,
                        (short)codePack.Length,
                        codePack);
                    _codeSectionAck = null;
                    _serial.SendMessage(downloadCodePackage.Serialize());
                    return true;
                };

                doSendCodeSplitPack.Invoke();

                WaitConditionOfMCU(
                    () =>
                    {
                        return (_codeSectionAck?.SectionAddress == i * _CodeChunkSplitSize)
                            || (_mcuState == StateEnum.BinaryCodeReceived);
                    },
                    () =>
                    {
                        return false;
                    },
                    doSendCodeSplitPack
                );
            }

            Console.WriteLine($"Interface: Call MCU Start!");
            WaitConditionOfMCU(
                () => { return _mcuState == StateEnum.Running; },
                () => { return false; },
                () =>
                {
                    _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Start).Serialize()); // Start
                    return true;
                }
            );

            MCUTestRunner.DebugSetMCUProgram(programAssemblyBytes, (bs) =>
            {
                NotifyLowerData(mcuUri, bs);
                // debug output all fields of cart object.
                foreach (var field in GetType().GetFields())
                {
                    var value = field.GetValue(this);
                    Console.WriteLine($"{field.Name}: {value}");
                }

            }); // note: apply NotifyLowerData on data received.
        }

        public override void SendUpperData(string mcuUri, byte[] data)
        {
            //todo: this is for VM data exchange, contains upperIO/lowerIO modifications.
            // For Debug use
            // MCUTestRunner.DebugSendUpper(data);
            if (_serial.isOpen) 
                _serial.SendMessage(
                    DIVERSerialPackage.CreateMemoryExchangeRequestPackage(_DefaultSlaveAddress, data.Length, data)
                .Serialize());
        }

        public override void NotifyLog(string mcuUri, string message) 
        {
            Console.WriteLine($"MCU Log from {mcuUri}:\n{message}");
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
