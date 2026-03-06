# Logic API 参考

本文档是编写 MCU 控制逻辑的完整 API 参考。所有逻辑代码以 C# 编写，经 DIVER 编译器编译为 MCU 字节码，运行在 MCU 虚拟机上（不是标准 .NET 运行时）。

完整类型定义见 [stubs/CartActivator.cs](stubs/CartActivator.cs)，可供 AI 辅助编码和 IDE 类型提示使用。

---

## 1. 基础结构

每个逻辑由两部分组成：**变量表**（CartDefinition）和**逻辑类**（LadderLogic）。

### 最小逻辑

```csharp
using CartActivator;

[LogicRunOnMCU(scanInterval = 1000)]
public class MyLogic : LadderLogic<CartDefinition>
{
    public override void Operation(int iteration)
    {
        Console.WriteLine($"Hello DIVER! iteration={iteration}");
    }
}
```

### 带变量表的逻辑

```csharp
using CartActivator;

public class MyCart : CartDefinition
{
    [AsUpperIO] public int targetSpeed;   // Host -> MCU（可控）
    [AsLowerIO] public int actualSpeed;   // MCU -> Host（上报）
}

[LogicRunOnMCU(scanInterval = 50)]
public class MyLogic : LadderLogic<MyCart>
{
    public override void Operation(int iteration)
    {
        cart.actualSpeed = cart.targetSpeed * 2;
    }
}
```

### 单文件多逻辑（多节点）

一个文件可定义多个逻辑类，分配到不同 MCU 节点运行。

**关键规则：所有 CartDefinition 中同名字段在运行时共享同一个变量。** 这意味着如果 `FrontCart` 和 `RearCart` 都有名为 `targetSpeed` 的字段，它们在 Host 变量面板中是同一个值。利用这一点可以实现跨节点数据共享；如果需要独立控制，请使用不同的字段名。

```csharp
using CartActivator;

public class FrontCart : CartDefinition
{
    [AsUpperIO] public float target_velocity_front;
    [AsLowerIO] public float actual_velocity_front;
    [AsLowerIO] public int front_stage;
}

public class RearCart : CartDefinition
{
    [AsUpperIO] public float target_velocity_rear;
    [AsLowerIO] public float actual_velocity_rear;
    [AsLowerIO] public int rear_stage;
}

[LogicRunOnMCU(scanInterval = 100)]
public class FrontLogic : LadderLogic<FrontCart>
{
    public override void Operation(int iteration) { /* ... */ }
}

[LogicRunOnMCU(scanInterval = 100)]
public class RearLogic : LadderLogic<RearCart>
{
    public override void Operation(int iteration) { /* ... */ }
}
```

**建议：不要让两个节点同时写同一个 LowerIO 字段。** 如果两个节点都给同名 LowerIO 赋值，后到的会覆盖先到的，导致数据混乱。用不同字段名区分（如 `actual_velocity_front` / `actual_velocity_rear`）。

---

## 2. 核心类型详解

### LogicRunOnMCUAttribute

标注在逻辑类上，声明该类运行在 MCU 上。

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `scanInterval` | `int` | 1000 | 扫描周期（毫秒），即 `Operation` 被调用的间隔 |
| `mcuUri` | `string` | `"default"` | 设备标识，一般不填，由 Host 分配节点 |

### LadderLogic\<T\>

逻辑基类。泛型参数 `T` 必须继承 `CartDefinition`。

| 成员 | 说明 |
|------|------|
| `T cart` | 变量表实例，通过它读写 UpperIO / LowerIO 字段 |
| `abstract void Operation(int iteration)` | 逻辑主入口，每个扫描周期调用一次 |

**iteration 的含义：** `iteration` 从 0 开始，每个扫描周期在 Host 与 MCU 通信正常时自动 +1。如果发生丢包，`iteration` 不会增加。因此可以通过检测 `iteration` 是否停滞来判断通信是否中断，用于安全逻辑（如通信丢失时紧急停车）。

逻辑类可以定义私有字段保存跨周期状态（如计数器、状态机阶段）。

### CartDefinition

变量表基类。字段必须为 `public`，并标注方向：

