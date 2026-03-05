# MCU Bootloader 固件升级协议文档

本文档详细描述 MCU Bootloader 的工作模式、通讯协议、字节级帧格式、UPG 文件格式和上位机 API，以便在其他工程中独立实现固件升级功能。

---

## 1. 工作模式

### 1.1 通讯模型

采用**一问一答**模式（类似 Modbus）：

- 上位机（PC）发送**请求帧**，下位机（MCU）返回**响应帧**
- 每次通讯严格遵循 "发一帧、收一帧" 的顺序
- 帧长度**固定 92 字节**（请求帧和响应帧长度相同）

### 1.2 串口配置

| 参数     | 值           |
|----------|-------------|
| 数据位   | 8           |
| 校验位   | 无 (None)   |
| 停止位   | 1           |
| 流控     | 无          |
| DTR/RTS  | 禁用        |

### 1.3 候选波特率

当波特率未知时，按以下顺序自动探测：

| 优先级 | 波特率    |
|--------|----------|
| 1      | 460800   |
| 2      | 115200   |
| 3      | 1000000  |
| 4      | 230400   |

### 1.4 升级流程

```
┌─────────────────────────────────────────────────┐
│  1. mbl_open()     打开串口（指定波特率或自动探测）  │
│  2. CommandRead     读取下位机当前固件信息           │
│  3. 版本比对         UPG 文件 vs 下位机信息          │
│  4. CommandErase    擦除固件（传入新固件元信息）      │
│  5. WriteFirmware   分块写入固件（每块 64 字节）     │
│  6. CommandRead     再次读取，验证 CRC 和长度        │
│  7. CommandExit     退出 Bootloader，重启进入 App   │
│  8. mbl_close()    关闭串口                        │
└─────────────────────────────────────────────────┘
```

---

## 2. 通讯协议

### 2.1 帧结构（固定 92 字节）

所有多字节字段均为**小端序 (Little-Endian)**。

```
偏移(字节)  长度    字段            说明
─────────────────────────────────────────────────
0x00        2      Header          帧头，固定 0xAA 0xBB
0x02        4      CommandType     命令/响应类型（uint32, LE）
0x06        80     Payload         数据载荷（不足部分填 0x00）
0x56        4      CRC32           校验码（uint32, LE）
0x5A        2      Tail            帧尾，固定 0xEE 0xEE
─────────────────────────────────────────────────
总计        92 字节
```

**CRC32 校验范围**：`CommandType + Payload`，即偏移 `0x02` 到 `0x55`（共 84 字节）。

CRC32 多项式：`0xEDB88320`（与 Python `zlib.crc32` 兼容），初始值 `0xFFFFFFFF`，最终异或 `0xFFFFFFFF`。

### 2.2 命令类型（PC → MCU）

| 命令          | CommandType  | 说明                       |
|--------------|-------------|---------------------------|
| `CMD_READ`   | 0x00000001  | 读取下位机固件信息            |
| `CMD_ERASE`  | 0x00000002  | 擦除固件（附带新固件元信息校验） |
| `CMD_WRITE`  | 0x00000003  | 写入固件数据（分块）           |
| `CMD_EXIT`   | 0x00000004  | 退出 Bootloader，重启进入 App |

### 2.3 响应类型（MCU → PC）

| 响应              | CommandType | 说明          |
|------------------|-------------|--------------|
| `RSP_READ_OK`    | 0x11        | Read 成功     |
| `RSP_ERASE_OK`   | 0x12        | Erase 成功    |
| `RSP_WRITE_OK`   | 0x13        | Write 成功    |
| `RSP_EXIT_OK`    | 0x14        | Exit 成功     |
| `RSP_READ_ERR`   | 0x81        | Read 失败     |
| `RSP_ERASE_ERR`  | 0x82        | Erase 失败    |
| `RSP_WRITE_ERR`  | 0x83        | Write 失败    |
| `RSP_EXIT_ERR`   | 0x84        | Exit 失败     |

---

## 3. 波特率自动探测协议

### 3.1 同步帧

| 方向       | 数据（4 字节）          |
|-----------|----------------------|
| PC → MCU  | `0xAA 0x55 0xA5 0xA5` |
| MCU → PC  | `0x55 0xAA 0x5A 0x5A` |

### 3.2 探测流程

1. 按优先级遍历候选波特率
2. 以当前波特率打开串口
3. 发送同步帧 `AA 55 A5 A5`
4. 等待响应（100ms 超时），期望收到 `55 AA 5A 5A`
5. 每个波特率最多尝试 2 次
6. 匹配成功则确认波特率，失败则关闭串口尝试下一个

