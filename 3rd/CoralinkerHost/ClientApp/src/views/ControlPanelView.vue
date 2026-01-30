<!--
  @file views/ControlPanelView.vue
  @description 独立控制面板页面
  
  功能：
  - 顶部状态栏：显示 DIVERSession 状态 + Start/Stop 按钮
  - 主体：嵌入式 ControlWindow（只读模式，只能操控不能修改）
-->

<template>
  <div class="control-panel-view">
    <!-- 状态栏 -->
    <header class="status-bar">
      <div class="status-left">
        <a href="/" class="back-link" title="Back to Editor">← Editor</a>
        <span class="separator">|</span>
        <span class="app-title">🎮 Control Panel</span>
      </div>
      
      <div class="status-center">
        <span class="status-indicator" :class="statusClass">
          <span class="status-dot"></span>
          {{ statusText }}
        </span>
      </div>
      
      <div class="status-right">
        <button 
          class="control-btn start"
          :disabled="!canStart"
          @click="handleStart"
        >
          ▶ Start
        </button>
        <button 
          class="control-btn stop"
          :disabled="!canStop"
          @click="handleStop"
        >
          ⏹ Stop
        </button>
      </div>
    </header>
    
    <!-- 控制面板主体 -->
    <main class="control-main">
      <ControlWindow 
        :visible="true" 
        :readonly="true" 
        :embedded="true"
      />
      
      <!-- 空状态提示 -->
      <div v-if="!hasWidgets" class="empty-hint">
        <p>No widgets configured</p>
        <p class="hint-sub">Go to <a href="/">Editor</a> to add and configure control widgets</p>
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore, useProjectStore } from '@/stores'
import { useSignalR } from '@/composables'
import ControlWindow from '@/components/control/ControlWindow.vue'

// ============================================
// Store 和 SignalR
// ============================================

const runtimeStore = useRuntimeStore()
const projectStore = useProjectStore()

// 初始化 SignalR 连接
useSignalR()

const { appState, canStart, canStop } = storeToRefs(runtimeStore)
const { controlLayout } = storeToRefs(projectStore)

// ============================================
// 计算属性
// ============================================

const statusClass = computed(() => {
  switch (appState.value) {
    case 'running': return 'running'
    case 'starting': return 'starting'
    case 'stopping': return 'stopping'
    case 'idle': return 'idle'
    default: return 'offline'
  }
})

const statusText = computed(() => {
  switch (appState.value) {
    case 'running': return 'Running'
    case 'starting': return 'Starting...'
    case 'stopping': return 'Stopping...'
    case 'idle': return 'Idle'
    default: return 'Offline'
  }
})

const hasWidgets = computed(() => {
  return controlLayout.value?.widgets?.length > 0
})

// ============================================
// 方法
// ============================================

async function handleStart() {
  await runtimeStore.start()
}

async function handleStop() {
  await runtimeStore.stop()
}

// ============================================
// 生命周期
// ============================================

onMounted(async () => {
  // 加载项目（获取控制面板布局）
  await projectStore.loadProject()
  // 刷新字段元信息
  await runtimeStore.refreshFieldMetas()
  // 同步会话状态
  await runtimeStore.syncSessionState()
})
</script>

<style scoped>
.control-panel-view {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: var(--body-color);
}

/* 状态栏 */
.status-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 20px;
  background: var(--panel-color);
  border-bottom: 1px solid var(--border-color);
  min-height: 50px;
}

.status-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.back-link {
  color: var(--text-muted);
  font-size: 13px;
  text-decoration: none;
}

.back-link:hover {
  color: var(--primary);
}

.separator {
  color: var(--border-color);
}

.app-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-color);
}

.status-center {
  display: flex;
  align-items: center;
}

.status-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  border-radius: 20px;
  font-size: 13px;
  font-weight: 500;
  background: var(--body-color);
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.status-indicator.running .status-dot {
  background: #22c55e;
  animation: pulse 1.5s ease-in-out infinite;
}

.status-indicator.running {
  color: #22c55e;
}

.status-indicator.starting .status-dot,
.status-indicator.stopping .status-dot {
  background: #f59e0b;
  animation: pulse 0.5s ease-in-out infinite;
}

.status-indicator.starting,
.status-indicator.stopping {
  color: #f59e0b;
}

.status-indicator.idle .status-dot {
  background: #3b82f6;
}

.status-indicator.idle {
  color: #3b82f6;
}

.status-indicator.offline .status-dot {
  background: #ef4444;
}

.status-indicator.offline {
  color: #ef4444;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.status-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.control-btn {
  padding: 8px 20px;
  border-radius: var(--radius);
  font-size: 13px;
  font-weight: 500;
  transition: all 0.15s;
}

.control-btn.start {
  background: #22c55e;
  color: white;
}

.control-btn.start:hover:not(:disabled) {
  background: #16a34a;
}

.control-btn.stop {
  background: #ef4444;
  color: white;
}

.control-btn.stop:hover:not(:disabled) {
  background: #dc2626;
}

.control-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* 主体 */
.control-main {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  position: relative;
}

/* 空状态 */
.empty-hint {
  position: absolute;
  text-align: center;
  color: var(--text-muted);
}

.empty-hint p {
  margin: 0;
  font-size: 16px;
}

.empty-hint .hint-sub {
  margin-top: 8px;
  font-size: 13px;
}

.empty-hint a {
  color: var(--primary);
}
</style>
