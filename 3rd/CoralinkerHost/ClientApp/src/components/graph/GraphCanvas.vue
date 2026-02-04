<!--
  @file components/graph/GraphCanvas.vue
  @description 节点图画布组件 - 使用 vue-flow
  
  核心功能：
  1. 使用 vue-flow 渲染节点图
  2. 自定义节点类型 (CoralNode, RootNode)
  3. 节点连接管理
  4. 与 store 同步
-->

<template>
  <div class="graph-canvas-wrapper">
    <VueFlow
      v-model:nodes="nodes"
      v-model:edges="edges"
      :node-types="nodeTypes"
      :default-viewport="{ x: 50, y: 50, zoom: 1 }"
      :min-zoom="0.25"
      :max-zoom="2"
      :snap-to-grid="true"
      :snap-grid="[20, 20]"
      :connection-line-style="{ stroke: '#4f8cff', strokeWidth: 2 }"
      :default-edge-options="defaultEdgeOptions"
      :nodes-draggable="!isRunning"
      :nodes-connectable="!isRunning"
      :elements-selectable="!isRunning"
      fit-view-on-init
      @nodes-change="onNodesChange"
      @edges-change="onEdgesChange"
      @connect="onConnect"
      @node-drag-stop="onNodeDragStop"
    >
      <!-- 背景网格 -->
      <Background :variant="BackgroundVariant.Dots" :gap="20" :size="1" />
      
      <!-- 控制面板 -->
      <Controls position="bottom-left" />
      
      <!-- 小地图 -->
      <MiniMap 
        position="bottom-right" 
        :node-color="miniMapNodeColor"
        :mask-color="'rgba(0, 0, 0, 0.6)'"
      />
    </VueFlow>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, markRaw } from 'vue'
import { 
  VueFlow, 
  useVueFlow,
  type Node, 
  type Edge,
  type Connection,
  type NodeChange,
  type EdgeChange
} from '@vue-flow/core'
import { Background, BackgroundVariant } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import { useLogStore, useRuntimeStore } from '@/stores'
import { storeToRefs } from 'pinia'
import * as deviceApi from '@/api/device'
import CoralNodeView from './CoralNodeView.vue'
import RootNodeView from './RootNodeView.vue'

// 导入 vue-flow 样式
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'

// ============================================
// Store 引用
// ============================================

const logStore = useLogStore()
const runtimeStore = useRuntimeStore()
const { nodeStates, isRunning } = storeToRefs(runtimeStore)

// ============================================
// vue-flow 交互控制
// ============================================

const { setInteractive } = useVueFlow()

// ============================================
// vue-flow 配置
// ============================================

// 自定义节点类型
const nodeTypes: Record<string, any> = {
  'coral-node': markRaw(CoralNodeView),
  'root-node': markRaw(RootNodeView)
}

// 默认边配置
const defaultEdgeOptions = {
  type: 'smoothstep',
  style: { stroke: '#4f8cff', strokeWidth: 2 },
  animated: false
}

// ============================================
// 图数据
// ============================================

const nodes = ref<Node[]>([])
const edges = ref<Edge[]>([])

// 是否正在从 store 加载（防止循环）
let isLoadingFromStore = false

// 节点状态轮询定时器
let nodeStatePollingTimer: ReturnType<typeof setInterval> | null = null
const NODE_STATE_POLLING_INTERVAL = 3000 // 3秒轮询一次

// ============================================
// 节点状态轮询
// ============================================

/**
 * 启动节点状态轮询
 * 定期查询所有 coral-node 节点的状态
 */
function startNodeStatePolling() {
  if (nodeStatePollingTimer) return
  
  console.log('[Graph] Starting node state polling')
  
  // 立即执行一次
  pollAllNodeStates()
  
  // 设置定时器
  nodeStatePollingTimer = setInterval(() => {
    pollAllNodeStates()
  }, NODE_STATE_POLLING_INTERVAL)
}

/**
 * 停止节点状态轮询
 */
function stopNodeStatePolling() {
  if (nodeStatePollingTimer) {
    console.log('[Graph] Stopping node state polling')
    clearInterval(nodeStatePollingTimer)
    nodeStatePollingTimer = null
  }
}

/**
 * 轮询所有节点的状态
 * 从后端获取所有节点状态，更新本地节点数据
 */
