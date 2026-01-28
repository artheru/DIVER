# MCU Serial Bridge 工程

本工程实现了一个**高速、可靠的 MCU 与上位机（PC）双向通信桥接系统**，主要用于将 MCU 侧的多种外设数据（串口、CAN、IO 等）统一通过一根高速串口透传到上位机，同时支持上位机向下配置、控制和写入数据。  
适用于 AGV、小型机器人、工业自动化控制等对实时性、稳定性要求较高的嵌入式场景。

系统分为 **MCU 固件端** 和 **PC 上位机端** 两大部分，形成完整的端到端通信链路，最高支持 2Mbps 波特率。

---

## 工程组成概览

工程包含三个相对独立但紧密协作的模块：

1. **MCU 固件（mcu 目录）**
   运行在裸机或 RTOS 的 MCU 上，负责：

   * 与真实外设（串口、CAN、GPIO）交互
   * 根据上位机指令完成配置、读写操作
   * 将外设产生的数据打包上报
   * 实现协议封装与高速串口收发

2. **C 核心库（c_core 目录）**
   上位机侧最核心的纯 C 实现，是整个 PC 端系统的“地基”。不依赖任何 .NET 或 STL，具有极高的可移植性与性能。
   主要功能：

   * 打开/管理 Windows 串口
   * 完整的协议粘包拆包、校验、解析逻辑
   * 三线程模型（接收线程、解析线程、发送线程）
   * 高性能无锁环形队列
   * 错误处理与超时机制

3. **C# 封装层（wrapper 目录）**
   使用 **P/Invoke** 直接调用 C 核心库 DLL（`mcu_serial_bridge.dll`），封装为面向对象的 C# API：

   * 提供 `MCUSerialBridge` 类，管理句柄生命周期
   * 提供 `MCUSerialBridgeError` 枚举、`VersionInfo`、`MCUState`、`PortConfig` 等结构
   * 封装 `ReadSerial` / `WriteSerial` / `ReadCAN` / `WriteCAN` 等操作
   * 可直接在 C# WPF、WinForms 或控制台程序中使用
* 路径示例：`wrapper/MCUSerialBridgeCLR.cs`、`wrapper/MCUSerialBridgeError.cs`、`wrapper/Test.cs`
* 构建方式：使用 `dotnet build`（已迁移为 csproj）

---

## 整体数据流

```text
MCU 固件
   │
   │ 高速串口（最高 2Mbps）
   ▼
c_core（纯 C：串口读写 + 协议解析 + 多线程队列）
   │
   ▼
wrapper（C# P/Invoke 封装）
   │
   ▼
C# 上位机应用（可直接调用 MCUSerialBridge 类）
```

---

## 工程目录结构（简化）

```text
.
├── mcu/          # MCU 固件完整工程
├── c_core/       # 纯 C 核心库（include + src）
├── wrapper/      # C# P/Invoke 封装层，输出可直接引用的类
├── SConstruct    # SCons 顶层构建脚本
├── README.md
└── .gitignore
```

---

## 各模块详细职责

### 1. MCU 固件（mcu）

* 负责与真实硬件直接交互（RS232/485、CAN、GPIO 等）
* 接收上位机下发的配置、复位、写数据指令并执行
* 主动采集或被动接收外设数据，打包后通过高速串口上报给上位机
* 实现完整的协议封装（帧头、长度、校验、帧尾）
* 支持响应确认机制（对需要确认的指令返回相同 Sequence 的响应）

### 2. C 核心库（c_core）—— PC 端核心

* **高性能**：三线程分离（接收、解析、发送），避免阻塞
* **抗粘包/拆包**：完整状态机处理任意拆分与合并的字节流
* **无锁队列**：单生产者-单消费者环形缓冲区，保证线程安全且零拷贝
* **跨平台潜力**：当前实现 Windows 串口 API，后续可轻松移植到 Linux
* **不依赖任何托管环境**：可在纯 C 程序、DLL、甚至嵌入式上位机中使用

### 3. C# 封装层（wrapper）

