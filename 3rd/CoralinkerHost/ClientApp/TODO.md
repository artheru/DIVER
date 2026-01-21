# Coralinker Frontend 重构 TODO

## 一、调研结果摘要

### 1.1 后端架构

#### API 路由 (ApiRoutes.cs)
- `/api/connect` - 连接节点：配置 Session → ConnectAll
- `/api/start` - 启动执行：ConfigureAndProgramAll → StartAll
- `/api/stop` - 停止执行：StopAll → DisconnectAll → ClearNodes
- `/api/node/probe` - 探测节点（添加前验证MCU可用性）
- `/api/runtime` - 获取运行时快照

#### DIVERSession (DIVERSession.cs)
- 已有后台状态轮询线程 `StateLoop()`，每 1.2 秒调用 `RefreshState()`
- `ConnectAll()` 后会自动调用 `StartStatePolling()`
- 状态轮询只在 Connect 后启动

#### MCUNode (MCUNode.cs)
- `Connect()` - Open + Reset + GetVersion + GetLayout + GetState
- `Configure()` - 配置端口
- `Program()` - 下载字节码
- `Start()` - 启动执行
- `RefreshState()` - 调用 GetState 更新状态
- `State` 属性包含: `RunningState`, `IsConfigured`, `IsProgrammed`, `Mode`

### 1.2 前端架构

#### 运行时状态 (runtime.ts store)
- `appState`: 'offline' | 'idle' | 'connecting' | 'running' | 'stopping'
- `connect()` - 调用 `/api/connect`
- `start()` - 调用 `/api/start`
- `stop()` - 调用 `/api/stop`

#### 节点视图 (CoralNodeView.vue)
- 当前状态指示器用三个小点表示：runState, configured, programmed
- 状态数据来自 `props.data`，由 GraphCanvas 传入
- 未实现定期状态刷新

#### 终端面板 (TerminalPanel.vue)
- 有 Connect / Start / Stop 三个按钮
- Connect 调用 `runtimeStore.connect()`
- Start 调用 `runtimeStore.start()`

---

## 二、问题分析

### 2.1 当前问题

1. **节点添加后无状态同步**：节点通过 Probe 添加后，不会定期获取状态，只能等 Connect/Start
2. **Connect 按钮冗余**：所有节点已通过 Probe 验证，Connect 不是必要的单独步骤
3. **状态显示不清晰**：三个小点不够直观，用户不知道当前是什么状态
4. **Start 流程不完整**：Start 依赖于先 Connect，应该合并为一个完整流程

### 2.2 目标状态

1. **实时状态监控**：节点添加后立即开始轮询状态
2. **简化操作**：移除 Connect 按钮，Start 执行完整流程
3. **清晰显示**：用文字显示 Mode、Configured、Programmed 状态
4. **超时处理**：GetState 超时时显示 Offline

---

## 三、后端修改

### 3.1 新增节点状态轮询 API

**文件**: `3rd/CoralinkerHost/Web/ApiRoutes.cs`

```csharp
// 新增：获取所有节点状态
app.MapGet("/api/nodes/state", (RuntimeSessionService runtime) =>
{
    var states = runtime.GetAllNodeStates();
    return Results.Json(new { ok = true, nodes = states });
});

// 新增：获取单个节点状态（通过临时连接）
app.MapGet("/api/node/{nodeId}/state", async (string nodeId, ProjectStore store, CancellationToken ct) =>
{
    // 从 project.nodeMap 中获取节点的 mcuUri
    // 创建临时 MCUNode 连接并获取状态
    // 返回状态信息
});
```

### 3.2 修改 RuntimeSessionService

**文件**: `3rd/CoralinkerHost/Services/RuntimeSessionService.cs`

添加方法：
```csharp
// 获取所有已连接节点的状态
public IReadOnlyList<NodeStateInfo> GetAllNodeStates()
{
    return _session.Nodes.Values
        .Select(node => new NodeStateInfo(
            node.NodeId,
            node.IsConnected,
            node.State?.RunningState.ToString() ?? "Offline",
            node.State?.IsConfigured != 0,
            node.State?.IsProgrammed != 0,
            node.State?.Mode.ToString() ?? "Unknown"
        ))
        .ToList();
}
```

### 3.3 新增独立节点状态服务（可选）