async function pollAllNodeStates() {
  // 如果正在运行，不需要轮询（由 runtime store 通过 SignalR 处理）
  if (isRunning.value) return
  
  // 获取所有 coral-node 节点
  const coralNodes = nodes.value.filter(n => n.type === 'coral-node')
  
  if (coralNodes.length === 0) return
  
  try {
    // 一次性获取所有节点状态
    const result = await deviceApi.getAllNodeStates()
    if (!result.ok) return
    
    // 构建 uuid -> state 映射
    const stateMap = new Map(result.nodes.map(s => [s.uuid, s]))
    
    // 更新本地节点数据
    for (const node of coralNodes) {
      const state = stateMap.get(node.id)
      if (state) {
        const needsUpdate = 
          node.data.runState !== state.runState ||
          node.data.isConfigured !== state.isConfigured ||
          node.data.isProgrammed !== state.isProgrammed ||
          node.data.isConnected !== state.isConnected
        
        if (needsUpdate) {
          node.data = {
            ...node.data,
            runState: state.runState || 'Offline',
            isConnected: state.isConnected,
            isConfigured: state.isConfigured,
            isProgrammed: state.isProgrammed
          }
        }
      }
    }
  } catch (error) {
    console.warn('[Graph] Failed to poll node states:', error)
  }
}

// ============================================
// 方法
// ============================================

/**
 * 添加新节点（需要提供经过 addNode API 返回的数据）
 * @param nodeData 从 addNode API 返回的节点数据
 */
interface AddNodeData {
  uuid: string    // 后端分配的 UUID
  mcuUri: string
  nodeName: string
  version?: {
    productionName: string
    gitTag: string
  }
  layout?: {
    ports: Array<{
      type: string
      name: string
    }>
  }
  // 根据 layout 生成的端口配置
  ports?: Array<{
    type: string
    name: string
    baud: number
    receiveFrameMs?: number
    retryTimeMs?: number
  }>
}

function addNode(nodeData?: AddNodeData) {
  if (isRunning.value) {
    console.warn('[Graph] Cannot add node while running')
    return
  }
  
  // 如果没有提供节点数据，记录警告并返回
  if (!nodeData || !nodeData.uuid) {
    console.warn('[Graph] addNode requires validated node data from addNode API with uuid')
    return
  }
  
  // 计算新节点位置
  const position = { x: 250 + Math.random() * 100, y: 100 + Math.random() * 100 }
  
  const newNode: Node = {
    id: nodeData.uuid,  // 使用后端分配的 UUID
    type: 'coral-node',
    position,
    data: {
      nodeName: nodeData.nodeName,
      mcuUri: nodeData.mcuUri,
      logicName: '',
      runState: 'idle',  // 添加成功后节点状态为 idle
      isConnected: true,
      isConfigured: false,
      isProgrammed: false,
      ports: nodeData.ports || [],
      layout: nodeData.layout || null  // 传入完整的 LayoutInfo
    }
  }
  
  nodes.value = [...nodes.value, newNode]
  
  // 保存位置到后端
  saveNodePosition(nodeData.uuid, position.x, position.y)
  
  console.log('[Graph] Node added:', newNode.data.nodeName, 'UUID:', nodeData.uuid, 'MCU:', nodeData.version?.productionName || 'Unknown')
}

/**
 * 保存节点位置到后端
 */
async function saveNodePosition(uuid: string, x: number, y: number) {
  try {
    await deviceApi.configureNode(uuid, {
      extraInfo: { x, y }
    })
  } catch (error) {
    console.warn('[Graph] Failed to save node position:', error)
  }
}

/**
 * 确保存在根节点
 */
function ensureRootNode() {
  const hasRoot = nodes.value.some(n => n.type === 'root-node')
  if (!hasRoot) {
    const rootNode: Node = {
      id: 'root',
      type: 'root-node',
      position: { x: 50, y: 100 },
      data: { name: 'PC' },
      deletable: false
    }
    nodes.value = [rootNode, ...nodes.value]
  }
}

/**
 * 清空图（保留根节点）
 */
async function clearGraph() {
  if (isRunning.value) {
    console.warn('[Graph] Cannot clear graph while running')
    return
  }
  
  try {
    // 清空后端节点
    await deviceApi.clearAllNodes()
  } catch (error) {
    console.error('[Graph] Failed to clear nodes:', error)
  }
  
  // 清空本地节点
  nodes.value = nodes.value.filter(n => n.type === 'root-node')
  edges.value = []
  syncLogTabs()
}

// ============================================
// 事件处理
// ============================================

function onNodesChange(changes: NodeChange[]) {
  // 过滤掉删除根节点的操作
  const filteredChanges = changes.filter(change => {
    if (change.type === 'remove') {
      const node = nodes.value.find(n => n.id === change.id)
      return node?.type !== 'root-node'
    }
    return true
  })
  
  if (filteredChanges.length > 0) {
    scheduleAutoSave()
  }
}

function onEdgesChange(changes: EdgeChange[]) {
  if (changes.length > 0) {
    scheduleAutoSave()
  }
}

