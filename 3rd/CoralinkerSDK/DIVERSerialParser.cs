using System.IO.Ports;
using System.Text;

namespace CoralinkerSDK;

public delegate void FunctionOnPackageReceivedType(DIVERSerialPackage package);

public class DIVERSerialListener
{
    private const int ReceiveSleepTimeMs = 5; // Sleep time in milliseconds for receiving data
    private const int ReponseBufferSize = 4096; // Size of the response buffer

    private readonly string _uri;
    private SerialPort _port;
    public bool isOpen { get; set; }

    private ResponseStateEnum _responseState = ResponseStateEnum.Initialized;
    private List<byte> _responseBuffer = new List<byte>();
    private uint _packageLength = 0;

    private FunctionOnPackageReceivedType _onReceived;

    public DIVERSerialListener(
        string uri, // URI for the serial port
        FunctionOnPackageReceivedType onReceived // Callback for receiving data
    )
    {
        _uri = uri;
        isOpen = false;
        _port = SerialPortResolver.OpenUri(uri);
        isOpen = true;
        _onReceived = onReceived;

        new Thread(CheckReceivedSerialData).Start();
    }

    private void CheckReceivedSerialData()
    {
        Thread.Sleep(ReceiveSleepTimeMs);
        try
        {
            while (true)
            {
                int bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    if (bytesToRead > ReponseBufferSize)
                    {
                        _port.DiscardInBuffer();
                        Console.WriteLine($"Serial{_uri}: Too many bytes in buffer, discarding {bytesToRead} bytes.");
                        _responseBuffer.Clear();
                        _responseState = ResponseStateEnum.Initialized;
                    }
                    else
                    {
                        var readBuffer = new byte[bytesToRead];
                        _port.Read(readBuffer, 0, bytesToRead);
                        ProcessBuffer(readBuffer);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void ProcessBuffer(byte[] buffer)
    {
        int i = 0;
        while (i < buffer.Length) {
            var newByte = buffer[i];
            switch (_responseState)
            {
                case ResponseStateEnum.Initialized:
                    if (newByte == DIVERSerialPackage.FrameHeader[0])
                    {
                        _responseBuffer.Add(newByte);
                        _responseState = ResponseStateEnum.PrefixReceived;
                    }
                    i++;
                    break;

                case ResponseStateEnum.PrefixReceived:
                    if (newByte == DIVERSerialPackage.FrameHeader[1])
                    {
                        _responseBuffer.Add(newByte);
                        _responseState = ResponseStateEnum.LengthReceiving;
                    }
                    else
                    {
                        _responseBuffer.Clear();
                        _responseState = ResponseStateEnum.Initialized;
                    }
                    i++;
                    break;

                case ResponseStateEnum.LengthReceiving:
                    _responseBuffer.Add(newByte);
                    if (_responseBuffer.Count == 4)
                    {
                        _packageLength = BitConverter.ToUInt16(_responseBuffer.ToArray(), 2);
                        if (_packageLength > ReponseBufferSize - 7 || _packageLength < 2)
                        {
                            Console.WriteLine($"Serial{_uri}: Invalid package length {_packageLength}, discarding buffer.");
                            _responseBuffer.Clear();
                            _responseState = ResponseStateEnum.Initialized;
                        }
                        else
                        {
                            _responseState = ResponseStateEnum.DataReceiving;
                        }
                    }
                    i++;
                    break;

                case ResponseStateEnum.DataReceiving:
                    int remainingBytes = (int)_packageLength + 3;
                    int bytesToRead = Math.Min(remainingBytes, buffer.Length - i);
                    _responseBuffer.AddRange(buffer.Skip(i).Take(bytesToRead));
                    i += bytesToRead;

                    if (_responseBuffer.Count == _packageLength + 7)
                    {
                        try
                        {
                            Console.WriteLine($"Serial{_uri}: Received package: {BitConverter.ToString(_responseBuffer.ToArray())}.");
                            var package = DIVERSerialPackage.Parse(_responseBuffer.ToArray());
                            if (package != null)
                            {
                                _onReceived?.Invoke(package);
                            }
                        }
                        catch (ArgumentException exception)
                        {
                            Console.WriteLine($"Serial{_uri}: Failed to parse package: {exception.Message}");
                        }

                        _responseBuffer.Clear();
                        _responseState = ResponseStateEnum.Initialized;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(_responseState), "Invalid response state.");
            }
        }
    }

    public void SendMessage(byte[] data)
    {
        Console.WriteLine($"Serial{_uri}: Send packet to mcu :{BitConverter.ToString(data)}");
        try
        {
            _port.Write(data, 0, data.Length);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Serial{_uri}: Failed to send data: {exception.Message}");
            return;
        }
    }
}

/// <summary>
/// Represents a serial communication package for the DIVER protocol, handling both sending and receiving.
/// Provides factory methods to construct packages and methods to parse received data into meaningful objects.
/// </summary>
public class DIVERSerialPackage
{
    // Constants defining the frame structure
    public static readonly byte[] FrameHeader = { 0xBB, 0xAA };
    public const byte FrameFooter = 0xEE;

    private const int HeaderLength = 2; // Frame header size
    private const int LengthFieldSize = 2; // Data segment length field size
    private const int ChecksumSize = 2; // CRC-16 checksum size
    private const int FooterSize = 1; // Frame footer size
    private const int MinimumFrameSize = HeaderLength + LengthFieldSize + ChecksumSize + FooterSize + 2; // Includes slave address and function code

    /// <summary>
    /// Gets the slave address of the package.
    /// </summary>
    public byte SlaveAddress { get; private set; }

    /// <summary>
    /// Gets the function code indicating the package type and direction.
    /// </summary>
    public FunctionCodeEnum FunctionCode { get; private set; }

    /// <summary>
    /// Gets the raw data segment of the package, excluding slave address and function code.
    /// </summary>
    public byte[] DataSegment { get; private set; }

    /// <summary>
    /// Private constructor to enforce creation via factory methods or parsing.
    /// </summary>
    private DIVERSerialPackage(byte slaveAddress, FunctionCodeEnum functionCode, byte[] dataSegment)
    {
        SlaveAddress = slaveAddress;
        FunctionCode = functionCode;
        DataSegment = dataSegment ?? throw new ArgumentNullException(nameof(dataSegment));
    }

    // --- Factory Methods for Sending Packages ---

    /// <summary>
    /// Creates a control package to send commands like start or reset to the MCU.
    /// </summary>
    /// <param name="slaveAddress">The address of the target MCU.</param>
    /// <param name="controlCode">The control command (e.g., Start, Reset).</param>
    /// <returns>A new DIVERSerialPackage instance for control.</returns>
    public static DIVERSerialPackage CreateControlPackage(byte slaveAddress, ControlCodeEnum controlCode)
    {
        byte[] dataSegment = new byte[] { (byte)controlCode };
        return new DIVERSerialPackage(slaveAddress, FunctionCodeEnum.Control, dataSegment);
    }

    /// <summary>
    /// Creates a configuration read request package to retrieve configuration from the MCU.
    /// </summary>
    /// <param name="slaveAddress">The address of the target MCU.</param>
    /// <returns>A new DIVERSerialPackage instance for configuration read request.</returns>
    public static DIVERSerialPackage CreateConfigurationReadRequestPackage(byte slaveAddress)
    {
        byte[] dataSegment = new byte[] { (byte)ConfigurationActionEnum.Read };
        return new DIVERSerialPackage(slaveAddress, FunctionCodeEnum.Configuration, dataSegment);
    }

    /// <summary>
    /// Creates a configuration write package to send configuration settings to the MCU.
    /// </summary>
    /// <param name="slaveAddress">The address of the target MCU.</param>
    /// <param name="configuration">The configuration data to send.</param>
    /// <param name="action">The configuration action (Write or WriteAndSave).</param>
    /// <returns>A new DIVERSerialPackage instance for configuration write.</returns>
    public static DIVERSerialPackage CreateConfigurationWritePackage(
        byte slaveAddress,
        Configuration configuration,
        ConfigurationActionEnum action)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (action == ConfigurationActionEnum.Read) throw new ArgumentException("Use CreateConfigurationReadRequestPackage for read requests.", nameof(action));
        if (configuration.Ports.Length > 16) throw new ArgumentException("Port count exceeds maximum of 16.", nameof(configuration));
        if (configuration.Ports == null)
            throw new ArgumentException("Ports array length must match PortCount.", nameof(configuration));

        var data = new List<byte> { (byte)action };
        data.Add(configuration.TerminalCount.UFP);
        data.Add(configuration.TerminalCount.Internal);
        data.Add(configuration.TerminalCount.Resource);
        data.Add(configuration.TerminalCount.DFP);
        data.AddRange(BitConverter.GetBytes(configuration.RelayCount.Exchange));
        data.AddRange(BitConverter.GetBytes(configuration.RelayCount.Connection));
        data.AddRange(BitConverter.GetBytes((short)configuration.Ports.Length));
        for (int i = 0; i < configuration.Ports.Length; i++)
        {
            var port = configuration.Ports[i];
            data.Add((byte)port.Type);
            data.AddRange(BitConverter.GetBytes(port.BaudRate));
            data.AddRange(BitConverter.GetBytes(port.BufferSize));
        }
        for (int i = 0; i < configuration.Terminals.Length; i++)
        {
            var terminal = configuration.Terminals[i];
            data.Add(terminal.MaxCurrentAmpere);
        }
        for (int i = 0; i < configuration.Relays.Length; i++)
        {
            var relay = configuration.Relays[i];
            data.Add(relay.MaxCurrentAmpere);
            var byteRelayType = (byte)((byte)(relay.RelayType) & 0x0F) + ((byte)(relay.IsOn) << 4);
            data.Add((byte)byteRelayType);
            data.Add(relay.TerminalIndex0);
            data.Add(relay.TerminalIndex1);
        }
        return new DIVERSerialPackage(slaveAddress, FunctionCodeEnum.Configuration, data.ToArray());
    }

    /// <summary>
    /// Creates a binary code section package to send code segments to the MCU.
    /// </summary>
    /// <param name="slaveAddress">The address of the target MCU.</param>
    /// <param name="totalLength">Total length of the code.</param>
    /// <param name="sectionAddress">Starting address of this code section.</param>
    /// <param name="sectionLength">Length of this code section.</param>
    /// <param name="data">The binary code data for this section.</param>
    /// <returns>A new DIVERSerialPackage instance for binary code section.</returns>
    public static DIVERSerialPackage CreateBinaryCodeSectionPackage(
        byte slaveAddress,
        int totalLength,
        int sectionAddress,
        short sectionLength,
        byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length != sectionLength) throw new ArgumentException("Data length must match sectionLength.", nameof(data));

        var dataSegment = new List<byte>();
        dataSegment.AddRange(BitConverter.GetBytes(totalLength));
        dataSegment.AddRange(BitConverter.GetBytes(sectionAddress));
        dataSegment.AddRange(BitConverter.GetBytes(sectionLength));
        dataSegment.AddRange(data);
        return new DIVERSerialPackage(slaveAddress, FunctionCodeEnum.BinaryCodeSection, dataSegment.ToArray());
    }

    /// <summary>
    /// Creates a memory exchange request package (UpperIO) to send data to the MCU.
    /// </summary>
    /// <param name="slaveAddress">The address of the target MCU.</param>
    /// <param name="memorySize">Size of the memory data.</param>
    /// <param name="data">The memory data to send.</param>
    /// <returns>A new DIVERSerialPackage instance for memory exchange request.</returns>
    public static DIVERSerialPackage CreateMemoryExchangeRequestPackage(
        byte slaveAddress,
        int memorySize,
        byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length != memorySize) throw new ArgumentException("Data length must match memorySize.", nameof(data));

        var dataSegment = new List<byte>();
        dataSegment.AddRange(BitConverter.GetBytes(memorySize));
        dataSegment.AddRange(data);
        return new DIVERSerialPackage(slaveAddress, FunctionCodeEnum.MemoryExchangeRequest, dataSegment.ToArray());
    }

    // --- Serialization ---

    /// <summary>
    /// Serializes the package into a byte array ready to be sent over the serial port.
    /// </summary>
    /// <returns>The serialized package as a byte array.</returns>
    public byte[] Serialize()
    {
        int dataSegmentLength = DataSegment.Length + 2; // Include slave address and function code
        var pack = new byte[HeaderLength + LengthFieldSize + dataSegmentLength + ChecksumSize + FooterSize];

        // Frame header
        Array.Copy(FrameHeader, 0, pack, 0, HeaderLength);

        // Data segment length (little-endian)
        var lengthBytes = BitConverter.GetBytes((ushort)dataSegmentLength);
        Array.Copy(lengthBytes, 0, pack, HeaderLength, LengthFieldSize);

        // Data segment: Slave address + Function code + Data
        pack[HeaderLength + LengthFieldSize] = SlaveAddress;
        pack[HeaderLength + LengthFieldSize + 1] = (byte)FunctionCode;
        Array.Copy(DataSegment, 0, pack, HeaderLength + LengthFieldSize + 2, DataSegment.Length);

        // CRC-16 checksum over length and data segment
        var crcBytes = new byte[dataSegmentLength + LengthFieldSize];
        Array.Copy(pack, HeaderLength, crcBytes, 0, crcBytes.Length);
        ushort crc = CalculateCRC16(crcBytes);
        var crcBytesArray = BitConverter.GetBytes(crc);
        pack[pack.Length - FooterSize - ChecksumSize] = crcBytesArray[0];
        pack[pack.Length - FooterSize - ChecksumSize + 1] = crcBytesArray[1];

        // Frame footer
        pack[pack.Length - FooterSize] = FrameFooter;

        return pack;
    }

    /// <summary>
    /// Calculates the CRC-16 checksum using the IBM polynomial (0xA001).
    /// </summary>
    /// <param name="data">The data to calculate the checksum over.</param>
    /// <returns>The computed CRC-16 value.</returns>
    private static ushort CalculateCRC16(byte[] data)
    {
        const ushort polynomial = 0xA001;
        ushort crc = 0xFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ polynomial);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return crc;
    }