考虑为未连接的节点提供状态查询：
- 在节点添加（Probe成功）后，可以定期通过临时连接获取状态
- 或者在前端发起请求时，后端临时连接获取状态

---

## 四、前端修改

### 4.1 移除 Connect 按钮

**文件**: `3rd/CoralinkerHost/ClientApp/src/components/logs/TerminalPanel.vue`

修改前（第63-72行）：
```vue
<button 
  class="action-btn connect" 
  :class="{ active: isConnected }"
  :disabled="!canConnect || isConnecting"
  @click="handleConnect" 
  title="Connect to MCU nodes (Open)"
>
  <span class="btn-icon">🔌</span>
  <span class="btn-text">{{ isConnected ? 'Connected' : 'Connect' }}</span>
</button>
```

修改后：删除整个 Connect 按钮

同时删除相关方法：
- `isConnecting` 状态
- `handleConnect()` 方法
- `canConnect` 引用

### 4.2 修改 Start 按钮行为

**文件**: `3rd/CoralinkerHost/ClientApp/src/stores/runtime.ts`

修改 `start()` 方法，执行完整流程：

```typescript
async function start() {
  if (!canStart.value) {
    throw new Error('Cannot start in current state')
  }
  
  try {
    appState.value = 'connecting'
    
    // 1. Connect (Open + Reset + GetVersion)
    const connectResult = await runtimeApi.connect()
    if (!connectResult.ok) {
      throw new Error(connectResult.error || 'Connection failed')
    }
    
    // 2. Start (Configure + Program + Start)
    const startResult = await runtimeApi.start()
    if (startResult.ok) {
      isRunning.value = true
      appState.value = 'running'
      
      // 获取可控变量列表
      await fetchControllableVariables()
      
      console.log('[Runtime] Started')
    }
    return startResult
  } catch (error) {
    appState.value = 'idle'
    console.error('[Runtime] Start failed:', error)
    throw error
  }
}
```

### 4.3 改进节点状态显示

**文件**: `3rd/CoralinkerHost/ClientApp/src/components/graph/CoralNodeView.vue`

修改前（第48-54行）：
```vue
<div class="status-indicators">
  <span class="status-dot" :class="runStateClass" :title="runStateText"></span>
  <span class="status-dot configured" :class="{ active: data.isConfigured }" title="Configured"></span>
  <span class="status-dot programmed" :class="{ active: data.isProgrammed }" title="Programmed"></span>
</div>
```

修改后：
```vue
<!-- 状态行：单独一行显示状态 -->
<div class="node-status-row">
  <span class="status-badge" :class="runStateBadgeClass">
    {{ runStateText }}
  </span>
  <span class="status-badge" :class="{ active: data.isConfigured }">
    {{ data.isConfigured ? 'Configured' : 'Not Configured' }}
  </span>
  <span class="status-badge" :class="{ active: data.isProgrammed }">
    {{ data.isProgrammed ? 'Programmed' : 'Not Programmed' }}
  </span>
</div>
```

添加样式：
```css
.node-status-row {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  padding: 6px 12px;
  background: rgba(0, 0, 0, 0.2);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.status-badge {
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 10px;
  font-weight: 500;
  background: rgba(100, 116, 139, 0.3);
  color: #94a3b8;
}

.status-badge.active {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
}

.status-badge.offline {
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
}

.status-badge.idle {
  background: rgba(245, 158, 11, 0.2);
  color: #f59e0b;
}

.status-badge.running {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
}
```

### 4.4 添加节点状态轮询

**方案A：使用 SignalR 推送**

**文件**: `3rd/CoralinkerHost/ClientApp/src/composables/useSignalR.ts`

添加节点状态更新处理：
```typescript
// 监听节点状态变化
connection.on('NodeStateUpdate', (nodeId: string, state: NodeStateInfo) => {
  runtimeStore.updateNodeState(nodeId, state)
})
```

**方案B：前端定时轮询**

**文件**: `3rd/CoralinkerHost/ClientApp/src/stores/runtime.ts`

