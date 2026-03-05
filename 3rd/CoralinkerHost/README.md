# Coralinker DIVER Host

ASP.NET Core + Vue 3 Web 应用，用于 DIVER MCU 节点的可视化编程和控制。

**端口**: 4499 (生产) / 5173 (开发前端) / 5000 (开发后端)

---

## 架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        ClientApp (Vue 3)                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐      │
│  │  GraphCanvas │  │VariablePanel│  │    TerminalPanel    │      │
│  │  (vue-flow) │  │             │  │ Terminal │ Build    │      │
│  └──────┬──────┘  └──────┬──────┘  └────┬─────┴────┬─────┘      │
│         │                │               │          │            │
│         │  ┌─────────────┴───────────────┴──────────┴───┐       │
│         │  │     Pinia Stores (runtime, logs, files)    │       │
│         │  └─────────────┬──────────────────────────────┘       │
│         │                │                                       │
│         └────────────────┼───────── HTTP REST ──────────────────│
│                          │     SignalR (terminalLine/buildLine)  │
└──────────────────────────┼───────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Host (ASP.NET Core)                       │
│  ┌─────────────┐  ┌─────────────────┐  ┌──────────────────┐     │
│  │  ApiRoutes  │  │RuntimeSession   │  │TerminalBroadcaster│    │
│  │             │  │Service          │  │ terminalLine     │     │
│  │             │  │                 │  │ buildLine        │     │
│  └──────┬──────┘  └────────┬────────┘  └─────────┬────────┘     │
└─────────┼──────────────────┼─────────────────────┼───────────────┘
          │                  │                     │
          └──────────────────┼─────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DIVERSession (SDK)                          │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  NodeEntry (内部)                                           │ │
│  │  - UUID 唯一标识                                            │ │
│  │  - Version, Layout (硬件信息)                               │ │
│  │  - NodeName, PortConfigs, ExtraInfo (用户配置)              │ │
│  │  - ProgramBytes, MetaJson, LogicName (代码)                 │ │
│  │  - Handle, State, Stats (运行时)                            │ │
│  │  - NodeLogBuffer (日志, seq 分页)                           │ │
│  └────────────────────────────────────────────────────────────┘ │
│  - 节点管理: AddNode, RemoveNode, ConfigureNode, ProgramNode     │
│  - 会话控制: Start, Stop                                         │
│  - 数据管理: GetAllCartFieldMetas, GetAllCartFields, SetCartField│
│  - 日志管理: GetNodeLogs (seq 分页)                              │
│  - 导入导出: ExportNodes, ImportNodes                            │
└─────────────────────────────────────────────────────────────────┘
```

**核心设计**：DIVERSession 是节点数据的唯一真实来源，前端通过 API 获取/修改数据。

---

## 快速开始

```powershell
# 前端开发
cd ClientApp
npm install
npm run dev

# 生产构建
npm run build

