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
  <div ref="graphWrapperRef" class="graph-canvas-wrapper">
    <VueFlow
      v-model:nodes="nodes"
      v-model:edges="edges"
      :node-types="nodeTypes"
      :default-viewport="{ x: 0, y: 0, zoom: 1 }"
      :min-zoom="0.25"
      :max-zoom="2"
      :snap-to-grid="true"
      :snap-grid="[20, 20]"
      :connection-line-style="{ stroke: '#4f8cff', strokeWidth: 2 }"
      :default-edge-options="defaultEdgeOptions"
      :nodes-draggable="!isRunning"
      :nodes-connectable="false"
      :elements-selectable="!isRunning"
      @nodes-change="onNodesChange"
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

    <div
      v-if="showVariablesFlow"
      class="variables-flow-layer"
      :style="{ transform: flowTransform }"
    >
      <svg
        class="variables-flow-svg"
        :viewBox="flowViewBox"
        :width="flowCanvasSize.width"
        :height="flowCanvasSize.height"
        :style="{ width: `${flowCanvasSize.width}px`, height: `${flowCanvasSize.height}px` }"
      >
        <defs>
          <marker
            id="var-flow-arrow"
            markerWidth="8"
            markerHeight="8"
            refX="7"
            refY="4"
            orient="auto"
            markerUnits="strokeWidth"
          >
            <path d="M 0 0 L 8 4 L 0 8 z" fill="currentColor" />
          </marker>
        </defs>
        <path
          v-for="line in flowLines"
          :key="line.id"
          class="flow-line"
          :class="`${line.direction}-io`"
          :d="line.path"
        />
      </svg>

      <div
        v-for="item in variableFlowItems"
        :key="item.id"
        class="variable-flow-item"
        :class="[
          `${item.direction}-io`,
          { dragging: draggedVariableName === item.name, reorderable: item.direction !== 'control' }
        ]"
        :style="{ left: `${item.x}px`, top: `${item.y}px`, width: `${item.width}px`, minHeight: `${item.height}px` }"
        :draggable="item.direction !== 'control'"
        @dragstart="handleVariableDragStart($event, item)"
        @dragover.prevent
        @drop.prevent="handleVariableDrop(item)"
        @dragend="handleVariableDragEnd"
      >
        <span class="var-type">{{ item.type }}</span>
        <span class="var-name" :title="item.name">{{ item.name }}</span>
        <span class="var-value" :title="item.value">{{ item.value }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch, onMounted, onUnmounted, markRaw } from 'vue'
import { 
  VueFlow, 
  useVueFlow,
  type Node, 
  type Edge,
  type NodeChange
} from '@vue-flow/core'
import { Background, BackgroundVariant } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import { useLogStore, useProjectStore, useRuntimeStore } from '@/stores'
import { storeToRefs } from 'pinia'
import * as deviceApi from '@/api/device'
import CoralNodeView from './CoralNodeView.vue'
import RootNodeView from './RootNodeView.vue'
import {
  computeFixedGraphLayout,
  NODE_SIZE,
  ROOT_SIZE,
  type FixedGraphLayoutResult,
  type VariableFlowItem
} from './variableFlowLayout'

// 导入 vue-flow 样式
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'

// ============================================
// Store 引用
// ============================================

const logStore = useLogStore()
const projectStore = useProjectStore()
const runtimeStore = useRuntimeStore()
const { nodeStates, isRunning, variableList } = storeToRefs(runtimeStore)

// ============================================
// vue-flow 交互控制
// ============================================

const { setInteractive, setViewport } = useVueFlow()

const graphWrapperRef = ref<HTMLDivElement | null>(null)

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
const showVariablesFlow = ref(true)
const flowTransform = ref('translate(50px, 50px) scale(1)')
let flowTransformFrame: number | null = null
let isApplyingFixedLayout = false
let hasCenteredInitialViewport = false

// 是否正在从 store 加载（防止循环）
let isLoadingFromStore = false

// 节点状态轮询定时器
let nodeStatePollingTimer: ReturnType<typeof setInterval> | null = null
const NODE_STATE_POLLING_INTERVAL = 3000 // 3秒轮询一次

const draggedVariableName = ref<string | null>(null)