---

## 4. 各命令 Payload 详细格式

### 4.1 CMD_READ（读取固件信息）

**请求 Payload**：无数据（80 字节全 0）。

**成功响应 Payload**（`RSP_READ_OK`, CommandType = 0x11）：

```
偏移    长度    类型        字段            说明
────────────────────────────────────────────────────────────
0x00    16     char[16]    PDN             产品型号
0x10    8      char[8]     Tag             标签版本
0x18    8      char[8]     Commit          Git Commit
0x20    24     char[24]    BuildTime       编译时间
0x38    4      uint32      AppLength       固件长度（字节）
0x3C    4      uint32      AppCRC32        固件 CRC32
0x40    4      uint32      AppInfoCRC32    固件信息区 CRC32
0x44    4      int32       IsValid         固件有效性（1=有效, 0=无效）
0x48    8      -           (reserved)      保留
────────────────────────────────────────────────────────────
总计    80 字节
```

### 4.2 CMD_ERASE（擦除固件）

**请求 Payload**：

```
偏移    长度    类型        字段            说明
────────────────────────────────────────────────────────────
0x00    8      byte[8]     Tag             新固件标签版本
0x08    8      byte[8]     Commit          新固件 Git Commit
0x10    24     byte[24]    BuildTime       新固件编译时间
0x28    4      uint32      AppLength       新固件总长度
0x2C    4      uint32      AppCRC32        新固件 CRC32
0x30    32     byte[32]    First32Bytes    加密固件的前 32 字节
────────────────────────────────────────────────────────────
总计    80 字节
```

**成功响应**：`RSP_ERASE_OK` (0x12)，Payload 无特定数据。

**超时建议**：10000 ~ 15000ms（Flash 擦除操作耗时较长）。

### 4.3 CMD_WRITE（写入固件数据块）

**请求 Payload**：

```
偏移    长度    类型        字段            说明
────────────────────────────────────────────────────────────
0x00    4      uint32      Offset          当前块在固件中的偏移
0x04    4      uint32      TotalLength     固件总长度
0x08    4      uint32      ChunkLength     当前块长度（最大 64）
0x0C    64     byte[64]    ChunkData       当前块数据（不足 64 补 0）
0x4C    4      -           (reserved)      保留
────────────────────────────────────────────────────────────
总计    80 字节
```

**成功响应**：`RSP_WRITE_OK` (0x13)，Payload 无特定数据。

**分块规则**：
- 每次最多写入 **64 字节**
- 从偏移 0 开始顺序写入
- 最后一块长度可小于 64 字节
- 写入完毕后 MCU 会自动校验固件 CRC

**超时建议**：1000ms。

### 4.4 CMD_EXIT（退出 Bootloader）

**请求 Payload**：无数据（80 字节全 0）。

**成功响应**：`RSP_EXIT_OK` (0x14)，MCU 随后重启进入 App。

**超时建议**：1000ms。

### 4.5 错误响应 Payload

当 MCU 返回 `RSP_xxx_ERR` 时，Payload 前 4 字节为 MCU 错误码：

```
偏移    长度    类型        字段            说明
────────────────────────────────────────────────────────────
0x00    4      uint32      ErrorCode       MCU 端错误码
0x04    76     char[76]    ErrorMessage    错误消息（可选）
────────────────────────────────────────────────────────────
```

---

## 5. 错误码定义

### 5.1 上位机/Windows 错误（0x8xxxxxxx）

| 错误码       | 名称                | 说明          |
|-------------|--------------------|--------------| 
| 0x80000001  | Win_OpenFailed     | 打开串口失败    |
| 0x80000002  | Win_ConfigFailed   | 串口配置失败    |
| 0x80000003  | Win_WriteFailed    | 串口写入失败    |
| 0x80000004  | Win_ReadFailed     | 串口读取失败    |
| 0x80000005  | Win_InvalidParam   | 无效参数       |
| 0x80000006  | Win_HandleNotFound | 句柄无效       |
| 0x80000007  | Win_OutOfMemory    | 内存分配失败    |
| 0x80000008  | Win_Timeout        | 操作超时       |
| 0x80000009  | Win_ProbeFailed    | 波特率探测失败  |
| 0x8000000A  | Win_AlreadyOpen    | 串口已打开     |
| 0x8000000B  | Win_NotOpen        | 串口未打开     |

