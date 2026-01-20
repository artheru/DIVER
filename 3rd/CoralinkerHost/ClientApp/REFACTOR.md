# CoralinkerHost 前端 Vue 3 重构方案

## 一、现有功能清单

### 1. 核心功能模块

| 模块 | 描述 | 当前实现位置 |
|------|------|-------------|
| **节点图编辑器** | LiteGraph 画布，DPI缩放，节点拖拽/缩放 | `app.js` L130-700 |
| **资源管理器** | Windows Explorer 风格树形结构 | `app.js` L1200-1500 |
| **代码编辑器** | CodeMirror C# 语法高亮 | `app.js` L1600-1900 |
| **Hex 编辑器** | 二进制文件查看/编辑，数据检查器 | `app.js` L2000-2500 |
| **变量监控** | Cart变量实时显示，可控变量编辑 | `app.js` L2600-2800 |
| **终端日志** | 构建输出，系统消息 | `app.js` L2850-3000 |
| **节点日志** | 每个MCU节点独立日志面板 | `app.js` L3000-3200 |
| **控制面板** | 滑块/摇杆/开关控件 | `controlPanel.html` |
| **项目管理** | 新建/保存/导出/导入 | `app.js` L700-900 |

### 2. 自定义节点类型

| 节点类型 | 属性 | 功能 |
|----------|------|------|
| `coral/root` | - | 根节点，PC端锚点 |
| `coral/node` | nodeName, mcuUri, logicName | MCU节点，显示状态/版本/布局 |

---

## 二、后端 API 规范

### REST API

```yaml
# 项目管理
GET  /api/project                    # 获取项目状态 → ProjectState
POST /api/project                    # 更新项目状态
POST /api/project/new                # 创建新项目（清空assets）
POST /api/project/save               # 持久化到磁盘
GET  /api/project/export             # 导出ZIP压缩包

# 文件操作
GET  /api/files/tree                 # 获取资源树 → FileNode[]
GET  /api/files/read?path=xxx        # 读取文件 → {kind:"text"|"binary", text?, base64?}
POST /api/files/write                # 写入文件 {path, kind, text?, base64?}
POST /api/files/delete               # 删除文件 {path}
POST /api/files/newInput             # 新建.cs文件 {name, template?}

# 资源管理
GET  /api/assets                     # 列出所有资源
POST /api/assets/upload              # 上传资源 (multipart/form-data)
DELETE /api/assets/{name}            # 删除资源

# 构建与运行
POST /api/build                      # 编译选中的.cs → {ok, buildId, artifacts[]}
POST /api/connect                    # 连接所有节点
POST /api/start                      # 启动执行
POST /api/stop                       # 停止执行
GET  /api/runtime                    # 获取运行时快照

# 变量控制
GET  /api/variables/controllable     # 获取可控变量列表
POST /api/variable/set               # 设置变量值 {name, value, typeHint?}

# 节点日志
GET  /api/logs/nodes                 # 获取有日志的节点列表
GET  /api/logs/node/{nodeId}?offset=0&limit=200  # 分页获取节点日志
POST /api/logs/node/{nodeId}/clear   # 清空节点日志
POST /api/logs/clear                 # 清空所有日志

# 节点配置
POST /api/node/{nodeId}/ports        # 配置节点端口 {ports[]}
POST /api/command                    # 发送命令 {command}
```

### SignalR Hub (`/hubs/terminal`)

```typescript
// Server → Client 事件
interface SignalREvents {
  terminalLine: (line: string) => void;           // 终端输出
  nodeLogLine: (nodeId: string, line: string) => void;  // 节点日志
  variables: (snapshot: VariableSnapshot) => void;      // 变量更新
  runtimeUpdate: (snapshot: RuntimeSnapshot) => void;   // 运行时状态
}
```

### 数据类型

