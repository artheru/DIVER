using System.Reflection;
using System.Text;
using CartActivator;

namespace DiverTest.DIVER.CoralinkerAdaption;


public class DefineCoralinkingAttribute<T>:Attribute where T:Coralinking
{

}
public class UseCoralinkerMCUAttribute<T> : Attribute where T: CoralinkerNodeDefinition
{
}

// Handle single CoralinkerNode's open, configure fetch and push, send program and start
// 1. Open serial port
// 2. Reset MCU
// 3. Fetch configuration
// 4. Push configuration
// 5. Send program
// 6. Start MCU
// 7. Send data to MCU
// 8. Receive data from MCU
// 9. Notify log from MCU
// 10. Notify lower data from MCU
internal class CoralinkerLowerNodeHandle
{
    private const byte _DefaultSlaveAddress = 0x01;
    private const int _CodeChunkSplitSize = 512; // MCU can not receive too much bytes once, need split
    private const int _MaxRetryCount = 3;
    private const int _MaxWaitCount = 5;
    private const int _WaitInterval = 100;

    private readonly string _uri;
    private readonly byte[] _asm;
    private readonly DIVERVehicle _root;

    private DIVERSerialListener _serial;
    private BinaryCodeSectionAckCommand _codeSectionAck = null;

    public volatile StateEnum mcuState = StateEnum.Uninitialized;
    private volatile bool _isMCUStarted = false;
    public volatile Configuration mcuConfiguration = null;
    public volatile Configuration newConfiguration = null;

    // constructor
    public CoralinkerLowerNodeHandle(string uri, byte[] asm, DIVERVehicle root)
    {
        _uri = uri;
        _asm = asm;
        _root = root;
    }

    public override string ToString()
    {
        return $"[<CoralinkerLowerNodeHandle> uri = {_uri}]";
    }

    public void SendUpperData(byte[] data)
    {
        if (_serial != null && _serial.isOpen)
        {
            _serial.SendMessage(
                DIVERSerialPackage.CreateMemoryExchangeRequestPackage(_DefaultSlaveAddress, data.Length, data)
                    .Serialize());
        }
    }

    // Serial callback
    private void OnMCUPackage(DIVERSerialPackage basePackage)
    {
        switch (basePackage.FunctionCode)
        {
            case FunctionCodeEnum.HeartBeatAck:
                var heartbeatPackage = basePackage.GetHeartBeatCommand();
                if (heartbeatPackage != null)
                {
                    mcuState = heartbeatPackage.State;
                    Console.WriteLine($"MCU HeartBeatAck state = {mcuState}");
                }
                break;
            case FunctionCodeEnum.MemoryExchangeResponse:
                if (!_isMCUStarted)
                {
                    // MemoryExchange before MCU started is not allowed
                    break;
                }
                var memExchangePackage = basePackage.GetMemoryExchangeResponseCommand();
                if (memExchangePackage != null)
                {
                    mcuState = StateEnum.Running;
                    _root.NotifyLowerData(_uri, memExchangePackage.MemoryExchangeData);
                    _root.NotifyLog(_uri, Encoding.UTF8.GetString(memExchangePackage.LogData));
                }
                break;
            case FunctionCodeEnum.ConfigurationAck:
                var configPackage = basePackage.GetConfiguration();
                if (configPackage != null)
                {
                    // Dump configuration in string full extended
                    Console.WriteLine("MCU ConfigurationAck as follows");
                    Console.WriteLine(configPackage.ToString());
                    mcuConfiguration = configPackage;
                } else
                {
                    Console.WriteLine("Error: MCU Configuration Ack is error!");
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
    static private bool WaitConditionOfMCU(Func<bool> finalCondCheck, Func<bool> notWaitableCondCheck, Func<bool> doAction)
    {
        int waitCount = 0;
        int retryCount = 0;
        while (waitCount < _MaxWaitCount)
        {
            if (finalCondCheck.Invoke())
            {
                Console.WriteLine($"WaitCondition: OK!");
                return true;
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
                    Console.WriteLine("WaitCondition: Retry count exceeded, exiting!");
                    return false;
                }
                waitCount = 0;
                Console.WriteLine("WaitCondition: Invoking action!");
                doAction.Invoke();
                Thread.Sleep(_WaitInterval);
            }
        }

        Console.WriteLine("WaitCondition: Unknown reason, exiting!");
        return false;
    }

    public bool OpenNode()
    {
        // Print out the node name and type
        Console.WriteLine("Coralinker: OpenNode: " + _uri);

        mcuState = StateEnum.Uninitialized;
        _serial = new DIVERSerialListener(_uri, OnMCUPackage);

        if (!_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, can not open port!");
            return false;
        }

        // Reset MCU if it is not in uninitialized state
        return WaitConditionOfMCU(
            () => { return mcuState == StateEnum.Initialized; },
            () => { return mcuState != StateEnum.Uninitialized; },
            () =>
            {
                _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Reset).Serialize());
                Thread.Sleep(_WaitInterval);
                mcuState = StateEnum.Uninitialized;
                return true;
            }
        );
    }