const coralNodes = computed(() => nodes.value.filter(node => node.type === 'coral-node'))
const flowVariables = computed(() => variableList.value.filter(variable => variable.direction !== 'control'))
const controlVariables = computed(() => variableList.value.filter(variable => variable.direction === 'control'))

const variableFlowLayout = computed<FixedGraphLayoutResult>(() => {
  return computeFixedGraphLayout({
    nodes: coralNodes.value.map(node => ({
      id: node.id,
      name: node.data?.nodeName,
      order: readNodeOrder(node),
      sourceId: typeof node.data?.logicName === 'string' ? node.data.logicName : undefined
    })),
    flowVariables: flowVariables.value,
    controlVariables: controlVariables.value,
    nodeOrder: currentNodeOrder(),
    variableOrder: projectStore.variableFlowOrder,
    rootSourceIds: projectStore.rootLogicName ? [projectStore.rootLogicName] : []
  })
})

const flowCanvasSize = computed(() => {
  const layout = variableFlowLayout.value
  const points = [
    { x: layout.rootRect.x + layout.rootRect.width, y: layout.rootRect.y + layout.rootRect.height },
    ...Object.values(layout.nodeRects).map(rect => ({ x: rect.x + rect.width, y: rect.y + rect.height })),
    ...variableFlowItems.value.map(item => ({ x: item.x + item.width, y: item.y + item.height }))
  ]
  const maxX = Math.ceil(Math.max(1200, ...points.map(point => point.x + 360)))
  const maxY = Math.ceil(Math.max(800, ...points.map(point => point.y + 240)))
  return { width: maxX, height: maxY }
})

const flowViewBox = computed(() => {
  return `0 0 ${flowCanvasSize.value.width} ${flowCanvasSize.value.height}`
})

const variableFlowItems = computed(() => variableFlowLayout.value.items)
const flowLines = computed(() => variableFlowLayout.value.lines)

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
            runState: state.runState || 'disconnected',
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

function startFlowTransformSync() {
  if (flowTransformFrame != null) return

  const tick = () => {
    syncFlowTransform()
    flowTransformFrame = requestAnimationFrame(tick)
  }

  flowTransformFrame = requestAnimationFrame(tick)
}

function stopFlowTransformSync() {
  if (flowTransformFrame != null) {
    cancelAnimationFrame(flowTransformFrame)
    flowTransformFrame = null
  }
}

function syncFlowTransform() {
  const wrapper = graphWrapperRef.value
  const pane = wrapper?.querySelector<HTMLElement>('.vue-flow__transformationpane')
  if (!pane) return

  const transform = pane.style.transform
  if (transform && transform !== flowTransform.value) {
    flowTransform.value = transform
  }
}

function readNodeOrder(node: Node): number | undefined {
  const order = asExtraInfo(node.data?.extraInfo).order
  return typeof order === 'number' ? order : undefined
}

function asExtraInfo(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? { ...(value as Record<string, unknown>) } : {}
}

function currentNodeOrder(): string[] {
  return [...coralNodes.value]
    .sort((a, b) => {
      const aOrder = readNodeOrder(a)
      const bOrder = readNodeOrder(b)
      if (aOrder != null && bOrder != null && aOrder !== bOrder) return aOrder - bOrder
      if (aOrder != null && bOrder == null) return -1
      if (aOrder == null && bOrder != null) return 1
      return nodes.value.indexOf(a) - nodes.value.indexOf(b)
    })
    .map(node => node.id)
}

function applyFixedLayout() {
  if (isApplyingFixedLayout) return
  isApplyingFixedLayout = true

  const layout = variableFlowLayout.value
  let changed = false

  const nextNodes = nodes.value.map(node => {
    if (node.type === 'root-node') {
      const nextStyle = {
        ...(node.style as Record<string, string> | undefined),
        width: `${layout.rootRect.width}px`,
        height: `${layout.rootRect.height}px`
      }
      const nextNode = {
        ...node,
        position: { x: layout.rootRect.x, y: layout.rootRect.y },
        draggable: false,
        style: nextStyle
      }
      changed = changed || !samePosition(node.position, nextNode.position) || node.draggable !== false || !sameStyle(node.style, nextStyle)
      return nextNode
    }

    if (node.type !== 'coral-node') return node
    const rect = layout.nodeRects[node.id]
    if (!rect) return node

    const nextStyle = {
      ...(node.style as Record<string, string> | undefined),
      width: `${rect.width}px`
    }
    const nextNode = {
      ...node,
      position: { x: rect.x, y: rect.y },
      draggable: !isRunning.value,
      style: nextStyle
    }
    changed = changed || !samePosition(node.position, nextNode.position) || node.draggable !== !isRunning.value || !sameStyle(node.style, nextStyle)
    return nextNode
  })

  if (changed) {
    nodes.value = nextNodes
  }

  isApplyingFixedLayout = false
}