# 启动后端
cd ..
dotnet run
```

---

## 目录结构

```
3rd/CoralinkerHost/
├── ClientApp/               # Vue 3 + Vite 前端
│   ├── src/
│   │   ├── api/             # HTTP API 封装
│   │   │   ├── device.ts    # 节点管理 (addNode, configureNode, ...)
│   │   │   ├── runtime.ts   # 运行控制 (start, stop, variables, logs)
│   │   │   └── project.ts   # 项目管理
│   │   ├── stores/          # Pinia 状态管理
│   │   │   ├── project.ts   # 项目状态 (selectedAsset, lastBuildId)
│   │   │   ├── runtime.ts   # 运行状态 (nodeStates, variables, appState)
│   │   │   ├── files.ts     # 文件管理
│   │   │   ├── logs.ts      # 日志管理 (Terminal/Build/节点日志)
│   │   │   └── wiretap.ts   # WireTap 状态管理
│   │   ├── protocol/        # 协议解析模块
│   │   │   ├── modbus/      # MODBUS RTU 解析器
│   │   │   ├── canopen/     # CANOpen + CiA 301/402 解析器
│   │   │   └── serial/      # 通用 Serial 解析器
│   │   ├── components/
│   │   │   ├── graph/       # 节点图 (Vue-flow)
│   │   │   │   ├── GraphCanvas.vue    # 画布，直接调用 API
│   │   │   │   ├── CoralNodeView.vue  # 节点视图
│   │   │   │   └── AddNodeDialog.vue  # 添加节点对话框
│   │   │   ├── control/     # 控制面板
│   │   │   └── variables/   # 变量面板
│   │   ├── types/           # TypeScript 类型定义
│   │   └── views/
│   │       ├── HomeView.vue         # 主页面（节点图编辑器）
│   │       └── ControlPanelView.vue # 独立遥控器页面 (/control)
│   └── vite.config.ts
├── Services/
│   ├── RuntimeSessionService.cs  # DIVERSession 异步封装 + 日志广播
│   ├── ProjectStore.cs           # 项目文件管理 (调用 DIVERSession.Export/Import)
│   ├── DiverBuildService.cs      # C# → MCU 编译
│   ├── TerminalBroadcaster.cs    # SignalR 消息推送
│   ├── VariableInspectorPushService.cs  # 定时推送变量和节点状态
│   └── WireTapAggregatorService.cs     # WireTap CAN 聚合 + Serial 节流推送
├── Web/
│   └── ApiRoutes.cs         # REST API 端点
├── data/                    # 运行时数据 (gitignored)
│   ├── project.json         # 项目状态 + 节点数据
│   └── assets/
│       ├── inputs/          # 用户代码 (.cs)
│       └── generated/       # 编译产物 (.bin, .bin.json)
└── wwwroot/                 # 前端构建产物
```

---

## 核心流程

### 节点生命周期

```
1. Add Node (探测并添加)
   POST /api/node/add { mcuUri: "serial://name=COM3&baudrate=1000000" }
   → DIVERSession.AddNode()
   → 返回 { uuid, nodeName: "Node-1-abc12345", version, layout }
   → 前端 GraphCanvas.addNode() 添加到画布

2. Configure Node (设置位置、端口配置)
   POST /api/node/{uuid}/configure
   { nodeName?, portConfigs?, extraInfo: { x, y } }
   → DIVERSession.ConfigureNode()
   → 节点拖拽停止时自动保存位置

3. Program Node (设置代码)
   POST /api/node/{uuid}/program { logicName: "TestLogic" }
   → 从 generated/ 读取 .bin + .bin.json
   → DIVERSession.ProgramNode()

4. Start (启动会话)
   POST /api/start
   → DIVERSession.Start()
   → 对每个节点: Open → Configure → Program → Start
   → 返回 { successNodes, totalNodes, errors[] }
   → 部分节点失败不阻塞成功的节点

5. Running (运行中)
   → 变量交换: UpperIO (可写) / LowerIO (只读)
   → SignalR 推送: VarsSnapshot (200ms), NodeSnapshot (500ms)
   → Graph 自动锁定（不可拖动/连接/删除节点）
   → Build/Start 按钮互斥

6. Stop (停止会话)
   POST /api/stop
   → DIVERSession.Stop()
   → 对每个节点: Reset → Close → 清理 Handle
```

### 前端数据流

```
┌─────────────────────────────────────────────────────────────────┐
│                        GraphCanvas.vue                           │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  nodes: Node[] (vue-flow 本地状态)                           ││
│  │  - 位置信息 (position.x, position.y)                         ││
│  │  - 节点数据 (data.nodeName, data.runState, ...)              ││
│  └─────────────────────────────────────────────────────────────┘│
│         │                                                        │
│         │ loadFromStore()     onNodeDragStop()                   │
│         ▼                     │                                  │
│  ┌──────────────┐      ┌──────▼───────┐                         │
│  │ GET /api/nodes│      │ POST configure│                        │
│  │ (NodeFullInfo)│      │ extraInfo:x,y │                        │
│  └──────────────┘      └──────────────┘                         │
└─────────────────────────────────────────────────────────────────┘
         │
         │ 节点状态更新
         ▼