* 直接 P/Invoke 调用 `mcu_serial_bridge.dll`，封装为面向对象 C# API
* wrapper 目录已迁移为 `csproj` 工程（不再由 SCons 编译 C#）
* 管理底层句柄生命周期（Open/Close）
* 提供串口与 CAN 的读写方法：`ReadSerial`、`WriteSerial`、`ReadCAN`、`WriteCAN`
* 提供端口配置、MCU 状态、版本信息等结构体
* 提供完整的错误码枚举 `MCUSerialBridgeError`，并可直接 `ToString()`

### 4. C# 示例 / 测试

* 可以在 `wrapper/Test.cs` 中看到最简使用示例
* 打开 MCU → 配置端口 → 读写数据 → 关闭句柄

## MCU 协议设计（精简但完整说明）

### 帧结构（固定格式，抗误码）

```text
BB AA | Len(2) | LenRev(2) | Payload(N) | CRC16(2) | EE EE
```

* **Header**：固定 `BB AA`
* **PayloadLen**：小端序 2 字节，表示 Payload 长度
* **PayloadLenRev**：冗余校验字段 = byte_swap(~PayloadLen)，用于快速检测长度错误
* **CRC16**：Modbus 标准 CRC，仅覆盖 Payload，低字节在前
* **End**：固定 `EE EE`

### Payload 结构

```text
CommandType(1) | Sequence(4) | Timestamp(4) | ErrorCode(4) | OtherData(...)
```

### CommandType（当前命令集）

#### PC → MCU 请求命令

| 命令名称              | 值   | 说明                                                                 |
|-----------------------|------|----------------------------------------------------------------------|
| CommandConfigure      | 0x01 | 配置端口（串口/CAN），MCU 执行后返回确认响应（0x81，无额外数据）   |
| CommandReset          | 0x02 | 复位 MCU，MCU 立即返回确认响应（0x82，无额外数据），随后延迟约 100~200ms 执行复位 |
| CommandState          | 0x03 | 读取 MCU 状态，MCU 返回响应（0x83，同 seq），携带 4 字节 MCUState |
| CommandVersion        | 0x04 | 读取 MCU 版本信息，MCU 返回响应（0x84，同 seq），携带 VersionInfo 结构 |
| CommandEnableWireTap  | 0x05 | 启用 Wire Tap 模式（DIVER 模式下也上传端口数据），MCU 返回确认响应（0x85，同 seq） |
| CommandStart          | 0x0F | 启动 MCU 运行（DIVER 模式或透传模式），MCU 返回确认响应（0x8F，同 seq） |
| CommandWritePort      | 0x10 | 向指定端口下发数据（串口/CAN），MCU 执行后返回确认响应（0x90，同 seq） |
| CommandWriteOutput    | 0x30 | 写 MCU IO 输出（4 字节），MCU 执行后返回确认响应（0xB0，同 seq） |
| CommandReadInput      | 0x40 | 请求读取 MCU IO 输入，MCU 返回响应（0xC0，同 seq），携带 4 字节输入数据 |
| CommandProgram        | 0x50 | 下载程序到 MCU。数据长度为 0 时切换到透传模式；非 0 时切换到 DIVER 模式并加载程序（支持分片传输），MCU 返回确认响应（0xD0，同 seq） |
| CommandMemoryUpperIO  | 0x60 | PC → MCU 内存交换（UpperIO，DIVER 模式下的输入变量），MCU 返回确认响应（0xE0，同 seq） |

#### MCU → PC 上报/响应命令

| 命令名称              | 值        | 说明                                                                 |
|-----------------------|-----------|----------------------------------------------------------------------|
| CommandUploadPort     | 0x20      | MCU 上报端口接收到的外设数据（透传模式或 Wire Tap 启用时），无需确认，seq = 0 |
| CommandMemoryLowerIO  | 0x70      | MCU → PC 内存交换上报（LowerIO，DIVER 模式下的输出变量），无需确认，seq = 0 |
| 响应命令              | 0x80-0xEF | 响应命令格式：0x80 \| 请求命令（如 0x81 = 响应 0x01） |
| CommandError          | 0xFF      | 双向：致命错误上报，seq = 0，通常携带错误码                         |

### PortType（端口类型）