```typescript
interface ProjectState {
  nodeMap: string;              // LiteGraph序列化JSON
  selectedAsset: string | null; // 当前选中的.cs文件
  selectedFile: string | null;  // 当前选中的文件路径
  lastBuildId: string | null;
}

interface FileNode {
  name: string;
  path: string;
  kind: "folder" | "file";
  children?: FileNode[];
}

interface VariableInfo {
  name: string;
  type: string;
  typeId: number;
  controllable: boolean;
  isLowerIO: boolean;
  isUpperIO: boolean;
  isMutual: boolean;
}

interface RuntimeSnapshot {
  nodes: NodeSnapshot[];
}

interface NodeSnapshot {
  nodeId: string;
  runState: string;
  isConfigured: boolean;
  isProgrammed: boolean;
  mode: string;
  version?: VersionInfo;
  layout?: LayoutInfo;
  ports?: PortInfo[];
}
```

---

## 三、Vue 3 技术栈选型

### 核心依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| **Vue 3** | ^3.4 | 框架核心 |
| **Vite** | ^5.0 | 构建工具 |
| **TypeScript** | ^5.3 | 类型安全 |
| **Pinia** | ^2.1 | 状态管理 |
| **VueUse** | ^10.7 | 组合式工具函数 |
| **Naive UI** | ^2.38 | UI组件库 |

### 为什么选择 Naive UI

1. **轻量级** - 支持 Tree Shaking，按需引入
2. **暗色主题** - 原生支持，适合IDE风格
3. **TypeScript 优先** - 完整类型定义
4. **组件丰富** - Tree, Table, Slider, Tabs 等都有
5. **设计简洁** - 不花哨，适合工具类应用
6. **无依赖** - 不需要额外的样式框架

### 关于 Pug

**建议不使用 Pug**，原因：
1. AI 生成/理解 HTML 更准确
2. 团队协作更友好
3. Vue 生态工具链更好支持
4. 节省的字符数有限，但增加了认知负担

---

## 四、项目目录结构

