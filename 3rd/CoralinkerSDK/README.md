# CoralinkerSDK

DIVER 运行时核心 SDK，提供 MCU 节点管理、数据交换、日志管理。

**设计原则**: DIVERSession 是独立于网页的核心管理层，可被任意终端（网页、CLI、桌面应用）使用。

---

## 架构

```
┌─────────────────────────────────────────────────────────────────┐
│               调用方 (CoralinkerHost / CLI / 桌面应用)            │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                     DIVERSession (单例)                          │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  内部存储:                                                 │  │
│  │  - _nodes: Dict<UUID, NodeEntry>     节点管理              │  │
│  │  - _variables: Dict<Name, Value>     变量存储              │  │
│  │  - NodeLogBuffer (per node)          日志缓冲              │  │
│  └───────────────────────────────────────────────────────────┘  │
│                               │                                  │
│              ┌────────────────┼────────────────┐                 │
│              ▼                ▼                ▼                 │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐    │
│  │   NodeEntry 1   │ │   NodeEntry 2   │ │   NodeEntry N   │    │
│  │ ┌─────────────┐ │ │                 │ │                 │    │
│  │ │ MCUNode     │ │ │    (运行时)      │ │    (运行时)      │    │
│  │ │ (Handle)    │ │ │                 │ │                 │    │
│  │ └─────────────┘ │ │                 │ │                 │    │
│  └─────────────────┘ └─────────────────┘ └─────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │  MCUSerialBridge    │
                    │     (Native)        │
                    └─────────────────────┘
```

---

## 核心设计

### 状态机

```
              Idle ◄──────────────────────────┐
               │                              │
   AddNode / ProgramNode /                  Stop()
   ConfigureNode / Import                     │
               │                              │
               ▼                              │
              Idle ─────── Start() ─────► Running
```

- **Idle**: 可以添加/删除/配置节点
- **Running**: 节点运行中，不允许修改配置

### NodeEntry 数据结构

```csharp
internal class NodeEntry
{
    // === 基本信息（不变量，Probe时获取）===
    string UUID;              // 唯一标识
    string McuUri;            // 连接地址 (COM3, serial://name=COM3&baudrate=2000000)
    VersionInfo? Version;     // 固件版本
    LayoutInfo? Layout;       // 硬件布局
    
    // === 用户设置（变量）===
    string NodeName;          // 节点名称 "Node-1-abc12345"
    PortConfig[] PortConfigs; // 端口配置
    JsonObject? ExtraInfo;    // 扩展信息（位置等，Host使用）
    
    // === 代码（ProgramNode时设置）===
    byte[] ProgramBytes;      // DIVER 字节码
    string? MetaJson;         // 字段映射 JSON
    string? LogicName;        // 逻辑名称（用于匹配）
    CartFieldInfo[] CartFields; // 解析后的字段
    
    // === 运行时（Start后才有）===
    MCUNode? Handle;          // MCU 连接句柄
    MCUState? State;          // 运行状态
    RuntimeStats? Stats;      // 统计信息
    
    // === 日志 ===
    NodeLogBuffer LogBuffer;  // 最多 10000 行
}
```

---

## API 参考

### 节点管理（只能在 Idle 状态调用）

| 方法 | 说明 |
|------|------|
| `ProbeNode(mcuUri)` | 探测节点，返回 `NodeProbeResult?` |
| `AddNode(mcuUri)` | 探测并添加节点，返回 UUID |
| `RemoveNode(uuid)` | 删除节点 |
| `RemoveAllNodes()` | 删除所有节点 |
| `ConfigureNode(uuid, settings)` | 修改节点设置 |
| `ProgramNode(uuid, bytes, metaJson, logicName)` | 设置节点代码 |
| `GetNodeState(uuid)` | 获取节点状态 |
| `GetNodeInfo(uuid)` | 获取节点完整信息 |
| `GetNodeStates()` | 获取所有节点状态 |
| `ExportNodes()` | 导出所有节点（持久化） |
| `ImportNodes(data)` | 导入节点（加载项目） |

### 会话管理

| 方法 | 说明 |
|------|------|
| `Start()` | 启动所有节点：Open → Configure → Program → Start |
| `Stop()` | 停止所有节点：Reset → Close → 清理 Handle |

### 数据管理

| 方法 | 说明 |
|------|------|
| `GetAllCartFields()` | 获取所有 Cart 字段 |
| `SetCartField(name, value)` | 设置字段值（非 LowerIO） |
| `GetCartField(name)` | 获取字段值 |

### 日志管理

| 方法 | 说明 |
|------|------|
| `GetNodeLogs(uuid, afterSeq?, maxCount)` | 获取日志（seq 分页） |
| `ClearNodeLogs(uuid)` | 清空节点日志 |
| `ClearAllLogs()` | 清空所有日志 |
| `GetLoggedNodeIds()` | 获取有日志的节点 |

---

## 使用示例

### 基本流程