function onConnect(connection: Connection) {
  // 创建新连接
  const newEdge: Edge = {
    id: `edge-${connection.source}-${connection.target}`,
    source: connection.source!,
    target: connection.target!,
    sourceHandle: connection.sourceHandle!,
    targetHandle: connection.targetHandle!,
    type: 'smoothstep',
    style: { stroke: '#4f8cff', strokeWidth: 2 }
  }
  
  edges.value = [...edges.value, newEdge]
  scheduleAutoSave()
}

function onNodeDragStop(event: { node: Node }) {
  const node = event.node
  
  // 只保存 coral-node 的位置
  if (node.type !== 'coral-node') return
  
  // 防抖保存位置
  const existing = positionSaveTimers.get(node.id)
  if (existing) {
    clearTimeout(existing)
  }
  
  positionSaveTimers.set(node.id, setTimeout(() => {
    positionSaveTimers.delete(node.id)
    saveNodePosition(node.id, node.position.x, node.position.y)
  }, 300))
}

// ============================================
// 自动保存
// ============================================

let autoSaveTimer: ReturnType<typeof setTimeout> | null = null

function scheduleAutoSave() {
  if (isLoadingFromStore) return
  
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer)
  }
  
  autoSaveTimer = setTimeout(() => {
    autoSaveTimer = null
    saveToStore()
  }, 500)
}

// 节点位置保存防抖定时器
let positionSaveTimers = new Map<string, ReturnType<typeof setTimeout>>()

/**
 * 保存图数据（节点数据由 DIVERSession 管理，这里只保存位置）
 */
function saveToStore() {
  if (isLoadingFromStore) return
  
  // 节点数据由 DIVERSession 管理
  // 这里只需要同步日志标签
  syncLogTabs()
}

/**
 * 从 DIVERSession 加载节点数据
 */
async function loadFromStore() {
  isLoadingFromStore = true
  
  try {
    // 从后端获取所有节点
    const result = await deviceApi.getAllNodes()
    
    if (!result.ok || !result.nodes) {
      console.log('[Graph] No nodes from backend')
      ensureRootNode()
      return
    }
    
    // 构建现有节点的运行时状态映射
    const existingRuntimeState = new Map<string, any>()
    for (const node of nodes.value) {
      if (node.type === 'coral-node') {
        existingRuntimeState.set(node.id, {
          runState: node.data?.runState,
          isConnected: node.data?.isConnected,
          isConfigured: node.data?.isConfigured,
          isProgrammed: node.data?.isProgrammed,
          layout: node.data?.layout
        })
      }
    }
    
    // 转换后端数据为 vue-flow 节点
    const newNodes: Node[] = result.nodes.map(n => {
      const existingState = existingRuntimeState.get(n.uuid)
      const extraInfo = n.extraInfo || {}
      
      return {
        id: n.uuid,
        type: 'coral-node',
        position: { 
          x: typeof extraInfo.x === 'number' ? extraInfo.x : 200 + Math.random() * 100, 
          y: typeof extraInfo.y === 'number' ? extraInfo.y : 100 + Math.random() * 100 
        },
        data: {
          nodeName: n.nodeName || 'Node',
          mcuUri: n.mcuUri || '',
          logicName: n.logicName || '',
          ports: n.portConfigs || [],
          // 运行时状态：优先保留现有状态，否则使用默认值
          runState: existingState?.runState || 'Offline',
          isConnected: existingState?.isConnected || false,
          isConfigured: existingState?.isConfigured || false,
          isProgrammed: existingState?.isProgrammed || false,
          layout: n.layout || existingState?.layout || null  // 使用完整的 LayoutInfo
        },
        deletable: true
      }
    })
    
    nodes.value = newNodes
    
    // 保留连线（如果有的话）
    // TODO: 连线信息也可以存储在 extraInfo 中
    
    ensureRootNode()
    syncLogTabs()
    
    console.log('[Graph] Loaded from DIVERSession:', nodes.value.length, 'nodes')
  } catch (error) {
    console.error('[Graph] Failed to load from DIVERSession:', error)
    ensureRootNode()
  } finally {
    isLoadingFromStore = false
  }
}

/**
 * 删除节点
 */
async function removeNode(uuid: string) {
  if (isRunning.value) {
    console.warn('[Graph] Cannot remove node while running')
    return false
  }
  
  try {
    const result = await deviceApi.removeNode(uuid)
    if (result.ok) {
      // 从本地移除节点
      nodes.value = nodes.value.filter(n => n.id !== uuid)
      // 移除相关连线
      edges.value = edges.value.filter(e => e.source !== uuid && e.target !== uuid)
      syncLogTabs()
      console.log('[Graph] Node removed:', uuid)
      return true
    }
  } catch (error) {
    console.error('[Graph] Failed to remove node:', error)
  }
  return false
}