┌─────────────────────────────────────────────────────────────────┐
│                        runtime store                             │
│  nodeStates: Map<uuid, NodeStateSnapshot>                        │
│  ← SignalR NodeSnapshot / GET /api/nodes/state                   │
└─────────────────────────────────────────────────────────────────┘
```

### 变量数据流

```
┌────────────────┐     200ms       ┌────────────────┐
│   Frontend     │ ◄──────────────│   Backend      │
│                │   VarsSnapshot  │                │
│   runtime.ts   │                 │ VariableInspector│
│   variables    │                 │ PushService    │
└────────────────┘                 └────────────────┘
       │                                  │
       │  POST /api/variable/set          │ GetAllCartFields
       ▼                                  ▼
┌─────────────────────────────────────────────────────┐
│                   DIVERSession                       │
│                   CartFields                         │
└─────────────────────────────────────────────────────┘
```

---

## API 端点

### 节点管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/node/probe` | POST | 只探测，不添加 |
| `/api/node/add` | POST | 探测并添加节点，返回 UUID |
| `/api/node/{uuid}/remove` | POST | 删除节点 |
| `/api/node/{uuid}/configure` | POST | 配置节点 (名称、端口、位置) |
| `/api/node/{uuid}/program` | POST | 设置节点代码 |
| `/api/node/{uuid}` | GET | 获取节点完整信息 |
| `/api/nodes` | GET | 获取所有节点信息 |
| `/api/nodes/state` | GET | 获取所有节点状态 |
| `/api/nodes/export` | GET | 导出节点数据 |
| `/api/nodes/import` | POST | 导入节点数据 |
| `/api/nodes/clear` | POST | 清空所有节点 |

### 会话控制

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/start` | POST | 启动所有节点 |
| `/api/stop` | POST | 停止所有节点 |
| `/api/session/state` | GET | 会话状态 (Idle/Running) |

### 变量管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/variables/meta` | GET | 获取字段元信息（不需要 Start，用于遥控器绑定） |
| `/api/variables` | GET | 获取所有变量（需要 Start） |
| `/api/variable/set` | POST | 设置变量值 |
| `/api/variable/{name}` | GET | 获取单个变量 |

### 日志管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/logs/nodes` | GET | 有日志的节点列表 |
| `/api/logs/node/{uuid}?afterSeq=&maxCount=` | GET | 获取日志 (seq 分页) |
| `/api/logs/node/{uuid}/clear` | POST | 清空日志 |
| `/api/logs/clear` | POST | 清空所有日志 |

### WireTap 监听

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/node/{uuid}/wiretap` | POST | 设置端口 WireTap 配置 |
| `/api/wiretap/configs` | GET | 获取所有节点的 WireTap 配置 |
| `/api/node/{uuid}/wiretap/logs` | GET | 获取节点的 WireTap 日志 |
| `/api/wiretap/logs` | GET | 获取所有节点的 WireTap 日志 |
| `/api/node/{uuid}/wiretap/export` | GET | 导出节点所有端口 WireTap 日志为 CSV（最多 10000 条） |

### 项目管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/project` | GET/POST | 项目状态 |
| `/api/project/new` | POST | 新建项目 |
| `/api/project/save` | POST | 保存项目 |
| `/api/project/export` | GET | 导出 ZIP |
| `/api/project/import` | POST | 导入 ZIP |
| `/api/build` | POST | 编译逻辑 |

### 文件管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/files/tree` | GET | 文件树 |
| `/api/files/read` | GET | 读取文件 |
| `/api/files/write` | POST | 写入文件 |
| `/api/assets` | GET | 资源列表 |
| `/api/logic/list` | GET | Logic 列表 |
| `/api/ports` | GET | 可用串口 |

---

## 项目文件格式

`data/project.json`:

