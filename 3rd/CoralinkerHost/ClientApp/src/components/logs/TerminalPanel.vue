<!--
  @file components/logs/TerminalPanel.vue
  @description 终端/日志面板组件
  
  包含：
  - Tab 切换 (Terminal / 各节点日志)
  - 构建和运行控制按钮
  - 日志自动滚动
  - 清空日志功能
-->

<template>
  <div class="terminal-panel">
    <!-- Tab 栏 -->
    <div class="tab-bar">
      <!-- Terminal Tab -->
      <button 
        class="tab" 
        :class="{ active: activeTab === 'terminal' }"
        @click="switchTab('terminal')"
      >
        Terminal
      </button>
      
      <!-- Build Tab -->
      <button 
        class="tab" 
        :class="{ active: activeTab === 'build' }"
        @click="switchTab('build')"
      >
        Build
        <span v-if="buildErrorCount > 0" class="error-badge">{{ buildErrorCount }}</span>
      </button>
      
      <!-- 节点 Tabs -->
      <button 
        v-for="info in nodeTabs" 
        :key="info.uuid"
        class="tab"
        :class="{ active: activeTab === info.uuid }"
        @click="switchTab(info.uuid)"
      >
        {{ info.nodeName || info.uuid.slice(0, 8) }}
      </button>
      
      <!-- 工具按钮区 -->
      <div class="tab-spacer"></div>
      
      <!-- 构建控制组 -->
      <div class="btn-group">
        <button 
          class="action-btn build" 
          :disabled="!hasInputFiles || isBuilding || isRunning"
          @click="handleBuild" 
          :title="isRunning ? 'Stop session before building' : 'Compile .cs files in inputs folder'"
        >
          <span class="btn-icon">⚙</span>
          <span class="btn-text">Build</span>
        </button>
      </div>
      
      <div class="btn-divider"></div>
      
      <!-- 运行控制组 -->
      <div class="btn-group">
        <!-- 状态指示 -->
        <div class="runtime-status">
          <span class="status-dot" :class="statusClass"></span>
          <span class="status-label">{{ sessionType }}</span>
          <span class="status-text">{{ statusText }}</span>
        </div>
        
        <button 
          class="action-btn start" 
          :disabled="!canStart || isStarting"
          @click="handleStart" 
          title="Connect, Configure, Program, and Start execution"
        >
          <span class="btn-icon">▶</span>
          <span class="btn-text">{{ isStarting ? 'Starting...' : 'Start' }}</span>
        </button>
        <button 
          class="action-btn stop" 
          :disabled="!canStop || isStopping"
          @click="handleStop" 
          title="Stop execution"
        >
          <span class="btn-icon">■</span>
          <span class="btn-text">{{ isStopping ? 'Stopping...' : 'Stop' }}</span>
        </button>
      </div>
      
      <div class="btn-divider"></div>
      
      <!-- 终端控制组 -->
      <div class="btn-group">
        <button class="action-btn" @click="clearCurrent" title="Clear terminal">
          <span class="btn-icon">🗑</span>
          <span class="btn-text">Clear</span>
        </button>
        <button 
          class="action-btn" 
          :class="{ active: autoScroll }" 
          @click="toggleAutoScroll" 
          title="Auto scroll"
        >
          <span class="btn-icon">{{ autoScroll ? '⬇' : '⏸' }}</span>
          <span class="btn-text">Scroll</span>
        </button>
      </div>
    </div>
    
    <!-- 日志内容 -->
    <div class="terminal-content" ref="contentRef" @click="handleLogClick">
      <div 
        v-for="(line, idx) in currentLines" 
        :key="idx" 
        class="log-line"
        :class="{ 'error-line': isErrorLine(line), 'warning-line': isWarningLine(line) }"
        v-html="formatLine(line)"
      ></div>
      
      <div v-if="currentLines.length === 0" class="empty-log">
        No logs yet
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useLogStore, useProjectStore, useRuntimeStore, useUiStore, useFilesStore } from '@/stores'
import { programNode } from '@/api/device'

// ============================================
// Emits
// ============================================

const emit = defineEmits<{
  /** 跳转到源代码 */
  (e: 'gotoSource', filePath: string, line: number): void
}>()

// ============================================
// Store 引用
// ============================================