    public bool FetchConfiguration()
    {
        if (_serial == null || !_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, serial port is not open!");
            return false;
        }

        if (mcuState != StateEnum.Initialized)
        {
            Console.WriteLine("Coralinker: Error, MCU is not initialized!");
            return false;
        }

        Console.WriteLine("Coralinker: ReadConfiguration: " + _uri);

        _serial.SendMessage(DIVERSerialPackage.CreateConfigurationReadRequestPackage(_DefaultSlaveAddress).Serialize());
        return WaitConditionOfMCU(
            () => { return mcuConfiguration != null; },
            () => { return false; },
            () =>
            {
                _serial.SendMessage(DIVERSerialPackage.CreateConfigurationReadRequestPackage(_DefaultSlaveAddress).Serialize());
                return true;
            }
        );
    }

    public bool ModifyRelays()
    {
        if (_serial == null || !_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, serial port is not open!");
            return false;
        }

        if (mcuConfiguration == null)
        {
            Console.WriteLine("Coralinker: Error, configuration is null!");
            return false;
        }

        if (mcuState != StateEnum.Initialized && mcuState != StateEnum.Configurated && mcuState != StateEnum.Configurating)
        {
            Console.WriteLine("Coralinker: Error, MCU is not initialized or configurated!");
            return false;
        }

        Console.WriteLine("Coralinker: ModifyRelays: " + _uri);
        _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
            _DefaultSlaveAddress, newConfiguration, ConfigurationActionEnum.WriteRelays).Serialize());
        Thread.Sleep(_WaitInterval * 10);
        return WaitConditionOfMCU(
            () => {
                // TODO this is not elegant but works
                var isEqual = newConfiguration.ToString() == mcuConfiguration.ToString(); return isEqual; },
            () => { return false; },
            () =>
            {
                _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
                    _DefaultSlaveAddress, newConfiguration, ConfigurationActionEnum.WriteRelays).Serialize());
                return true;
            }
        );
    }

    public bool WritePortsConfiguration()
    {
        if (_serial == null || !_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, serial port is not open!");
            return false;
        }

        if (mcuConfiguration == null)
        {
            Console.WriteLine("Coralinker: Error, configuration is null!");
            return false;
        }

        if (mcuState != StateEnum.Initialized && mcuState != StateEnum.Configurating)
        {
            Console.WriteLine("Coralinker: Error, MCU is not initialized or configurating!");
            return false;
        }

        Console.WriteLine("Coralinker: WritePortsConfiguration: " + _uri);

        _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
            _DefaultSlaveAddress, mcuConfiguration, ConfigurationActionEnum.WritePorts).Serialize());
        return WaitConditionOfMCU(
            () => { return mcuState == StateEnum.Configurated; },
            () => { return false; },
            () =>
            {
                _serial.SendMessage(DIVERSerialPackage.CreateConfigurationWritePackage(
                    _DefaultSlaveAddress, mcuConfiguration, ConfigurationActionEnum.WritePorts).Serialize());
                return true;
            }
        );
    }

    public bool SetAssembly()
    {
        if (_serial == null || !_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, serial port is not open!");
            return false;
        }

        if (mcuState != StateEnum.Configurated)
        {
            Console.WriteLine("Coralinker: Error, MCU is not configurated!");
            return false;
        }

        // MCU can not receive too much bytes once, need split
        static List<byte[]> SplitArrayIntoChunks(byte[] array, int chunkSize)
        {
            List<byte[]> chunks = new();
            for (int i = 0; i < array.Length; i += chunkSize)
            {
                int currentChunkSize = Math.Min(chunkSize, array.Length - i);
                byte[] chunk = new byte[currentChunkSize];
                Array.Copy(array, i, chunk, 0, currentChunkSize);
                chunks.Add(chunk);
            }
            return chunks;
        }
        var codeList = SplitArrayIntoChunks(_asm, _CodeChunkSplitSize);

        Console.WriteLine($"Coralinker: SetAssembly: {codeList.Count} chunks, {_asm.Length} bytes, to {_uri}");
        for (int i = 0; i < codeList.Count; i++)
        {
            var doSendCodeSplitPack = () => {
                var codePack = codeList[i];
                var downloadCodePackage = DIVERSerialPackage.CreateBinaryCodeSectionPackage(
                    _DefaultSlaveAddress,
                    _asm.Length,
                    _CodeChunkSplitSize * i,
                    (short)codePack.Length,
                    codePack);
                _codeSectionAck = null;
                _serial.SendMessage(downloadCodePackage.Serialize());
                return true;
            };

            doSendCodeSplitPack.Invoke();
            bool sendOK = WaitConditionOfMCU(
                () =>
                {
                    return (_codeSectionAck?.SectionAddress == i * _CodeChunkSplitSize)
                           || (mcuState == StateEnum.BinaryCodeReceived);
                },
                () =>
                {
                    return false;
                },
                doSendCodeSplitPack
            );

            if (!sendOK)
            {
                return false;
            }
        }

        return WaitConditionOfMCU(
                () => {
                    return mcuState == StateEnum.BinaryCodeReceived;
                },
                () => { return false; },
                () => { return true; }
        );
    }

    public bool Start()
    {
        if (_serial == null || !_serial.isOpen)
        {
            Console.WriteLine("Coralinker: Error, serial port is not open!");
            return false;
        }

        if (mcuState != StateEnum.BinaryCodeReceived)
        {
            Console.WriteLine("Coralinker: Error, MCU is not binary code received!");
            return false;
        }

        Console.WriteLine("Coralinker: Start: " + _uri);
        return WaitConditionOfMCU(
            () => { return mcuState == StateEnum.Running; },
            () => { return false; },
            () =>
            {
                _serial.SendMessage(DIVERSerialPackage.CreateControlPackage(_DefaultSlaveAddress, ControlCodeEnum.Start).Serialize());
                _isMCUStarted = true;
                return true;
            }
        );
    }
}

