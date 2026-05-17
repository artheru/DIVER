# CoralinkerSDK

DIVER 运行时核心 SDK，提供 MCU 节点管理、数据交换、日志管理。

**设计原则**: DIVERSession 是独立于网页的核心管理层，可被任意终端（网页、CLI、桌面应用）使用。

---

## 架构

```text
┌─────────────────────────────────────────────────────────────────┐
│               调用方 (CoralinkerHost / CLI / 桌面应用)            │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                     DIVERSession (单例)                          │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  内部存储:                                                 │  │
│  │  - _nodes: Dict<UUID, NodeEntry>       MCU 节点管理        │  │
│  │  - _virtualNodes: Dict<Id, VirtualNode> 上层控制节点声明   │  │
│  │  - _variables: Dict<Name, Value>       变量值存储          │  │
│  │  - NodeLogBuffer (per node)          日志缓冲              │  │
│  └───────────────────────────────────────────────────────────┘  │
│                               │                                  │
│              ┌────────────────┼────────────────┐                 │
│              ▼                ▼                ▼                 │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐    │
│  │   NodeEntry 1   │ │   NodeEntry 2   │ │ VirtualNodeEntry│    │
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

```text
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

### MCUNode 并发释放保护（SafeDispose）

`MCUNode` 在本轮更新中加入了面向并发场景的安全释放机制，解决“后台线程仍在调用桥接接口时对象被释放”带来的竞争问题。

关键点：

- 通过 `_bridgeCallLock`、`_activeBridgeCalls`、`_isDisposingBridge` 协调并发调用
- 对桥接调用统一走 `TryEnterBridgeCall()` / `ExitBridgeCall()`
- `Disconnect()` 走 `SafeDisposeBridge()`，会等待活跃调用结束再释放底层句柄
- `IsConnected` 会同时检查 `IsOpen` 与 `!_isDisposingBridge`，避免释放窗口误判在线

收益：

- 降低回调线程、状态轮询线程、业务线程并发下的释放竞态
- 避免释放过程中继续进入 native 调用导致不稳定行为

### 串口错误上报接入 DIVERSession

本轮同时将 MSB 的传输错误回调链路接入到会话层日志：

- `MCUSerialBridge.RegisterErrorCallback(...)`
- `MCUNode.OnError`（内部事件）
- `DIVERSession` 在 `StartNode()` 中订阅 `OnError`
- 最终写入 `OnNodeLog`（消息前缀为 `[Transport]`），前端沿用现有节点日志通道显示

这样串口异常与重连状态（scheduled/failed/success）可直接在前端节点日志中观察。

### 统一变量声明表

`DIVERSession` 是变量类型、方向和可控性的唯一裁决层。调用方不应该在 UI 或上层框架中根据额外接口再修正变量类型。

内部数据分两层：

- `_variables` 只保存运行时值，key 是变量名。
- MCU `NodeEntry.CartFields` 和上层 `VirtualNodeEntry` 共同提供变量声明，包含 `TypeId`、Upper/Lower/Mutual、ControlItem、Root cart 接管等语义。

`GetAllCartFieldMetas()` 与 `GetAllCartFields()` 会合并所有声明源后返回结果：

- MCU 节点通过 `ProgramNode(..., metaJson, ...)` 注册字段声明。
- Root runtime、Medulla 或 CLI 上层控制器通过 `RegisterVirtualNode(...)` 注册虚拟节点声明。
- 同名变量是同一个运行时值；如果 Root/上层控制器声明了同名 cart 字段，session 会用该声明决定 UI/控制语义，同时 MCU 节点仍按自己的 `CartFields` 序列化 UpperIO。
- 同名字段类型冲突会写入 session 日志，不应静默退化为 `Unknown`。

这保证 CoralinkerHost、Medulla、CLI 使用同一套变量规则，不需要各自复刻前端补丁。

### 节点状态映射与断链判定（结构化）

本轮同时收敛了节点状态判定逻辑，避免依赖日志文本解析：

- `DIVERSession` 基于结构化信息映射状态：`error` / `disconnected` / `running` / `idle`
- `GetState` 返回非 0 错误码时，`MCUState` 为空，节点按 `disconnected` 处理
- `HasFatalError = true` 时优先映射为 `error`
- 不再通过解析 `[Transport]` 文本推断连接状态

