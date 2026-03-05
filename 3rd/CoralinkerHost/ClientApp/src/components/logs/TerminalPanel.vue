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
      
      <!-- 终端控制组 -->
      <div class="btn-group">
        <button 
          v-if="currentNodeHasWireTap"
          class="action-btn" 
          @click="exportNodeCsv" 
          title="Export WireTap logs to CSV"
        >
          <span class="btn-icon">📥</span>
          <span class="btn-text">Export</span>
        </button>
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
    <!-- 有 WireTap 的节点使用分栏视图 -->
    <WireTapLogView
      v-if="currentNodeHasWireTap"
      :uuid="activeTab"
      :node-name="currentNodeInfo.nodeName"
      :auto-scroll="autoScroll"
      @clear-console="clearCurrent"
    />
    
    <!-- 节点日志视图（无 WireTap 时，样式与 WireTap 的 Console 列一致） -->
    <div v-else-if="isNodeTab" class="console-only-view">
      <div class="console-column">
        <div class="column-header">
          <span class="column-title">Console</span>
        </div>
        <div class="column-content" ref="contentRef" @click="handleLogClick">
          <div 
            v-for="(entry, idx) in nodeEntries" 
            :key="idx" 
            class="log-entry console-entry"
            :class="{ 'error-entry': isErrorLine(entry.message), 'warning-entry': isWarningLine(entry.message) }"
          >
            <span v-if="entry.hostTime" class="entry-time">{{ entry.hostTime }} <span v-if="entry.mcuTimestampMs" class="mcu-time">{{ formatMcuTime(entry.mcuTimestampMs) }}</span></span>
            <span v-html="formatLine(entry.message)"></span>
          </div>
          <div v-if="nodeEntries.length === 0" class="empty-log">No logs yet</div>
        </div>
      </div>
    </div>
    
    <!-- Terminal/Build 日志视图 -->
    <div v-else class="terminal-content" ref="contentRef" @click="handleLogClick">
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
import { useLogStore, useWireTapStore } from '@/stores'
import { exportNodeWireTapCsv } from '@/api/device'
import WireTapLogView from './WireTapLogView.vue'

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
const wireTapStore = useWireTapStore()

const { activeTab, currentLines, nodeTabs, buildLines } = storeToRefs(logStore)

// Build 错误计数
const buildErrorCount = computed(() => {
  return buildLines.value.filter(line => line.includes(': error ')).length
})

// 当前 Tab 是否是有 WireTap 数据的节点（config 激活 OR 有已采集的日志）
const currentNodeHasWireTap = computed(() => {
  if (activeTab.value === 'terminal' || activeTab.value === 'build') {
    return false
  }
  const ports = wireTapStore.getPortsWithDataForNode(activeTab.value)
  return ports.length > 0
})

// 当前节点信息
const currentNodeInfo = computed(() => {
  const info = nodeTabs.value.find(n => n.uuid === activeTab.value)
  return info || { uuid: activeTab.value, nodeName: activeTab.value.slice(0, 8) }
})

// 当前 Tab 是否是节点 Tab（非 terminal/build）
const isNodeTab = computed(() => {
  return activeTab.value !== 'terminal' && activeTab.value !== 'build'
})

// 节点日志条目（结构化，直接从 store 获取）
const nodeEntries = computed(() => {
  return logStore.currentNodeEntries
})

function formatMcuTime(ms: number): string {
  if (ms === 0) return ''
  const totalSec = Math.floor(ms / 1000)
  const millis = ms % 1000
  const minutes = Math.floor(totalSec / 60)
  const seconds = totalSec % 60
  return `+${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`
}

// ============================================
// 本地状态
// ============================================

const contentRef = ref<HTMLDivElement | null>(null)
const autoScroll = ref(true)

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
 * 导出当前节点 WireTap 日志为 CSV
 */
async function exportNodeCsv() {
  if (!currentNodeHasWireTap.value) return
  try {
    const blob = await exportNodeWireTapCsv(activeTab.value)
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `wiretap_${currentNodeInfo.value.nodeName || activeTab.value.slice(0, 8)}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  } catch (err) {
    console.error('[TerminalPanel] CSV export failed:', err)
  }
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

// ============================================
// 监听日志变化，自动滚动
// ============================================

watch([currentLines, nodeEntries], () => {
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

/* ================================
   节点 Console 视图（无 WireTap 时）
   样式与 WireTapLogView 的 Console 列一致
   ================================ */

.console-only-view {
  flex: 1;
  display: flex;
  background: #0a0e14;
  overflow: hidden;
}

.console-only-view .console-column {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.console-only-view .column-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.console-only-view .column-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-color);
}

.console-only-view .column-content {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
  font-family: var(--font-mono);
  font-size: 11px;
  line-height: 1.5;
}

/* Console 日志条目 */
.console-only-view .log-entry {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 4px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.console-only-view .console-entry {
  display: flex;
  gap: 6px;
  padding: 2px 0;
  white-space: pre-wrap;
  word-break: break-all;
  color: var(--text-color);
}

.console-only-view .console-entry .entry-time {
  flex-shrink: 0;
  color: #64748b;
  font-size: 10px;
}

.console-only-view .console-entry .mcu-time {
  color: #8b5cf6;
}

.console-only-view .empty-log {
  color: var(--text-muted);
  text-align: center;
  padding: 20px;
  font-style: italic;
}

/* 错误/警告条目高亮 */
.console-only-view .log-entry.error-entry {
  background: rgba(239, 68, 68, 0.1);
  border-left: 3px solid #ef4444;
  padding-left: 8px;
  margin-left: -8px;
  border-radius: 0;
}

.console-only-view .log-entry.warning-entry {
  background: rgba(245, 158, 11, 0.1);
  border-left: 3px solid #f59e0b;
  padding-left: 8px;
  margin-left: -8px;
  border-radius: 0;
}
</style>
