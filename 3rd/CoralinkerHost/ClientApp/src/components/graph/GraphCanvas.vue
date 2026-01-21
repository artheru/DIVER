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
  type Node, 
  type Edge,
  type Connection,
  type NodeChange,
  type EdgeChange
} from '@vue-flow/core'
import { Background, BackgroundVariant } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import { useProjectStore, useLogStore, useRuntimeStore } from '@/stores'
import { storeToRefs } from 'pinia'
import { pollNodeState } from '@/api/device'
import { restoreSession } from '@/api/runtime'
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

const projectStore = useProjectStore()
const logStore = useLogStore()
const runtimeStore = useRuntimeStore()
const { nodeMap } = storeToRefs(projectStore)
const { nodes: runtimeNodes, isRunning } = storeToRefs(runtimeStore)

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

// 节点计数器
let nodeCounter = 1

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
 */
async function pollAllNodeStates() {
  // 如果正在运行，不需要轮询（由 runtime store 处理）
  if (isRunning.value) return
  
  // 获取所有 coral-node 节点
  const coralNodes = nodes.value.filter(n => n.type === 'coral-node' && n.data?.mcuUri)
  
  if (coralNodes.length === 0) return
  
  // 并行轮询所有节点状态
  const pollPromises = coralNodes.map(async (node) => {
    try {
      const result = await pollNodeState(node.data.mcuUri)
      
      if (result.ok) {
        // 更新节点状态
        const targetNode = nodes.value.find(n => n.id === node.id)
        if (targetNode) {
          const needsUpdate = 
            targetNode.data.runState !== result.runState ||
            targetNode.data.isConfigured !== result.isConfigured ||
            targetNode.data.isProgrammed !== result.isProgrammed
          
          if (needsUpdate) {
            targetNode.data = {
              ...targetNode.data,
              runState: result.runState || 'Offline',
              isConfigured: result.isConfigured || false,
              isProgrammed: result.isProgrammed || false
            }
            console.log(`[Graph] Node ${node.id} state updated:`, result.runState, 'Config:', result.isConfigured, 'Program:', result.isProgrammed)
          }
        }
      }
    } catch (error) {
      // 轮询失败，标记为 Offline
      const targetNode = nodes.value.find(n => n.id === node.id)
      if (targetNode && targetNode.data.runState !== 'Offline') {
        targetNode.data = {
          ...targetNode.data,
          runState: 'Offline',
          isConfigured: false,
          isProgrammed: false
        }
        console.log(`[Graph] Node ${node.id} polling failed, marked as Offline`)
      }
    }
  })
  
  await Promise.allSettled(pollPromises)
}

// ============================================
// 方法
// ============================================

/**
 * 生成下一个节点名称
 */
function getNextNodeName(): string {
  const existingNames = new Set(
    nodes.value
      .filter(n => n.type === 'coral-node')
      .map(n => n.data?.nodeName)
  )
  
  while (existingNames.has(`Node${nodeCounter}`)) {
    nodeCounter++
  }
  
  return `Node${nodeCounter++}`
}

/**
 * 添加新节点（需要提供经过验证的 MCU 数据）
 * @param nodeData 经过 probe 验证的节点数据
 */
interface AddNodeData {
  nodeId: string  // 后端分配的节点 ID（与 DIVERSession 中的 ID 一致）
  mcuUri: string
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
  // 添加节点现在需要先通过 probe 验证
  if (!nodeData || !nodeData.nodeId) {
    console.warn('[Graph] addNode requires validated node data from probe with nodeId')
    return
  }
  
  const newNode: Node = {
    id: nodeData.nodeId,  // 使用后端分配的 ID，确保与 DIVERSession 一致
    type: 'coral-node',
    position: { x: 250 + Math.random() * 100, y: 100 + Math.random() * 100 },
    data: {
      nodeName: getNextNodeName(),
      mcuUri: nodeData.mcuUri,
      logicName: '',
      runState: 'idle',  // probe 成功后节点已连接，状态为 idle
      isConnected: true,
      isConfigured: false,
      isProgrammed: false,
      // 使用 probe 返回的端口配置
      ports: nodeData.ports || [],
      layout: nodeData.layout ? { ports: nodeData.layout.ports } : null
    }
  }
  
  nodes.value = [...nodes.value, newNode]
  scheduleAutoSave()
  
