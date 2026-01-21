<!--
  @file components/graph/PortConfigEdit.vue
  @description 端口配置编辑对话框
  
  根据 MCU 的 Layout 信息，配置每个端口的参数：
  - Serial 端口: Baud rate, ReceiveFrameMs
  - CAN 端口: Baud rate, RetryTimeMs
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false">
    <n-card title="Port Configuration" style="width: 550px">
      <div v-if="editablePorts.length === 0" class="empty-state">
        <p>No ports available.</p>
        <p class="hint">Connect to device first to get port layout.</p>
      </div>
      
      <div v-else class="port-list">
        <div
          v-for="(port, index) in editablePorts"
          :key="index"
          class="port-item"
        >
          <div class="port-header">
            <span class="port-index">Port {{ index }}</span>
            <span class="port-type" :class="port.type.toLowerCase()">{{ port.type }}</span>
            <span class="port-name">{{ port.name || `Port${index}` }}</span>
          </div>
          
          <div class="port-config">
            <!-- Baud Rate - 根据端口类型显示不同选项 -->
            <n-form-item label="Baud Rate" label-placement="left" :show-feedback="false">
              <n-select
                :value="port.baud"
                @update:value="(v) => port.baud = parseBaudValue(v)"
                :options="getBaudOptions(port.type)"
                filterable
                tag
                :filter="baudFilter"
                style="width: 150px"
                placeholder="Select or input"
              />
            </n-form-item>
            
            <!-- Serial specific: ReceiveFrameMs -->
            <n-form-item
              v-if="port.type === 'Serial'"
              label="Frame Ms"
              label-placement="left"
              :show-feedback="false"
            >
              <n-input-number
                v-model:value="port.receiveFrameMs"
                :min="0"
                :max="1000"
                :step="1"
                style="width: 100px"
              />
            </n-form-item>
            
            <!-- CAN specific: RetryTimeMs -->
            <n-form-item
              v-if="port.type === 'CAN'"
              label="Retry Ms"
              label-placement="left"
              :show-feedback="false"
            >
              <n-input-number
                v-model:value="port.retryTimeMs"
                :min="0"
                :max="1000"
                :step="1"
                style="width: 100px"
              />
            </n-form-item>
          </div>
        </div>
      </div>
      
      <template #footer>
        <div class="dialog-footer">
          <n-button @click="handleCancel">Cancel</n-button>
          <n-button type="primary" @click="handleConfirm" :disabled="!canSave">
            Save
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { NModal, NCard, NButton, NFormItem, NSelect, NInputNumber } from 'naive-ui'
import type { PortDescriptor, PortConfig } from '@/types'

// Props
const props = defineProps<{
  /** 当前端口配置 */
  modelValue: PortConfig[]
  /** 端口布局描述（来自 MCU Layout） */
  ports: PortDescriptor[]
  /** 是否显示 */
  show: boolean
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:modelValue', value: PortConfig[]): void
  (e: 'update:show', value: boolean): void
}>()

// 可编辑的端口配置
interface EditablePort {
  index: number
  type: 'Serial' | 'CAN' | 'Unknown'
  name: string
  baud: number
  receiveFrameMs: number
  retryTimeMs: number
}

const showModal = computed({
  get: () => props.show,
  set: (v) => emit('update:show', v)
})

const editablePorts = ref<EditablePort[]>([])

// Serial 波特率选项
const serialBaudOptions = [
  { label: '9600', value: 9600 },
  { label: '19200', value: 19200 },
  { label: '38400', value: 38400 },
  { label: '57600', value: 57600 },
  { label: '115200', value: 115200 },
  { label: '230400', value: 230400 },
  { label: '460800', value: 460800 },
  { label: '921600', value: 921600 },
  { label: '2000000', value: 2000000 },
  { label: '3000000', value: 3000000 }
]

// CAN 波特率选项
const canBaudOptions = [
  { label: '125000', value: 125000 },
  { label: '250000', value: 250000 },
  { label: '500000', value: 500000 },
  { label: '1000000', value: 1000000 }
]

// 根据端口类型获取波特率选项
function getBaudOptions(type: 'Serial' | 'CAN' | 'Unknown') {
  if (type === 'CAN') return canBaudOptions
  return serialBaudOptions
}