    // --- Parsing ---

    /// <summary>
    /// Parses a received byte array into a DIVERSerialPackage instance.
    /// </summary>
    /// <param name="receivedData">The raw data received from the serial port.</param>
    /// <returns>A parsed DIVERSerialPackage instance.</returns>
    /// <exception cref="ArgumentException">Thrown if the data is invalid (e.g., wrong header, footer, length, or CRC).</exception>
    public static DIVERSerialPackage Parse(byte[] receivedData)
    {
        if (receivedData == null) throw new ArgumentNullException(nameof(receivedData));
        if (receivedData.Length < MinimumFrameSize) throw new ArgumentException("Received data is too short to be a valid package.");

        // Validate header
        if (receivedData[0] != FrameHeader[0] || receivedData[1] != FrameHeader[1])
            throw new ArgumentException("Invalid frame header.");

        // Validate footer
        if (receivedData[receivedData.Length - 1] != FrameFooter)
            throw new ArgumentException("Invalid frame footer.");

        // Validate length
        ushort declaredLength = BitConverter.ToUInt16(receivedData, HeaderLength);
        ushort actualLength = (ushort)(receivedData.Length - HeaderLength - 2 - ChecksumSize - FooterSize);
        if (declaredLength != actualLength)
            throw new ArgumentException($"Declared length { declaredLength } does not match actual data length {actualLength}.");

        // Validate CRC
        var crcData = new byte[declaredLength + LengthFieldSize];
        Array.Copy(receivedData, HeaderLength, crcData, 0, crcData.Length);
        ushort receivedCrc = BitConverter.ToUInt16(receivedData, receivedData.Length - FooterSize - ChecksumSize);
        ushort calculatedCrc = CalculateCRC16(crcData);
        if (receivedCrc != calculatedCrc)
            throw new ArgumentException($"CRC mismatch: received {receivedCrc:X4}, calculated {calculatedCrc:X4}.");

        // Extract fields
        byte slaveAddress = receivedData[HeaderLength + LengthFieldSize];
        FunctionCodeEnum functionCode = (FunctionCodeEnum)receivedData[HeaderLength + LengthFieldSize + 1];
        byte[] dataSegment = new byte[declaredLength - 2];
        Array.Copy(receivedData, HeaderLength + LengthFieldSize + 2, dataSegment, 0, dataSegment.Length);

        return new DIVERSerialPackage(slaveAddress, functionCode, dataSegment);
    }

