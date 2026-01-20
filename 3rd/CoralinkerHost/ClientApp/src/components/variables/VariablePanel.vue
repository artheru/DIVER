<!--
  @file components/variables/VariablePanel.vue
  @description 变量面板组件
  
  显示所有 Cart 变量的当前值，支持：
  - 实时值更新 (通过 SignalR)
  - 可控变量的内联编辑
  - 类型安全的输入验证
-->

<template>
  <div class="variable-panel">
    <!-- 连接状态栏 -->
    <div class="status-bar">
      <span class="status-dot" :class="connectionClass"></span>
      <span>{{ connectionStatus }}</span>
      
      <div class="status-spacer"></div>
      
      <button 
        v-if="!isConnected" 
        class="action-btn"
        @click="handleConnect"
        :disabled="connecting"
      >
        {{ connecting ? 'Connecting...' : 'Connect' }}
      </button>
      
      <template v-else>
        <button 
          v-if="!isRunning" 
          class="action-btn start"
          @click="handleStart"
        >
          Start
        </button>
        <button 
          v-else 
          class="action-btn stop"
          @click="handleStop"
        >
          Stop
        </button>
      </template>
    </div>
    
    <!-- 变量列表 -->
    <div class="variable-list">
      <div 
        v-for="variable in variableList" 
        :key="variable.name"
        class="variable-row"
      >
        <!-- 变量名 -->
        <div class="var-name" :title="variable.name">
          {{ variable.name }}
        </div>
        
        <!-- 变量类型 -->
        <div class="var-type">
          {{ variable.type }}
        </div>
        
        <!-- 变量值 -->
        <div class="var-value">
          <!-- 可编辑模式 -->
          <template v-if="editingVar === variable.name">
            <input 
              ref="editInputRef"
              v-model="editValue"
              class="edit-input"
              @blur="cancelEdit"
              @keyup.enter="confirmEdit(variable)"
              @keyup.escape="cancelEdit"
            />
          </template>
          
          <!-- 显示模式 -->
          <template v-else>
            <span class="value-text">{{ formatValue(variable.value) }}</span>
            
            <!-- 可控变量显示编辑按钮 -->
            <button 
              v-if="isControllable(variable.name)"
              class="edit-btn"
              @click="startEdit(variable)"
              title="Edit"
            >
              ✏️
            </button>
          </template>
        </div>
      </div>
      
      <!-- 空状态 -->
      <div v-if="variableList.length === 0" class="empty-state">
        {{ isConnected ? 'No variables' : 'Not connected' }}
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore, useUiStore } from '@/stores'
import type { VariableValue } from '@/types'

// ============================================
// Store 引用
// ============================================

const runtimeStore = useRuntimeStore()
const uiStore = useUiStore()
const { isConnected, isRunning, variableList, controllableVarNames } = storeToRefs(runtimeStore)

// ============================================
// 本地状态
// ============================================

const connecting = ref(false)
const editingVar = ref<string | null>(null)
const editValue = ref('')
const editInputRef = ref<HTMLInputElement[]>([])

// ============================================
// 计算属性
// ============================================

const connectionClass = computed(() => {
  if (isRunning.value) return 'running'
  if (isConnected.value) return 'connected'
  return 'disconnected'
})

const connectionStatus = computed(() => {
  if (isRunning.value) return 'Running'
  if (isConnected.value) return 'Connected'
  return 'Disconnected'
})

// ============================================
// 方法
// ============================================

/**
 * 检查变量是否可控
 */
function isControllable(name: string): boolean {
  return controllableVarNames.value.has(name)
}

/**
 * 格式化变量值显示
 */
function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '-'
  
  if (typeof value === 'number') {
    // 浮点数显示 4 位小数
    if (!Number.isInteger(value)) {
      return value.toFixed(4)
    }
    return String(value)
  }
  
  if (Array.isArray(value)) {
    // 字节数组显示为 hex
    if (value.length > 8) {
      return `[${value.slice(0, 8).map(b => b.toString(16).padStart(2, '0')).join(' ')}...]`
    }
    return `[${value.map(b => b.toString(16).padStart(2, '0')).join(' ')}]`
  }
  
  return String(value)
}

/**
 * 开始编辑
 */
