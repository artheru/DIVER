# CoralinkerSDK

CoralinkerSDK 是 DIVER 运行时的核心 SDK，提供 MCU 节点管理、Cart 对象序列化/反序列化、以及 UpperIO/LowerIO 数据交换功能。

## 架构概览

```
┌─────────────────────────────────────────────────────────────────┐
│             调用方 (CoralinkerHost / 纯 C# 应用)                │
└─────────────────────────────┬───────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DIVERSession (单例)                        │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ - 节点生命周期管理 (Add/Remove/Connect/Disconnect)          ││
│  │ - 数据交换 (LowerIO→UpperIO 自动回传)                       ││
│  │ - Cart 字段读写 (HostRuntime 变量存储)                      ││
│  └─────────────────────────────────────────────────────────────┘│
│                              │                                  │
│              ┌───────────────┼───────────────┐                  │
│              ▼               ▼               ▼                  │
│  ┌───────────────┐ ┌───────────────┐ ┌───────────────┐          │
│  │   MCUNode 1   │ │   MCUNode 2   │ │   MCUNode N   │          │
│  │ (wrap Bridge) │ │ (wrap Bridge) │ │ (wrap Bridge) │          │
│  └───────┬───────┘ └───────┬───────┘ └───────┬───────┘          │
└──────────┼─────────────────┼─────────────────┼──────────────────┘
           │                 │                 │
           ▼                 ▼                 ▼
   ┌───────────────┐  ┌───────────────┐  ┌───────────────┐
   │MCUSerialBridge│  │MCUSerialBridge│  │MCUSerialBridge│
   │   (Native)    │  │   (Native)    │  │   (Native)    │
   └───────────────┘  └───────────────┘  └───────────────┘
```

## 核心组件

### DIVERSession (单例)

全局会话管理器，负责：

- **节点管理**: AddNode / RemoveNode / GetNode / ClearNodes
- **生命周期**: Configure / ConnectAll / DisconnectAll / StartAll / StopAll
- **数据交换**: LowerIO 到达后自动触发 UpperIO 下发（后台线程发送）
- **兼容接口**: 仍保留 BroadcastUpperIO / SendUpperIO（可选手动）

```csharp
// 获取单例
var session = DIVERSession.Instance;

// 配置会话
// - AssemblyPath: 可选，包含 Cart 类型的程序集
// - 每个 Node 有自己的 ProgramBytes (.bin) 和 MetaJson (.bin.json)
session.Configure(new SessionConfiguration {
    AssemblyPath = "builds/current/LogicBuild.dll",  // 可选
    Nodes = new[] {
        new NodeConfiguration {
            NodeId = "node1",
            McuUri = "serial://name=COM3&baudrate=1000000",
            ProgramBytes = File.ReadAllBytes("generated/TestLogic.bin"),
            MetaJson = File.ReadAllText("generated/TestLogic.bin.json")
        },
        // 多个节点可以运行不同的 Logic
        new NodeConfiguration {
            NodeId = "node2",
            McuUri = "serial://name=COM4&baudrate=1000000",
            ProgramBytes = File.ReadAllBytes("generated/AnotherLogic.bin"),
            MetaJson = File.ReadAllText("generated/AnotherLogic.bin.json")
        }
    }
});

// 连接并启动
session.ConnectAll();
session.ConfigureAndProgramAll();
session.StartAll();

// 数据交换（LowerIO 到达后自动下发 UpperIO）
HostRuntime.SetCartVariable("node1", "inputA", 42);
HostRuntime.SetCartVariable("node1", "inputB", 100);
// 不需要手动调用 BroadcastUpperIO，LowerIO 到达后会自动下发
var result = HostRuntime.GetCartVariable<int>("node1", "result");

// 停止并断开
session.StopAll();
session.DisconnectAll();
```

### MCUNode

单个 MCU 节点的封装，包装 `MCUSerialBridge`：

```csharp
var node = new MCUNode("node1", "COM3");
node.ProgramBytes = File.ReadAllBytes("TestLogic.bin");
node.PortConfigs = HostRuntime.DefaultPortConfigs;

if (node.Connect()) {
    node.Configure();
    node.Program();
    node.Start();
    // ...
    node.Stop();
    node.Disconnect();
}
```

### HostRuntime (静态库)

Cart 对象的加载、变量存储、序列化/反序列化：

```csharp
// 创建 Cart 对象
var cart = HostRuntime.CreateCartTarget("/path/to/LogicBuild.dll");

// 解析字段元数据
var fields = HostRuntime.ParseMetaJson(metaJson);
HostRuntime.BindCartFields(cart, fields);

// 变量存储（无 Cart 对象也可用）
HostRuntime.SetCartVariable("node1", "inputA", 42);
HostRuntime.SetCartVariable("node1", "inputB", 100);
var value = HostRuntime.GetCartVariable<int>("node1", "result");

// 解析 LowerIO 时会自动跳过 iteration（前 4 字节）
// 并写入 HostRuntime 变量存储
```

