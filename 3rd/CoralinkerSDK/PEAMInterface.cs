using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace CoralinkerSDK;

public class PEAMInterface
{
    public readonly object Target;

    public PEAMInterface(object target)
    {
        Target = target;
    }

    public void StopDIVER()
    {
        // todo...
    }


    public class NodeConfiguration
    {
        public byte[] asmBytes;
        public string metaJson;
        public string diverSrc;
        public string diverMapJson;
        public string mcuUri;
        public string name;
    }

    public void RunDIVER(NodeConfiguration[] configurations)
    {
        foreach (var nodeConfiguration in configurations)
        {
            StartNode(
                nodeConfiguration.asmBytes,
                nodeConfiguration.metaJson,
                nodeConfiguration.diverSrc,
                nodeConfiguration.diverMapJson,
                nodeConfiguration.mcuUri,
                nodeConfiguration.name);
        }



        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.OpenNode())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} open failed!");
                return;
            }
        }

        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.FetchConfiguration())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} fetch configuration failed!");
                return;
            }
        }

        foreach (var nodeHandle in _nodeMap)
        {
            nodeHandle.Value.mcuConfiguration.Ports = new ConfigurationPort[] {
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 500000, BufferSize = 32},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 500000, BufferSize = 32},
            };
        }


        // Write Ports to nodes
        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.WritePortsConfiguration())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} write ports configuration failed!");
                return;
            }
        }

        // Start all nodes
        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.SetAssembly())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} set assembly failed!");
                return;
            }

            if (!nodeHandle.Value.Start())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} start failed!");
                return;
            }
        }
    }

    // map from uri to CoralinkerLowerHandle
    private Dictionary<string, CoralinkerLowerNodeHandle> _nodeMap = new();

    private void SendUpperData(string mcuUri, byte[] data)
    {
        if (_nodeMap.TryGetValue(mcuUri, out var nodeHandle))
        {
            nodeHandle.SendUpperData(data);
        }
        else
        {
            Console.WriteLine($"Coralinker: Error, node {mcuUri} not found!");
        }
    }

    internal void NotifyLog(string mcuUri, string message)
    {
        Console.WriteLine($"Coralinker: MCU Log from {mcuUri}:\n{message}");
    }

    private void SetMCUProgram(string mcuUri, byte[] programAssemblyBytes)
    {
        // Insert a new node to _nodeMap
        if (!_nodeMap.ContainsKey(mcuUri))
        {
            var nodeHandle = new CoralinkerLowerNodeHandle(mcuUri, programAssemblyBytes, this);
            _nodeMap[mcuUri] = nodeHandle;
        }
        else
        {
            Console.WriteLine($"Coralinker: Error, node {mcuUri} already exists!");
        }
    }


    /// ////////////////////////////// INTERFACES ////////////////////////////////////////////

    // whenever a lower io data is uploaded, call this.
    internal void NotifyLowerData(string mcuUri, byte[] lowerIOData)
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
                tup.fields[cid].fi.SetValue(Target, value);
            }

            // ok to send current data.
            using var sends = new MemoryStream();
            using var bw = new BinaryWriter(sends);
            // TODO: If should add iteartion count to upperIO?
            bw.Write(tup.iterations++);
            for (var cid = 0; cid < tup.fields.Length; cid++)
            {
                // Skip Lower IO fields, since they are defined as LowNode writeback
                // Host read in cart definition.
                if (tup.fields[cid].isLower) continue;

                bw.Write((short)cid);
                bw.Write((byte)tup.fields[cid].typeid);
                var val = tup.fields[cid].fi.GetValue(Target);
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
            Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered");

    }

    private void NotifyErrorData(string mcuUri, int il_offset, string error)
    {
        DebugInfoPrinter.PrintDebugInfo(mcuUri, il_offset, error);
    }

    private void StartNode(byte[] asmBytes, string metaJson, string diverSrc, string diverMapJson, string mcuUri, string name="NoName")
    {
        Console.WriteLine($"Set logic {name}(len={asmBytes.Length}) to run on MCU-VM @ device {mcuUri}");

        var fields = JsonConvert.DeserializeObject<PField[]>(metaJson);
        if (mcu_logics.ContainsKey(mcuUri))
            throw new Exception(
                $"Already have logic for {mcuUri}: {name}(len={mcu_logics[mcuUri].asmBytes.Length})");
        mcu_logics[mcuUri] = new LogicInfo()
            { fields = fields, name = name, asmBytes = asmBytes, meta_json = metaJson };
        DebugInfoPrinter.Set(mcuUri, diverSrc, diverMapJson, name);
        foreach (var pField in fields)
        {
            pField.fi = Target.GetType().GetField(pField.field);
            if (pField.fi == null)
                throw new Exception($"field {pField.field} doesn't exist in target(cart) object?");
            pField.isUpper = pField.fi.GetCustomAttributes().Any(p => p.GetType().Name.Contains("AsUpperIO"));
            pField.isLower = pField.fi.GetCustomAttributes().Any(p => p.GetType().Name.Contains("AsLowerIO"));
            // todo: check type.
        }
        SetMCUProgram(mcuUri, asmBytes);
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
        public string meta_json;
        public byte[] asmBytes;
    }
    private Dictionary<string, LogicInfo> mcu_logics = new();

    /// <summary>
    /// Placeholder hook: call this when the MCU reports an execution fault with an IL offset.
    /// </summary>
    internal void NotifyExecutionError(string mcuUri, int ilOffset, string message)
    {
        NotifyErrorData(mcuUri, ilOffset, message);
    }
}

internal static class DebugInfoPrinter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (string diver, string map, string logic)> Store = new();

    public static void Set(string uri, string diver, string map, string logicName)
    {
        lock (Gate) Store[uri] = (diver, map, logicName);
    }

    private sealed class MapEntry { public int a; public int m; public int l; public string n; }

    private static (string method, int line, int winStart, int winEnd) ResolveByOffset(string mapJson, int abs)
    {
        try
        {
            var entries = JsonConvert.DeserializeObject<List<MapEntry>>(mapJson);
            if (entries != null && entries.Count > 0)
            {
                MapEntry best = null;
                int bestDelta = int.MaxValue;
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
        catch
        {
            // ignore
        }

        return ("?", 1, 1, 10);
    }

    private static void PrintWindow(string diver, int start, int end)
    {
        using var sr = new StringReader(diver);
        int ln = 0;
        while (true)
        {
            var line = sr.ReadLine();
            if (line == null) break;
            if (line.StartsWith("===")) continue;
            ln++;
            if (ln < start) continue;
            if (ln > end) break;
            Console.WriteLine(line);
        }
    }

    public static void PrintDebugInfo(string mcuUri, int il_offset, string msg)
    {
        try
        {
            if (!TryGet(mcuUri, out var any) && !TryGetAny(out any))
                return;

            var (method, line, windowStart, windowEnd) = ResolveByOffset(any.map, il_offset);

            Console.WriteLine($"[DIVER] Fault in method {method} @0x{il_offset:X} (.diver line {line}) on {mcuUri}: {msg}");
            PrintWindow(any.diver, windowStart, windowEnd);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIVER] Error handling fault: {ex.Message}");
        }
    }

    private static bool TryGet(string uri, out (string diver, string map, string logic) v)
    {
        lock (Gate) return Store.TryGetValue(uri, out v);
    }

    private static bool TryGetAny(out (string diver, string map, string logic) v)
    {
            lock (Gate)
            {
                foreach (var kv in Store) { v = kv.Value; return true; }
                v = default; return false;
            }
    }
}