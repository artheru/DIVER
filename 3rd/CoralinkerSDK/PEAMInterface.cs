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
        public string mcuUri;
        public string name;
    }

    public void RunDIVER(NodeConfiguration[] configurations)
    {
        foreach (var nodeConfiguration in configurations)
        {
            StartNode(nodeConfiguration.asmBytes, nodeConfiguration.metaJson, nodeConfiguration.mcuUri,
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

    private void NotifyErrorData(int il_offset, string error)
    {
        DIVERVehicle.PrintDebugInfo(il_offset, msg); // todo: resemble DIVERInterface.cs
    }

    private void StartNode(byte[] asmBytes, string metaJson, string mcuUri, string name="NoName")
    {
        Console.WriteLine($"Set logic {name}(len={asmBytes.Length}) to run on MCU-VM @ device {mcuUri}");

        var fields = JsonConvert.DeserializeObject<PField[]>(metaJson);
        if (mcu_logics.ContainsKey(mcuUri))
            throw new Exception(
                $"Already have logic for {mcuUri}: {name}(len={mcu_logics[mcuUri].asmBytes.Length})");
        mcu_logics[mcuUri] = new LogicInfo()
            { fields = fields, name = name, asmBytes = asmBytes, meta_json = metaJson };
        foreach (var pField in fields)
        {
            pField.fi = Target.GetType().GetField(pField.field);
            if (pField.fi == null)
                throw new Exception($"field {pField.field} doesn't exist in target(cart) object?");
            pField.isUpper = pField.fi.GetCustomAttributes().Any(p => p.GetType().Name.Contains("AsUpperIO"));
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
}