// override RunDIVER
// Connect
// TestWire
// UpdateWire
// base.Start
//    override SetMCU
// StartAll

public abstract class CoralinkerDIVERVehicle : DIVERVehicle
{
    // map from uri to CoralinkerLowerHandle
    private Dictionary<string, CoralinkerLowerNodeHandle> _nodeMap =
        new();

    public override void SendUpperData(string mcuUri, byte[] data)
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

    public override void NotifyLog(string mcuUri, string message)
    {
        Console.WriteLine($"Coralinker: MCU Log from {mcuUri}:\n{message}");
    }

    public override void SetMCUProgram(string mcuUri, byte[] programAssemblyBytes)
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

    public override void RunDIVER()
    {
        // This will call base class's Start method, in which SetMCUProgram will be called for every child node.
        base.RunDIVER();

        if (!OpenNodes())
        {
            return;
        }

        if (!FetchNodesConfiguration())
        {
            return;
        }

        var existingWiring = GatherWirings();

        bool topologyDefined = false;
        foreach (var attr in GetType().GetCustomAttributes())
        {
            if (!attr.GetType().IsSubclassOfRawGeneric(typeof(DefineCoralinkingAttribute<>), out var gType)) continue;
            topologyDefined = true;

            // gType is wiring requirements.
            var linker_type = gType.GenericTypeArguments[0];
            var clinking = Activator.CreateInstance(linker_type) as Coralinking;
            clinking.Define();
            var req = clinking.GatherRequirements();

            // check topology and SKU is compatible to requirements, then validate if update is required.
            // if bad topology/SKU, throw. don't run.
            if (req(existingWiring)) break; // wiring is good, OK to run

            var solutions = clinking.Solve();
            // break.
            // update comparators
            // update connections.

            Console.WriteLine($"layout updated for `{GetType().Name}` according to `{clinking.GetType().Name}`");
        }

        if (!topologyDefined)
            throw new Exception(
                $"No node topology for `{GetType().Name}`, use DefineCoralinking<T> to define a linking requreiment");

        //// TODO: This should be included by SKU Def
        //// for each node set configuration
        foreach (var nodeHandle in _nodeMap)
        {
            nodeHandle.Value.mcuConfiguration.Ports = new ConfigurationPort[] {
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.DualDirectionSerial, BaudRate = 9600, BufferSize = 1024},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.DualDirectionSerial, BaudRate = 9600, BufferSize = 1024},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.Modbus, BaudRate = 9600, BufferSize = 256},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 500000, BufferSize = 32},
                new ConfigurationPort { Type = ConfigurationPortTypeEnum.CAN, BaudRate = 500000, BufferSize = 32},
            };
        }

        //// Disconnect all connections
        //foreach (var nodeHandle in _nodeMap)
        //{
        //    nodeHandle.Value.newConfiguration = nodeHandle.Value.mcuConfiguration;
        //    // relays index from 25 to 25 + 11, set off
        //    for (int i = 0; i < 11; i++)
        //    {
        //        nodeHandle.Value.newConfiguration.Relays[25 + i].IsOn = ConfigurationRelayIsOnEnum.Off;
        //    }

        //    nodeHandle.Value.ModifyRelays();
        //}

        //Thread.Sleep(1000);

        //// Connect DPDT
        //foreach (var nodeHandle in _nodeMap)
        //{
        //    for (int i = 0; i < 25; i++)
        //    {
        //        nodeHandle.Value.newConfiguration.Relays[i].IsOn = ConfigurationRelayIsOnEnum.Off;
        //    }

        //    nodeHandle.Value.newConfiguration.Relays[8].IsOn = ConfigurationRelayIsOnEnum.On;
        //    nodeHandle.Value.ModifyRelays();
        //}

        //// Connect DPDT
        //foreach (var nodeHandle in _nodeMap)
        //{
        //    for (int i = 0; i < 25; i++)
        //    {
        //        nodeHandle.Value.newConfiguration.Relays[i].IsOn = ConfigurationRelayIsOnEnum.Off;
        //    }

        //    nodeHandle.Value.newConfiguration.Relays[8].IsOn = ConfigurationRelayIsOnEnum.On;
        //    nodeHandle.Value.ModifyRelays();
        //}

        //Thread.Sleep(1000);

        //// Connect SPST
        //foreach (var nodeHandle in _nodeMap)
        //{
        //    nodeHandle.Value.newConfiguration.Relays[25 + 1].IsOn = ConfigurationRelayIsOnEnum.On;
        //    nodeHandle.Value.ModifyRelays();
        //}

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
        StartNodes();
    }

    bool OpenNodes()
    {
        foreach(var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.OpenNode())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} open failed!");
                return false;
            }
        }

        return true;
    }

    bool FetchNodesConfiguration()
    {
        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.FetchConfiguration())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} fetch configuration failed!");
                return false;
            }
        }
        return true;
    }

    bool StartNodes()
    {
        foreach (var nodeHandle in _nodeMap)
        {
            if (!nodeHandle.Value.SetAssembly())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} set assembly failed!");
                return false;
            }

            if (!nodeHandle.Value.Start())
            {
                Console.WriteLine($"Coralinker: Error, node {nodeHandle.Key} start failed!");
                return false;
            }
        }
        return true;
    }

    WiringLayout[] GatherWirings()
    {
        return null;
    }

    public enum PortIndex : int
    {
        Serial1 = 0, Serial2 = 1,
        Modbus1 = 2, Modbus2 = 3, Modbus3 = 4,
        CAN1 = 5, CAN2 = 6,
    }
    public class WiringLayout
    {
        public string[][] pin_grouping;
        public string node_path = "dddd"; // path on programmable harness topology. d=downlink, r=rightlink, l=leftlink.
    }
}