```
3rd/CoralinkerHost/
├── ClientApp/                      # Vue 3 前端项目
│   ├── src/
│   │   ├── api/                    # API 封装层
│   │   │   ├── index.ts            # Axios 实例配置
│   │   │   ├── project.ts          # 项目相关 API
│   │   │   ├── files.ts            # 文件操作 API
│   │   │   ├── runtime.ts          # 运行时 API
│   │   │   └── variables.ts        # 变量控制 API
│   │   │
│   │   ├── composables/            # 组合式函数 (Hooks)
│   │   │   ├── useSignalR.ts       # SignalR 连接管理
│   │   │   ├── useLiteGraph.ts     # LiteGraph 封装
│   │   │   ├── useHexEditor.ts     # Hex 编辑器逻辑
│   │   │   ├── useAutoSave.ts      # 自动保存逻辑
│   │   │   └── useNodeLogs.ts      # 节点日志管理
│   │   │
│   │   ├── stores/                 # Pinia 状态管理
│   │   │   ├── project.ts          # 项目状态
│   │   │   ├── runtime.ts          # 运行时状态 (节点、变量)
│   │   │   ├── files.ts            # 文件树状态
│   │   │   ├── logs.ts             # 日志状态
│   │   │   └── ui.ts               # UI 状态 (当前Tab, 选中项等)
│   │   │
│   │   ├── components/             # 通用组件
│   │   │   ├── common/
│   │   │   │   ├── AppDialog.vue   # 通用对话框
│   │   │   │   ├── IconButton.vue  # 图标按钮
│   │   │   │   └── SyncIndicator.vue
│   │   │   │
│   │   │   ├── graph/              # 节点图相关
│   │   │   │   ├── GraphCanvas.vue # LiteGraph 画布容器
│   │   │   │   ├── GraphToolbox.vue
│   │   │   │   └── NodeConfigModal.vue
│   │   │   │
│   │   │   ├── editor/             # 编辑器相关
│   │   │   │   ├── CodeEditor.vue  # CodeMirror 封装
│   │   │   │   ├── HexEditor.vue   # Hex 编辑器
│   │   │   │   ├── HexInspector.vue
│   │   │   │   └── EditorTabs.vue
│   │   │   │
│   │   │   ├── assets/             # 资源管理
│   │   │   │   ├── AssetTree.vue
│   │   │   │   ├── AssetDropZone.vue
│   │   │   │   └── NewFileDialog.vue
│   │   │   │
│   │   │   ├── variables/          # 变量监控
│   │   │   │   ├── VariablePanel.vue
│   │   │   │   ├── VariableRow.vue
│   │   │   │   └── VariableEditor.vue
│   │   │   │
│   │   │   ├── logs/               # 日志面板
│   │   │   │   ├── LogTabs.vue
│   │   │   │   ├── TerminalPane.vue
│   │   │   │   └── NodeLogPane.vue
│   │   │   │
│   │   │   └── control/            # 控制面板组件
│   │   │       ├── SliderWidget.vue
│   │   │       ├── JoystickWidget.vue
│   │   │       ├── SwitchWidget.vue
│   │   │       └── WidgetConfigDialog.vue
│   │   │
│   │   ├── layouts/                # 页面布局
│   │   │   └── MainLayout.vue
│   │   │
│   │   ├── views/                  # 页面视图
│   │   │   ├── HomeView.vue        # 主页面 (编辑器)
│   │   │   └── ControlPanelView.vue # 控制面板页面
│   │   │
│   │   ├── types/                  # TypeScript 类型定义
│   │   │   ├── project.ts
│   │   │   ├── runtime.ts
│   │   │   ├── files.ts
│   │   │   └── litegraph.d.ts
│   │   │
│   │   ├── utils/                  # 工具函数
│   │   │   ├── format.ts           # 格式化 (bytes, hex等)
│   │   │   ├── typeMapping.ts      # 变量类型映射
│   │   │   └── debounce.ts
│   │   │
│   │   ├── styles/                 # 全局样式
│   │   │   ├── variables.css       # CSS 变量定义
│   │   │   ├── litegraph.css       # LiteGraph 覆盖样式
│   │   │   └── main.css
│   │   │
│   │   ├── App.vue                 # 根组件
│   │   ├── main.ts                 # 入口文件
│   │   └── router.ts               # 路由配置
│   │
│   ├── public/
│   │   └── lib/                    # 静态库文件
│   │       ├── litegraph.min.js
│   │       └── codemirror/
│   │
│   ├── index.html
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── package.json
│
├── wwwroot/                        # Vite 构建输出目录
│   └── (构建产物)
│
└── (后端代码不变)
```

---

## 五、组件拆分详解

### 1. 状态管理 (Pinia Stores)

```typescript
// stores/runtime.ts
export const useRuntimeStore = defineStore('runtime', () => {
  // 节点状态
  const nodes = ref<Map<string, NodeSnapshot>>(new Map())
  
  // 变量状态
  const variables = ref<Map<string, VariableValue>>(new Map())
  const controllableVars = ref<Set<string>>(new Set())
  
  // 运行状态
  const isConnected = ref(false)
  const isRunning = ref(false)
  
  // Actions
  async function connect() { ... }
  async function start() { ... }
  async function stop() { ... }
  async function setVariable(name: string, value: any, type: string) { ... }
  
  return { nodes, variables, controllableVars, isConnected, isRunning, connect, start, stop, setVariable }
})

// stores/logs.ts
export const useLogStore = defineStore('logs', () => {
  const terminalLines = ref<string[]>([])
  const nodeLogs = ref<Map<string, string[]>>(new Map())
  const activeTab = ref<string>('terminal')
  
  const MAX_LINES = 2000
  
  function appendTerminal(line: string) {
    terminalLines.value.push(line)
    if (terminalLines.value.length > MAX_LINES) {
      terminalLines.value.splice(0, terminalLines.value.length - MAX_LINES)
    }
  }
  
  function appendNodeLog(nodeId: string, line: string) { ... }
  
  return { terminalLines, nodeLogs, activeTab, appendTerminal, appendNodeLog }
})
```