这保证了断链重连期间不会误显示为 `idle`。

---

## API 参考

### 2026-03 可靠性改动导读

- **桥接回调链路**：`MCUSerialBridge` 传输事件通过 `MCUNode.OnError` 接入 `OnNodeLog`。
- **并发安全释放**：`SafeDisposeBridge()` 阻止释放窗口继续进入 native 调用。
- **状态判定收敛**：以结构化返回为准，明确 `error` / `disconnected` / `running` / `idle` 四态。

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
| `RegisterVirtualNode(sourceId, displayName, cartFields, controlFields?)` | 注册 Root/Medulla 等上层虚拟节点字段声明 |
| `UnregisterVirtualNode(sourceId)` | 注销虚拟节点字段声明 |
| `SetVirtualControlField(name, value)` | 设置虚拟节点 ControlItem 字段 |
| `SetRootControlField(name, value)` | `SetVirtualControlField` 的 Root 语义兼容别名 |
| `GetCartField(name)` | 获取字段值 |

#### 写入值类型规则

`DIVERSession` 是 SDK 层，不只服务网页。`SetCartField()`、`SetVirtualControlField()`、`SetCartFieldAndSignalUpperIO()` 都接受 `object` 值，并按该变量声明中的 `TypeId` 做最终归一化：

- 原生 C# 调用方（例如 Medulla、CLI、桌面程序）应优先传真实基础类型，例如 `float`、`int`、`bool`。如果传入类型已经和 `TypeId` 匹配，session 会走 fast path 直接保存，不经过 JSON 解包，也尽量不经过 `Convert.*`。
- Web/HTTP 调用方可能传入 `System.Text.Json.JsonElement`。session 会识别它，并按目标 `TypeId` 读取为对应基础类型，避免 `JsonElement` 泄漏到 `_variables`。
- 如果传入的是可转换的其它数值类型（例如 `double` 写入 `Single` 字段、`int` 写入 `Int16` 字段），session 会按声明类型转换。
- `_variables` 中不应长期保存 transport 类型；它保存的是运行时值，而类型、方向、可控性来自统一声明表。

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

### Root / Medulla 虚拟节点

Root runtime 或 Medulla 这类运行在主机侧的上层控制器，也应该作为一个“节点”注册到 `DIVERSession`。它们不一定有 MCU 连接句柄，但必须声明自己读写哪些变量。

```csharp
var session = DIVERSession.Instance;

session.RegisterVirtualNode(
    sourceId: "root-runtime",
    displayName: "Root:DiffDrive",
    cartFields: new[] {
        new VirtualCartFieldDeclaration(
            Name: "left_diff_speed",
            TypeId: 6,        // Int32
            IsLowerIO: false,
            IsUpperIO: true,
            IsMutual: false,
            IsControl: false,
            IsRootCart: true)
    },
    controlFields: new[] {
        new VirtualCartFieldDeclaration(
            Name: "joystickX",
            TypeId: 8,        // Single
            IsLowerIO: false,
            IsUpperIO: false,
            IsMutual: false,
            IsControl: true,
            IsRootCart: false)
    });

// UI/遥控器写 ControlItem
session.SetVirtualControlField("joystickX", 0.35f);

// Root/Medulla 计算后发布 UpperIO，session 会唤醒 MCU 节点下发
session.SetCartFieldAndSignalUpperIO("left_diff_speed", 800);
```

注意：

- `SetCartField()` 是外部控制入口，只允许写普通 MCU UpperIO/MutualIO 和 ControlItem。
- Root cart 字段被 `IsRootCart=true` 接管后，不能由遥控面板直接写；应写 ControlItem，再由 Root/Medulla 逻辑计算输出。
- `SetCartFieldAndSignalUpperIO()` 是 Root/上层逻辑发布结果的入口，会更新变量值并唤醒所有正在运行的 MCU 节点发送 UpperIO。

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
    string RunState,        // "idle" | "running" | "disconnected" | "error"
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

```text
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
| `OnNodeLog` | `Action<string, string, string, uint>` | 节点日志 (uuid, hostTimestamp, message, mcuTimestampMs) |
| `OnFatalError` | `Action<string, string>` | 节点致命错误 (uuid, errorJson) |
| `OnWireTapData` | `Action<WireTapDataEventArgs>` | WireTap 数据事件 |