添加状态轮询：
```typescript
let statePollingTimer: number | null = null

function startStatePolling() {
  if (statePollingTimer) return
  
  statePollingTimer = window.setInterval(async () => {
    try {
      const states = await runtimeApi.getNodeStates()
      for (const state of states.nodes) {
        updateNodeState(state.nodeId, state)
      }
    } catch (error) {
      console.error('[Runtime] State polling failed:', error)
    }
  }, 2000) // 每2秒轮询
}

function stopStatePolling() {
  if (statePollingTimer) {
    clearInterval(statePollingTimer)
    statePollingTimer = null
  }
}
```

### 4.5 新增 runtime API

**文件**: `3rd/CoralinkerHost/ClientApp/src/api/runtime.ts`

```typescript
/**
 * 获取所有节点状态
 */
export async function getNodeStates(): Promise<{ ok: boolean; nodes: NodeStateInfo[] }> {
  return get('/api/nodes/state')
}
```

### 4.6 新增类型定义

**文件**: `3rd/CoralinkerHost/ClientApp/src/types/index.ts`

```typescript
/**
 * 节点状态信息
 */
export interface NodeStateInfo {
  nodeId: string
  isConnected: boolean
  runState: 'Offline' | 'Idle' | 'Running' | 'Error'
  isConfigured: boolean
  isProgrammed: boolean
  mode: string
}
```

---

## 五、控制流程重构

### 5.1 Connect 按钮（已移除）

- [x] **5.1.1** Connect 只负责 Open 连接（调用 `/api/connect`）
- [x] **5.1.2** 成功后更新状态为 Idle
- [ ] **5.1.3** 移除 Connect 按钮，合并到 Start 流程

### 5.2 Start 按钮

- [x] **5.2.1** Start 执行完整流程：Configure → Program → Start
- [x] **5.2.2** 调用顺序：后端 `/api/start` 会处理完整流程
- [x] **5.2.3** 任一步骤失败则终止并报错
- [x] **5.2.4** 成功后更新状态为 Running
- [ ] **5.2.5** 修改前端 Start，先调用 Connect 再调用 Start

### 5.3 Stop 按钮

- [x] **5.3.1** Stop 调用 `/api/stop`
- [x] **5.3.2** 成功后更新状态为 Idle

---

## 六、实施计划

### 第一阶段：改进状态显示（前端）

1. [ ] 修改 `CoralNodeView.vue` 状态显示区域
2. [ ] 添加状态文字和样式
3. [ ] 测试显示效果

### 第二阶段：简化操作按钮（前端）

1. [ ] 修改 `TerminalPanel.vue` 移除 Connect 按钮
2. [ ] 修改 `runtime.ts` store，合并 Connect 到 Start 流程
3. [ ] 测试 Start/Stop 功能

### 第三阶段：实现状态轮询

1. [ ] 后端：添加节点状态 API 或 SignalR 推送
2. [ ] 前端：实现状态轮询/监听
3. [ ] 更新节点状态显示
4. [ ] 处理超时情况（显示 Offline）

---

## 七、代码修改清单

| 文件 | 修改类型 | 描述 |
|------|----------|------|
| `ClientApp/src/components/logs/TerminalPanel.vue` | 删除 | 移除 Connect 按钮及相关代码 |
| `ClientApp/src/components/graph/CoralNodeView.vue` | 修改 | 改进状态显示为文字形式 |
| `ClientApp/src/stores/runtime.ts` | 修改 | Start 方法合并 Connect 流程 |
| `ClientApp/src/api/runtime.ts` | 新增 | 添加节点状态查询 API |
| `ClientApp/src/types/index.ts` | 新增 | 添加 NodeStateInfo 类型 |
| `CoralinkerHost/Web/ApiRoutes.cs` | 新增 | 添加节点状态 API |
| `CoralinkerHost/Services/RuntimeSessionService.cs` | 新增 | 添加状态查询方法 |

---

## 八、注意事项

1. **状态轮询频率**：不宜过快（建议 2 秒），避免串口占用冲突
2. **超时处理**：GetState 失败时应标记为 Offline，而非报错
3. **UI 响应**：轮询更新应平滑，避免 UI 闪烁
4. **资源清理**：组件卸载时停止轮询

---

## 九、测试要点

1. [ ] 节点添加后状态显示正确
2. [ ] Start 按钮执行完整流程
3. [ ] 状态文字正确显示 Mode/Configured/Programmed
4. [ ] 节点断开后显示 Offline
5. [ ] Stop 后状态重置为 Idle
6. [ ] 多节点场景测试
