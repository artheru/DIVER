#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import sys

# =============================
# 1. Python 错误类
# =============================


class MSBError:
    def __init__(self, code, name, desc):
        self.code = code
        self.name = name
        self.desc = desc


# =============================
# 2. 错误集合
# =============================
ERRORS = []


def AddMSBError(code, name, desc):
    ERRORS.append(MSBError(code, name, desc))


# =============================
# 3. 填充错误码列表
# =============================
AddMSBError(0x00000000, "OK", "Success")
AddMSBError(0x00000001, "NoData", "Port no new data")

# Windows 错误
AddMSBError(0x80000001, "Win_Unknown", "Unknown Windows error")
AddMSBError(0x80000002, "Win_InvalidParam", "Invalid parameter")
AddMSBError(0x80000003, "Win_AllocFail", "Memory allocation failed")
AddMSBError(0x80000004, "Win_HandleNotFound", "Handle not found")
AddMSBError(0x80000005, "Win_ResourceBusy", "Resource busy")
AddMSBError(0x80000006, "Win_BufferFull", "Buffer is full")
AddMSBError(0x80000007, "Win_UserBufferTooSmall", "Buffer is too small")
AddMSBError(0x80000010, "Win_CannotOpenPort", "Cannot open port")
AddMSBError(0x80000011, "Win_CannotGetCommState", "Cannot get comm state")
AddMSBError(0x80000012, "Win_CannotSetCommState", "Cannot set comm state")
AddMSBError(0x80000013, "Win_CannotCreateThread", "Cannot create thread")

# 协议错误
AddMSBError(0xE0000001, "Proto_Invalid", "Protocol invalid")
AddMSBError(0xE0000002, "Proto_Checksum", "CRC check failed")
AddMSBError(0xE0000003, "Proto_Timeout", "Protocol timeout")
AddMSBError(0xE0000004, "Proto_FrameTooLong", "Frame too long")
AddMSBError(0xE0000005, "Proto_UnknownCommand", "Unknown command")
AddMSBError(0xE0000006, "Proto_InvalidPayload", "Invalid payload")

# 状态错误
AddMSBError(0xF0000000, "State_NotRunning", "Not running, configure")
AddMSBError(0xF0000001, "State_Running", "Can not configure, already running")

# 配置错误
AddMSBError(0xC0000000, "Config_PortNumOver", "Port number over")
AddMSBError(0xC0000001, "Config_SerialNumOver", "Serial port number over")
AddMSBError(0xC0000002, "Config_CANNumOver", "CAN number over")
AddMSBError(0xC0000010, "Config_UnknownPortType", "Unknown Port Type")

# 串口错误
AddMSBError(0x01000001, "Serial_OpenFail", "Serial open failed")
AddMSBError(0x01000002, "Serial_NotOpen", "Serial not open")
AddMSBError(0x01000003, "Serial_ReadFail", "Serial read failed")
AddMSBError(0x01000004, "Serial_WriteFail", "Serial write failed")
AddMSBError(0x01000005, "Serial_Busy", "Serial is busy")

# CAN 错误
AddMSBError(0x02000000, "CAN_DataError", "CAN data error")
AddMSBError(0x02000001, "CAN_SendFail", "CAN send failed")
AddMSBError(0x02000002, "CAN_RecvFail", "CAN receive failed")
AddMSBError(0x02000003, "CAN_BufferFull", "CAN buffer full")
AddMSBError(0x02000004, "CAN_NotInit", "CAN not initialized")

# 端口错误
AddMSBError(0x10000001, "Port_WriteBusy", "Port is busy now")

# MCU 错误
AddMSBError(0x00010001, "MCU_Unknown", "MCU unknown error")
AddMSBError(0x00010002, "MCU_IOSizeError", "IO Should be 4 bytes")
AddMSBError(0x00010010, "MCU_OverTemperature", "MCU over temperature")

# =============================
# 4. 生成 C 头文件
# =============================


def generate_c_include(output_file):
    max_len = max(len(f"MSB_Error_{err.name}") for err in ERRORS)
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write("#ifndef MCU_ERROR_C_H\n")
        f.write("#define MCU_ERROR_C_H\n\n")
        f.write("// Auto-generated C header for MCU Serial Bridge Errors\n\n")

        f.write("#include <stdint.h>\n\n")
        f.write("#ifdef __cplusplus\n")
        f.write("extern \"C\" {\n")
        f.write("#endif\n\n")
        f.write("typedef enum {\n")
        for err in ERRORS:
            name = f"MSB_Error_{err.name}".ljust(max_len)
            f.write(f"    {name} = 0x{err.code:08X}, // {err.desc}\n")
        f.write("} MCUSerialBridgeError;\n\n")

        f.write("#ifdef __cplusplus\n")
        f.write("} // extern \"C\"\n")
        f.write("#endif\n")
        
        f.write("#endif // MCU_ERROR_C_H\n")
    print(f"[INFO] C header generated: {output_file}")

# =============================
# 5. 生成 C# 枚举文件
# =============================


def generate_cs_include(output_file):
    max_len = max(len(f"MSB_Error_{err.name}") for err in ERRORS)
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write("using System;\n\n")
        f.write("namespace MCUSerialBridgeCLR\n{\n")
        f.write("    public enum MCUSerialBridgeError : uint\n    {\n")
        for err in ERRORS:
            f.write(f"        {err.name} = 0x{err.code:08X}, // {err.desc}\n")
        f.write("    }\n\n")
        f.write("    public static class MCUSerialBridgeErrorExtensions\n    {\n")
        f.write(
            "        public static string ToDescription(this MCUSerialBridgeError err)\n        {\n")
        f.write("            return err switch\n            {\n")
        for err in ERRORS:
            f.write(
                f"                MCUSerialBridgeError.{err.name} => \"{err.name}|{err.desc}\",\n")
        f.write("                _ => \"Unknown Error\",\n")
        f.write("            };\n")
        f.write("        }\n")
        f.write("    }\n")
        f.write("}\n")
    print(f"[INFO] C# file generated: {output_file}")


# =============================
# 6. 脚本入口
# =============================
if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python gen_errors.py <c|cs> <output_file>")
        sys.exit(1)

    target, output_file = sys.argv[1], sys.argv[2]

    if target.lower() == "c":
        generate_c_include(output_file)
    elif target.lower() == "cs":
        generate_cs_include(output_file)
    else:
        print("[ERROR] Unknown target, use 'c' or 'cs'")
        sys.exit(1)