    // --- Data Interpretation Methods ---

    /// <summary>
    /// Interprets the data segment as a HeartBeatCommand if the function code matches.
    /// </summary>
    /// <returns>The parsed HeartBeatCommand, or null if not applicable.</returns>
    public HeartBeatCommand GetHeartBeatCommand()
    {
        if (FunctionCode == FunctionCodeEnum.HeartBeatAck && DataSegment.Length == 3)
        {
            return new HeartBeatCommand
            {
                State = (StateEnum)DataSegment[0],
                ErrorCode = (ErrorCodeEnum)BitConverter.ToUInt16(DataSegment, 1)
            };
        }
        return null;
    }

    /// <summary>
    /// Interprets the data segment as a Configuration if the function code is ConfigurationAck.
    /// </summary>
    /// <returns>The parsed Configuration, or null if not applicable.</returns>
    public Configuration GetConfiguration()
    {
        if (FunctionCode != FunctionCodeEnum.ConfigurationAck || DataSegment.Length < 11)
            return null;

        ConfigurationActionEnum action = (ConfigurationActionEnum)DataSegment[0];
        if (action != ConfigurationActionEnum.Read) return null; // ConfigurationAck should always be Read

        var config = new Configuration
        {
            TerminalCount = new ConfigurationTerminalCount
            {
                UFP = DataSegment[1],
                Internal = DataSegment[2],
                Resource = DataSegment[3],
                DFP = DataSegment[4]
            },
            RelayCount = new ConfigurationRelayCount
            {
                Exchange = BitConverter.ToInt16(DataSegment, 5),
                Connection = BitConverter.ToInt16(DataSegment, 7)
            },
        };

        int totalPortCount = BitConverter.ToInt16(DataSegment, 9);
        int totalTerminalCount = config.TerminalCount.UFP + config.TerminalCount.Internal + config.TerminalCount.Resource + config.TerminalCount.DFP;
        int totalRelayCount = config.RelayCount.Exchange + config.RelayCount.Connection;
        int expectedLength = 11 +
                             totalPortCount * 7 +
                             totalTerminalCount * 1 +
                             totalRelayCount * 4;

        Console.WriteLine($"DIVERSerialPackage: Expected length = {expectedLength}, Actual length = {DataSegment.Length}");

        if (DataSegment.Length != expectedLength) return null;

        config.Ports = new ConfigurationPort[totalPortCount];
        config.Terminals = new CongigurationTerminal[totalTerminalCount];
        config.Relays = new ConfigurationRelay[totalRelayCount];

        int offset = 11;
        for (int i = 0; i < totalPortCount; i++)
        {
            config.Ports[i] = new ConfigurationPort
            {
                Type = (ConfigurationPortTypeEnum)DataSegment[offset],
                BaudRate = BitConverter.ToInt32(DataSegment, offset + 1),
                BufferSize = BitConverter.ToInt16(DataSegment, offset + 5)
            };
            offset += 7;
        }
        for (int i = 0; i < totalTerminalCount; i++)
        {
            config.Terminals[i] = new CongigurationTerminal
            {
                MaxCurrentAmpere = DataSegment[offset]
            };
            offset += 1;
        }
        for (int i = 0; i < totalRelayCount; i++)
        {
            config.Relays[i] = new ConfigurationRelay
            {
                MaxCurrentAmpere = DataSegment[offset],
                RelayType = (ConfigurationRelayTypeEnum)(DataSegment[offset + 1] & 0x0F),
                IsOn = (ConfigurationRelayIsOnEnum)(byte)(DataSegment[offset + 1] >> 4),
                TerminalIndex0 = DataSegment[offset + 2],
                TerminalIndex1 = DataSegment[offset + 3]
            };
            offset += 4;
        }
        return config;
    }

