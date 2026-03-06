# 串口与 CAN 通信

## 端口（port）参数

所有通信 API 的 `port` 参数是端口索引（从 0 开始）。端口列表由节点硬件 Layout 决定——添加节点时 MCU 自动上报可用端口的类型和名称。Web 界面的节点卡片上会显示每个端口的名称（如 "RS485-1"、"CAN-1"）和对应的 index。

端口顺序由硬件固件定义，通常串口在前、CAN 在后，但不同硬件可能不同。以 Web 界面显示为准。

## CAN

### 发送

```csharp
RunOnMCU.WriteCANMessage(int port, CANMessage message);
```

```csharp
RunOnMCU.WriteCANMessage(canPort, new CANMessage
{
    ID = 0x200 + nodeId,
    RTR = false,
    Payload = new byte[] { mode, b1, b2, b3, b4, cw0, cw1 }
});
```

### 接收

```csharp
CANMessage msg = RunOnMCU.ReadCANMessage(int port, int canId);
```

返回 `null` 表示该 CAN ID 没有新数据。每次调用读取最近一帧。

```csharp
var msg = RunOnMCU.ReadCANMessage(canPort, 0x180 + nodeId);
if (msg != null)
{
    ushort statusWord = (ushort)(msg.Payload[0] | (msg.Payload[1] << 8));
    int actualVelocity = msg.Payload[2] | (msg.Payload[3] << 8)
                       | (msg.Payload[4] << 16) | (msg.Payload[5] << 24);
}
```

### CANMessage 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `ID` | `ushort` | 11-bit 标准帧 ID（0x000 ~ 0x7FF） |
| `RTR` | `bool` | `false` = 数据帧 |
| `Payload` | `byte[]` | 0~8 字节 |
| `DLC` | `int`（只读） | = `Payload.Length` |

## 串口

### 发送

```csharp
RunOnMCU.WriteStream(byte[] payload, int port);
```

### 接收

```csharp
byte[] data = RunOnMCU.ReadStream(int port); // null = 无数据
```

## 数字 IO（Snapshot）

统一传入 4 字节（32 位）。`ReadSnapshot` 对应 32 路 DI，`WriteSnapshot` 对应 32 路 DO。实际硬件可能不满 32 路，此时只有前几个 bit 有效。

```csharp
byte[] inputs = RunOnMCU.ReadSnapshot();   // 4 字节，bit0~bit31 = DI0~DI31
RunOnMCU.WriteSnapshot(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // DO0 = 1，其余 = 0
```

## WireTap 抓包

CoralinkerHost 提供 WireTap 功能，可在 Web 界面上对任意端口开启 TX/RX 监听，实时查看收发报文。CAN 报文支持按 COB-ID 聚合查看，串口支持 Hex/Text 模式。可导出为 CSV。
