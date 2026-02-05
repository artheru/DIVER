using System;

namespace MCUSerialBridgeCLR
{
    public enum MCUSerialBridgeError : uint
    {
        OK = 0x00000000, // Success
        NoData = 0x00000001, // Port no new data
        Win_Unknown = 0x80000001, // Unknown Windows error
        Win_InvalidParam = 0x80000002, // Invalid parameter
        Win_AllocFail = 0x80000003, // Memory allocation failed
        Win_HandleNotFound = 0x80000004, // Handle not found
        Win_ResourceBusy = 0x80000005, // Resource busy
        Win_BufferFull = 0x80000006, // Buffer is full
        Win_UserBufferTooSmall = 0x80000007, // Buffer is too small
        Win_CannotOpenPort = 0x80000010, // Cannot open port
        Win_CannotGetCommState = 0x80000011, // Cannot get comm state
        Win_CannotSetCommState = 0x80000012, // Cannot set comm state
        Win_CannotCreateThread = 0x80000013, // Cannot create thread
        Proto_Invalid = 0xE0000001, // Protocol invalid
        Proto_Checksum = 0xE0000002, // CRC check failed
        Proto_Timeout = 0xE0000003, // Protocol timeout
        Proto_FrameTooLong = 0xE0000004, // Frame too long
        Proto_UnknownCommand = 0xE0000005, // Unknown command
        Proto_InvalidPayload = 0xE0000006, // Invalid payload
        Proto_ProgramTooLarge = 0xE0000010, // Program too large for MCU
        Proto_ProgramInvalidOffset = 0xE0000011, // Program chunk offset or length mismatch
        State_NotRunning = 0xF0000000, // Not running, configure and program first
        State_Running = 0xF0000001, // Can not configure or program, already running
        State_NotConfigured = 0xF0000002, // Not configured, Can not start
        State_AlreadyConfigured = 0xF0000003, // Already configured, Can not configure again
        State_NotProgrammed = 0xF0000004, // Program not loaded (DIVER mode)
        State_NotDIVERMode = 0xF0000005, // Not in DIVER mode
        Config_PortNumOver = 0xC0000000, // Port number over
        Config_SerialNumOver = 0xC0000001, // Serial port number over
        Config_CANNumOver = 0xC0000002, // CAN number over
        Config_UnknownPortType = 0xC0000010, // Unknown Port Type
        Serial_OpenFail = 0x01000001, // Serial open failed
        Serial_NotOpen = 0x01000002, // Serial not open
        Serial_ReadFail = 0x01000003, // Serial read failed
        Serial_WriteFail = 0x01000004, // Serial write failed
        Serial_Busy = 0x01000005, // Serial is busy
        CAN_DataError = 0x02000000, // CAN data error
        CAN_SendFail = 0x02000001, // CAN send failed
        CAN_RecvFail = 0x02000002, // CAN receive failed
        CAN_BufferFull = 0x02000003, // CAN buffer full
        CAN_NotInit = 0x02000004, // CAN not initialized
        Port_WriteBusy = 0x10000001, // Port is busy now
        MCU_Unknown = 0x00010001, // MCU unknown error
        MCU_IOSizeError = 0x00010002, // IO Should be 4 bytes
        MCU_OverTemperature = 0x00010010, // MCU over temperature
        MCU_RuntimeNotAvailable = 0x00000020, // DIVER runtime not available, cannot load program
        MCU_MemoryAllocFailed = 0x00000021, // Memory allocation failed
        MCU_SerialDataFlushFailed = 0x00000022, // SerialData Buffer is Full
    }