    /// <summary>
    /// Interprets the data segment as a BinaryCodeSectionAckCommand if the function code matches.
    /// </summary>
    /// <returns>The parsed BinaryCodeSectionAckCommand, or null if not applicable.</returns>
    public BinaryCodeSectionAckCommand GetBinaryCodeSectionAckCommand()
    {
        if (FunctionCode == FunctionCodeEnum.BinaryCodeSectionAck && DataSegment.Length == 9)
        {
            return new BinaryCodeSectionAckCommand
            {
                State = (StateEnum)DataSegment[0],
                ErrorCode = (ErrorCodeEnum)BitConverter.ToUInt16(DataSegment, 1),
                SectionAddress = BitConverter.ToInt32(DataSegment, 3),
                SectionLength = BitConverter.ToInt16(DataSegment, 7)
            };
        }
        return null;
    }

    /// <summary>
    /// Interprets the data segment as a MemoryExchangeResponseCommand if the function code matches.
    /// </summary>
    /// <returns>The parsed MemoryExchangeResponseCommand, or null if not applicable.</returns>
    public MemoryExchangeResponseCommand GetMemoryExchangeResponseCommand()
    {
        if (FunctionCode == FunctionCodeEnum.MemoryExchangeResponse && DataSegment.Length >= 8)
        {
            int memorySize = BitConverter.ToInt32(DataSegment, 0);
            int logSize = BitConverter.ToInt32(DataSegment, 4);

            if (memorySize < 0 || logSize < 0) return null; // Invalid sizes
            if (DataSegment.Length != 8 + memorySize + logSize) return null;

            var cmd = new MemoryExchangeResponseCommand
            {
                MemorySize = memorySize,
                LogSize = logSize,
                MemoryExchangeData = new byte[memorySize],
                LogData = new byte[logSize],
            };

            Array.Copy(DataSegment, 8, cmd.MemoryExchangeData, 0, memorySize);
            Array.Copy(DataSegment, 8 + memorySize, cmd.LogData, 0, logSize);
            return cmd;
        }
        return null;
    }
}