### SerialPortResolver (静态类)

串口发现工具，仅负责查找端口名称，不负责打开端口：

```csharp
// 验证端口是否存在
string? port = SerialPortResolver.ResolveByName("COM3");

// 根据 VID/PID 查找
string[] ports = SerialPortResolver.ResolveByVidPid("1234", "5678");

// 列出所有可用端口
string[] all = SerialPortResolver.ListAllPorts();
```

### CartFieldInfo

字段元数据，描述 Cart 中每个字段的属性：

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | string | 字段名称 |
| `Offset` | int | 在 Cart 内存中的偏移 |
| `TypeId` | byte | 类型标识 (0=bool, 6=int, 8=float, ...) |
| `Flags` | byte | IO 标志: 0x01=UpperIO, 0x02=LowerIO, 0x00=Mutual |

**IO 标志说明**:

- `IsUpperIO` (0x01): Host -> MCU 方向
- `IsLowerIO` (0x02): MCU -> Host 方向  
- `IsMutual` (0x00): 双向交换

## 数据流

### UpperIO (Host → MCU)

```
Host:                           MCU:
 CartTarget                      vm_put_upper_memory()
     ↓                                ↑
 HostRuntime.SerializeUpperIO()      │
     ↓                                │
 byte[] upperData ─────────────────►  │
                    MCUSerialBridge
```

### LowerIO (MCU → Host)

```

说明：

- LowerIO 回调线程不可阻塞，DIVERSession 只设置标记
- UpperIO 由后台线程发送，避免卡住 LowerIO
MCU:                            Host:
 vm_get_lower_memory()           CartTarget
     ↓                                ↑
     │                    HostRuntime.DeserializeLowerIO()
     │                                ↑
     └──────────────────► byte[] lowerData
           MCUSerialBridge (callback)
```

## Wire Format

UpperIO/LowerIO 数据格式为 `[TypeId (1B)][Value (N bytes)]` 的序列：

| TypeId | 类型 | 值长度 |
|--------|------|--------|
| 0x00 | bool | 1 |
| 0x01 | byte | 1 |
| 0x02 | sbyte | 1 |
| 0x03 | char | 2 |
| 0x04 | short | 2 |
| 0x05 | ushort | 2 |
| 0x06 | int | 4 |
| 0x07 | uint | 4 |
| 0x08 | float | 4 |

**注意**: 数据只包含对应方向的字段:

- UpperIO 数据: 仅包含 `IsUpperIO` 或 `IsMutual` 的字段
- LowerIO 数据: 仅包含 `IsLowerIO` 或 `IsMutual` 的字段  
- LowerIO 数据前 4 字节为 `iteration`（int32），HostRuntime 会自动跳过并解析

## 编译产物命名

每个 Logic 类（如 `TestLogic`）编译后生成：

| 文件 | 说明 |
|------|------|
| `TestLogic.bin` | MCU 字节码 |
| `TestLogic.bin.json` | Cart 字段元数据（含 offset, typeid, flags） |
| `TestLogic.diver` | 中间表示（调试用） |
| `TestLogic.diver.map.json` | 源码映射（调试用） |

## 测试程序（Program.cs）

SDK 自带测试程序仅用于验证通信与变量同步：

- 启动后每 1 秒更新一次 `digital_output`
- 序列：`1 → 2 → 4 → ... → 1<<14 → 1` 循环
- UpperIO 由 LowerIO 驱动自动下发，无需手动发送

**示例 `TestLogic.bin.json`**：

```json
[
  {"field":"counter", "typeid":6, "offset":0, "flags":2},
  {"field":"inputA", "typeid":6, "offset":5, "flags":1},
  {"field":"inputB", "typeid":6, "offset":10, "flags":1},
  {"field":"result", "typeid":6, "offset":15, "flags":2}
]
```

## 文件结构

```
CoralinkerSDK/
├── DIVERSession.cs         # 单例会话管理器
├── MCUNode.cs              # MCU 节点封装
├── HostRuntime.cs          # Cart 序列化/反序列化
├── SerialPortResolver.cs   # 串口发现工具
├── CartFieldInfo.cs        # 字段元数据
├── NodeConfiguration.cs    # 节点配置
├── MCUSerialBridgeCLR.cs   # (链接) Native DLL 包装器
├── MCUSerialBridgeError.cs # (链接) 错误码定义
└── README.md               # 本文档
```

## 依赖

- `MCUSerialBridge`: Native DLL wrapper (`MCUSerialBridgeCLR.cs`)
- `Newtonsoft.Json`: JSON 解析
- `System.IO.Ports`: 串口枚举
- `System.Management`: Windows WMI (VID/PID 查找)