function startEdit(variable: VariableValue) {
  editingVar.value = variable.name
  editValue.value = String(variable.value ?? '')
  
  // 通知 store 正在编辑（跳过 SignalR 更新）
  runtimeStore.setEditingVar(variable.name)
  
  // 聚焦输入框
  nextTick(() => {
    if (editInputRef.value.length > 0) {
      editInputRef.value[0]?.focus()
      editInputRef.value[0]?.select()
    }
  })
}

/**
 * 取消编辑
 */
function cancelEdit() {
  editingVar.value = null
  editValue.value = ''
  runtimeStore.setEditingVar(null)
}

/**
 * 确认编辑
 */
async function confirmEdit(variable: VariableValue) {
  if (!editValue.value.trim()) {
    cancelEdit()
    return
  }
  
  try {
    // 根据类型解析值
    let parsedValue: unknown = editValue.value
    
    const typeHint = variable.type.toLowerCase()
    
    if (typeHint.includes('int') || typeHint === 'byte' || typeHint === 'sbyte') {
      parsedValue = parseInt(editValue.value, 10)
      if (isNaN(parsedValue as number)) {
        throw new Error('Invalid integer')
      }
    } else if (typeHint.includes('float') || typeHint.includes('double')) {
      parsedValue = parseFloat(editValue.value)
      if (isNaN(parsedValue as number)) {
        throw new Error('Invalid number')
      }
    } else if (typeHint === 'bool' || typeHint === 'boolean') {
      parsedValue = editValue.value.toLowerCase() === 'true' || editValue.value === '1'
    }
    
    await runtimeStore.setVariable(variable.name, parsedValue, variable.type)
    uiStore.success('Variable Set', `${variable.name} = ${parsedValue}`)
  } catch (error) {
    uiStore.error('Set Failed', String(error))
  } finally {
    cancelEdit()
  }
}

/**
 * 连接
 */
async function handleConnect() {
  connecting.value = true
  try {
    await runtimeStore.connect()
    uiStore.success('Connected', 'Connected to nodes')
  } catch (error) {
    uiStore.error('Connection Failed', String(error))
  } finally {
    connecting.value = false
  }
}

/**
 * 启动
 */
async function handleStart() {
  try {
    await runtimeStore.start()
    uiStore.success('Started', 'Execution started')
  } catch (error) {
    uiStore.error('Start Failed', String(error))
  }
}

/**
 * 停止
 */
async function handleStop() {
  try {
    await runtimeStore.stop()
    uiStore.info('Stopped', 'Execution stopped')
  } catch (error) {
    uiStore.error('Stop Failed', String(error))
  }
}
</script>

<style scoped>
.variable-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
}

/* 状态栏 */
.status-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border-color);
  font-size: 12px;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.status-dot.connected {
  background: var(--warning);
}

.status-dot.running {
  background: var(--success);
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.status-spacer {
  flex: 1;
}

.action-btn {
  padding: 4px 12px;
  background: var(--primary);
  border-radius: var(--radius-sm);
  color: white;
  font-size: 12px;
}

.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.action-btn.start {
  background: var(--success);
}

.action-btn.stop {
  background: var(--danger);
}

/* 变量列表 */
.variable-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}

.variable-row {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 8px;
  padding: 6px 8px;
  border-radius: var(--radius-sm);
  font-size: 12px;
}

.variable-row:hover {
  background: rgba(255, 255, 255, 0.03);
}

.var-name {
  font-weight: 500;
  color: var(--text-color);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.var-type {
  color: var(--text-muted);
  font-family: var(--font-mono);
}

.var-value {
  display: flex;
  align-items: center;
  gap: 6px;
  font-family: var(--font-mono);
}

.value-text {
  color: var(--primary);
}

.edit-btn {
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border-radius: var(--radius-sm);
  font-size: 12px;
  opacity: 0;
  transition: opacity var(--transition-fast);
}

.variable-row:hover .edit-btn {
  opacity: 0.5;
}

.edit-btn:hover {
  opacity: 1 !important;
  background: rgba(255, 255, 255, 0.1);
}

.edit-input {
  width: 80px;
  padding: 2px 6px;
  background: var(--body-color);
  border: 1px solid var(--primary);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-family: var(--font-mono);
  font-size: 12px;
}

.empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100px;
  color: var(--text-muted);
}
</style>