```json
{
  "selectedAsset": "MyLogic.cs",
  "selectedFile": "assets/generated/MyLogic.bin.json",
  "lastBuildId": "20260127-143052",
  "nodes": {
    "abc123def456...": {
      "mcuUri": "serial://name=COM3&baudrate=1000000",
      "nodeName": "Node-1-abc12345",
      "portConfigs": [
        { "type": "Serial", "name": "RS485-1", "baud": 115200, "receiveFrameMs": 0 },
        { "type": "CAN", "name": "CAN-1", "baud": 1000000, "retryTimeMs": 10 }
      ],
      "programBase64": "...",
      "metaJson": "[{\"field\":\"speed\",\"typeid\":6,\"offset\":0,\"flags\":1}]",
      "logicName": "TestLogic",
      "extraInfo": {
        "x": 250,
        "y": 150
      }
    }
  },
  "controlLayout": {
    "windowX": 100,
    "windowY": 100,
    "gridCols": 12,
    "gridRows": 12,
    "isLocked": false,
    "widgets": [
      {
        "id": "widget-1",
        "type": "joystick",
        "gridX": 0, "gridY": 0, "gridW": 5, "gridH": 7,
        "config": {
          "variableX": "inputA", "variableY": "inputB",
          "minX": -100, "maxX": 100, "minY": -100, "maxY": 100,
          "autoReturnX": true, "autoReturnY": true,
          "keyUp": "W", "keyDown": "S", "keyLeft": "A", "keyRight": "D",
          "moveSpeed": 100, "returnSpeed": 200
        }
      },
      {
        "id": "widget-2",
        "type": "slider",
        "gridX": 6, "gridY": 0, "gridW": 5, "gridH": 2,
        "config": {
          "variable": "throttle",
          "min": 0, "max": 100, "autoReturn": false,
          "keyDecrease": "Z", "keyIncrease": "X",
          "moveSpeed": 100, "returnSpeed": 200
        }
      },
      {
        "id": "widget-3",
        "type": "switch",
        "gridX": 6, "gridY": 3, "gridW": 3, "gridH": 2,
        "config": {
          "variable": "enabled", "states": 2, "keyToggle": "C"
        }
      },
      {
        "id": "widget-4",
        "type": "gauge",
        "gridX": 0, "gridY": 8, "gridW": 4, "gridH": 4,
        "config": {
          "variable": "temperature", "style": "gauge",
          "min": 0, "max": 100, "unit": "°C"
        }
      },
      {
        "id": "widget-5",
        "type": "lamp",
        "gridX": 5, "gridY": 8, "gridW": 3, "gridH": 2,
        "config": {
          "variable": "status", "bitCount": 8,
          "bitsPerLine": 8, "showIndex": true, "color": "#22c55e"
        }
      }
    ]
  }
}
```

---

## 后端服务

| 服务 | 职责 |
|------|------|
| `RuntimeSessionService` | DIVERSession 异步封装 + Start/Stop/Probe/Program 操作，Start 时调用 Aggregator.Reset() |
| `ProjectStore` | 项目文件管理，调用 DIVERSession.Export/Import |
| `DiverBuildService` | C# → MCU 编译，输出到 Build 日志面板 |
| `TerminalBroadcaster` | SignalR 消息推送 (Terminal/Build/节点日志分离) |
| `VariableInspectorPushService` | 每 200ms 推送变量，每 500ms 推送节点状态 |
| `WireTapAggregatorService` | 数据聚合与节流：CAN 帧按 ID/DLC/方向分组，Serial 帧批量推送，节点日志批量推送（均 ~250ms） |
| `JsonHelper` | 统一 JSON 序列化配置 (PascalCase ↔ camelCase) |

### JSON 序列化

C# 使用 PascalCase，JavaScript 使用 camelCase。通过 `JsonHelper` 统一处理：

```csharp
// Web/JsonHelper.cs
public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,      // 序列化: PascalCase → camelCase
        PropertyNameCaseInsensitive = true,                      // 反序列化: 不区分大小写
    };

    public static IResult Json<T>(T obj) => Results.Json(obj, Options);
}
```

所有 API 端点使用 `JsonHelper.Json()` 返回数据，确保前后端字段名匹配。

---

## 前端开发

### Store 职责

| Store | 职责 |
|-------|------|
| `project` | selectedAsset, selectedFile, lastBuildId, controlLayout |
| `runtime` | nodeStates, nodeInfos, variables, fieldMetas, appState |
| `files` | 文件树、打开的 Tab |
| `logs` | Terminal/Build/节点日志，Build 错误可点击跳转 |
| `ui` | 通知、对话框 |

### 关键组件