// --- Enums and Data Structures ---

public enum ResponseStateEnum : byte
{
    Initialized = 0x00,
    PrefixReceived = 0x01,
    LengthReceiving = 0x02,
    DataReceiving = 0x03,
}

public enum FunctionCodeEnum : byte
{
    Control = 0x00,
    Configuration = 0x10,
    ConfigurationAck = 0x90,
    BinaryCodeSection = 0x11,
    BinaryCodeSectionAck = 0x91,
    MemoryExchangeRequest = 0x20,
    MemoryExchangeResponse = 0xA0,
    HeartBeatAck = 0xF0,
    CoreDumpAck = 0xB1
}

public enum ControlCodeEnum : byte
{
    Start = 0x01,
    Reset = 0x81
}

public enum ConfigurationActionEnum : byte
{
    Read = 0x00,
    WritePorts = 0x01,
    WriteAndSavePorts = 0x02,
    WriteRelays = 0x03,
    WriteAndSaveRelays = 0x04
}

public enum ConfigurationPortTypeEnum : byte
{
    CAN = 0x00,
    Modbus = 0x10,
    DualDirectionSerial = 0x20,
    Invalid = 0xFF
}

public enum StateEnum : byte
{
    Uninitialized = 0x00,
    Initializing = 0x01,
    Initialized = 0x02,
    Configurating = 0x03,
    Configurated = 0x04,
    BinaryCodeReceiving = 0x05,
    BinaryCodeReceived = 0x06,
    Running = 0x07,
    ConfigurationError = 0xF1,
    BinaryCodeReceiveError = 0xF2,
    ExecutionError = 0xF3
}