| 标注 | 方向 | 说明 |
|------|------|------|
| `[AsUpperIO]` | Host -> MCU | Host 侧可写，MCU 侧只读。用于控制量（目标速度、开关等） |
| `[AsLowerIO]` | MCU -> Host | MCU 侧可写，Host 侧只读。用于状态上报（实际速度、传感器值等） |

### CANMessage

CAN 帧结构：

| 字段 | 类型 | 说明 |
|------|------|------|
| `ID` | `ushort` | 标准帧 ID，11 位（0x000 ~ 0x7FF） |
| `RTR` | `bool` | `false` = 数据帧，`true` = 远程帧 |
| `Payload` | `byte[]` | 数据负载，0~8 字节 |
| `DLC` | `int`（只读） | 数据长度码，由 `Payload.Length` 推导 |

---

## 3. 支持的字段类型

CartDefinition 字段只能使用以下类型（MCU 虚拟机序列化协议限定）。

### 基础类型

| C# 类型 | TypeID | 大小 |
|---------|--------|------|
| `bool` | 0 | 1 byte |
| `byte` | 1 | 1 byte |
| `sbyte` | 2 | 1 byte |
| `char` | 3 | 2 bytes |
| `short` | 4 | 2 bytes |
| `ushort` | 5 | 2 bytes |
| `int` | 6 | 4 bytes |
| `uint` | 7 | 4 bytes |
| `float` | 8 | 4 bytes |

### 复合类型

| C# 类型 | TypeID | 说明 |
|---------|--------|------|
| `string` | 12 | UTF-8 编码，长度前缀为 ushort |
| 一维数组 | 11 | 元素类型必须是上表基础类型之一，如 `byte[]`、`int[]`、`float[]` |

### 不支持的类型

以下类型**不能**用于 CartDefinition 字段，使用会导致运行时异常：

`long`、`ulong`、`double`、`decimal`、自定义 class/struct、`List<T>`、`Dictionary<K,V>`、多维数组 `T[,]`、锯齿数组 `T[][]`。

---

## 4. 通信 API（RunOnMCU 静态类）

所有方法在 `Operation()` 内调用。`port` 参数是端口索引，由节点硬件 Layout 决定（添加节点时自动发现）。Web 界面上显示端口名称（如 "RS485-1"、"CAN-1"）及其对应 index。

### 4.1 CAN 总线

```csharp
RunOnMCU.WriteCANMessage(int port, CANMessage message);
CANMessage msg = RunOnMCU.ReadCANMessage(int port, int canId); // null = 无数据
```

发送示例：

```csharp
RunOnMCU.WriteCANMessage(canPort, new CANMessage
{
    ID = 0x601,
    RTR = false,
    Payload = new byte[] { 0x40, 0x41, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00 }
});
```

接收示例：

```csharp
var msg = RunOnMCU.ReadCANMessage(canPort, 0x581);
if (msg != null && msg.Payload != null)
{
    int statusWord = msg.Payload[0] | (msg.Payload[1] << 8);
}
```

### 4.2 串口

```csharp
RunOnMCU.WriteStream(byte[] payload, int port);
byte[] data = RunOnMCU.ReadStream(int port); // null = 无数据
```

### 4.3 底层 Event API

CAN 和 Serial 的底层接口。通常用 4.1/4.2 语法糖即可，仅特殊协议需要：

```csharp
RunOnMCU.WriteEvent(byte[] payload, int port, int event_id);
byte[] data = RunOnMCU.ReadEvent(int port, int event_id); // null = 无数据
```

### 4.4 数字 IO（Snapshot）

读写 MCU 数字输入/输出引脚。统一传入 4 字节（32 位），`ReadSnapshot` 对应 32 路 DI，`WriteSnapshot` 对应 32 路 DO。实际硬件可能不满 32 路，此时只有前几个 bit 有效。

```csharp
byte[] inputs = RunOnMCU.ReadSnapshot();   // 返回 4 字节，bit0~bit31 对应 DI0~DI31
RunOnMCU.WriteSnapshot(byte[] payload);    // 传入 4 字节，bit0~bit31 对应 DO0~DO31
```

### 4.5 时间