| 组件 | 说明 |
|------|------|
| `GraphCanvas.vue` | Vue-flow 节点图画布，运行时自动锁定交互 |
| `CoralNodeView.vue` | MCU 节点视图，含升级按钮、端口配置、IO 状态 |
| `AddNodeDialog.vue` | 添加节点对话框，调用 addNode API |
| `UpgradeDialog.vue` | 固件升级对话框（支持从 Node 视图直接打开） |
| `TerminalPanel.vue` | 终端面板 (Terminal/Build 标签分离，Build/Start 互斥) |
| `VariablePanel.vue` | 变量表格 + 编辑 |
| `ControlWindow.vue` | 遥控器面板（摇杆、滑块、开关、仪表盘、指示灯） |
| `FatalErrorDialog.vue` | MCU 错误弹窗，支持跳转到源代码 |

### 遥控器面板

ControlWindow 提供可拖拽、可调整大小的遥控器窗口，支持五种控件：

| 控件 | 说明 | 键盘绑定 |
|------|------|----------|
| **Joystick** | 双轴摇杆，绑定 X/Y 两个变量 | 上下左右四个方向键，支持 WASD/IJKL/方向键预设 |
| **Slider** | 单轴滑块，绑定一个变量 | +/- 两个方向键，支持 ZX/NM/RF 等预设 |
| **Switch** | 开关，绑定一个 bool 变量 | 单键切换，支持 C/V/B 预设 |
| **Gauge** | 只读仪表盘，支持纯文本/进度条/仪表盘样式 | 无（只读） |
| **Lamp** | 只读 LED 指示灯，显示二进制位状态 | 无（只读） |

**控件控制特性**：
- Joystick/Slider 支持自动回弹，回弹位置用百分比设置（0-100%，默认 50%）
- 键盘绑定在输入框获得焦点时自动禁用

**Gauge 仪表盘特性**：
- 显示样式：纯文本（数值/字符串）、水平/垂直进度条、仪表盘
- 仪表盘模式显示 5 个刻度数字（0%, 25%, 50%, 75%, 100%）
- 超出范围时指针变红，可超出 180° 最大约 ±20°
- 严重超限时（>110°）指针抖动警示

**Lamp 指示灯特性**：
- 可绑定任意数值变量（i8/u8/i16/u16/i32/u32）
- 支持多位显示（默认 1 位），每 8 位一行
- 可自定义 LED 颜色
- 位索引从 0 开始递增

**通用特性**：
- 网格布局（默认 12x12 格，每格 32px）
- 控件可拖拽、可调整大小
- 布局锁定功能
- 布局持久化到 `projectStore.controlLayout`
- 字段元信息在页面加载时获取（`/api/variables/meta`），无需 Start 即可配置绑定
- **变量列表区分**：可控变量（绿色背景）和只读变量（橙色背景）
- **触摸设备支持**：自动检测触摸设备，隐藏键盘绑定显示，支持触摸拖动操作

**独立页面** `/control`：
- 简洁的只读遥控器界面，适合手机/平板使用
- 顶部状态栏显示运行状态和 Start/Stop 按钮
- 布局锁定，只能操控不能修改配置

### 日志面板

TerminalPanel 包含两个独立标签：

| 标签 | 内容 | 说明 |
|------|------|------|
| **Terminal** | 运行时日志 | Start/Stop、系统消息、SignalR 状态 |
| **Build** | 编译日志 | MSBuild 输出，每次 Build 自动清空 |

**Build 错误跳转**：编译错误行 (如 `TestLogic.cs(107,31): error CS1026`) 可点击跳转到源代码对应行。

### WireTap 端口监听

WireTap 功能允许实时监听 MCU 端口（Serial/CAN）的通信数据，用于调试和协议分析。

**功能特性**：
- 在节点视图的端口统计区域点击 TX/RX 圆点启用/禁用监听
- 支持 Serial 和 CAN 两种端口类型
- 日志面板显示多列视图：Console + 各端口日志
- 数据支持折叠/展开（超过 16 字节自动折叠）
- 选中十六进制数据可 Inspect 查看多种数据解析（u16/i16/u32/f32 等）
- 日志存储在 DIVERSession 内存中（最大 10000 条），刷新页面不丢失
- Start 时清空日志，Stop 时保留日志和计数器
- WireTap 开关只控制嵌入式实时传送，关闭后已有数据的日志面板不会消失