function samePosition(left: { x: number; y: number }, right: { x: number; y: number }): boolean {
  return left.x === right.x && left.y === right.y
}

function sameStyle(left: unknown, right: Record<string, string>): boolean {
  const current = (left && typeof left === 'object' ? left : {}) as Record<string, unknown>
  return current.width === right.width && current.height === right.height
}

function centerInitialViewportOnRoot() {
  if (hasCenteredInitialViewport) return
  hasCenteredInitialViewport = true
  void nextTick().then(() => {
    const width = graphWrapperRef.value?.clientWidth ?? 0
    setViewport({ x: width > 0 ? width / 2 : 360, y: 20, zoom: 1 })
  })
}

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
  
  const order = coralNodes.value.length
  const position = { x: 0, y: 0 }
  
  const newNode: Node = {
    id: nodeData.uuid,  // 使用后端分配的 UUID
    type: 'coral-node',
    position,
    data: {
      nodeName: nodeData.nodeName,
      mcuUri: nodeData.mcuUri,
      logicName: '',
      runState: 'disconnected',  // 初始未运行时统一按 disconnected 处理
      isConnected: false,
      isConfigured: false,
      isProgrammed: false,
      ports: nodeData.ports || [],
      layout: nodeData.layout || null,  // 传入完整的 LayoutInfo
      extraInfo: { order }
    }
  }
  
  nodes.value = [...nodes.value, newNode]
  applyFixedLayout()
  
  void saveNodeOrder(currentNodeOrder())
  
  console.log('[Graph] Node added:', newNode.data.nodeName, 'UUID:', nodeData.uuid, 'MCU:', nodeData.version?.productionName || 'Unknown')
}

/**
 * 保存节点顺序到后端
 */
async function saveNodeOrder(order: string[]) {
  try {
    await Promise.all(order.map((uuid, index) => {
      const node = nodes.value.find(item => item.id === uuid)
      const extraInfo = { ...asExtraInfo(node?.data?.extraInfo), order: index }
      if (node) {
        node.data = { ...node.data, extraInfo }
      }
      return deviceApi.configureNode(uuid, { extraInfo })
    }))
    await projectStore.saveProject({ silent: true })
  } catch (error) {
    console.warn('[Graph] Failed to save node order:', error)
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
      position: { x: 0, y: 0 },
      data: { name: 'PC' },
      draggable: false,
      style: { width: `${ROOT_SIZE.width}px`, height: `${ROOT_SIZE.height}px` },
      deletable: false
    }
    nodes.value = [rootNode, ...nodes.value]
  }
  applyFixedLayout()
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
  
  const meaningfulChanges = filteredChanges.filter(change => change.type !== 'position')
  if (meaningfulChanges.length > 0) {
    scheduleAutoSave()
  }
}

function onNodeDragStop(event: { node: Node }) {
  const node = event.node
  
  if (node.type !== 'coral-node') {
    applyFixedLayout()
    return
  }

  const order = currentNodeOrder()
  const sourceIndex = order.indexOf(node.id)
  if (sourceIndex === -1) {
    applyFixedLayout()
    return
  }

  const targetIndex = targetNodeOrderIndex(node)
  if (targetIndex !== sourceIndex && targetIndex >= 0) {
    const nextOrder = [...order]
    const [moved] = nextOrder.splice(sourceIndex, 1)
    if (moved) {
      nextOrder.splice(targetIndex, 0, moved)
      applyNodeOrderLocally(nextOrder)
      void saveNodeOrder(nextOrder)
    }
  }

  applyFixedLayout()
}