### 5.2 协议层错误（0xExxxxxxx）

| 错误码       | 名称                    | 说明           |
|-------------|------------------------|---------------|
| 0xE0000001  | Proto_HeaderError      | 帧头错误       |
| 0xE0000002  | Proto_TailError        | 帧尾错误       |
| 0xE0000003  | Proto_CRCError         | CRC32 校验失败 |
| 0xE0000004  | Proto_LengthError      | 帧长度错误     |
| 0xE0000005  | Proto_UnknownResponse  | 未知响应类型   |
| 0xE0000006  | Proto_ResponseMismatch | 响应类型不匹配 |

### 5.3 MCU 端错误（0x0F0000xx）

| 错误码       | 名称                          | 说明              |
|-------------|------------------------------|------------------|
| 0x0F000001  | MCU_UnknownCommand           | 未知命令          |
| 0x0F000002  | MCU_InvalidPayload           | 无效 Payload      |
| 0x0F000003  | MCU_FlashEraseFailed         | Flash 擦除失败    |
| 0x0F000004  | MCU_FirmwareDecryptionError  | 固件解密错误      |
| 0x0F000005  | MCU_FirmwareLengthError      | 固件长度错误      |
| 0x0F000006  | MCU_NotErased                | 未擦除           |
| 0x0F000007  | MCU_WriteOffsetMisaligned    | 写入偏移未对齐    |
| 0x0F000008  | MCU_WriteLengthTooLong       | 写入长度过长      |
| 0x0F000009  | MCU_WriteError               | 写入错误          |
| 0x0F00000A  | MCU_WriteFirmwareCrcMismatch | 固件 CRC 不匹配   |
| 0x0F00000B  | MCU_WriteAppInvalid          | App 无效          |

---

## 6. UPG 文件格式

UPG 文件是固件升级的封装格式，包含固件元信息和加密固件数据。

### 6.1 文件头结构

```
偏移      长度    类型        字段                  说明
──────────────────────────────────────────────────────────────────────
0x0000    4      uint32      CRC32All             整体 CRC32（校验 0x04 到文件末尾）
0x0004    16     char[16]    ProductName          产品型号
0x0014    4      uint32      BLStructureVersion   固件格式版本（必须为 1）
0x0018    8      byte[8]     FirmwareTag          固件标签版本
0x0020    8      byte[8]     FirmwareCommit       固件 Git Commit
0x0028    24     byte[24]    BuildTime            编译时间
0x0040    4      uint32      SectionNumber        段数（当前只支持 1）
0x0044    4      uint32      SectionUPGAddress    段在 UPG 文件中的偏移
0x0048    4      uint32      SectionLength        段长度（固件大小）
0x004C    4      uint32      SectionFlashAddress  段 Flash 地址
0x0050    4      uint32      SectionCRC           段 CRC32
0x0054    ...    -           (reserved)           保留区域
──────────────────────────────────────────────────────────────────────
```

### 6.2 固件数据区

```
偏移      长度              说明
──────────────────────────────────────────────────────────────────────
0x0100    32               加密头（前 32 字节，传给 Erase 命令的 First32Bytes）
0x0120    SectionLength    加密固件数据（写入 MCU 的实际数据）
──────────────────────────────────────────────────────────────────────
```

- **Erase 命令**需要传入 `0x0100` 开始的前 32 字节
- **Write 命令**需要传入 `0x0120` 开始的 `SectionLength` 字节

---

## 7. CRC32 算法

所有 CRC32 计算使用相同的标准算法（与 Python `zlib.crc32` 兼容）：

```
多项式：  0xEDB88320（反向表示）
初始值：  0xFFFFFFFF
最终异或：0xFFFFFFFF
```

伪代码：

```python
def crc32(data: bytes) -> int:
    crc = 0xFFFFFFFF
    for byte in data:
        crc = crc32_table[(crc ^ byte) & 0xFF] ^ (crc >> 8)
    return crc ^ 0xFFFFFFFF
```

Python 可直接使用 `zlib.crc32(data) & 0xFFFFFFFF`。

---

## 8. C API 参考

原始 C 库导出函数（DLL 名称：`mcu_serial_bridge.dll`）：

