# Coralinker DIVER Host

ASP.NET Core + Vue 3 Web 应用，用于 DIVER MCU 节点的可视化编程和控制。

**端口**: 4499 (生产) / 5173 (开发)

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
│   │   ├── stores/          # Pinia 状态管理
│   │   │   ├── project.ts   # 项目状态
│   │   │   ├── runtime.ts   # 运行时状态
│   │   │   ├── files.ts     # 文件状态
│   │   │   └── logs.ts      # 日志状态
│   │   ├── components/
│   │   │   ├── graph/       # 节点图 (GraphCanvas, CoralNodeView)
│   │   │   ├── logs/        # 终端面板
│   │   │   └── variables/   # 变量面板
│   │   ├── composables/     # useSignalR, useAutoSave
│   │   └── views/           # HomeView, ControlPanelView
│   └── vite.config.ts
├── Services/
│   ├── RuntimeSessionService.cs  # 运行时管理
│   ├── ProjectStore.cs           # 项目存储
│   ├── DiverBuildService.cs      # 编译服务
│   └── TerminalBroadcaster.cs    # SignalR 推送
├── Web/
│   └── ApiRoutes.cs         # REST API 端点
├── data/                    # 运行时数据 (gitignored)
│   ├── project.json         # 项目状态
│   └── assets/              # 资源文件
└── wwwroot/                 # 前端构建产物
```

---

## 核心流程

### 节点生命周期

```
Probe ──────────────────────────────────────────────────────────
    │  POST /api/node/probe                                     
    │  节点加入 DIVERSession 并保持连接                          
    ▼                                                           
状态轮询 ─────────────────────────────────────────────────────── 
    │  DIVERSession 后台线程每 1.2 秒刷新状态                    
    │  前端通过 /api/node/poll-state 获取状态                    
    ▼                                                           
Start ──────────────────────────────────────────────────────────
    │  POST /api/start                                          
    │  ConfigureConnectedNodes → ConfigureAndProgramAll → StartAll
    ▼                                                           
Running ────────────────────────────────────────────────────────
    │  变量交换: UpperIO (Host→MCU) / LowerIO (MCU→Host)         
    ▼                                                           
Stop ───────────────────────────────────────────────────────────
    │  POST /api/stop                                           
    │  StopAll (保持连接，回到 Idle 状态)                        
```

### 数据流

```
┌──────────────┐     HTTP REST      ┌──────────────┐
│   Frontend   │ ◄─────────────────►│   Backend    │
│   (Vue 3)    │                    │  (ASP.NET)   │
└──────┬───────┘                    └──────┬───────┘
       │                                   │
       │   SignalR (实时)                   │
       │◄──────────────────────────────────┤
       │   - TerminalLog                   │
       │   - NodeLog                       │
       │   - VariableUpdate                │
       ▼                                   ▼
┌──────────────┐                    ┌──────────────┐
│ Pinia Stores │                    │ DIVERSession │
└──────────────┘                    │   MCUNode[]  │
                                    └──────────────┘
```

---

## API 端点

### 节点管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/node/probe` | POST | Probe 并加入 Session |
| `/api/node/remove` | POST | 从 Session 移除 |
| `/api/node/poll-state` | POST | 获取单节点状态 |
| `/api/nodes/state` | GET | 获取所有节点状态 |

### 运行控制

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/start` | POST | 启动执行 |
| `/api/stop` | POST | 停止执行 |
| `/api/build` | POST | 编译逻辑 |

### 项目管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/project` | GET/POST | 项目状态 |
| `/api/project/export` | GET | 导出 ZIP |
| `/api/project/import` | POST | 导入 ZIP |
| `/api/files/tree` | GET | 文件树 |
| `/api/files/read` | GET | 读取文件 |
| `/api/files/write` | POST | 写入文件 |

### 变量控制

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/variable/set` | POST | 设置变量值 |
| `/api/variables/controllable` | GET | 可控变量列表 |

---

## 前端开发

### Store 职责

| Store | 职责 |
|-------|------|
| `project` | 节点图配置、selectedAsset |
| `runtime` | 连接状态、节点状态、变量值 |
| `files` | 文件树、打开的 Tab、buildVersion |
| `logs` | 终端日志、节点日志 |
| `ui` | 通知、对话框 |

### 关键组件

| 组件 | 说明 |
|------|------|
| `GraphCanvas.vue` | Vue-flow 节点图画布 |
| `CoralNodeView.vue` | MCU 节点视图（状态 Badge、端口配置） |
| `TerminalPanel.vue` | 终端 + 控制按钮 |
| `VariablePanel.vue` | 变量表格 + 内联编辑 |

### SignalR 事件

| 事件 | 方向 | 数据 |
|------|------|------|
| `TerminalLog` | Server→Client | 终端日志行 |
| `NodeLog` | Server→Client | 节点日志 |
| `VariableUpdate` | Server→Client | 变量值更新 |

---

## 后端服务

| 服务 | 职责 |
|------|------|
| `RuntimeSessionService` | 管理 DIVERSession、节点状态、日志缓存 |
| `ProjectStore` | 项目状态持久化 |
| `DiverBuildService` | C# → MCU 编译 |
| `TerminalBroadcaster` | SignalR 消息推送 |

---

## 常见问题

### 节点状态显示 Offline

- 检查 Probe 是否成功（查看终端日志）
- 确认 DIVERSession 中有节点（`/api/nodes/state`）

### Start 失败

- 确保已 Build 且选择了 Logic
- 检查节点端口配置是否正确

### 变量不更新

- 检查 SignalR 连接状态
- 确认变量不是 LowerIO（只读）

---

## 技术栈

- **前端**: Vue 3, Vite 7, TypeScript, Pinia, Vue-flow, Naive UI
- **后端**: ASP.NET Core 8, SignalR
- **SDK**: CoralinkerSDK (DIVERSession, MCUNode, HostRuntime)
