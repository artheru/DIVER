using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;


namespace DIVERSerial
{
    public delegate void FunctionReceiveBytes(byte[] bytes);

    public class DIVERSerial
    {
        private byte[] _receiveBytes;
        private SerialPort _port;
        private int _state = 0;
        private List<byte> _receiveList = new List<byte>();
        private int _length = 0;
        private FunctionReceiveBytes _onReceivedLowerIO;
        private FunctionReceiveBytes _onReceivedLogs;

        public bool isOpen;

        public DIVERSerial(
            string name,
            int baudRate,
            FunctionReceiveBytes onReceivedLowerIO,
            FunctionReceiveBytes onReceivedLogs
        )
        {
            isOpen = false;

            _port = new SerialPort();
            _port.PortName = name;
            _port.BaudRate = baudRate;
            _port.Parity = Parity.None;
            _port.DataBits = 8;
            _port.StopBits = StopBits.One;
            _port.Handshake = Handshake.None;
            try {
                _port.Open();
            }
            catch (Exception exception) {
                return;
            }
            isOpen = true;

            _onReceivedLowerIO = onReceivedLowerIO;
            _onReceivedLogs = onReceivedLogs;

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(10);
                    try
                    {
                        int bytesToRead = _port.BytesToRead;
                        if (bytesToRead > 0)
                        {
                            var readBuffer = new byte[bytesToRead];
                            //var start = DateTime.Now;
                            _port.Read(readBuffer, 0, bytesToRead);
                            //var time1 = DateTime.Now - start;
                            ProcessBuffer(readBuffer);
                            //var time2 = DateTime.Now - start;
                            //Hedingben.ToastText($"total time:{time2.TotalMilliseconds},read time:{time1},read count:{bytesToRead}", "timeDebug");
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(exception);
                    }
                }
            }).Start();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    var readBuffer = new byte[bytesToRead];
                    //var start = DateTime.Now;
                    _port.Read(readBuffer, 0, bytesToRead);
                    //var time1 = DateTime.Now - start;
                    ProcessBuffer(readBuffer);
                    //var time2 = DateTime.Now - start;
                    //Hedingben.ToastText($"total time:{time2.TotalMilliseconds},read time:{time1},read count:{bytesToRead}", "timeDebug");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        private void ProcessBuffer(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                var newByte = buffer[i];
                switch (_state)
                {
                    case 0:
                        _receiveList.Clear();
                        if (newByte == 0xBB)
                        {
                            _receiveList.Add(newByte);
                            _state = 1;
                        }
                        break;
                    case 1:
                        if (newByte == 0xAA)
                        {
                            _receiveList.Add(newByte);
                            _state = 2;
                        }
                        else
                        {
                            _state = 0;
                        }
                        break;
                    case 2:
                        _receiveList.Add(newByte);
                        if (_receiveList.Count >= 4)
                        {
                            _length = BitConverter.ToUInt16(_receiveList.ToArray(), 2);
                        }

                        if (_receiveList.Count == _length + 7)
                        {
                            _state = 0;
                            if (_receiveList[_receiveList.Count - 1] == 0xEE)
                            {
                                _receiveBytes = _receiveList.ToArray();
                                Console.WriteLine($"SerialAdaptor: Receive from mcu: {BitConverter.ToString(_receiveBytes)}");
                                if (_receiveBytes[5] == 0xA0)
                                {
                                    var upperIODataSize = BitConverter.ToUInt32(_receiveBytes, 6);
                                    var upperIOData = new byte[upperIODataSize];
                                    Array.Copy(_receiveBytes, 14, upperIOData, 0, upperIODataSize);
                                    _onReceivedLowerIO(upperIOData);
                                    var logDataSize = BitConverter.ToUInt32(_receiveBytes, 10);
                                    if (logDataSize > 0)
                                    {
                                        byte[] logData = new byte[logDataSize];
                                        Array.Copy(_receiveBytes, 14 + upperIODataSize, logData, 0, logData.Length);
                                        _onReceivedLogs(logData);
                                    }
                                }
                            }
                            else
                            {
                                _state = 0;
                                Console.WriteLine($"SerialAdaptor: ERROR, mcu packet error, should end with 0xEE , actual {_receiveList[_receiveList.Count - 1]}");
                            }
                        }
                        break;
                    default: break;
                }
            }
        }

        public void SendMessage(byte[] data)
        {
            Console.WriteLine($"SerialAdaptor: Send packet to mcu :{BitConverter.ToString(data)}");
            try
            {
                _port.Write(data, 0, data.Length);
            }
            catch (Exception exception)
            {
                return;
            }
        }

        public byte[] GetMessage()
        {
            return _receiveBytes;
        }
    }

    /// <summary>
    /// Represents a serial communication package for the DIVER protocol, handling both sending and receiving.
    /// Provides factory methods to construct packages and methods to parse received data into meaningful objects.
    /// </summary>
    public class DIVERSerialPackage
    {
        // Constants defining the frame structure
        private static readonly byte[] FrameHeader = { 0xBB, 0xAA };
        private const byte FrameFooter = 0xEE;
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
            if (configuration.PortCount > 16) throw new ArgumentException("Port count exceeds maximum of 16.", nameof(configuration));
            if (configuration.Ports == null || configuration.Ports.Length < configuration.PortCount)
                throw new ArgumentException("Ports array length must match PortCount.", nameof(configuration));

            var data = new List<byte> { (byte)action };
            data.AddRange(BitConverter.GetBytes(configuration.UpperMemorySize));
            data.AddRange(BitConverter.GetBytes(configuration.LowerMemorySize));
            data.AddRange(BitConverter.GetBytes(configuration.PortCount));
            for (int i = 0; i < configuration.PortCount; i++)
            {
                var port = configuration.Ports[i];
                data.Add((byte)port.Type);
                data.AddRange(BitConverter.GetBytes(port.BaudRate));
                data.AddRange(BitConverter.GetBytes(port.BufferSize));
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
            uint totalLength,
            uint sectionAddress,
            ushort sectionLength,
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
            uint memorySize,
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
            const ushort polynomial = 0xA001; // CRC-16-IBM polynomial
            ushort crc = 0xFFFF; // Initial value

            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= polynomial;
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
            if (declaredLength != receivedData.Length - HeaderLength - ChecksumSize - FooterSize)
                throw new ArgumentException("Declared length does not match actual data length.");

            // Validate CRC
            var crcData = new byte[declaredLength + LengthFieldSize];
            Array.Copy(receivedData, HeaderLength, crcData, 0, crcData.Length);
            ushort receivedCrc = BitConverter.ToUInt16(receivedData, receivedData.Length - FooterSize - ChecksumSize);
            ushort calculatedCrc = CalculateCRC16(crcData);
            if (receivedCrc != calculatedCrc)
                throw new ArgumentException("CRC checksum mismatch.");

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
                UpperMemorySize = BitConverter.ToUInt32(DataSegment, 1),
                LowerMemorySize = BitConverter.ToUInt32(DataSegment, 5),
                PortCount = BitConverter.ToUInt16(DataSegment, 9)
            };

            int expectedLength = 11 + config.PortCount * 7; // action + sizes + port_count + ports
            if (DataSegment.Length != expectedLength) return null;

            config.Ports = new ConfigurationPort[config.PortCount];
            for (int i = 0; i < config.PortCount; i++)
            {
                int offset = 11 + i * 7;
                config.Ports[i] = new ConfigurationPort
                {
                    Type = (ConfigurationPortTypeEnum)DataSegment[offset],
                    BaudRate = BitConverter.ToUInt32(DataSegment, offset + 1),
                    BufferSize = BitConverter.ToUInt16(DataSegment, offset + 5)
                };
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
                    SectionAddress = BitConverter.ToUInt32(DataSegment, 3),
                    SectionLength = BitConverter.ToUInt16(DataSegment, 7)
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
                uint memorySize = BitConverter.ToUInt32(DataSegment, 0);
                uint logSize = BitConverter.ToUInt32(DataSegment, 4);
                if (DataSegment.Length != 8 + memorySize + logSize) return null;

                var cmd = new MemoryExchangeResponseCommand
                {
                    MemorySize = memorySize,
                    LogSize = logSize,
                    Data = new byte[memorySize + logSize]
                };
                Array.Copy(DataSegment, 8, cmd.Data, 0, cmd.Data.Length);
                return cmd;
            }
            return null;
        }
    }

    // --- Enums and Data Structures ---

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
        LogAck = 0xB0,
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
        Write = 0x01,
        WriteAndSave = 0x02
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

    public class ConfigurationPort
    {
        public ConfigurationPortTypeEnum Type { get; set; }
        public uint BaudRate { get; set; }
        public ushort BufferSize { get; set; }
    }

    public class Configuration
    {
        public uint UpperMemorySize { get; set; }
        public uint LowerMemorySize { get; set; }
        public ushort PortCount { get; set; }
        public ConfigurationPort[] Ports { get; set; }
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
        public uint SectionAddress { get; set; }
        public ushort SectionLength { get; set; }
    }

    public class MemoryExchangeResponseCommand
    {
        public uint MemorySize { get; set; }
        public uint LogSize { get; set; }
        public byte[] Data { get; set; }
    }
}