const logStore = useLogStore()
const projectStore = useProjectStore()
const runtimeStore = useRuntimeStore()
const uiStore = useUiStore()
const filesStore = useFilesStore()

const { activeTab, currentLines, nodeTabs, buildLines } = storeToRefs(logStore)
const { isBackendAvailable, isRunning, sessionType, canStart, canStop } = storeToRefs(runtimeStore)
const { fileTree } = storeToRefs(filesStore)

// Build 错误计数
const buildErrorCount = computed(() => {
  return buildLines.value.filter(line => line.includes(': error ')).length
})

// 状态样式类
// Offline = 网页连不上后端, Idle = 网页能连上后端但不在运行, Running = 在运行
const statusClass = computed(() => {
  if (isRunning.value) return 'running'
  if (isBackendAvailable.value) return 'idle'
  return 'offline'
})

// 状态文本
const statusText = computed(() => {
  if (isRunning.value) return 'Running'
  if (isBackendAvailable.value) return 'Idle'
  return 'Offline'
})

// ============================================
// 计算属性
// ============================================

/**
 * 检查 inputs 目录是否有 .cs 文件
 * 只要有任何 .cs 文件就允许 Build
 */
const hasInputFiles = computed(() => {
  // 在 fileTree 中查找 inputs 文件夹
  const inputsFolder = fileTree.value.find(node => node.name === 'inputs')
  if (!inputsFolder || !inputsFolder.children) return false
  
  // 检查是否有任何 .cs 文件
  return inputsFolder.children.some(child => 
    child.kind === 'file' && child.name.endsWith('.cs')
  )
})

// ============================================
// 本地状态
// ============================================

const contentRef = ref<HTMLDivElement | null>(null)
const autoScroll = ref(true)
const isBuilding = ref(false)
const isStarting = ref(false)
const isStopping = ref(false)

// ============================================
// 方法
// ============================================

/**
 * 切换 Tab
 */
function switchTab(tabId: string) {
  logStore.switchTab(tabId)
  
  // 切换后自动滚动到底部
  nextTick(() => {
    scrollToBottom()
  })
}

/**
 * 清空当前日志
 */
function clearCurrent() {
  logStore.clearCurrent()
}

/**
 * 切换自动滚动
 */
function toggleAutoScroll() {
  autoScroll.value = !autoScroll.value
  
  if (autoScroll.value) {
    scrollToBottom()
  }
}

/**
 * 滚动到底部
 */
function scrollToBottom() {
  if (contentRef.value && autoScroll.value) {
    contentRef.value.scrollTop = contentRef.value.scrollHeight
  }
}

/**
 * 格式化日志行（支持 ANSI 颜色代码和可点击的文件链接）
 */
