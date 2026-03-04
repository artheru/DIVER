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
            if (mcu_logics.TryGetValue(mcuUri, out var tup))
            {
                DIVERCommonUtils.NotifyLowerData(
                    lowerIOData,
                    this,
                    tup,
                    data => SendUpperData(mcuUri, data));
            }
            else
                Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered");
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
        #pragma warning disable CS0649 // Fields are assigned via JSON deserialization
        #pragma warning restore CS0649

        private Dictionary<string, LogicInfo> mcu_logics = new();

        /// ///////////////////////////////// Debugging /////////////////////////////////////

        private static DIVERVehicle _outer => _outerRef.Target as DIVERVehicle;
        private static WeakReference _outerRef = new WeakReference(null);

        internal static void BindOuter(DIVERVehicle v)
        {
            
        }


        /// <summary>
        /// Source map entry - matches the format generated by DiverCompiler/Processor.cs
        /// </summary>
        private class MapEntry
        {
            public int ilOffset;      // Absolute byte offset in the generated program
            public int methodIndex;   // Index of the method in the compiled output
            public int diverLine;     // Line number in the .diver disassembly file
            public string methodName; // Fully qualified method name
            public string sourceFile; // Original C# source file name
            public int sourceLine;    // Line number in the original C# source file
        }

        /// <summary>
        /// Resolve IL offset to source location using the debug map
        /// </summary>
        private static (string method, int diverLine, int winStart, int winEnd, string sourceFile, int sourceLine) ResolveByOffset(string mapJson, int abs)
        {
            // Map is a JSON array of entries matching MapEntry fields
            try
            {
                var entries = JsonConvert.DeserializeObject<List<MapEntry>>(mapJson);
                if (entries != null && entries.Count > 0)
                {
                    // Find the nearest entry with ilOffset <= abs
                    MapEntry best = null;
                    int bestDelta = int.MaxValue;
                    // Entries are roughly sorted; linear scan is fine for small maps
                    foreach (var e in entries)
                    {
                        int d = abs - e.ilOffset;
                        if (d >= 0 && d < bestDelta)
                        {
                            bestDelta = d;
                            best = e;
                            if (d == 0) break;
                        }
                    }
                    if (best != null)
                    {
                        int ws = Math.Max(1, best.diverLine - 5);
                        int we = best.diverLine + 5;
                        return (best.methodName ?? "?", best.diverLine, ws, we, best.sourceFile ?? "", best.sourceLine);
                    }
                }
            }
            catch { /* fall back to naive parsing below */ }

            // Fallback: if parsing fails, show start of file
            return ("?", 1, 1, 10, "", 0);
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

                var (method, diverLine, windowStart, windowEnd, sourceFile, sourceLine) = ResolveByOffset(any.map, il_offset);

                // Show source location if available
                if (!string.IsNullOrEmpty(sourceFile) && sourceLine > 0)
                {
                    Console.WriteLine($"[DIVER] Fault in {sourceFile}({sourceLine}) method {method} @0x{il_offset:X}: {msg}");
                }
                else
                {
                    Console.WriteLine($"[DIVER] Fault in method {method} @0x{il_offset:X} (.diver line {diverLine}): {msg}");
                }

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

