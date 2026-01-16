using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DiverTest.DIVER.CoralinkerAdaption;
using Newtonsoft.Json;

namespace CartActivator
{
    static class DiverDebugStore
    {
        private static readonly Dictionary<string, (string diver, string map, string logic)> store = new();
        public static void Set(string uri, string diver, string map, string logic)
        {
            lock (store) store[uri] = (diver, map, logic);
        }
        public static bool TryGet(string uri, out (string diver, string map, string logic) v)
        {
            lock (store) return store.TryGetValue(uri, out v);
        }
        public static bool TryGetAny(out (string diver, string map, string logic) v)
        {
            lock (store)
            {
                foreach (var kv in store) { v = kv.Value; return true; }
                v = default; return false;
            }
        }
    }
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
                var iteration = br.ReadInt32();
                tup.iterations = iteration;
                //Console.WriteLine($"recv iter {iteration} lowerIO data from {mcuUri}, operation {tup.name}", $"DIVER-{tup.name}");

                for (int cid = 0; cid < tup.fields.Length; cid++)
                {
                    // Skip upper-only fields on host writeback (we still consume the bytes)
                    byte typeid = br.ReadByte();
                    object value;
                    switch (typeid)
                    {
                        case 0:
                            value = br.ReadBoolean();
                            break;
                        case 1:
                            value = br.ReadByte();
                            break;
                        case 2:
                            value = (sbyte)br.ReadByte();
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
                        case 16:
                        {
                            // ReferenceID: only used for null
                            int rid = br.ReadInt32();
                            value = null;
                            break;
                        }
                        case 12:
                        {
                            // String: [StringHeader=12][len:2][bytes]
                            int slen = br.ReadUInt16();
                            var bytes = br.ReadBytes(slen);
                            value = Encoding.UTF8.GetString(bytes);
                            break;
                        }
                        case 11:
                        {
                            // ArrayHeader: [11][elemTid:1][len:4][payload]
                            byte elemTid = br.ReadByte();
                            int arrLen = br.ReadInt32();
                            Array arr;
                            switch (elemTid)
                            {
                                case 0:
                                {
                                    var bytes = br.ReadBytes(arrLen);
                                    var a = new bool[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = bytes[i] != 0;
                                    arr = a;
                                    break;
                                }
                                case 1:
                                    arr = br.ReadBytes(arrLen);
                                    break;
                                case 2:
                                {
                                    var bytes = br.ReadBytes(arrLen);
                                    var a = new sbyte[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = unchecked((sbyte)bytes[i]);
                                    arr = a;
                                    break;
                                }
                                case 3:
                                {
                                    var a = new char[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadChar();
                                    arr = a;
                                    break;
                                }
                                case 4:
                                {
                                    var a = new short[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadInt16();
                                    arr = a;
                                    break;
                                }
                                case 5:
                                {
                                    var a = new ushort[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadUInt16();
                                    arr = a;
                                    break;
                                }
                                case 6:
                                {
                                    var a = new int[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadInt32();
                                    arr = a;
                                    break;
                                }
                                case 7:
                                {
                                    var a = new uint[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadUInt32();
                                    arr = a;
                                    break;
                                }
                                case 8:
                                {
                                    var a = new float[arrLen];
                                    for (int i = 0; i < arrLen; i++) a[i] = br.ReadSingle();
                                    arr = a;
                                    break;
                                }
                                default:
                                    throw new Exception($"Unsupported array element type {elemTid}");
                            }
                            value = arr;
                            break;
                        }
                        default:
                            throw new Exception($"Unsupported type in lowerIO stream: {typeid}");
                    }

                    if (tup.fields[cid].isUpper) continue;
                    var field = tup.fields[cid].fi;
                    value = CoerceValue(value, field.FieldType);
                    field.SetValue(this, value);
                }

                using var sends = new MemoryStream();
                using var bw = new BinaryWriter(sends);

                for (var cid = 0; cid < tup.fields.Length; cid++)
                {
                    // Skip Lower IO fields, since they are defined as LowNode writeback
                    // Host read in cart definition.
                    if (tup.fields[cid].isLower) continue;

                    var val = tup.fields[cid].fi.GetValue(this);
                    if (val is string s)
                    {
                        var bytes = Encoding.UTF8.GetBytes(s);
                        bw.Write((byte)12); // StringHeader
                        bw.Write((ushort)bytes.Length);
                        bw.Write(bytes);
                    }
                    else if (val is Array arr)
                    {
                        // array of primitives
                        byte elemTid;
                        if (arr is bool[] ab)
                        {
                            elemTid = 0;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(ab.Length);
                            for (int i = 0; i < ab.Length; i++) bw.Write((byte)(ab[i] ? 1 : 0));
                            continue;
                        }
                        if (arr is byte[] a1)
                        {
                            elemTid = 1;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a1.Length); bw.Write(a1);
                            continue;
                        }
                        if (arr is sbyte[] a2)
                        {
                            elemTid = 2;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a2.Length);
                            for (int i = 0; i < a2.Length; i++) bw.Write((byte)a2[i]);
                            continue;
                        }
                        if (arr is char[] ac)
                        {
                            elemTid = 3;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(ac.Length);
                            for (int i = 0; i < ac.Length; i++) bw.Write(ac[i]);
                            continue;
                        }
                        if (arr is short[] a4)
                        {
                            elemTid = 4;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a4.Length);
                            for (int i = 0; i < a4.Length; i++) bw.Write(a4[i]);
                            continue;
                        }
                        if (arr is ushort[] a5)
                        {
                            elemTid = 5;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a5.Length);
                            for (int i = 0; i < a5.Length; i++) bw.Write(a5[i]);
                            continue;
                        }
                        if (arr is int[] a6)
                        {
                            elemTid = 6;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a6.Length);
                            for (int i = 0; i < a6.Length; i++) bw.Write(a6[i]);
                            continue;
                        }
                        if (arr is uint[] a7)
                        {
                            elemTid = 7;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a7.Length);
                            for (int i = 0; i < a7.Length; i++) bw.Write(a7[i]);
                            continue;
                        }
                        if (arr is float[] a8)
                        {
                            elemTid = 8;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a8.Length);
                            for (int i = 0; i < a8.Length; i++) bw.Write(a8[i]);
                            continue;
                        }
                        throw new Exception($"Unsupported array element type for field {tup.fields[cid].field}");
                    }
                    else
                    {
                        // primitive
                        switch (Type.GetTypeCode(val.GetType()))
                        {
                            case TypeCode.Boolean:
                                bw.Write((byte)0); bw.Write((bool)val);
                                break;
                            case TypeCode.Byte:
                                bw.Write((byte)1); bw.Write((byte)val);
                                break;
                            case TypeCode.SByte:
                                bw.Write((byte)2); bw.Write((sbyte)val);
                                break;
                            case TypeCode.Char:
                                bw.Write((byte)3); bw.Write((char)val);
                                break;
                            case TypeCode.Int16:
                                bw.Write((byte)4); bw.Write((short)val);
                                break;
                            case TypeCode.UInt16:
                                bw.Write((byte)5); bw.Write((ushort)val);
                                break;
                            case TypeCode.Int32:
                                bw.Write((byte)6); bw.Write((int)val);
                                break;
                            case TypeCode.UInt32:
                                bw.Write((byte)7); bw.Write((uint)val);
                                break;
                            case TypeCode.Single:
                                bw.Write((byte)8); bw.Write((float)val);
                                break;
                            default:
                                throw new Exception($"Unsupported field primitive type for {tup.fields[cid].field}");
                        }
                    }
                }
                SendUpperData(mcuUri, sends.ToArray());
            }
            else
                Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered", $"DIVER-{tup.name}");

        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            if (targetType.IsInstanceOfType(value))
                return value;

            if (value is Array sourceArray && targetType.IsArray)
            {
                var elementType = targetType.GetElementType();
                if (elementType == null)
                    return value;

                var length = sourceArray.Length;
                var converted = Array.CreateInstance(elementType, length);
                for (int i = 0; i < length; i++)
                {
                    var element = sourceArray.GetValue(i);
                    object? convertedElement;
                    if (element == null)
                    {
                        convertedElement = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                    }
                    else
                    {
                        convertedElement = Convert.ChangeType(element, elementType, CultureInfo.InvariantCulture);
                    }
                    converted.SetValue(convertedElement, i);
                }
                return converted;
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
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
                var diverSrc = UTF8Encoding.UTF8.GetString(ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.diver")));
                var diverMap = UTF8Encoding.UTF8.GetString(ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.diver.map.json")));
                
                // Console.WriteLine(json);

                var fields = JsonConvert.DeserializeObject<PField[]>(json);
                if (mcu_logics.ContainsKey(attr.mcuUri))
                    throw new Exception($"Already have logic for {attr.mcuUri}: LadderLogic {logic.Name}");
                mcu_logics[attr.mcuUri] = new LogicInfo() { fields = fields, name = logic.Name, diver = diverSrc, map = diverMap };
                DiverDebugStore.Set(attr.mcuUri, diverSrc, diverMap, logic.Name);
                foreach (var pField in fields)
                {
                    pField.fi = GetType().GetField(pField.field);
                    if (pField.fi == null)
                        throw new Exception($"field {pField.field} doesn't exist in cart object?");
                    pField.isUpper = pField.fi.IsDefined(typeof(AsUpperIO));
                    pField.isLower = pField.fi.IsDefined(typeof(AsLowerIO));
                    // todo: check type.
                }
                _outerRef = new WeakReference(this);
                SetMCUProgram(attr.mcuUri, asmBytes);
            }
        }

        /// ///////////////////////////////// DONT CARE /////////////////////////////////////
        class PField
        {
            public string field;
            public FieldInfo fi;
            public bool isUpper;
            public bool isLower;
            public int typeid, offset;
        }

        class LogicInfo
        {
            public string name;
            public PField[] fields;
            public int iterations;
            public string diver;
            public string map;
        }
        private Dictionary<string, LogicInfo> mcu_logics = new();

        /// ///////////////////////////////// Debugging /////////////////////////////////////

        private static DIVERVehicle _outer => _outerRef.Target as DIVERVehicle;
        private static WeakReference _outerRef = new WeakReference(null);

        internal static void BindOuter(DIVERVehicle v)
        {
            
        }


        private class MapEntry { public int a; public int m; public int l; public string n; }

        private static (string method, int line, int winStart, int winEnd) ResolveByOffset(string mapJson, int abs)
        {
            // Map is a JSON array of entries: {a:abs, m:methodId, l:line, n:name}
            try
            {
                var entries = JsonConvert.DeserializeObject<List<MapEntry>>(mapJson);
                if (entries != null && entries.Count > 0)
                {
                    // Find the nearest entry with a <= abs
                    MapEntry best = null;
                    int bestDelta = int.MaxValue;
                    // Entries are roughly sorted; linear scan is fine for small maps
                    foreach (var e in entries)
                    {
                        int d = abs - e.a;
                        if (d >= 0 && d < bestDelta)
                        {
                            bestDelta = d;
                            best = e;
                            if (d == 0) break;
                        }
                    }
                    if (best != null)
                    {
                        int ws = Math.Max(1, best.l - 5);
                        int we = best.l + 5;
                        return (best.n ?? "?", best.l, ws, we);
                    }
                }
            }
            catch { /* fall back to naive parsing below */ }

            // Fallback: if parsing fails, show start of file
            return ("?", 1, 1, 10);
        }

        private static void PrintWindow(string diver, int start, int end)
        {
            using (var sr = new StringReader(diver))
            {
                int ln = 0;
                while (true)
                {
                    var line = sr.ReadLine();
                    if (line.StartsWith("===")) continue;
                    if (line == null) break;
                    ln++;
                    if (ln < start) continue;
                    if (ln > end) break;
                    Console.WriteLine(line);
                }
            }
        }

        public static void PrintDebugInfo(int il_offset, string msg)
        {
            try
            {
                if (!DiverDebugStore.TryGetAny(out var any))
                {
                    return;
                }

                var (method, line, windowStart, windowEnd) = ResolveByOffset(any.map, il_offset);

                Console.WriteLine($"[DIVER] Fault in method {method} @0x{il_offset:X} (.diver line {line}): {msg}");

                PrintWindow(any.diver, windowStart, windowEnd);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIVER] Error handling fault: {ex.Message}");
            }
        }
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
                // Console.WriteLine("Lower Data notified");
                // foreach (var field in GetType().GetFields())
                // {
                //     var value = field.GetValue(this);
                //     Console.WriteLine($"    {field.Name}: {value}");
                // }

            }); // note: apply NotifyLowerData on data received.
        }

        public override void SendUpperData(string mcu_device_url, byte[] data)
        {
            // data must be an ArrayHeader-wrapped byte array built by caller.
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
            private static Action<byte[]> lo_notifier;
            private static void StateChanged(byte* changedstates, int length)
            {
                byte[] byteArray = new byte[length];
                Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
                lo_notifier(byteArray);
            }

            public delegate void NotifyErrorDelegate(int il_offset, byte* changedStates, int length);
            [DllImport("MCURuntime.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_error_report_cb(NotifyErrorDelegate callback);
            private static NotifyErrorDelegate DNotifyError = Error;

            private static void Error(int il_offset, byte* err, int length)
            {
                byte[] byteArray = new byte[length];
                Marshal.Copy((IntPtr)err, byteArray, 0, length);
                var msg = Encoding.ASCII.GetString(byteArray);
                DIVERVehicle.PrintDebugInfo(il_offset, msg);
            }


            [DllImport("MCURuntime.dll")]
            static extern void test(byte* bin, int len);
            [DllImport("MCURuntime.dll")]
            static extern void put_upper(byte* bin, int len);


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
                set_error_report_cb(DNotifyError);
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

