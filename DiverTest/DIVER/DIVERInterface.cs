using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using DiverTest.DIVER.CoralinkerAdaption;
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

        public virtual void RunDIVER()
        {
            Start(Assembly.GetAssembly(GetType()));
        }

        void Start(Assembly asm)
        {
            var logics = asm.GetTypes()
                .Where(p => p.GetCustomAttribute<LogicRunOnMCUAttribute>() != null).ToArray();
            foreach (var logic in logics)
            {
                var attr = logic.GetCustomAttribute<LogicRunOnMCUAttribute>();
                if (!logic.IsSubclassOfRawGeneric(typeof(LadderLogic<>), out var ll) ||
                    ll.GenericTypeArguments[0] != GetType()) continue;

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


    public abstract class LocalDebugDIVERVehicle : DIVERVehicle
    {

        public override void SetMCUProgram(string mcu_device_url, byte[] program)
        {
            //todo: replace the following with your implementation.
            MCUTestRunner.DebugSetMCUProgram(program, (bs) =>
            {
                NotifyLowerData("default", bs);
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
            MCUTestRunner.DebugSendUpper(data);
        }

        public override void NotifyLog(string mcu_device_url, string message)
        {
            Console.WriteLine($"{mcu_device_url}:{message}");
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
                })
                { Name = "MCU-debug" }.Start();
            }
        }
    }
}
