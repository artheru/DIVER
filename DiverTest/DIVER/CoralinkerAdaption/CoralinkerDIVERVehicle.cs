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


public abstract class CoralinkerDIVERVehicle : DIVERVehicle
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
                if (heartbeatPackage != null)
                {
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

    void OpenCoralinkers()
    {
        //
    }

    public class WiringLayout
    {
        public string[][] pin_grouping;
        public string node_path="dddd"; // path on programmable harness topology. d=downlink, r=rightlink, l=leftlink.
    }

    WiringLayout[] GatherWirings()
    {
        return null;
    }
    
    public override void RunDIVER()
    {
        OpenCoralinkers(); // do topology discovery.

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

        // update mcu program and run.
        base.RunDIVER();
    }

    public enum PortIndex : int
    {
        CAN1 = 0, CAN2 = 1,
        Modbus1 = 2, Modbus2 = 3,
        Serial1 = 4, Serial2 = 5,
    }
}