```c
// 打开串口（baud=0 自动探测）
MCUBootloaderError mbl_open(mbl_handle** handle, const char* port, uint32_t baud);

// 关闭串口
MCUBootloaderError mbl_close(mbl_handle* handle);

// 获取当前波特率
uint32_t mbl_get_baudrate(mbl_handle* handle);

// 注册进度回调
MCUBootloaderError mbl_register_progress_callback(
    mbl_handle* handle,
    mbl_progress_callback_t callback,  // void(int progress, MCUBootloaderError error, void* ctx)
    void* user_ctx);

// 读取下位机固件信息
MCUBootloaderError mbl_command_read(mbl_handle* handle, MBL_FirmwareInfo* info, uint32_t timeout_ms);

// 擦除固件
MCUBootloaderError mbl_command_erase(mbl_handle* handle, const MBL_EraseParams* params, uint32_t timeout_ms);

// 写入固件数据块（单次最多 64 字节）
MCUBootloaderError mbl_command_write(
    mbl_handle* handle,
    uint32_t offset, uint32_t total_length,
    const uint8_t* chunk_data, uint32_t chunk_length,
    uint32_t timeout_ms);

// 退出 Bootloader
MCUBootloaderError mbl_command_exit(mbl_handle* handle, uint32_t timeout_ms);

// 便捷：自动分块写入完整固件（每块 64 字节，触发进度回调）
MCUBootloaderError mbl_write_firmware(
    mbl_handle* handle,
    const uint8_t* firmware, uint32_t firmware_len,
    uint32_t timeout_ms);
```

---

## 9. C# 封装 API 参考

C# 封装位于 `MCUBootloaderCLR` 命名空间，通过 P/Invoke 调用底层 C DLL。

### 9.1 MCUBootloaderHandler 类

```csharp
public class MCUBootloaderHandler : IDisposable
{
    bool IsOpen { get; }
    uint Baudrate { get; }

    MCUBootloaderError Open(string port, uint baud = 0);
    MCUBootloaderError Close();
    MCUBootloaderError RegisterProgressCallback(Action<int, MCUBootloaderError> callback);
    MCUBootloaderError CommandRead(out FirmwareInfo info, uint timeout = 1000);
    MCUBootloaderError CommandErase(UPGFile upgFile, uint timeout = 10000);
    MCUBootloaderError CommandWrite(uint offset, uint totalLength, byte[] chunkData, uint timeout = 1000);
    MCUBootloaderError CommandExit(uint timeout = 1000);
    MCUBootloaderError WriteFirmware(UPGFile upgFile, uint timeout = 1000);
    MCUBootloaderError UpgradeFirmware(UPGFile upgFile, uint eraseTimeout = 10000, uint writeTimeout = 1000);
}
```

### 9.2 UPGFile 类

```csharp
public class UPGFile
{
    UPGFile(string path);         // 从文件路径加载
    UPGFile(byte[] data);         // 从字节数组加载

    string ProductName { get; }
    string FirmwareTag { get; }
    string FirmwareCommit { get; }
    string BuildTime { get; }
    uint FirmwareSize { get; }
    uint SectionCRC { get; }
    byte[] EncryptedData { get; } // 前 32 字节是加密头，之后是固件数据
}
```

### 9.3 典型使用示例

```csharp
// 1. 解析 UPG 文件
var upg = new UPGFile("firmware.upg");

// 2. 打开连接（自动探测波特率）
using var bl = new MCUBootloaderHandler();
bl.Open("COM3", 0);

// 3. 读取下位机信息
bl.CommandRead(out FirmwareInfo info, 2000);

// 4. 注册进度回调
bl.RegisterProgressCallback((progress, error) => {
    Console.WriteLine($"Progress: {progress}%, Error: {error}");
});

// 5. 擦除
bl.CommandErase(upg, 15000);

// 6. 写入固件
bl.WriteFirmware(upg, 1000);

// 7. 验证
bl.CommandRead(out FirmwareInfo newInfo, 2000);

// 8. 退出 Bootloader
bl.CommandExit(1000);
```

---

## 10. 移植指南

如需在其他工程中实现固件升级功能，需要实现以下关键部分：

1. **串口通讯层**：打开/配置/读写串口，支持超时
2. **帧编解码**：按 92 字节固定帧格式组包/解包
3. **CRC32 计算**：标准 CRC32（0xEDB88320）
4. **UPG 文件解析**：读取文件头元信息和加密固件数据
5. **命令流程**：按 Read → Erase → Write(循环) → Read(验证) → Exit 的顺序执行
6. **波特率探测**（可选）：发送同步帧探测可用波特率

若无需处理原始字节帧，可直接引用 `mcu_serial_bridge.dll` 并通过 P/Invoke 或 FFI 调用 C API。