function targetNodeOrderIndex(node: Node): number {
  const layout = variableFlowLayout.value
  const order = currentNodeOrder()
  const nodeRect = layout.nodeRects[node.id]
  const draggedCenterX = node.position.x + (nodeRect?.width ?? NODE_SIZE.width) / 2

  let bestIndex = order.indexOf(node.id)
  let bestDistance = Number.POSITIVE_INFINITY
  order.forEach((id, index) => {
    const rect = layout.nodeRects[id]
    if (!rect) return
    const centerX = rect.x + rect.width / 2
    const distance = Math.abs(draggedCenterX - centerX)
    if (distance < bestDistance) {
      bestDistance = distance
      bestIndex = index
    }
  })
  return bestIndex
}

function applyNodeOrderLocally(order: string[]) {
  const orderIndex = new Map(order.map((id, index) => [id, index]))
  nodes.value = nodes.value.map(node => {
    if (node.type !== 'coral-node') return node
    const index = orderIndex.get(node.id)
    if (index == null) return node
    return {
      ...node,
      data: {
        ...node.data,
        extraInfo: { ...asExtraInfo(node.data?.extraInfo), order: index }
      }
    }
  })
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

/**
 * 保存图数据（节点数据由 DIVERSession 管理，这里同步日志标签）
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
        position: { x: 0, y: 0 },
        data: {
          nodeName: n.nodeName || 'Node',
          mcuUri: n.mcuUri || '',
          logicName: n.logicName || '',
          buildInfo: n.buildInfo || null,
          ports: n.portConfigs || [],
          // 运行时状态：优先保留现有状态，否则使用默认值
          runState: existingState?.runState || 'disconnected',
          isConnected: existingState?.isConnected || false,
          isConfigured: existingState?.isConfigured || false,
          isProgrammed: existingState?.isProgrammed || false,
          layout: n.layout || existingState?.layout || null,  // 使用完整的 LayoutInfo
          extraInfo
        },
        deletable: true
      }
    })
    
    nodes.value = newNodes
    
    // 保留连线（如果有的话）
    // TODO: 连线信息也可以存储在 extraInfo 中
    
    ensureRootNode()
    applyFixedLayout()
    centerInitialViewportOnRoot()
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
      applyNodeOrderLocally(currentNodeOrder())
      applyFixedLayout()
      void saveNodeOrder(currentNodeOrder())
      syncLogTabs()
      console.log('[Graph] Node removed:', uuid)
      return true
    }
  } catch (error) {
    console.error('[Graph] Failed to remove node:', error)
  }
  return false
}

function handleVariableDragStart(event: DragEvent, item: VariableFlowItem) {
  if (item.direction === 'control') return
  draggedVariableName.value = item.name
  event.dataTransfer?.setData('text/plain', item.name)
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
  }
}

function handleVariableDrop(target: VariableFlowItem) {
  const sourceName = draggedVariableName.value
  draggedVariableName.value = null
  if (!sourceName || target.direction === 'control' || sourceName === target.name) return

  const order = variableFlowLayout.value.variableOrder
  const sourceIndex = order.indexOf(sourceName)
  const targetIndex = order.indexOf(target.name)
  if (sourceIndex === -1 || targetIndex === -1) return

  const nextOrder = [...order]
  const [moved] = nextOrder.splice(sourceIndex, 1)
  if (!moved) return
  nextOrder.splice(targetIndex, 0, moved)
  void saveVariableFlowOrder(nextOrder)
}

function handleVariableDragEnd() {
  draggedVariableName.value = null
}

async function saveVariableFlowOrder(order: string[]) {
  if (sameStringArray(projectStore.variableFlowOrder, order)) return
  try {
    projectStore.setVariableFlowOrder(order)
    await projectStore.saveProject({ silent: true })
  } catch (error) {
    console.warn('[Graph] Failed to save variable flow order:', error)
  }
}

function sameStringArray(left: string[], right: string[]): boolean {
  return left.length === right.length && left.every((value, index) => value === right[index])
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
    if (state === 'running') return '#34d399'
    if (state === 'error') return '#fb7185'
    if (state === 'idle') return '#eab308'
    if (state === 'disconnected' || state === 'offline') return '#cbd5e1'
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
            runState: snapshot.runState || 'disconnected',
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
  await runtimeStore.refreshVariables()
  await runtimeStore.refreshFieldMetas()
  await nextTick()
  applyFixedLayout()
  startFlowTransformSync()
  
  // 启动节点状态轮询
  startNodeStatePolling()
})

onUnmounted(() => {
  stopFlowTransformSync()

  // 停止节点状态轮询
  stopNodeStatePolling()
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
  applyFixedLayout()
}, { immediate: true })

watch(variableFlowLayout, () => {
  applyFixedLayout()
}, { deep: true })

watch(() => variableFlowLayout.value.variableOrder, (order) => {
  if (flowVariables.value.length > 0 && !sameStringArray(projectStore.variableFlowOrder, order)) {
    void saveVariableFlowOrder(order)
  }
}, { deep: true })

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
  position: relative;
  width: 100%;
  height: 100%;
  background: #0b1220;
  overflow: hidden;
}

.variables-flow-toggle {
  position: absolute;
  left: 52px;
  bottom: 14px;
  z-index: 6;
  height: 28px;
  padding: 0 10px;
  border: 1px solid #334155;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.92);
  color: #cbd5e1;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.35);
}

.variables-flow-toggle:hover,
.variables-flow-toggle.active {
  border-color: #60a5fa;
  background: rgba(37, 99, 235, 0.24);
  color: #eff6ff;
}

.variables-flow-layer {
  position: absolute;
  inset: 0;
  z-index: 5;
  transform-origin: 0 0;
  pointer-events: none;
}

.variables-flow-svg {
  position: absolute;
  left: 0;
  top: 0;
  overflow: visible;
  color: rgba(148, 163, 184, 0.75);
  pointer-events: none;
}

.flow-line {
  fill: none;
  stroke: currentColor;
  stroke-width: 1.3;
  stroke-linecap: round;
  marker-end: url(#var-flow-arrow);
  opacity: 1;
}

.flow-line.upper-io {
  color: #22c55e;
}

.flow-line.lower-io {
  color: #fb923c;
}

.flow-line.mutual-io {
  color: #a855f7;
}

.variable-flow-item {
  position: absolute;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  grid-template-rows: 16px 16px;
  column-gap: 5px;
  box-sizing: border-box;
  padding: 4px 6px;
  border: 1px solid rgba(148, 163, 184, 0.45);
  border-radius: 8px;
  background: rgba(15, 23, 42, 0.9);
  color: #e2e8f0;
  font-size: 11px;
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.38);
  pointer-events: auto;
}

.variable-flow-item.upper-io {
  border-color: rgba(34, 197, 94, 0.7);
  background: rgba(34, 197, 94, 0.28);
}

.variable-flow-item.lower-io {
  border-color: rgba(251, 146, 60, 0.7);
  background: rgba(251, 146, 60, 0.28);
}

.variable-flow-item.mutual-io {
  border-color: rgba(168, 85, 247, 0.72);
  background: rgba(168, 85, 247, 0.34);
}

.variable-flow-item.control-io {
  min-height: 30px;
  grid-template-columns: auto minmax(0, 1fr);
  grid-template-rows: 13px 13px;
  border-color: rgba(56, 189, 248, 0.72);
  background: rgba(56, 189, 248, 0.28);
  font-size: 10px;
}

.variable-flow-item.reorderable {
  cursor: grab;
}

.variable-flow-item.reorderable:active,
.variable-flow-item.dragging {
  cursor: grabbing;
  opacity: 0.72;
}

.var-type {
  grid-column: 1;
  grid-row: 1 / span 2;
  align-self: center;
  justify-self: center;
  padding: 2px 4px;
  border-radius: 5px;
  background: rgba(255, 255, 255, 0.12);
  font-family: var(--font-mono);
  color: #f8fafc;
}

.var-name,
.var-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.var-name {
  grid-column: 2;
  grid-row: 1;
  font-weight: 700;
  align-self: end;
  justify-self: end;
  text-align: right;
}

.var-value {
  grid-column: 2;
  grid-row: 2;
  color: #cbd5e1;
  font-family: var(--font-mono);
  align-self: start;
  justify-self: end;
  text-align: right;
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