  console.log('[Graph] Node added:', newNode.data.nodeName, 'MCU:', nodeData.version?.productionName || 'Unknown', 'Ports:', nodeData.ports?.length || 0)
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
function clearGraph() {
  nodes.value = nodes.value.filter(n => n.type === 'root-node')
  edges.value = []
  nodeCounter = 1
  scheduleAutoSave()
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

function onNodeDragStop() {
  scheduleAutoSave()
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

// 是否正在保存到 store（防止 save → watch → load 循环）
let isSavingToStore = false

function saveToStore() {
  if (isLoadingFromStore) return
  
  isSavingToStore = true
  
  // 转换为存储格式 - 只保存静态配置，不保存运行时状态
  const graphData = {
    nodes: nodes.value.map(n => {
      if (n.type === 'coral-node') {
        // Coral 节点只保存静态配置
        return {
          id: n.id,
          type: n.type,
          position: n.position,
          data: {
            nodeName: n.data?.nodeName || 'Node',
            mcuUri: n.data?.mcuUri || '',
            logicName: n.data?.logicName || '',
            ports: n.data?.ports || []
            // 不保存 runState, isConnected, isConfigured, isProgrammed, layout
          }
        }
      }
      // Root 节点
      return {
        id: n.id,
        type: n.type,
        position: n.position,
        data: { name: n.data?.name || 'PC' }
      }
    }),
    edges: edges.value.map(e => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })),
    viewport: { x: 0, y: 0, zoom: 1 }
  }
  
  projectStore.setNodeMap(graphData as any)
  
  // 重置保存标志（延迟一帧，确保 watch 已触发）
  setTimeout(() => { isSavingToStore = false }, 0)
  
  // 同步日志标签
  syncLogTabs()
}

function loadFromStore() {
  if (!nodeMap.value) {
    ensureRootNode()
    return
  }
  
  isLoadingFromStore = true
  
  try {
    const data = nodeMap.value as any
    
    // 检查是否是旧的 LiteGraph 格式
    if (data.last_node_id !== undefined) {
      // 迁移旧格式
      migrateFromLiteGraph(data)
    } else if (data.nodes && Array.isArray(data.nodes)) {
      // 新格式 - 加载静态配置，保留现有节点的运行时状态
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
      
      nodes.value = data.nodes.map((n: any) => {
        if (n.type === 'coral-node') {
          // 尝试保留现有节点的运行时状态
          const existingState = existingRuntimeState.get(n.id)
          return {
            id: n.id,
            type: n.type,
            position: n.position || { x: 100, y: 100 },
            data: {
              // 静态配置从存储加载
              nodeName: n.data?.nodeName || 'Node',
              mcuUri: n.data?.mcuUri || '',
              logicName: n.data?.logicName || '',
              ports: n.data?.ports || [],
              // 运行时状态：优先保留现有状态，否则使用默认值
              runState: existingState?.runState || 'Offline',
              isConnected: existingState?.isConnected || false,
              isConfigured: existingState?.isConfigured || false,
              isProgrammed: existingState?.isProgrammed || false,
              layout: existingState?.layout || null
            },
            deletable: true
          }
        }
        // Root 节点
        return {
          id: n.id,
          type: n.type,
          position: n.position || { x: 50, y: 100 },
          data: n.data || { name: 'PC' },
          deletable: false
        }
      })
      
      edges.value = (data.edges || []).map((e: any) => ({
        id: e.id,
        source: e.source,
        target: e.target,
        sourceHandle: e.sourceHandle || 'out',
        targetHandle: e.targetHandle || 'in',
        type: 'smoothstep',
        style: { stroke: '#4f8cff', strokeWidth: 2 }
      }))
    }
    
    ensureRootNode()
    syncLogTabs()
    
    console.log('[Graph] Loaded from store:', nodes.value.length, 'nodes,', edges.value.length, 'edges')
  } catch (error) {
    console.error('[Graph] Failed to load from store:', error)
    ensureRootNode()
  } finally {
    isLoadingFromStore = false
  }
}

/**
 * 从旧的 LiteGraph 格式迁移
 */
function migrateFromLiteGraph(data: any) {
  console.log('[Graph] Migrating from LiteGraph format...')
  
  const newNodes: Node[] = []
  const newEdges: Edge[] = []
  
  // 转换节点
  for (const oldNode of data.nodes || []) {
    if (oldNode.type === 'coral/root') {
      newNodes.push({
        id: String(oldNode.id),
        type: 'root-node',
        position: { 
          x: Array.isArray(oldNode.pos) ? oldNode.pos[0] : (oldNode.pos?.['0'] || 50),
          y: Array.isArray(oldNode.pos) ? oldNode.pos[1] : (oldNode.pos?.['1'] || 50)
        },
        data: { name: oldNode.properties?.name || 'PC' },
        deletable: false
      })
    } else if (oldNode.type === 'coral/node') {
      newNodes.push({
        id: String(oldNode.id),
        type: 'coral-node',
        position: {
          x: Array.isArray(oldNode.pos) ? oldNode.pos[0] : (oldNode.pos?.['0'] || 200),
          y: Array.isArray(oldNode.pos) ? oldNode.pos[1] : (oldNode.pos?.['1'] || 100)
        },
        data: {
          // 静态配置从旧数据迁移
          nodeName: oldNode.properties?.nodeName || 'Node',
          mcuUri: oldNode.properties?.mcuUri || '',
          logicName: oldNode.properties?.logicName || '',
          ports: oldNode.properties?.ports || [],
          // 运行时状态使用默认值
          runState: 'Offline',
          isConnected: false,
          isConfigured: false,
          isProgrammed: false,
          layout: null
        }
      })
    }
  }
  
  // 转换连线
  for (const oldLink of data.links || []) {
    if (!oldLink) continue
    
    const linkData = Array.isArray(oldLink) ? {
      id: oldLink[0],
      origin_id: oldLink[1],
      target_id: oldLink[3]
    } : oldLink
    
    newEdges.push({
      id: `edge-${linkData.id}`,
      source: String(linkData.origin_id),
      target: String(linkData.target_id),
      sourceHandle: 'out',
      targetHandle: 'in',
      type: 'smoothstep',
      style: { stroke: '#4f8cff', strokeWidth: 2 }
    })
  }
  
  nodes.value = newNodes
  edges.value = newEdges
  
  // 保存新格式
  setTimeout(() => saveToStore(), 100)
}

/**
 * 同步日志标签
 */
function syncLogTabs() {
  const nodeIds: string[] = []
  const nodeNames = new Map<string, string>()
  
  for (const node of nodes.value) {
    if (node.type === 'coral-node') {
      nodeIds.push(node.id)
      nodeNames.set(node.id, node.data?.nodeName || node.id)
    }
  }
  
  logStore.syncNodeTabs(nodeIds, nodeNames)
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

watch(runtimeNodes, (newNodes) => {
  if (!newNodes || isLoadingFromStore) return
  
  let needsUpdate = false
  const updatedNodes = [...nodes.value]
  
  for (const [nodeId, snapshot] of newNodes) {
    const idx = updatedNodes.findIndex(n => n.id === nodeId)
    if (idx !== -1 && updatedNodes[idx]?.type === 'coral-node') {
      const currentNode = updatedNodes[idx]
      updatedNodes[idx] = {
        ...currentNode,
        data: {
          ...(currentNode.data || {}),
          // 只更新运行时状态，不覆盖用户配置的 ports
          runState: snapshot.runState || 'Offline',
          isConnected: snapshot.isConnected,
          isConfigured: snapshot.isConfigured,
          isProgrammed: snapshot.isProgrammed,
          // layout 只在没有时才从 snapshot 获取（硬件信息）
          layout: currentNode.data?.layout || snapshot.layout
          // ports 是用户配置，不从 runtime snapshot 更新，保留用户的编辑
        }
      }
      needsUpdate = true
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
  loadFromStore()
  
  // 恢复节点会话（如果有 MCU 节点）
  await restoreNodes()
  
  // 启动节点状态轮询
  startNodeStatePolling()
})

onUnmounted(() => {
  // 停止节点状态轮询
  stopNodeStatePolling()
})

// 监听 nodeMap 变化（仅在外部变化时重新加载，忽略自己保存触发的变化）
watch(nodeMap, (newValue) => {
  if (newValue && !isLoadingFromStore && !isSavingToStore) {
    loadFromStore()
  }
}, { deep: true })

// 监听运行状态变化，控制轮询
watch(isRunning, (running) => {
  if (running) {
    // 运行中时停止独立轮询（由 runtime store 管理）
    stopNodeStatePolling()
  } else {
    // 停止运行后恢复独立轮询
    startNodeStatePolling()
  }
})

// ============================================
// 恢复节点会话
// ============================================

/**
 * 恢复节点会话（用于导入项目后）
 */
async function restoreNodes() {
  const mcuNodes = nodes.value.filter(n => n.type === 'coral-node' && n.data?.mcuUri)
  if (mcuNodes.length === 0) return
  
  console.log('[Graph] Restoring session for', mcuNodes.length, 'MCU node(s)...')
  try {
    const result = await restoreSession()
    if (result.ok) {
      console.log(`[Graph] Session restored: ${result.connected}/${result.total} node(s) connected`)
      // 更新节点状态
      for (const nodeInfo of result.nodes) {
        if (nodeInfo.success) {
          const node = nodes.value.find(n => n.id === nodeInfo.nodeId)
          if (node) {
            node.data = { ...node.data, runState: 'Idle', isConnected: true }
          }
        }
      }
    } else {
      console.warn('[Graph] Session restore failed:', result.error)
    }
  } catch (error) {
    console.error('[Graph] Session restore error:', error)
  }
}

// ============================================
// 暴露方法
// ============================================

defineExpose({
  addNode,
  clearGraph,
  ensureRootNode,
  loadFromStore,
  saveToStore,
  restoreNodes,
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
