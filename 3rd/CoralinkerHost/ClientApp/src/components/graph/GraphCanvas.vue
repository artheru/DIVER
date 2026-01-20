<!--
  @file components/graph/GraphCanvas.vue
  @description LiteGraph 节点图画布组件
  
  关键功能：
  1. 初始化 LiteGraph 画布
  2. 注册自定义节点类型 (CoralNode, RootNode)
  3. DPI 缩放支持
  4. 节点图序列化/反序列化
-->

<template>
  <div class="graph-canvas-wrapper" ref="wrapperRef">
    <!-- 工具栏 -->
    <div class="graph-toolbox">
      <button class="toolbox-btn" @click="addNode" title="Add Node">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
          <rect x="2" y="2" width="16" height="16" rx="3" stroke="currentColor" stroke-width="1.5"/>
          <line x1="10" y1="6" x2="10" y2="14" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
          <line x1="6" y1="10" x2="14" y2="10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        </svg>
      </button>
    </div>
    
    <!-- LiteGraph 画布 -->
    <canvas ref="canvasRef"></canvas>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { LGraph, LGraphCanvas, LGraphNode, LiteGraph } from 'litegraph.js'
import { useProjectStore, useLogStore } from '@/stores'
import { storeToRefs } from 'pinia'

// ============================================
// Store 引用
// ============================================

const projectStore = useProjectStore()
const logStore = useLogStore()
const { nodeMap } = storeToRefs(projectStore)

// ============================================
// 组件状态
// ============================================

const wrapperRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)

let graph: LGraph | null = null
let canvas: LGraphCanvas | null = null
let resizeObserver: ResizeObserver | null = null

// 节点计数器 (用于生成唯一名称)
let nodeCounter = 1

// ============================================
// 自定义节点类型
// ============================================

/**
 * 生成下一个节点名称
 */
function getNextNodeName(): string {
  const existingNames = new Set<string>()
  
  if (graph) {
    for (const node of graph._nodes || []) {
      if (node.properties?.nodeName) {
        existingNames.add(node.properties.nodeName as string)
      }
    }
  }
  
  while (existingNames.has(`Node${nodeCounter}`)) {
    nodeCounter++
  }
  
  return `Node${nodeCounter++}`
}

/**
 * 注册自定义节点类型
 * 包括 CoralNode (MCU节点) 和 RootNode (根节点)
 */
function registerNodeTypes() {
  // ---- CoralNode: MCU 节点 ----
  class CoralNode extends LGraphNode {
    static title = 'Coral MCU'
    static desc = 'MCU Node'
    
    constructor() {
      super('Coral MCU')
      
      // 节点尺寸
      this.size = [300, 400]
      ;(this as any).resizable = true
      
      // 默认属性
      this.properties = {
        nodeName: getNextNodeName(),
        mcuUri: 'serial://name=COM3&baudrate=1000000',
        logicName: 'TestLogic',
        runState: 'Idle',
        isConfigured: false,
        isProgrammed: false
      }
      
      // 输入输出槽
      this.addInput('in', 'flow')
      this.addOutput('out', 'flow')
      
      // 控件
      this.addWidget('text', 'nodeName', this.properties.nodeName, (v) => {
        this.properties.nodeName = v as string
        this.title = v as string
        scheduleAutoSave()
      })
      
      this.addWidget('text', 'mcuUri', this.properties.mcuUri, (v) => {
        this.properties.mcuUri = v as string
        scheduleAutoSave()
      })
      
      this.addWidget('text', 'logicName', this.properties.logicName, (v) => {
        this.properties.logicName = v as string
        scheduleAutoSave()
      })
    }
    
    /**
     * 绘制节点前景（状态信息）
     */
    onDrawForeground(ctx: CanvasRenderingContext2D) {
      const titleH = LiteGraph.NODE_TITLE_HEIGHT || 30
      const widgetH = LiteGraph.NODE_WIDGET_HEIGHT || 20
      const gap = 4
      const w = this.size[0]
      const propsCount = 3 // nodeName, mcuUri, logicName
      const afterPropsY = titleH + propsCount * (widgetH + gap) + 10
      
      // 分隔线
      ctx.strokeStyle = 'rgba(255,255,255,0.1)'
      ctx.beginPath()
      ctx.moveTo(10, afterPropsY)
      ctx.lineTo(w - 10, afterPropsY)
      ctx.stroke()
      
      // 状态显示
      const statusY = afterPropsY + 20
      ctx.font = '12px sans-serif'
      ctx.fillStyle = '#a0aec0'
      ctx.fillText(`Status: ${this.properties.runState || 'Unknown'}`, 12, statusY)
      
      const configStatus = this.properties.isConfigured ? '✓' : '✗'
      const progStatus = this.properties.isProgrammed ? '✓' : '✗'
      ctx.fillText(`Config: ${configStatus}  Program: ${progStatus}`, 12, statusY + 18)
    }
  }
  
  // ---- RootNode: 根节点 ----
  class RootNode extends LGraphNode {
    static title = 'Root'
    static desc = 'Root Node (PC)'
    
    constructor() {
      super('Root')
      this.size = [200, 90]
      this.properties = { name: 'PC' }
      this.addOutput('out', 'flow')
    }
  }
  
  // 注册节点类型
  LiteGraph.registerNodeType('coral/node', CoralNode as any)
  LiteGraph.registerNodeType('coral/root', RootNode as any)
}