/**
 * 同步日志标签
 */
function syncLogTabs() {
  const nodes_data: Array<{ uuid: string; nodeName: string }> = []
  
  for (const node of nodes.value) {
    if (node.type === 'coral-node') {
      nodes_data.push({
        uuid: node.id,
        nodeName: node.data?.nodeName || node.id
      })
    }
  }
  
  logStore.syncNodeTabs(nodes_data)
}

/**
 * 小地图节点颜色
 */
function miniMapNodeColor(node: Node): string {
  if (node.type === 'root-node') return '#3b82f6'
  if (node.type === 'coral-node') {
    const state = node.data?.runState?.toLowerCase()
    if (state === 'running') return '#22c55e'
    if (state === 'error') return '#ff4f6d'
    if (state === 'idle') return '#f59e0b'
    return '#64748b'
  }
  return '#64748b'
}

// ============================================
// 运行时状态同步
// ============================================

watch(nodeStates, (newStates) => {
  if (!newStates || isLoadingFromStore) return
  
  let needsUpdate = false
  const updatedNodes = [...nodes.value]
  
  for (const [uuid, snapshot] of newStates) {
    const idx = updatedNodes.findIndex(n => n.id === uuid)
    if (idx !== -1 && updatedNodes[idx]?.type === 'coral-node') {
      const currentNode = updatedNodes[idx]
      const currentData = currentNode.data || {}
      
      // 检查是否有变化
      if (currentData.runState !== snapshot.runState ||
          currentData.isConnected !== snapshot.isConnected ||
          currentData.isConfigured !== snapshot.isConfigured ||
          currentData.isProgrammed !== snapshot.isProgrammed) {
        
        updatedNodes[idx] = {
          ...currentNode,
          data: {
            ...currentData,
            runState: snapshot.runState || 'Offline',
            isConnected: snapshot.isConnected,
            isConfigured: snapshot.isConfigured,
            isProgrammed: snapshot.isProgrammed
          }
        }
        needsUpdate = true
      }
    }
  }
  
  if (needsUpdate) {
    nodes.value = updatedNodes
  }
}, { deep: true })

// ============================================
// 生命周期
// ============================================

onMounted(async () => {
  await loadFromStore()
  
  // 启动节点状态轮询
  startNodeStatePolling()
})

onUnmounted(() => {
  // 停止节点状态轮询
  stopNodeStatePolling()
  
  // 清理位置保存定时器
  for (const timer of positionSaveTimers.values()) {
    clearTimeout(timer)
  }
  positionSaveTimers.clear()
})

// 监听运行状态变化，控制轮询和交互
watch(isRunning, (running) => {
  if (running) {
    // 运行中时停止独立轮询（由 runtime store 通过 SignalR 管理）
    stopNodeStatePolling()
    // 锁定图交互（同步 Controls 组件的锁按钮状态）
    setInteractive(false)
  } else {
    // 停止运行后恢复独立轮询
    startNodeStatePolling()
    // 解锁图交互
    setInteractive(true)
  }
}, { immediate: true })

// ============================================
// 刷新节点
// ============================================

/**
 * 刷新节点数据（从 DIVERSession 重新加载）
 */
async function refreshNodes() {
  await loadFromStore()
}

// ============================================
// 暴露方法
// ============================================

defineExpose({
  addNode,
  removeNode,
  clearGraph,
  ensureRootNode,
  loadFromStore,
  refreshNodes,
  getNodes: () => nodes.value,
  getEdges: () => edges.value
})
</script>

<style scoped>
.graph-canvas-wrapper {
  width: 100%;
  height: 100%;
  background: #0b1220;
}

/* vue-flow 主题覆盖 */
:deep(.vue-flow) {
  background: #0b1220;
}

:deep(.vue-flow__background) {
  background: #0b1220;
}

:deep(.vue-flow__background pattern circle) {
  fill: #1e293b;
}

:deep(.vue-flow__edge-path) {
  stroke: #4f8cff;
  stroke-width: 2;
}

:deep(.vue-flow__edge.selected .vue-flow__edge-path) {
  stroke: #22c55e;
  stroke-width: 3;
}

:deep(.vue-flow__connection-line) {
  stroke: #4f8cff;
  stroke-width: 2;
  stroke-dasharray: 5 5;
}

/* 控制面板样式 */
:deep(.vue-flow__controls) {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
}

:deep(.vue-flow__controls-button) {
  background: #1e293b;
  border: none;
  color: #a0aec0;
}

:deep(.vue-flow__controls-button:hover) {
  background: #334155;
  color: #e2e8f0;
}

:deep(.vue-flow__controls-button svg) {
  fill: currentColor;
}

/* 小地图样式 */
:deep(.vue-flow__minimap) {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
}
</style>