**CAN 聚合显示**：
- CAN 报文按 `(方向, RTR, DLC, CAN_ID)` 分组聚合显示为表格行
- 行按 CAN ID 升序 → 方向 (RX 先) → DLC 排序，位置稳定不跳动
- 默认只显示最新 1 条报文数据，点击展开可查看最近 5 条
- 每行显示帧率（近 1 秒内）、总帧数、最后接收时间
- 行颜色根据最新接收时间渐变：活跃报文鲜艳，>3 秒未更新变灰
- 聚合由后端 `WireTapAggregatorService` 处理，~250ms 推送一次快照

**Serial 节流**：
- Serial 帧由后端批量推送（~250ms 节流），前端保持平铺列表显示
- Modbus 等协议数据不做内容聚合，仅减少 SignalR 推送频率

**CSV 导出**：
- 按节点导出所有端口的原始 WireTap 日志，最多 10000 条
- CSV 格式：`Timestamp,MCU_Timestamp_Ms,PortIndex,PortType,Direction,DataLength,HexData,CAN_ID,CAN_DLC,CAN_RTR`
- 通过日志面板工具栏的 📥 Export 按钮（节点 Tab 激活时显示）或 `GET /api/node/{uuid}/wiretap/export` 导出

**MCU 硬件时间戳**：
- 所有上行数据包（Serial/CAN/Console）携带 MCU 端硬件时间戳（`PayloadHeader.timestamp_ms`，来源于 `g_hal_timestamp_us / 1000`）
- 时间戳在 MCU 侧 caller 函数入口处捕获，避免 USB 批量传输导致的时间聚簇
- 前端双时间显示：灰色 Host PC 接收时间 + 紫色 MCU 相对时间（`+mm:ss.mmm` 格式）
- 节点日志通过结构化字段 `{ hostTimestamp, message, mcuTimestampMs }` 传递，不嵌入消息正文

**节点日志节流**：
- `Console.WriteLine` 产生的节点日志由 `WireTapAggregatorService` 批量推送（~250ms 节流）
- 避免高频日志导致的 SignalR 推送风暴和前端卡顿

**Start 时数据清空**：
- Start 时前端清空所有 WireTap 日志和节点日志
- 后端 `WireTapAggregatorService.Reset()` 同步清空 CAN 聚合数据、Serial 队列和日志队列

**协议解析功能**：
- 点击日志条目旁的 🔍 按钮可进行协议解析
- CAN 聚合视图中展开的每条帧也支持协议解析和 Inspect
- 支持的协议：
  - **MODBUS RTU** (Serial)：功能码解析、CRC 校验、寄存器值解析
  - **CANOpen** (CAN)：NMT、SDO、PDO、Heartbeat、Emergency 解析
  - **CiA 301**：通信配置对象字典（0x1000 区域）
  - **CiA 402**：驱动器配置（Controlword、Statusword、运行模式等）
- 协议解析架构可扩展，位于 `ClientApp/src/protocol/`

### SignalR 事件

| 事件 | 方向 | 频率/说明 |
|------|------|------|
| `terminalLine` | Server→Client | 终端日志行（实时） |
| `buildLine` | Server→Client | Build 日志行（编译时） |
| `nodeLogLine` | Server→Client | 节点日志 (uuid, message)（实时） |
| `varsSnapshot` | Server→Client | 变量快照（**200ms**） |
| `nodeSnapshot` | Server→Client | 节点状态快照（**500ms**，含 TX/RX/IO） |
| `nodeLogBatch` | Server→Client | 节点日志批量推送（~250ms 节流，每条为 `{hostTimestamp, message, mcuTimestampMs}`） |
| `wireTapData` | Server→Client | WireTap 逐帧数据（旧接口，保留兼容） |
| `wireTapCanAggregated` | Server→Client | CAN 聚合快照（~250ms，按 ID/DLC/方向/RTR 分组） |
| `wireTapSerialBatch` | Server→Client | Serial 批量数据（~250ms 节流） |
| `fatalError` | Server→Client | MCU Fatal Error (HardFault/ASSERT)（实时） |
| `upgradeProgress` | Server→Client | 固件升级进度（实时） |