    public static class MCUSerialBridgeErrorExtensions
    {
        public static string ToDescription(this MCUSerialBridgeError err)
        {
            return err switch
            {
                MCUSerialBridgeError.OK => "OK|Success",
                MCUSerialBridgeError.NoData => "NoData|Port no new data",
                MCUSerialBridgeError.Win_Unknown => "Win_Unknown|Unknown Windows error",
                MCUSerialBridgeError.Win_InvalidParam => "Win_InvalidParam|Invalid parameter",
                MCUSerialBridgeError.Win_AllocFail => "Win_AllocFail|Memory allocation failed",
                MCUSerialBridgeError.Win_HandleNotFound => "Win_HandleNotFound|Handle not found",
                MCUSerialBridgeError.Win_ResourceBusy => "Win_ResourceBusy|Resource busy",
                MCUSerialBridgeError.Win_BufferFull => "Win_BufferFull|Buffer is full",
                MCUSerialBridgeError.Win_UserBufferTooSmall => "Win_UserBufferTooSmall|Buffer is too small",
                MCUSerialBridgeError.Win_CannotOpenPort => "Win_CannotOpenPort|Cannot open port",
                MCUSerialBridgeError.Win_CannotGetCommState => "Win_CannotGetCommState|Cannot get comm state",
                MCUSerialBridgeError.Win_CannotSetCommState => "Win_CannotSetCommState|Cannot set comm state",
                MCUSerialBridgeError.Win_CannotCreateThread => "Win_CannotCreateThread|Cannot create thread",
                MCUSerialBridgeError.Proto_Invalid => "Proto_Invalid|Protocol invalid",
                MCUSerialBridgeError.Proto_Checksum => "Proto_Checksum|CRC check failed",
                MCUSerialBridgeError.Proto_Timeout => "Proto_Timeout|Protocol timeout",
                MCUSerialBridgeError.Proto_FrameTooLong => "Proto_FrameTooLong|Frame too long",
                MCUSerialBridgeError.Proto_UnknownCommand => "Proto_UnknownCommand|Unknown command",
                MCUSerialBridgeError.Proto_InvalidPayload => "Proto_InvalidPayload|Invalid payload",
                MCUSerialBridgeError.Proto_ProgramTooLarge => "Proto_ProgramTooLarge|Program too large for MCU",
                MCUSerialBridgeError.Proto_ProgramInvalidOffset => "Proto_ProgramInvalidOffset|Program chunk offset or length mismatch",
                MCUSerialBridgeError.State_NotRunning => "State_NotRunning|Not running, configure and program first",
                MCUSerialBridgeError.State_Running => "State_Running|Can not configure or program, already running",
                MCUSerialBridgeError.State_NotConfigured => "State_NotConfigured|Not configured, Can not start",
                MCUSerialBridgeError.State_AlreadyConfigured => "State_AlreadyConfigured|Already configured, Can not configure again",
                MCUSerialBridgeError.State_NotProgrammed => "State_NotProgrammed|Program not loaded (DIVER mode)",
                MCUSerialBridgeError.State_NotDIVERMode => "State_NotDIVERMode|Not in DIVER mode",
                MCUSerialBridgeError.Config_PortNumOver => "Config_PortNumOver|Port number over",
                MCUSerialBridgeError.Config_SerialNumOver => "Config_SerialNumOver|Serial port number over",
                MCUSerialBridgeError.Config_CANNumOver => "Config_CANNumOver|CAN number over",
                MCUSerialBridgeError.Config_UnknownPortType => "Config_UnknownPortType|Unknown Port Type",
                MCUSerialBridgeError.Serial_OpenFail => "Serial_OpenFail|Serial open failed",
                MCUSerialBridgeError.Serial_NotOpen => "Serial_NotOpen|Serial not open",
                MCUSerialBridgeError.Serial_ReadFail => "Serial_ReadFail|Serial read failed",
                MCUSerialBridgeError.Serial_WriteFail => "Serial_WriteFail|Serial write failed",
                MCUSerialBridgeError.Serial_Busy => "Serial_Busy|Serial is busy",
                MCUSerialBridgeError.CAN_DataError => "CAN_DataError|CAN data error",
                MCUSerialBridgeError.CAN_SendFail => "CAN_SendFail|CAN send failed",
                MCUSerialBridgeError.CAN_RecvFail => "CAN_RecvFail|CAN receive failed",
                MCUSerialBridgeError.CAN_BufferFull => "CAN_BufferFull|CAN buffer full",
                MCUSerialBridgeError.CAN_NotInit => "CAN_NotInit|CAN not initialized",
                MCUSerialBridgeError.Port_WriteBusy => "Port_WriteBusy|Port is busy now",
                MCUSerialBridgeError.MCU_Unknown => "MCU_Unknown|MCU unknown error",
                MCUSerialBridgeError.MCU_IOSizeError => "MCU_IOSizeError|IO Should be 4 bytes",
                MCUSerialBridgeError.MCU_OverTemperature => "MCU_OverTemperature|MCU over temperature",
                MCUSerialBridgeError.MCU_RuntimeNotAvailable => "MCU_RuntimeNotAvailable|DIVER runtime not available, cannot load program",
                MCUSerialBridgeError.MCU_MemoryAllocFailed => "MCU_MemoryAllocFailed|Memory allocation failed",
                MCUSerialBridgeError.MCU_SerialDataFlushFailed => "MCU_SerialDataFlushFailed|SerialData Buffer is Full",
                _ => "Unknown Error",
            };
        }
    }
}
