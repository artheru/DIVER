# Coralinker DIVER Host - 实现总结

**更新日期**: 2026-01-21

---

## 核心功能

| 功能 | 状态 | 说明 |
|------|------|------|
| 离线 Web 应用 | ✅ | 无 CDN 依赖 |
| 节点图编辑器 | ✅ | Vue-flow 实现 |
| 文件管理 | ✅ | 资源树 + 代码编辑器 |
| 编译系统 | ✅ | C# → MCU 字节码 |
| 变量控制 | ✅ | 实时编辑可控变量 |
| 日志系统 | ✅ | 终端 + 节点日志 |
| 项目管理 | ✅ | 保存/加载/导出 ZIP |
| SignalR 实时通信 | ✅ | 日志推送、变量更新 |

---

## 最新重构 (2026-01-21)

### 节点状态管理

**问题**：节点添加后显示 Offline，Start 前无法获取真实状态

**解决方案**：

1. **Probe 后保持连接**：调用 `AddAndConnectNode()` 将节点加入 DIVERSession
2. **状态轮询**：DIVERSession 后台线程每 1.2 秒刷新节点状态
3. **Start 时复用连接**：使用 `ConfigureConnectedNodes()` 配置程序，不断开现有连接
4. **Stop 后保持连接**：停止执行但不断开，继续显示状态

### 新流程

```
Probe 节点 ─────────────────────────────────────────────────────
    │  调用 /api/node/probe                                      
    ▼                                                            
节点加入 DIVERSession 并保持连接                                  
    │  AddAndConnectNode()                                       
    ▼                                                            
状态轮询获取真实状态（Idle/Running）                              
    │  DIVERSession.StateLoop 每 1.2 秒刷新                      
    ▼                                                            
Start ─────────────────────────────────────────────────────────  
    │  ConfigureConnectedNodes (配置程序)                        
    │  ConfigureAndProgramAll (配置端口 + 下载)                  
    │  StartAll (启动执行)                                       
    ▼                                                            
Stop ──────────────────────────────────────────────────────────  
    │  StopAll (停止执行，保持连接)                               
    ▼                                                            
节点回到 Idle 状态（可继续轮询）                                  
```

### 后端修改

| 文件 | 修改 |
|------|------|
| `DIVERSession.cs` | 添加 `AddAndConnectNode`, `GetNodeByUri`, `ConfigureConnectedNodes` |
| `RuntimeSessionService.cs` | 添加 `AddAndConnectNodeAsync`, `RemoveNodeAsync`, 修改 `StartFullAsync` |
| `ApiRoutes.cs` | 修改 `/api/node/probe` 保持连接, 添加 `/api/node/remove` |

### 前端修改

| 文件 | 修改 |
|------|------|
| `GraphCanvas.vue` | 节点状态轮询（Start 前） |
| `CoralNodeView.vue` | 状态 Badge 显示（Idle/Running/Configured/Programmed） |
| `TerminalPanel.vue` | 移除 Connect 按钮，Start 处理完整流程 |
| `runtime.ts` | 简化 `start()` 方法 |
| `files.ts` | 添加 `buildVersion` 触发 Logic 列表刷新 |

---

## API 端点

### 节点管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/node/probe` | POST | Probe 并保持连接 |
| `/api/node/remove` | POST | 从 Session 移除节点 |
| `/api/node/poll-state` | POST | 获取节点状态 |
| `/api/nodes/state` | GET | 获取所有节点状态 |

### 运行控制

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/start` | POST | 完整启动流程 |
| `/api/stop` | POST | 停止执行（保持连接） |
| `/api/build` | POST | 编译逻辑 |

### 项目管理

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/project` | GET/POST | 项目状态 |
| `/api/project/export` | GET | 导出 ZIP |
| `/api/project/import` | POST | 导入 ZIP |

---

## 目录结构

```
3rd/CoralinkerHost/
├── ClientApp/               # Vue 3 前端
│   ├── src/
│   │   ├── api/             # HTTP API
│   │   ├── stores/          # Pinia 状态管理
│   │   ├── components/      # Vue 组件
│   │   │   ├── graph/       # 节点图编辑器
│   │   │   ├── logs/        # 日志面板
│   │   │   └── variables/   # 变量面板
│   │   └── views/           # 页面视图
│   └── vite.config.ts
├── Services/                # 后端服务
│   ├── RuntimeSessionService.cs  # 运行时管理
│   ├── ProjectStore.cs           # 项目存储
│   └── DiverBuildService.cs      # 编译服务
├── Web/
│   └── ApiRoutes.cs         # REST API
└── wwwroot/                 # 前端构建产物
```

---

## 运行方式

```powershell
# 开发模式（前端热重载）
cd ClientApp
npm run dev

# 生产构建
npm run build

# 启动后端
cd ..
dotnet run
```

**访问地址**: http://localhost:4499 (生产) 或 http://localhost:5173 (开发)

---

## 状态: 可用于测试

- ✅ 节点 Probe 后保持连接
- ✅ 实时状态显示（Idle/Running）
- ✅ Start/Stop 正常工作
- ✅ Build 后 Logic 列表自动刷新