function formatLine(line: string): string {
  // 简单的 ANSI 颜色映射
  // \x1b[32m -> 绿色, \x1b[33m -> 黄色, \x1b[31m -> 红色, \x1b[0m -> 重置
  let formatted = line
    .replace(/\x1b\[32m/g, '<span class="log-green">')
    .replace(/\x1b\[33m/g, '<span class="log-yellow">')
    .replace(/\x1b\[31m/g, '<span class="log-red">')
    .replace(/\x1b\[36m/g, '<span class="log-cyan">')
    .replace(/\x1b\[0m/g, '</span>')
    .replace(/\x1b\[\d+m/g, '') // 移除其他未处理的 ANSI 代码
  
  // 匹配文件路径和行号: xxx.cs(123,45) 或 xxx.cs(123)
  // 格式: filepath(line,column): error/warning message
  const filePattern = /([A-Za-z]:\\[^\s(]+\.cs|\/?[^\s(]+\.cs)\((\d+)(?:,\d+)?\)/g
  formatted = formatted.replace(filePattern, (match, filePath, lineNum) => {
    // 从完整路径中提取文件名
    const fileName = filePath.split(/[/\\]/).pop() || filePath
    return `<a class="file-link" data-file="${fileName}" data-line="${lineNum}" href="#">${match}</a>`
  })
  
  return formatted
}

/**
 * 检查是否为错误行
 */
function isErrorLine(line: string): boolean {
  return line.includes(': error ') || line.includes('Build FAILED')
}

/**
 * 检查是否为警告行
 */
function isWarningLine(line: string): boolean {
  return line.includes(': warning ')
}

/**
 * 处理日志内容点击事件（用于文件链接跳转）
 */
function handleLogClick(event: MouseEvent) {
  const target = event.target as HTMLElement
  
  if (target.classList.contains('file-link')) {
    event.preventDefault()
    
    const fileName = target.getAttribute('data-file')
    const lineNum = target.getAttribute('data-line')
    
    if (fileName && lineNum) {
      console.log(`[TerminalPanel] Goto source: ${fileName}:${lineNum}`)
      emit('gotoSource', fileName, parseInt(lineNum, 10))
    }
  }
}

/**
 * 记录错误到终端并显示弹窗
 */
function logError(category: string, message: string) {
  logStore.logUI(`[${category}] \x1b[31mERROR:\x1b[0m ${message}`)
  uiStore.error(`${category} Failed`, message)
}

/**
 * 执行构建
 */
async function handleBuild() {
  if (isBuilding.value) return
  
  isBuilding.value = true
  
  // 先清空前端 Build 日志，再切换标签
  logStore.clearBuild()
  logStore.switchTab('build')
  
  try {
    const result = await projectStore.build()
    if (result.ok) {
      uiStore.success('Build Success', `Build ID: ${result.buildId}`)
      // 刷新文件树，显示新生成的文件
      await filesStore.loadFileTree()
      // 通知构建完成，触发 Logic 列表刷新
      filesStore.notifyBuildComplete()
      // 刷新所有打开的文件
      await filesStore.refreshOpenTabs()
      // 刷新变量列表（变量定义可能已更改）
      await runtimeStore.refreshVariables()
      // 重新编程所有已选择 Logic 的节点
      await reprogramAllNodes()
    } else {
      uiStore.error('Build Failed', result.error || 'Unknown error')
    }
  } catch (error) {
    uiStore.error('Build Failed', String(error))
  } finally {
    isBuilding.value = false
  }
}

/**
 * 重新编程所有已选择 Logic 的节点
 */
async function reprogramAllNodes() {
  // 先刷新节点信息，确保获取最新的 logicName
  await runtimeStore.refreshNodes()
  
  // 获取所有节点信息
  const nodeInfoList = runtimeStore.nodeInfoList
  
  // 筛选出已选择 Logic 的节点
  const nodesToProgram = nodeInfoList.filter(node => node.logicName)
  
  if (nodesToProgram.length === 0) {
    logStore.logUI('[Build] No nodes with Logic selected')
    return
  }
  
  logStore.logUI(`[Build] Re-programming ${nodesToProgram.length} node(s)...`)
  
  for (const node of nodesToProgram) {
    try {
      logStore.logUI(`[Build] Programming ${node.nodeName} with ${node.logicName}...`)
      const result = await programNode(node.uuid, node.logicName!)
      if (result.ok) {
        logStore.logUI(`[Build] \x1b[32m✓\x1b[0m ${node.nodeName} programmed (${result.programSize} bytes)`)
      } else {
        logStore.logUI(`[Build] \x1b[31m✗\x1b[0m ${node.nodeName} failed to program`)
      }
    } catch (error) {
      logStore.logUI(`[Build] \x1b[31m✗\x1b[0m ${node.nodeName} error: ${error}`)
    }
  }
  
  // 再次刷新节点信息以更新程序大小等
  await runtimeStore.refreshNodes()
}

/**
 * 启动执行 (Connect → Configure → Program → Start)
 */
async function handleStart() {
  if (isStarting.value || !canStart.value) return
  
  isStarting.value = true
  
  // 切换到 Terminal 标签
  logStore.switchTab('terminal')
  logStore.logUI('Starting execution sequence (Connect → Configure → Program → Start)...')
  
  try {
    // Start 会先连接，然后在后端执行 Configure → Program → Start 流程
    const result = await runtimeStore.start()
    if (result.ok) {
      logStore.logUI('\x1b[32mExecution started\x1b[0m')
      uiStore.success('Started', 'Execution started')
    } else {
      logError('Start', (result as { error?: string }).error || 'Start failed')
    }
  } catch (error) {
    logError('Start', String(error))
  } finally {
    isStarting.value = false
  }
}

/**
 * 停止执行
 */
async function handleStop() {
  if (isStopping.value || !canStop.value) return
  
  isStopping.value = true
  logStore.logUI('Stopping execution...')
  
  try {
    await runtimeStore.stop()
    logStore.logUI('\x1b[33mExecution stopped\x1b[0m')
    uiStore.success('Stopped', 'Execution stopped')
  } catch (error) {
    logError('Stop', String(error))
  } finally {
    isStopping.value = false
  }
}

// ============================================
// 监听日志变化，自动滚动
// ============================================

watch(currentLines, () => {
  nextTick(() => {
    scrollToBottom()
  })
}, { deep: true })
</script>

<style scoped>
.terminal-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
}

/* Tab 栏 */
.tab-bar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 6px 10px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  overflow-x: auto;
}

.tab {
  padding: 5px 12px;
  background: transparent;
  border-radius: var(--radius-sm);
  color: var(--text-muted);
  font-size: 12px;
  white-space: nowrap;
  transition: all var(--transition-fast);
}

.tab:hover {
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-color);
}

.tab.active {
  background: rgba(79, 140, 255, 0.15);
  color: var(--text-color);
}

.tab-spacer {
  flex: 1;
}

/* 按钮组 */
.btn-group {
  display: flex;
  align-items: center;
  gap: 4px;
}

/* 运行时状态指示 */
.runtime-status {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: var(--radius-sm);
  margin-right: 8px;
}

.runtime-status .status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.runtime-status .status-dot.idle {
  background: var(--warning);
}

.runtime-status .status-dot.offline {
  background: var(--text-muted);
}

.runtime-status .status-dot.running {
  background: var(--success);
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.runtime-status .status-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-color);
}

.runtime-status .status-text {
  font-size: 11px;
  color: var(--text-muted);
}

.btn-divider {
  width: 1px;
  height: 20px;
  background: var(--border-color);
  margin: 0 8px;
}

/* Action 按钮 */
.action-btn {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  color: var(--text-muted);
  font-size: 11px;
  white-space: nowrap;
  transition: all var(--transition-fast);
}

.action-btn:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-color);
}

.action-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.action-btn.active {
  background: rgba(79, 140, 255, 0.15);
  color: var(--primary);
  border-color: rgba(79, 140, 255, 0.3);
}

.btn-icon {
  font-size: 12px;
  line-height: 1;
}

.btn-text {
  font-weight: 500;
}

/* 特殊按钮颜色 */
.action-btn.build:hover:not(:disabled) {
  background: rgba(79, 140, 255, 0.15);
  color: var(--primary);
}

.action-btn.start:hover:not(:disabled) {
  background: rgba(34, 197, 94, 0.15);
  color: var(--success);
}

.action-btn.stop:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.15);
  color: var(--danger);
}