| 类型              | 值    | 说明                 |
|-------------------|-------|----------------------|
| PortType_Serial   | 0x01  | RS232 / RS485 串口   |
| PortType_CAN      | 0x02  | CAN 总线             |
| PortType_LED      | 0x03  | LED 灯条（暂未实现） |

### 端口配置结构体（统一 16 字节对齐）

* 串口配置：port_type + baud + receive_frame_ms（接收帧超时） + 7字节保留
* CAN 配置：port_type + baud + retry_time_ms（自动重试间隔） + 7字节保留

### MCU 运行模式

MCU 支持两种运行模式：

1. **Bridge 模式（透传模式）**：MCU 作为透明桥接，将端口数据直接透传给 PC，PC 也可以直接向端口写入数据。
   * 通过 `CommandProgram` 发送空程序（数据长度为 0）进入此模式
   * 端口数据通过 `CommandUploadPort` 自动上报

2. **DIVER 模式（程序运行模式）**：MCU 运行下载的程序，执行 DIVER 虚拟机逻辑。
   * 通过 `CommandProgram` 发送非空程序进入此模式
   * PC 通过 `CommandMemoryUpperIO` 向 MCU 发送输入变量（UpperIO）
   * MCU 通过 `CommandMemoryLowerIO` 向 PC 上报输出变量（LowerIO）
   * 默认情况下，DIVER 模式下端口数据不上报，除非启用 Wire Tap 模式

### 程序下载（分片传输）

`CommandProgram` 支持大程序的分片传输：

* 使用 `ProgramPacket` 结构：`total_len`（总长度）、`offset`（当前偏移）、`chunk_len`（分片长度）、`data[]`（分片数据）
* MCU 会缓存所有分片，直到接收完整程序后标记为已加载
* 首个分片的 `offset` 必须为 0

---

## 错误码设计（32 位统一）

| 高位范围   | 含义                   | 典型场景                     |
|------------|------------------------|------------------------------|
| 0x8xxxxxxx | Windows/上位机侧错误   | 打开串口失败、内存不足       |
| 0xExxxxxxx | 协议层错误             | CRC 错、长度不匹配、粘包异常 |
| 0x01xxxxxx | MCU 串口相关错误       | 外设串口溢出、超时           |
| 0x02xxxxxx | MCU CAN 相关错误       | 发送失败、总线关闭           |
| 0x00xxxxxx | 保留扩展               | 未来自定义错误               |

---

## C 层主要 API（纯 C 接口）

提供以下核心函数，完整接口形式请参考头文件（返回值为 `MCUSerialBridgeError`，成功时为 `MSB_Error_OK`）：

* `msb_open` / `msb_close`：打开和关闭串口连接
* `msb_configure`：批量配置多个端口（串口/CAN）
* `msb_reset`：远程复位 MCU
* `msb_read_input` / `msb_write_output`：读写 4 字节 GPIO（输入/输出）
* `msb_read_port`：从指定端口按帧读取一包数据（带超时，非阻塞/阻塞可选）
* `msb_write_port`：向指定端口写入数据（带超时）

**简单使用示例**：

```c
msb_handle* handle = NULL;
MCUSerialBridgeError err = msb_open(&handle, "COM3", 2000000);
if (err == MSB_Error_OK) {
    msb_configure(handle, num_ports, ports, timeout);
    // ... 正常读写 ...
    msb_close(handle);
}
```

## .NET 层接口

核心托管类 `MCUSerialBridge`（实现 `IDisposable`）提供连接管理、MCU 控制、端口操作（串口/CAN）、GPIO 读写、DIVER 模式支持等功能。

**完整 API 文档请参考源代码**：`wrapper/MCUSerialBridgeCLR.cs`

**使用示例**：

* 透传模式：`wrapper/Test.cs`
* DIVER 模式：`wrapper/TestDIVER.cs`

---

## 构建与运行

完整构建步骤（包括 MCU 编译、烧录、PC 端编译、运行）请参考 **[BUILD.md](./BUILD.md)**，涵盖：

* 所需工具链（arm-none-eabi-gcc、pyOCD、SCons、MSVC、.NET SDK）
* 常见问题排查