// 波特率过滤函数 - 支持数字前缀匹配
function baudFilter(pattern: string, option: { label?: string | unknown; value?: number | string }) {
  // 只允许数字输入
  if (!/^\d*$/.test(pattern)) return false
  const label = typeof option.label === 'string' ? option.label : ''
  return label.startsWith(pattern)
}

// 解析波特率值 - 确保返回数字
function parseBaudValue(value: number | string): number {
  if (typeof value === 'number') return value
  const parsed = parseInt(value, 10)
  return isNaN(parsed) ? 115200 : parsed
}

// 是否可以保存
const canSave = computed(() => {
  return editablePorts.value.length > 0
})

// 初始化可编辑端口列表
// 优先从 modelValue（现有配置）读取，它包含了端口的 name 和 type
function initEditablePorts() {
  console.log('[PortConfigEdit] initEditablePorts called')
  console.log('[PortConfigEdit] modelValue:', props.modelValue)
  console.log('[PortConfigEdit] ports (layout):', props.ports)
  
  // 如果 modelValue 有数据，使用它（包含 name）
  if (props.modelValue && props.modelValue.length > 0) {
    editablePorts.value = props.modelValue.map((pc, index) => ({
      index,
      type: (pc.type as 'Serial' | 'CAN' | 'Unknown') || 'Unknown',
      name: pc.name || `Port${index}`,
      baud: pc.baud ?? 115200,
      receiveFrameMs: pc.receiveFrameMs ?? 0,
      retryTimeMs: pc.retryTimeMs ?? 10
    }))
    console.log('[PortConfigEdit] Loaded from modelValue:', editablePorts.value.length, 'ports')
    return
  }
  
  // 否则从 ports（layout 描述）生成默认配置
  if (!props.ports || props.ports.length === 0) {
    editablePorts.value = []
    console.log('[PortConfigEdit] No ports available')
    return
  }
  
  editablePorts.value = props.ports.map((pd, index) => ({
    index: pd.index ?? index,
    type: pd.type || 'Unknown',
    name: pd.name || `Port${index}`,
    baud: pd.type === 'CAN' ? 1000000 : 115200,
    receiveFrameMs: 0,
    retryTimeMs: 10
  }))
  console.log('[PortConfigEdit] Generated from layout:', editablePorts.value.length, 'ports')
}

// 事件处理
function handleCancel() {
  showModal.value = false
}

function handleConfirm() {
  // 转换为 PortConfig 数组（保留 name）
  const configs: PortConfig[] = editablePorts.value.map(ep => ({
    type: ep.type,
    name: ep.name,  // 保留端口名称
    baud: ep.baud,
    receiveFrameMs: ep.type === 'Serial' ? ep.receiveFrameMs : undefined,
    retryTimeMs: ep.type === 'CAN' ? ep.retryTimeMs : undefined
  }))
  
  emit('update:modelValue', configs)
  showModal.value = false
}

// 监听 show 变化，初始化数据
watch(() => props.show, async (newVal) => {
  if (newVal) {
    // 使用 nextTick 确保 modelValue 已经更新
    await nextTick()
    initEditablePorts()
    console.log('[PortConfigEdit] Initialized with', editablePorts.value.length, 'ports')
  }
})

onMounted(async () => {
  if (props.show) {
    await nextTick()
    initEditablePorts()
  }
})
</script>

<style scoped>
.empty-state {
  padding: 20px;
  text-align: center;
  color: var(--text-muted);
}

.empty-state .hint {
  font-size: 12px;
  margin-top: 8px;
}

.port-list {
  max-height: 400px;
  overflow-y: auto;
}

.port-item {
  padding: 12px;
  margin-bottom: 8px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: var(--radius-sm);
  border: 1px solid var(--border-color);
}

.port-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--border-color);
}

.port-index {
  font-weight: 600;
  color: var(--text-color);
  font-family: var(--font-mono);
}

.port-type {
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 500;
  text-transform: uppercase;
}

.port-type.serial {
  background: rgba(34, 197, 94, 0.15);
  color: var(--success);
}

.port-type.can {
  background: rgba(59, 130, 246, 0.15);
  color: var(--info);
}

.port-type.unknown {
  background: rgba(160, 174, 192, 0.15);
  color: var(--text-muted);
}

.port-name {
  color: var(--text-muted);
  font-size: 12px;
}

.port-config {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}

.port-config :deep(.n-form-item) {
  margin-bottom: 0;
}

.port-config :deep(.n-form-item-label) {
  font-size: 12px;
  color: var(--text-muted);
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