public enum ErrorCodeEnum : ushort
{
    OK = 0x0000,
    CommandFunctionCodeError = 0x0100,
    CommandLengthError = 0x0101,
    CommandParameterError = 0x0102,
    ConfigurationInWrongState = 0x0200,
    ConfigurationOutOfMemory = 0x0201,
    ConfigurationUnknownPortType = 0x0202,
    ConfigurationPortIndexOutOfRange = 0x0203,
    ConfigurationPortRegisterationError = 0x0204,
    BinaryCodeInWrongState = 0x0300,
    BinaryCodeStartAddressSkipped = 0x0301,
    BinaryCodeOutOfMemory = 0x0302,
    BinaryCodeTotalLengthUnmatch = 0x0303,
    BinaryCodeEndAddressOutOfRange = 0x0304,
    ControlCanNotStart = 0x0400,
    ExecutionFIFOBufferFull = 0x0600,
    ExecutionCanNotSendLowerIO = 0x0602,
    ExecutionLowerMemoryOverSize = 0x0603
}

public enum ConfigurationRelayTypeEnum : byte
{
    DPDT_FixedConnection = 0x00,
    DPDT_MagneticLatch = 0x01,
    DPDT_MotorThermalSolder = 0x02,
    DPDT_VaporThermalSolder = 0x03,
    SPST_FixedConnection = 0x08,
    SPST_MagneticLatch = 0x09,
    SPST_MotorThermalSolder = 0x0A,
    SPST_VaporThermalSolder = 0x0B
}