### 2. 组合式函数 (Composables)

```typescript
// composables/useSignalR.ts
export function useSignalR() {
  const logStore = useLogStore()
  const runtimeStore = useRuntimeStore()
  
  const connection = ref<HubConnection | null>(null)
  const isConnected = ref(false)
  
  async function connect() {
    connection.value = new HubConnectionBuilder()
      .withUrl('/hubs/terminal')
      .withAutomaticReconnect()
      .build()
    
    connection.value.on('terminalLine', (line) => {
      logStore.appendTerminal(line)
    })
    
    connection.value.on('nodeLogLine', (nodeId, line) => {
      logStore.appendNodeLog(nodeId, line)
    })
    
    connection.value.on('variables', (snapshot) => {
      runtimeStore.updateVariables(snapshot)
    })
    
    await connection.value.start()
    isConnected.value = true
  }
  
  onMounted(connect)
  onUnmounted(() => connection.value?.stop())
  
  return { connection, isConnected }
}

// composables/useLiteGraph.ts
export function useLiteGraph(canvasRef: Ref<HTMLCanvasElement | null>) {
  const graph = shallowRef<LGraph | null>(null)
  const canvas = shallowRef<LGraphCanvas | null>(null)
  
  function initGraph() {
    if (!canvasRef.value) return
    
    graph.value = new LGraph()
    canvas.value = new LGraphCanvas(canvasRef.value, graph.value)
    
    // 注册自定义节点
    registerCustomNodes()
    
    // 安装增强功能
    installDpiScaling(canvas.value)
    installInlinePromptEditor(canvas.value)
    installNodeResizing(canvas.value)
  }
  
  function serialize(): string {
    return JSON.stringify(graph.value?.serialize())
  }
  
  function deserialize(data: string) {
    graph.value?.configure(JSON.parse(data))
  }
  
  onMounted(initGraph)
  
  return { graph, canvas, serialize, deserialize }
}
```

### 3. 核心组件示例

```vue
<!-- components/graph/GraphCanvas.vue -->
<template>
  <div class="graph-container" ref="containerRef">
    <GraphToolbox @add-node="addNode" />
    <canvas ref="canvasRef" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useLiteGraph } from '@/composables/useLiteGraph'
import { useProjectStore } from '@/stores/project'
import { storeToRefs } from 'pinia'

const containerRef = ref<HTMLDivElement>()
const canvasRef = ref<HTMLCanvasElement>()

const { graph, canvas, serialize, deserialize } = useLiteGraph(canvasRef)
const projectStore = useProjectStore()
const { nodeMap } = storeToRefs(projectStore)

// 加载项目时恢复节点图
watch(nodeMap, (map) => {
  if (map) deserialize(map)
}, { immediate: true })

// 节点变化时自动保存
function onGraphChange() {
  projectStore.setNodeMap(serialize())
}

function addNode() {
  // 添加 coral/node 节点
}

defineExpose({ graph, canvas, serialize })
</script>

<style scoped>
.graph-container {
  position: relative;
  width: 100%;
  height: 100%;
}

canvas {
  width: 100%;
  height: 100%;
}
</style>
```