// ============================================
// 自动保存
// ============================================

let autoSaveTimer: ReturnType<typeof setTimeout> | null = null

function scheduleAutoSave() {
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer)
  }
  
  autoSaveTimer = setTimeout(() => {
    autoSaveTimer = null
    saveToStore()
  }, 700)
}

function saveToStore() {
  if (!graph) return
  
  const data = graph.serialize()
  projectStore.setNodeMap(JSON.stringify(data))
  
  // 同步节点日志标签
  syncLogTabs()
}

/**
 * 同步日志标签与图中的节点
 */
function syncLogTabs() {
  if (!graph) return
  
  const nodeIds: string[] = []
  const nodeNames = new Map<string, string>()
  
  for (const node of graph._nodes || []) {
    if (node.type === 'coral/node') {
      const id = String(node.id)
      nodeIds.push(id)
      nodeNames.set(id, (node.properties?.nodeName as string) || id)
    }
  }
  
  logStore.syncNodeTabs(nodeIds, nodeNames)
}

// ============================================
// 图操作
// ============================================

/**
 * 添加新节点
 */
function addNode() {
  if (!graph) return
  
  const node = LiteGraph.createNode('coral/node')
  if (node) {
    // 在画布中心附近放置
    node.pos = [200 + Math.random() * 100, 200 + Math.random() * 100]
    graph.add(node)
    scheduleAutoSave()
  }
}

/**
 * 确保存在根节点
 */
function ensureRootNode() {
  if (!graph) return
  
  const roots = graph.findNodesByType('coral/root')
  if (roots.length === 0) {
    const root = LiteGraph.createNode('coral/root')
    if (root) {
      root.pos = [50, 50]
      graph.add(root)
    }
  }
}

/**
 * 从 Store 加载节点图
 */
function loadFromStore() {
  if (!graph || !nodeMap.value) return
  
  try {
    const data = JSON.parse(nodeMap.value)
    graph.configure(data)
    
    // 确保有根节点
    ensureRootNode()
    
    // 同步日志标签
    syncLogTabs()
    
    console.log('[Graph] Loaded from store')
  } catch (error) {
    console.error('[Graph] Failed to load:', error)
    ensureRootNode()
  }
}

// ============================================
// DPI 缩放
// ============================================

function setupDpiScaling() {
  if (!canvas || !canvasRef.value || !wrapperRef.value) return
  
  const dpr = window.devicePixelRatio || 1
  const rect = wrapperRef.value.getBoundingClientRect()
  
  // 设置画布实际像素尺寸
  canvasRef.value.width = rect.width * dpr
  canvasRef.value.height = rect.height * dpr
  
  // 设置 CSS 尺寸
  canvasRef.value.style.width = `${rect.width}px`
  canvasRef.value.style.height = `${rect.height}px`
  
  // 通知 LiteGraph 调整大小
  canvas.resize(rect.width, rect.height)
}

// ============================================
// 生命周期
// ============================================

onMounted(async () => {
  if (!canvasRef.value) return
  
  // 注册节点类型
  registerNodeTypes()
  
  // 创建图和画布
  graph = new LGraph()
  canvas = new LGraphCanvas(canvasRef.value, graph)
  
  // 禁用右键菜单
  canvas.processContextMenu = () => null
  
  // 等待 DOM 更新后设置 DPI
  await nextTick()
  setupDpiScaling()
  
  // 监听窗口大小变化
  resizeObserver = new ResizeObserver(() => {
    setupDpiScaling()
  })
  
  if (wrapperRef.value) {
    resizeObserver.observe(wrapperRef.value)
  }
  
  // 加载已保存的图
  loadFromStore()
  
  // 启动图运行循环
  graph.start()
  
  console.log('[Graph] Canvas initialized')
})

onUnmounted(() => {
  if (autoSaveTimer) {
    clearTimeout(autoSaveTimer)
  }
  
  if (resizeObserver) {
    resizeObserver.disconnect()
  }
  
  if (graph) {
    graph.stop()
  }
})

// 监听 nodeMap 变化（外部加载时）
watch(nodeMap, (newValue, oldValue) => {
  // 只有当值真正变化且不是由本组件触发时才重新加载
  if (newValue !== oldValue && graph) {
    // 这里可以添加更复杂的逻辑来判断是否需要重新加载
  }
})

// ============================================
// 暴露方法供父组件调用
// ============================================

defineExpose({
  addNode,
  graph: () => graph,
  canvas: () => canvas
})
</script>

<style scoped>
.graph-canvas-wrapper {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
}

canvas {
  display: block;
  width: 100%;
  height: 100%;
}

/* 工具栏 */
.graph-toolbox {
  position: absolute;
  top: 10px;
  left: 10px;
  z-index: 10;
  display: flex;
  gap: 4px;
}

.toolbox-btn {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  color: var(--text-muted);
  transition: all var(--transition-fast);
}

.toolbox-btn:hover {
  background: var(--primary);
  border-color: var(--primary);
  color: white;
}
</style>
