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
   * 路径示例：`wrapper/MCUSerialBridge.cs`、`wrapper/MCUSerialBridgeError.cs`、`wrapper/Test.cs`

---
## 整体数据流

```
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

```
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
* 管理底层句柄生命周期（Open/Close）
* 提供串口与 CAN 的读写方法：`ReadSerial`、`WriteSerial`、`ReadCAN`、`WriteCAN`
* 提供端口配置、MCU 状态、版本信息等结构体
* 提供完整的错误码枚举 `MCUSerialBridgeError`，并可直接 `ToString()`

### 4. C# 示例 / 测试

* 可以在 `wrapper/Test.cs` 中看到最简使用示例
* 打开 MCU → 配置端口 → 读写数据 → 关闭句柄

## MCU 协议设计（精简但完整说明）

### 帧结构（固定格式，抗误码）
```
BB AA | Len(2) | LenRev(2) | Payload(N) | CRC16(2) | EE EE
```
- **Header**：固定 `BB AA`
- **PayloadLen**：小端序 2 字节，表示 Payload 长度
- **PayloadLenRev**：冗余校验字段 = byte_swap(~PayloadLen)，用于快速检测长度错误
- **CRC16**：Modbus 标准 CRC，仅覆盖 Payload，低字节在前
- **End**：固定 `EE EE`

### Payload 结构
```
CommandType(1) | Sequence(4) | Timestamp(4) | ErrorCode(4) | OtherData(...)
```

### CommandType（当前命令集）

| 命令名称            | 值    | 说明                                                                 |
|---------------------|-------|----------------------------------------------------------------------|
| CommandConfigure    | 0x01  | 上位机 → MCU：配置端口，MCU 执行后返回确认响应（0x81，无额外数据）   |
| CommandReset        | 0x02  | 上位机 → MCU：复位 MCU，MCU 立即返回确认响应（0x82，无额外数据），随后延迟约 200ms 执行复位 |
| CommandWritePort    | 0x10  | 上位机 → MCU：向指定端口下发数据，MCU 执行后返回确认响应（0x90，同 seq） |
| CommandUploadPort   | 0x20  | MCU → 上位机：MCU 上报端口接收到的外设数据（透传），无需确认，seq = 0 |
| CommandWriteOutput  | 0x30  | 上位机 → MCU：写 MCU IO 输出（4 字节），MCU 执行后返回确认响应（0xB0，同 seq） |
| CommandReadInput    | 0x40  | 上位机 → MCU：请求读取 MCU IO 输入，MCU 返回响应（0xC0，同 seq），携带 4 字节输入数据 |
| CommandError        | 0xFF  | 双向：致命错误上报，seq = 0，通常携带错误码                         |

### PortType（端口类型）
| 类型              | 值    | 说明                 |
|-------------------|-------|----------------------|
| Serial232And485   | 0x01  | RS232 / RS485        |
| CAN               | 0x02  | CAN 总线             |
| LEDStrip          | 0x03  | LED 灯条（暂未实现） |

### 端口配置结构体（统一 16 字节对齐）
- 串口配置：port_type + baud + receive_frame_ms（接收帧超时） + 7字节保留
- CAN 配置：port_type + baud + retry_time_ms（自动重试间隔） + 7字节保留

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

- `msb_open` / `msb_close`：打开和关闭串口连接
- `msb_configure`：批量配置多个端口（串口/CAN）
- `msb_reset`：远程复位 MCU
- `msb_read_input` / `msb_write_output`：读写 4 字节 GPIO（输入/输出）
- `msb_read_port`：从指定端口按帧读取一包数据（带超时，非阻塞/阻塞可选）
- `msb_write_port`：向指定端口写入数据（带超时）

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

核心托管类 `MCUSerialBridge`（实现 `IDisposable`）提供：

* 构造函数 `MCUSerialBridge()`：创建对象，但不自动打开串口
* `Open(string portName, uint baud)`：打开指定串口
* `Close()`：关闭串口
* `Configure(IEnumerable<PortConfig> ports)`：配置所有端口（支持 `SerialPortConfig` 和 `CANPortConfig`）
* `WriteOutput(byte[] data, uint timeout)`：写输出数据
* `ReadSerial(byte portIndex, out byte[] buffer, uint timeout)`
* `WriteSerial(byte portIndex, byte[] data, uint timeout)`
* `ReadCAN(byte portIndex, out CANMessage message, uint timeout)`
* `WriteCAN(byte portIndex, CANMessage message, uint timeout)`
* `GetState(out MCUState state)`
* `GetVersion(out VersionInfo version, uint timeout)`
* 属性 `IsOpen`：判断是否已打开

**错误枚举**：`MCUSerialBridgeError`，可直接 `ToDescription()` 获取描述

**C# 使用示例**：

直接参考`Test.cs`

---
## 构建与运行

完整构建步骤（包括 MCU 编译、烧录、PC 端编译、运行）请参考 **[BUILD.md](./BUILD.md)**，涵盖：
- 所需工具链（arm-none-eabi-gcc、pyOCD、SCons、MSVC、.NET SDK）
- 常见问题排查
