using System.Text;

namespace CoralinkerSDK;

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

    public readonly string uri;
    private readonly byte[] _asm;
    private readonly PEAMInterface _root;

    private DIVERSerialListener _serial;
    private BinaryCodeSectionAckCommand _codeSectionAck = null;

    public volatile StateEnum mcuState = StateEnum.Uninitialized;
    private volatile bool _isMCUStarted = false;
    public volatile Configuration mcuConfiguration = null;
    public volatile Configuration newConfiguration = null;

    // constructor
    public CoralinkerLowerNodeHandle(string uri_, byte[] asm, PEAMInterface root)
    {
        uri = uri_;
        _asm = asm;
        _root = root;
    }

    public override string ToString()
    {
        return $"[<CoralinkerLowerNodeHandle> uri = {uri}]";
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
                    if (memExchangePackage.MemoryExchangeData.Length > 0)
                    {
                        _root.NotifyLowerData(uri, memExchangePackage.MemoryExchangeData);
                    }
                    _root.NotifyLog(uri, Encoding.UTF8.GetString(memExchangePackage.LogData));
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
        Console.WriteLine("Coralinker: OpenNode: " + uri);

        mcuState = StateEnum.Uninitialized;
        _serial = new DIVERSerialListener(uri, OnMCUPackage);

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

        Console.WriteLine("Coralinker: ReadConfiguration: " + uri);

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

        Console.WriteLine("Coralinker: ModifyRelays: " + uri);
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

        Console.WriteLine("Coralinker: WritePortsConfiguration: " + uri);

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

        Console.WriteLine($"Coralinker: SetAssembly: {codeList.Count} chunks, {_asm.Length} bytes, to {uri}");
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

        Console.WriteLine("Coralinker: Start: " + uri);
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