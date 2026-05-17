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
    <!-- 变量列表头 -->
    <div class="variable-header">
      <div class="col-type">Type</div>
      <div class="col-name">Name</div>
      <div class="col-value">Value</div>
      <div class="col-action"></div>
    </div>
    
    <!-- 颜色图例 -->
    <div class="color-legend">
      <span class="legend-item upper"><span class="legend-dot"></span>UpperIO</span>
      <span class="legend-item lower"><span class="legend-dot"></span>LowerIO</span>
      <span class="legend-item mutual"><span class="legend-dot"></span>MutualIO</span>
      <span class="legend-item control"><span class="legend-dot"></span>ControlItem</span>
    </div>
    
    <!-- 变量列表 -->
    <div class="variable-list">
      <div 
        v-for="variable in variableList" 
        :key="variable.name"
        class="variable-row"
        :class="directionClass(variable)"
      >
        <!-- 类型 -->
        <div class="col-type" :title="variable.type">
          {{ formatType(variable.type) }}
        </div>
        
        <!-- 名称 -->
        <div class="col-name" :title="variable.name">
          {{ variable.name }}
        </div>
        
        <!-- 值 -->
        <div class="col-value">
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
          <template v-else>
            <span class="value-text">{{ formatValue(variable.value) }}</span>
          </template>
        </div>
        
        <!-- 操作按钮 -->
        <div class="col-action">
          <button 
            v-if="isControllable(variable.name)"
            class="action-icon edit"
            @click="startEdit(variable)"
            title="Edit variable"
          >
            ✏️
          </button>
          <span 
            v-else 
            class="action-icon locked"
            title="Read-only variable"
          >
            🔒
          </span>
        </div>
      </div>
      
      <!-- 空状态 -->
      <div v-if="variableList.length === 0" class="empty-state">
        No variables
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore, useUiStore } from '@/stores'
import type { VariableValue } from '@/types'

// ============================================
// Store 引用
// ============================================

const runtimeStore = useRuntimeStore()
const uiStore = useUiStore()
const { variableList, controllableVarNames } = storeToRefs(runtimeStore)

// ============================================
// 本地状态
// ============================================

const editingVar = ref<string | null>(null)
const editValue = ref('')
const editInputRef = ref<HTMLInputElement[]>([])

// ============================================
// 方法
// ============================================

/**
 * 检查变量是否可控
 */
function isControllable(name: string): boolean {
  return controllableVarNames.value.has(name)
}

function directionOf(variable: VariableValue): string {
  return variable.direction || (isControllable(variable.name) ? 'upper' : 'lower')
}

function directionClass(variable: VariableValue): string {
  return `${directionOf(variable)}-io`
}

/**
 * 格式化类型显示 (简化长类型名)
 */
function formatType(type: string): string {
  // 简化常见类型名
  const typeMap: Record<string, string> = {
    'Int32': 'i32',
    'Int16': 'i16',
    'Int64': 'i64',
    'UInt32': 'u32',
    'UInt16': 'u16',
    'UInt64': 'u64',
    'Single': 'f32',
    'Double': 'f64',
    'Boolean': 'bool',
    'Byte': 'u8',
    'SByte': 'i8',
    'String': 'str'
  }
  return typeMap[type] || type
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

</script>

<style scoped>
.variable-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

/* 列表头 */
.variable-header {
  display: grid;
  grid-template-columns: 40px 1fr 80px 28px;
  gap: 8px;
  padding: 6px 8px;
  border-bottom: 1px solid var(--border-color);
  font-size: 11px;
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

/* 颜色图例 */
.color-legend {
  display: flex;
  gap: 12px;
  padding: 4px 8px;
  font-size: 10px;
  color: var(--text-muted);
  border-bottom: 1px solid var(--border-color);
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 4px;
}

.legend-dot {
  width: 8px;
  height: 8px;
  border-radius: 2px;
}

.legend-item.upper .legend-dot {
  background: rgba(34, 197, 94, 0.6);
}

.legend-item.lower .legend-dot {
  background: rgba(251, 146, 60, 0.6);
}

.legend-item.mutual .legend-dot {
  background: rgba(168, 85, 247, 0.6);
}

.legend-item.control .legend-dot {
  background: rgba(56, 189, 248, 0.6);
}

/* 变量列表 */
.variable-list {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
}

.variable-row {
  display: grid;
  grid-template-columns: 40px 1fr 80px 28px;
  gap: 8px;
  padding: 5px 8px;
  font-size: 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
}

.variable-row:hover {
  background: rgba(255, 255, 255, 0.08);
}

/* UpperIO (可写) - 绿色背景 */
.variable-row.upper-io {
  background: rgba(34, 197, 94, 0.15);
}

.variable-row.upper-io:hover {
  background: rgba(34, 197, 94, 0.25);
}

/* LowerIO (只读) - 橙色背景 */
.variable-row.lower-io {
  background: rgba(251, 146, 60, 0.15);
}

.variable-row.lower-io:hover {
  background: rgba(251, 146, 60, 0.25);
}

.variable-row.mutual-io {
  background: rgba(168, 85, 247, 0.15);
}

.variable-row.mutual-io:hover {
  background: rgba(168, 85, 247, 0.25);
}

.variable-row.control-io {
  background: rgba(56, 189, 248, 0.15);
}

.variable-row.control-io:hover {
  background: rgba(56, 189, 248, 0.25);
}

/* 列样式 */
.col-type {
  font-family: var(--font-mono);
  font-size: 10px;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.col-name {
  font-weight: 500;
  color: var(--text-color);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.col-value {
  font-family: var(--font-mono);
  text-align: right;
  color: var(--primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.col-action {
  display: flex;
  align-items: center;
  justify-content: center;
}

.value-text {
  display: block;
  width: 100%;
  text-align: right;
}

/* 操作图标 */
.action-icon {
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  border-radius: var(--radius-sm);
}

.action-icon.edit {
  background: transparent;
  cursor: pointer;
  opacity: 0.4;
  transition: opacity var(--transition-fast), background var(--transition-fast);
}

.variable-row:hover .action-icon.edit {
  opacity: 0.7;
}

.action-icon.edit:hover {
  opacity: 1;
  background: rgba(255, 255, 255, 0.1);
}

.action-icon.locked {
  opacity: 0.25;
  font-size: 10px;
}

/* 编辑输入框 */
.edit-input {
  width: 100%;
  padding: 2px 6px;
  background: var(--body-color);
  border: 1px solid var(--primary);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-family: var(--font-mono);
  font-size: 12px;
  text-align: right;
}

/* 空状态 */
.empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100px;
  color: var(--text-muted);
}
</style>