public enum ConfigurationRelayIsOnEnum : byte
{
    Unknown = 0x00,
    On = 0x01,
    Off = 0x02
}

public class ConfigurationPort
{
    public ConfigurationPortTypeEnum Type { get; set; }
    public int BaudRate { get; set; }
    public short BufferSize { get; set; }

    // To String Method
    public override string ToString()
    {
        return $"[ Type={Type}, BaudRate={BaudRate}, BufferSize={BufferSize} ]";
    }
}

public class ConfigurationTerminalCount
{
    public byte UFP { get; set; }
    public byte Internal { get; set; }
    public byte Resource { get; set; }
    public byte DFP { get; set; }

    public override string ToString()
    {
        return $"[ UFP={UFP}, Internal={Internal}, Resource={Resource}, DFP={DFP} ]";
    }
}

public class ConfigurationRelayCount
{
    public short Exchange { get; set; }
    public short Connection { get; set; }

    public override string ToString()
    {
        return $"[ Exchange={Exchange}, Connection={Connection} ]";
    }
}

public class CongigurationTerminal
{
    public byte MaxCurrentAmpere { get; set; }

    public override string ToString()
    {
        return $"[ CongigurationTerminal: MaxCurrentAmpere={MaxCurrentAmpere} ]";
    }
}

public class ConfigurationRelay
{
    public byte MaxCurrentAmpere { get; set; }
    public ConfigurationRelayTypeEnum RelayType { get; set; }
    public ConfigurationRelayIsOnEnum IsOn { get; set; }
    public byte TerminalIndex0 { get; set; }
    public byte TerminalIndex1 { get; set; }

    public override string ToString()
    {
        return $"[ MaxCurrentAmpere={MaxCurrentAmpere}, RelayType={RelayType}, IsOn={IsOn}, TerminalIndex0={TerminalIndex0}, TerminalIndex1={TerminalIndex1} ]";
    }
}

public class Configuration
{
    public ConfigurationTerminalCount TerminalCount { get; set; }
    public ConfigurationRelayCount RelayCount { get; set; }
    public ConfigurationPort[] Ports { get; set; }
    public CongigurationTerminal[] Terminals { get; set; }
    public ConfigurationRelay[] Relays { get; set; }

    // To String Method
    // Call child's to string method for terminal count and relay count
    // Then print ports length
    // then iterate 
    // through ports and print each port's to string
    // then iterate through terminals and print each terminal's to string
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Configuration: TerminalCount={TerminalCount}, RelayCount={RelayCount}, PortsCount={Ports?.Length}");
        if (Ports!= null)
        {
            foreach (var port in Ports)
            {
                sb.AppendLine(port.ToString());
            }
        }
        if (Terminals != null)
        {
            foreach (var terminal in Terminals)
            {
                sb.AppendLine(terminal.ToString());
            }
        }
        
        if (Relays != null)
        {
            foreach (var relay in Relays)
            {
                sb.AppendLine(relay.ToString());
            }
        }
      
        return sb.ToString();
    }
}

public class HeartBeatCommand
{
    public StateEnum State { get; set; }
    public ErrorCodeEnum ErrorCode { get; set; }
}

public class BinaryCodeSectionAckCommand
{
    public StateEnum State { get; set; }
    public ErrorCodeEnum ErrorCode { get; set; }
    public int SectionAddress { get; set; }
    public short SectionLength { get; set; }
}

public class MemoryExchangeResponseCommand
{
    public int MemorySize { get; set; }
    public int LogSize { get; set; }
    public byte[] MemoryExchangeData { get; set; }
    public byte[] LogData { get; set; }
}