```csharp
var session = DIVERSession.Instance;

// 1. 添加节点
var uuid1 = session.AddNode("COM3");
var uuid2 = session.AddNode("serial://name=COM18&baudrate=2000000");

if (uuid1 == null) {
    Console.WriteLine("Add node failed");
    return;
}

// 2. 配置节点
session.ConfigureNode(uuid1, new NodeSettings {
    NodeName = "Motor Controller",
    PortConfigs = new[] {
        new SerialPortConfig(9600, 20),
        new CANPortConfig(500000, 10)
    }
});

// 3. 设置代码
var programBytes = File.ReadAllBytes("generated/TestLogic.bin");
var metaJson = File.ReadAllText("generated/TestLogic.bin.json");
session.ProgramNode(uuid1, programBytes, metaJson, "TestLogic");

// 4. 启动
var result = session.Start();
if (!result.Success) {
    foreach (var err in result.Errors) {
        Console.WriteLine($"Node {err.NodeName} failed: {err.Error}");
    }
}

// 5. 运行中...
while (session.IsRunning) {
    var states = session.GetNodeStates();
    var fields = session.GetAllCartFields();
    
    // 设置变量
    session.SetCartField("targetSpeed", 100);
    
    Thread.Sleep(200);
}

// 6. 停止
session.Stop();
```

### 日志获取（避免重复）

```csharp
long? lastSeq = null;

while (true) {
    var logs = session.GetNodeLogs(uuid, lastSeq, 100);
    if (logs == null) break;
    
    foreach (var entry in logs.Entries) {
        Console.WriteLine($"[{entry.Seq}] {entry.Timestamp:HH:mm:ss} {entry.Message}");
    }
    
    lastSeq = logs.LatestSeq;  // 下次从这里开始
    
    if (!logs.HasMore) {
        Thread.Sleep(500);
    }
}
```

### 项目保存/加载

```csharp
// 保存 - ExportNodes 返回完整的节点数据
var exportData = session.ExportNodes();
// 包含: McuUri, NodeName, PortConfigs, ProgramBase64, MetaJson, LogicName, ExtraInfo
var json = JsonSerializer.Serialize(exportData);
File.WriteAllText("project.json", json);

// 加载 - ImportNodes 恢复所有节点状态
var json = File.ReadAllText("project.json");
var importData = JsonSerializer.Deserialize<Dictionary<string, NodeExportData>>(json);
session.ImportNodes(importData);
// 注意: 加载后节点处于 Idle 状态，需要重新 Start
```

**重要**: ExportNodes 导出的数据包含完整的代码（ProgramBase64, MetaJson, LogicName），
调用 ProgramNode 后立即可以导出保存。

---

## DTO 结构

### NodeProbeResult

```csharp
record NodeProbeResult(
    VersionInfo Version,
    LayoutInfo Layout
);
```

### NodeSettings

```csharp
record NodeSettings {
    string? NodeName;
    PortConfig[]? PortConfigs;
    JsonObject? ExtraInfo;  // 前端扩展信息
}
```

### NodeStateSnapshot

```csharp
record NodeStateSnapshot(
    string UUID,
    string McuUri,
    string NodeName,
    bool IsConnected,
    string RunState,        // "idle" | "running" | "error" | "offline"
    bool IsConfigured,
    bool IsProgrammed,
    RuntimeStatsSnapshot? Stats
);
```

### NodeFullInfo

```csharp
record NodeFullInfo(
    string UUID,
    string McuUri,
    string NodeName,
    VersionInfoSnapshot? Version,
    LayoutInfoSnapshot? Layout,
    PortConfigSnapshot[] PortConfigs,
    bool HasProgram,
    int ProgramSize,
    string? LogicName,
    CartFieldSnapshot[] CartFields,
    JsonObject? ExtraInfo
);
```

### NodeExportData

```csharp
record NodeExportData {
    string McuUri;
    string NodeName;
    PortConfigSnapshot[]? PortConfigs;
    string? ProgramBase64;
    string? MetaJson;
    string? LogicName;
    JsonObject? ExtraInfo;
}
```

### LogQueryResult

```csharp
record LogQueryResult(
    string UUID,
    long LatestSeq,         // 最新日志的 seq
    LogEntry[] Entries,
    bool HasMore
);

record LogEntry(
    long Seq,               // 自增序列号
    DateTime Timestamp,
    string Message
);
```

### StartResult

```csharp
record StartResult(
    bool Success,
    int TotalNodes,
    int SuccessNodes,
    List<NodeStartError> Errors
);

record NodeStartError(string UUID, string NodeName, string Error);
```

---

## CartFieldInfo 字段标志

| 标志 | 值 | 方向 | 说明 |
|------|------|------|------|
| IsUpperIO | 0x01 | Host → MCU | 只能 Host 设置 |
| IsLowerIO | 0x02 | MCU → Host | 只读，自动更新 |
| IsMutual | 0x00 | 双向 | 可读可写 |

---

## 文件结构

```
CoralinkerSDK/
├── DIVERSession.cs         # 单例会话管理器 + DTO 定义
├── MCUNode.cs              # MCU 节点封装
├── HostRuntime.cs          # 变量序列化工具
├── SerialPortResolver.cs   # 串口发现
├── CartFieldInfo.cs        # 字段元数据
├── NodeConfiguration.cs    # 旧配置结构（兼容）
└── README.md
```

---

## 事件

| 事件 | 签名 | 说明 |
|------|------|------|
| `OnStateChanged` | `Action<DIVERSessionState>` | 状态变更 |
| `OnNodeLog` | `Action<string, string>` | 节点日志 (uuid, message) |