**运行时数据流**：后端每 500ms 从下位机轮询状态，同时通过 SignalR 推送到前端。前端不再需要 HTTP 轮询。

### 类型定义 (`types/index.ts`)

```typescript
// 节点完整信息
interface NodeFullInfo {
  uuid: string
  mcuUri: string
  nodeName: string
  version?: VersionInfo
  layout?: LayoutInfo       // 硬件布局 (ports, IO counts)
  portConfigs: PortConfig[]
  hasProgram: boolean
  logicName?: string
  extraInfo?: { x?: number, y?: number }
}

// 节点状态快照
interface NodeStateSnapshot {
  uuid: string
  mcuUri: string
  nodeName: string
  isConnected: boolean
  runState: string  // "idle" | "running" | "error" | "offline"
  isConfigured: boolean
  isProgrammed: boolean
  stats?: RuntimeStats
}

// 硬件布局
interface LayoutInfo {
  digitalInputCount: number
  digitalOutputCount: number
  portCount: number
  ports: PortDescriptor[]
}

// 字段元信息（用于遥控器绑定，不需要 Start）
interface CartFieldMeta {
  name: string
  type: string
  typeId: number
  isLowerIO: boolean   // 只读字段
  isUpperIO: boolean   // 可写字段
  isMutual: boolean    // 双向字段
}

// 遥控器布局
interface ControlLayoutConfig {
  windowX: number
  windowY: number
  gridCols: number
  gridRows: number
  isLocked: boolean
  widgets: ControlWidget[]
}
```

---

## 常见问题

### 节点添加失败

- 检查串口是否被占用
- 确认 MCU 固件版本正确
- 查看终端日志的错误信息

### Start 失败

- 确保已 Build 且 ProgramNode 设置了代码
- 检查节点是否都有 LogicName
- 部分节点失败会返回 errors 数组，成功的节点仍会运行

### 变量不更新

- 检查 SignalR 连接状态
- LowerIO 字段只读，不能设置
- 确认节点处于 Running 状态
- Build 完成后会自动刷新变量列表（删除/修改变量后重新编译即可更新）

### 日志重复

**节点日志**：
- 使用 `afterSeq` 参数获取新日志
- 首次获取时不传 afterSeq
- logs store 自动管理 lastSeq

**终端/Build 日志**：
- SignalR 连接使用单例模式，避免多个组件创建重复连接
- `useSignalR()` composable 的 connection/state 定义在模块级别
- 如果仍然出现重复，检查浏览器是否打开了多个标签页

### 节点位置不保存

- 节点位置存储在 `extraInfo.x, extraInfo.y`
- 拖拽停止时自动调用 `configureNode` 保存
- 检查后端是否返回 ok

### Logic 不保存

- 选择 Logic 后会调用 `/api/node/{uuid}/program` API
- Logic 数据存储在 DIVERSession 中，前端 dirty 检查不会触发
- 点击 Save 总是会调用后端保存（不依赖 dirty 状态）

### 节点操作后数据丢失

- 所有节点操作（添加、删除、重命名、配置端口、选择 Logic）都会自动保存到磁盘
- 无需手动点击 Save

### 固件升级

- AddNodeDialog 中 Probe 成功后可升级
- CoralNodeView 中点击升级按钮（⬆）可直接升级已添加的节点
- 升级前会自动 Probe 获取当前版本信息

---

## 编辑器功能

- **Monaco Editor** 语法高亮、代码补全
- **Ctrl+S** 保存当前文件
- **generated/** 目录下的文件为只读
- **Build 错误跳转** 点击编译错误行号自动打开文件并定位

---

## 技术栈

- **前端**: Vue 3, Vite, TypeScript, Pinia, Vue-flow, Naive UI, Monaco Editor
- **后端**: ASP.NET Core 8, SignalR
- **SDK**: CoralinkerSDK (DIVERSession)