```vue
<!-- components/variables/VariablePanel.vue -->
<template>
  <div class="variable-panel">
    <n-data-table
      :columns="columns"
      :data="variableList"
      :row-key="row => row.name"
      size="small"
      striped
    />
  </div>
</template>

<script setup lang="ts">
import { computed, h } from 'vue'
import { NDataTable, NButton, NInput, NIcon } from 'naive-ui'
import { EditOutlined, LockOutlined } from '@vicons/antd'
import { useRuntimeStore } from '@/stores/runtime'
import { storeToRefs } from 'pinia'

const runtimeStore = useRuntimeStore()
const { variables, controllableVars } = storeToRefs(runtimeStore)

const variableList = computed(() => 
  Array.from(variables.value.entries()).map(([name, val]) => ({
    name,
    value: val.value,
    type: val.type,
    controllable: controllableVars.value.has(name)
  }))
)

const columns = [
  { title: 'Name', key: 'name', width: 200 },
  { title: 'Type', key: 'type', width: 100 },
  { 
    title: 'Value', 
    key: 'value',
    render: (row) => row.controllable 
      ? h(VariableEditor, { variable: row })
      : h('span', row.value)
  },
  {
    title: '',
    key: 'action',
    width: 40,
    render: (row) => h(NIcon, {}, {
      default: () => h(row.controllable ? EditOutlined : LockOutlined)
    })
  }
]
</script>
```

---

## 六、迁移步骤

### Phase 1: 环境搭建 (Day 1)

```bash
cd 3rd/CoralinkerHost
npm create vite@latest ClientApp -- --template vue-ts
cd ClientApp
npm install
npm install pinia @vueuse/core naive-ui axios @microsoft/signalr
npm install -D @types/node sass
```

配置 `vite.config.ts`:
```typescript
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src')
    }
  },
  server: {
    proxy: {
      '/api': 'http://localhost:4499',
      '/hubs': {
        target: 'http://localhost:4499',
        ws: true
      }
    }
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true
  }
})
```

### Phase 2: 核心架构 (Day 2-3)

1. 创建 Pinia stores 骨架
2. 实现 API 封装层
3. 实现 `useSignalR` 组合式函数
4. 创建基础布局组件

### Phase 3: 节点图迁移 (Day 4-5)

1. 封装 `useLiteGraph` 
2. 迁移自定义节点类型
3. 迁移 DPI 缩放、内联编辑、节点缩放功能
4. 实现 `GraphCanvas.vue`

### Phase 4: 编辑器迁移 (Day 6-7)

1. 封装 CodeMirror 为 Vue 组件
2. 迁移 Hex 编辑器逻辑
3. 实现 Tab 系统

### Phase 5: 功能面板迁移 (Day 8-9)

1. 资源树组件
2. 变量监控面板
3. 日志面板（Terminal + Node Logs）
4. 控制面板页面

### Phase 6: 测试与优化 (Day 10)

1. 端到端测试
2. 性能优化
3. 清理旧代码

---

## 七、ASP.NET Core 集成

### 开发模式

修改 `Program.cs` 添加 Vite 开发服务器代理:

```csharp
// 开发模式下，前端由 Vite 提供
if (app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "ClientApp";
        spa.UseViteDevelopmentServer();
    });
}
else
{
    app.UseStaticFiles(); // 生产模式直接使用 wwwroot
}
```

### 生产构建

```bash
cd ClientApp
npm run build  # 输出到 ../wwwroot
```

---

## 八、预期收益

| 维度 | 现状 | 重构后 |
|------|------|--------|
| **代码组织** | 单文件 3200+ 行 | 50+ 个专职组件 |
| **可维护性** | 修改一处影响多处 | 组件独立，松耦合 |
| **类型安全** | 无类型检查 | 完整 TypeScript |
| **状态管理** | 全局变量散落 | Pinia 集中管理 |
| **样式隔离** | 全局 CSS 污染 | Scoped CSS |
| **开发体验** | 刷新整页 | HMR 热更新 |
| **可测试性** | 几乎无法测试 | 组件可独立测试 |

---

## 九、下一步行动

确认此方案后，我将：

1. **初始化 Vite + Vue 3 项目**
2. **创建基础架构** (stores, api, composables)
3. **逐步迁移组件**，保持功能可用
4. **删除旧的 app.js/app.css**

你可以选择：
- **A. 全量重构** - 完全按此方案执行
- **B. 渐进迁移** - 先迁移核心组件，其他保持原状
- **C. 调整方案** - 对技术选型或目录结构有修改意见

请确认或提出修改意见。
