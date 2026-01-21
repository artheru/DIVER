# CoralinkerSDK

DIVER 运行时核心 SDK，提供 MCU 节点管理、Cart 对象序列化/反序列化、UpperIO/LowerIO 数据交换。

## 架构

```
┌────────────────────────────────────────────────────────────┐
│              调用方 (CoralinkerHost / C# 应用)              │
└────────────────────────────┬───────────────────────────────┘
                             ▼
┌────────────────────────────────────────────────────────────┐
│                   DIVERSession (单例)                       │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ - 节点生命周期: Add/Remove/Connect/Disconnect        │  │
│  │ - 即时连接: AddAndConnectNode (Probe 后保持连接)      │  │
│  │ - 程序配置: Configure / ConfigureConnectedNodes      │  │
│  │ - 数据交换: LowerIO→UpperIO 自动回传                 │  │
│  │ - 状态轮询: 后台线程定期刷新节点状态                  │  │
│  └──────────────────────────────────────────────────────┘  │
│                             │                              │
│             ┌───────────────┼───────────────┐              │
│             ▼               ▼               ▼              │
│   ┌───────────────┐ ┌───────────────┐ ┌───────────────┐    │
│   │   MCUNode 1   │ │   MCUNode 2   │ │   MCUNode N   │    │
│   └───────┬───────┘ └───────┬───────┘ └───────┬───────┘    │
└───────────┼─────────────────┼─────────────────┼────────────┘
            ▼                 ▼                 ▼
    ┌───────────────┐ ┌───────────────┐ ┌───────────────┐
    │MCUSerialBridge│ │MCUSerialBridge│ │MCUSerialBridge│
    │   (Native)    │ │   (Native)    │ │   (Native)    │
    └───────────────┘ └───────────────┘ └───────────────┘
```

## 核心流程

### 新流程（推荐）：Probe 后保持连接

```csharp
var session = DIVERSession.Instance;

// 1. Probe 时添加并连接节点（保持连接）
var node1 = session.AddAndConnectNode("node1", "serial://name=COM3&baudrate=1000000");
var node2 = session.AddAndConnectNode("node2", "serial://name=COM18&baudrate=1000000");

// 2. 节点已连接，可以获取实时状态（Idle/Running 等）
var state = node1.State;  // 不会是 Offline

// 3. Start 时配置程序（不清空节点）
session.ConfigureConnectedNodes(new SessionConfiguration {
    AssemblyPath = "builds/current/LogicBuild.dll",  // 可选
    Nodes = new[] {
        new NodeConfiguration {
            NodeId = "node1",
            McuUri = "serial://name=COM3&baudrate=1000000",
            ProgramBytes = File.ReadAllBytes("generated/TestLogic.bin"),
            MetaJson = File.ReadAllText("generated/TestLogic.bin.json")
        }
    }
});

// 4. Configure + Program + Start
session.ConfigureAndProgramAll();
session.StartAll();

// 5. Stop 后保持连接，继续显示状态
session.StopAll();  // 节点仍然连接，状态变为 Idle
```

### 传统流程：Configure 时创建节点

```csharp
// Configure 会清空现有节点
session.Configure(config);
session.ConnectAll();
session.ConfigureAndProgramAll();
session.StartAll();
session.StopAll();
session.DisconnectAll();
```

## DIVERSession API

| 方法 | 说明 |
|------|------|
| `AddNode(nodeId, mcuUri)` | 添加节点（不连接） |
| `AddAndConnectNode(nodeId, mcuUri)` | **添加并连接节点（Probe 用）** |
| `GetNodeByUri(mcuUri)` | 通过 URI 查找节点 |
| `RemoveNode(nodeId)` | 移除节点 |
| `Configure(config)` | 传统配置（清空节点） |
| `ConfigureConnectedNodes(config)` | **配置已连接节点（不清空）** |
| `ConnectAll()` | 连接所有节点 |
| `DisconnectAll()` | 断开所有节点 |
| `ConfigureAndProgramAll()` | 配置端口 + 下载程序 |
| `StartAll()` | 启动执行 |
| `StopAll()` | 停止执行（保持连接） |

## MCUNode

```csharp
// 属性
node.NodeId        // 节点 ID
node.McuUri        // 串口 URI
node.IsConnected   // 是否已连接
node.IsRunning     // 是否正在运行
node.Version       // MCU 版本信息
node.Layout        // 硬件布局
node.State         // 当前状态（Idle/Running/...）
node.LastError     // 最后错误信息

// 方法
node.Connect()      // 连接（Open + Reset + GetVersion + GetLayout + GetState）
node.Disconnect()   // 断开
node.Configure()    // 配置端口
node.Program()      // 下载程序
node.Start()        // 启动执行
node.Stop()         // 停止执行
node.RefreshState() // 刷新状态（只调用 GetState）
```

## HostRuntime 变量存储

```csharp
// 设置变量
HostRuntime.SetCartVariable("node1", "inputA", 42);

// 获取变量
var value = HostRuntime.GetCartVariable<int>("node1", "result");

// 获取所有变量
var allVars = HostRuntime.GetAllVariables();
```

## CartFieldInfo 字段标志

| 标志 | 值 | 方向 |
|------|------|------|
| IsUpperIO | 0x01 | Host → MCU |
| IsLowerIO | 0x02 | MCU → Host |
| IsMutual | 0x00 | 双向 |

## 编译产物

| 文件 | 说明 |
|------|------|
| `*.bin` | MCU 字节码 |
| `*.bin.json` | 字段元数据 |
| `*.diver` | 中间表示 |
| `*.diver.map.json` | 源码映射 |

## 文件结构

```
CoralinkerSDK/
├── DIVERSession.cs         # 单例会话管理器
├── MCUNode.cs              # MCU 节点封装
├── HostRuntime.cs          # 变量存储、序列化
├── SerialPortResolver.cs   # 串口发现
├── CartFieldInfo.cs        # 字段元数据
├── NodeConfiguration.cs    # 节点配置
└── README.md
```