/* 日志内容 */
.terminal-content {
  flex: 1;
  overflow-y: auto;
  padding: 10px;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.6;
  background: #0a0e14;
}

.log-line {
  white-space: pre-wrap;
  word-break: break-all;
}

.empty-log {
  color: var(--text-muted);
  text-align: center;
  padding: 20px;
}

/* 日志颜色 */
:deep(.log-green) {
  color: #22c55e;
}

:deep(.log-yellow) {
  color: #f59e0b;
}

:deep(.log-red) {
  color: #ef4444;
}

:deep(.log-cyan) {
  color: #22d3ee;
}

/* 错误/警告行高亮 */
.log-line.error-line {
  background: rgba(239, 68, 68, 0.1);
  border-left: 3px solid #ef4444;
  padding-left: 8px;
  margin-left: -8px;
}

.log-line.warning-line {
  background: rgba(245, 158, 11, 0.1);
  border-left: 3px solid #f59e0b;
  padding-left: 8px;
  margin-left: -8px;
}

/* 可点击的文件链接 */
:deep(.file-link) {
  color: #58a6ff;
  text-decoration: underline;
  cursor: pointer;
}

:deep(.file-link:hover) {
  color: #79b8ff;
}

/* 错误计数徽章 */
.error-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  margin-left: 4px;
  background: #ef4444;
  color: white;
  font-size: 10px;
  font-weight: 600;
  border-radius: 8px;
}
</style>