```csharp
int us = RunOnMCU.GetMicrosFromStart();  // 微秒
int ms = RunOnMCU.GetMillisFromStart();  // 毫秒
int s  = RunOnMCU.GetSecondsFromStart(); // 秒
```

从 MCU 上电开始的累计时间。

### 4.6 日志

```csharp
Console.WriteLine($"speed={cart.actualSpeed}");
```

输出到 CoralinkerHost 日志面板。建议仅在关键阶段打印，高频打印影响性能。

---

## 5. 编码约束

### 必须

- 文件顶部 `using CartActivator;`。
- 逻辑类继承 `LadderLogic<T>`，`T` 继承 `CartDefinition`。
- 逻辑类标注 `[LogicRunOnMCU]`。
- Override `public override void Operation(int iteration)`。
- CartDefinition 字段只用第 3 节列出的类型。

### 建议

- `Operation()` 内避免循环中 `new byte[]`，优先用类级别固定缓冲区复用。
- CAN Payload 长度 / 字段定义必须与设备端协议一致。
- `scanInterval` 低于 50ms 时需确保 `Operation()` 执行足够快。
- `[AsLowerIO]` 字段仅用于上报，不要在 Host 侧写入。
- 多节点场景下，不要让两个节点写同一个 LowerIO 字段名（后到覆盖先到）。
- 利用 `iteration` 停滞检测通信丢包，用于安全逻辑。
- 优先从 `examples/` 拷贝模板修改。

---

## 6. 常用编码模式

### 状态机

```csharp
private int stage = 0;

public override void Operation(int iteration)
{
    switch (stage)
    {
        case 0:
            // 初始化 / 发 NMT
            stage = 1;
            break;
        case 1:
            // 等待就绪
            var msg = RunOnMCU.ReadCANMessage(canPort, statusCobId);
            if (msg != null && IsReady(msg))
                stage = 2;
            break;
        case 2:
            // 正常控制循环
            SendTargetVelocity(cart.targetSpeed);
            ReadActualVelocity();
            break;
    }
}
```

### 定时执行（基于 iteration 分频）

```csharp
public override void Operation(int iteration)
{
    // scanInterval=50ms，每 20 次 = 1 秒执行一次
    if (iteration % 20 == 0)
    {
        Console.WriteLine($"1s heartbeat, speed={cart.actualSpeed}");
    }

    // 每个周期都执行
    ReadSensors();
    ControlLoop();
}
```

### 通信安全检测（基于 iteration）

`iteration` 仅在 Host-MCU 通信正常时递增，丢包时不增加。利用这一特性做安全逻辑：

```csharp
private int lastIteration = -1;
private int commLossCount = 0;

public override void Operation(int iteration)
{
    if (iteration == lastIteration)
    {
        commLossCount++;
        if (commLossCount > 10)
        {
            EmergencyStop();
            return;
        }
    }
    else
    {
        commLossCount = 0;
        lastIteration = iteration;
    }

    NormalControl();
}
```

### CAN 工具封装

```csharp
public static class CanHelper
{
    public static void WriteCANPayload(byte[] payload, int port, int canId)
    {
        RunOnMCU.WriteCANMessage(port, new CANMessage
        {
            ID = (ushort)canId,
            RTR = false,
            Payload = payload
        });
    }

    public static byte[] ReadCANPayload(int port, int canId)
    {
        var msg = RunOnMCU.ReadCANMessage(port, canId);
        return msg == null ? null : msg.Payload;
    }
}
```

### Modbus RTU 请求（串口）

```csharp
private byte[] modbusBuffer = new byte[8];

private void SendModbusRead(int port, byte slaveAddr, ushort startReg, ushort count)
{
    modbusBuffer[0] = slaveAddr;
    modbusBuffer[1] = 0x03;
    modbusBuffer[2] = (byte)(startReg >> 8);
    modbusBuffer[3] = (byte)(startReg & 0xFF);
    modbusBuffer[4] = (byte)(count >> 8);
    modbusBuffer[5] = (byte)(count & 0xFF);
    ushort crc = CalcCRC16(modbusBuffer, 6);
    modbusBuffer[6] = (byte)(crc & 0xFF);
    modbusBuffer[7] = (byte)(crc >> 8);
    RunOnMCU.WriteStream(modbusBuffer, port);
}
```
