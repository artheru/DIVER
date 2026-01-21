<!--
  @file components/graph/McuUriEdit.vue
  @description MCU URI 编辑对话框
  
  支持两种 URI 格式：
  1. serial://name=COMXX&baudrate=YYYYYY
  2. serial://vid=XXXX&pid=YYYY&serial=SSSS
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false">
    <n-card title="Edit MCU URI" style="width: 450px">
      <!-- 模式切换 -->
      <div class="mode-switch">
        <n-radio-group v-model:value="mode" size="small">
          <n-radio-button value="name">COM Port</n-radio-button>
          <n-radio-button value="vidpid">VID/PID</n-radio-button>
        </n-radio-group>
      </div>
      
      <!-- COM Port 模式 -->
      <div v-if="mode === 'name'" class="form-section">
        <n-form-item label="COM Port">
          <n-select
            v-model:value="comPort"
            :options="portOptions"
            placeholder="Select COM port"
            :loading="loadingPorts"
            filterable
          />
          <n-button size="small" @click="refreshPorts" :loading="loadingPorts" style="margin-left: 8px">
            Refresh
          </n-button>
        </n-form-item>
        
        <n-form-item label="Baudrate">
          <n-select
            v-model:value="baudrate"
            :options="baudrateOptions"
            placeholder="Select baudrate"
          />
        </n-form-item>
      </div>
      
      <!-- VID/PID 模式 -->
      <div v-else class="form-section">
        <n-form-item label="Vendor ID (VID)">
          <n-input v-model:value="vid" placeholder="e.g., 1234" />
        </n-form-item>
        
        <n-form-item label="Product ID (PID)">
          <n-input v-model:value="pid" placeholder="e.g., 5678" />
        </n-form-item>
        
        <n-form-item label="Serial Number (optional)">
          <n-input v-model:value="serial" placeholder="e.g., ABC123" />
        </n-form-item>
        
        <n-form-item label="Baudrate">
          <n-select
            v-model:value="baudrate"
            :options="baudrateOptions"
            placeholder="Select baudrate"
          />
        </n-form-item>
      </div>
      
      <!-- 预览 -->
      <div class="uri-preview">
        <span class="label">URI:</span>
        <code>{{ generatedUri }}</code>
      </div>
      
      <template #footer>
        <div class="dialog-footer">
          <n-button @click="handleCancel">Cancel</n-button>
          <n-button type="primary" @click="handleConfirm" :disabled="!isValid">
            Confirm
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { NModal, NCard, NButton, NFormItem, NInput, NSelect, NRadioGroup, NRadioButton } from 'naive-ui'
import { getAvailablePorts } from '@/api/device'

// Props
const props = defineProps<{
  modelValue: string
  show: boolean
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'update:show', value: boolean): void
}>()

// 本地状态
const showModal = computed({
  get: () => props.show,
  set: (v) => emit('update:show', v)
})

const mode = ref<'name' | 'vidpid'>('name')
const comPort = ref('')
const baudrate = ref(1000000)
const vid = ref('')
const pid = ref('')
const serial = ref('')

const loadingPorts = ref(false)
const availablePorts = ref<string[]>([])

// 波特率选项
const baudrateOptions = [
  { label: '9600', value: 9600 },
  { label: '19200', value: 19200 },
  { label: '38400', value: 38400 },
  { label: '57600', value: 57600 },
  { label: '115200', value: 115200 },
  { label: '230400', value: 230400 },
  { label: '460800', value: 460800 },
  { label: '921600', value: 921600 },
  { label: '1000000', value: 1000000 },
  { label: '2000000', value: 2000000 }
]

// 端口选项
const portOptions = computed(() => {
  return availablePorts.value.map(port => ({
    label: port,
    value: port
  }))
})

// 生成的 URI
const generatedUri = computed(() => {
  if (mode.value === 'name') {
    if (!comPort.value) return 'serial://name=...&baudrate=...'
    return `serial://name=${comPort.value}&baudrate=${baudrate.value}`
  } else {
    if (!vid.value || !pid.value) return 'serial://vid=...&pid=...&baudrate=...'
    let uri = `serial://vid=${vid.value}&pid=${pid.value}`
    if (serial.value) {
      uri += `&serial=${serial.value}`
    }
    uri += `&baudrate=${baudrate.value}`
    return uri
  }
})

// 验证
const isValid = computed(() => {
  if (mode.value === 'name') {
    return !!comPort.value && !!baudrate.value
  } else {
    return !!vid.value && !!pid.value && !!baudrate.value
  }
})

// 解析现有 URI
function parseUri(uri: string) {
  if (!uri || !uri.startsWith('serial://')) {
    return
  }
  
  const paramString = uri.substring('serial://'.length)
  const params = new Map<string, string>()
  
  paramString.split('&').forEach(param => {
    const [key, value] = param.split('=')
    if (key && value) {
      params.set(key.toLowerCase(), value)
    }
  })
  
  if (params.has('name')) {
    mode.value = 'name'
    comPort.value = params.get('name') || ''
  } else if (params.has('vid') && params.has('pid')) {
    mode.value = 'vidpid'
    vid.value = params.get('vid') || ''
    pid.value = params.get('pid') || ''
    serial.value = params.get('serial') || ''
  }
  
  if (params.has('baudrate')) {
    baudrate.value = parseInt(params.get('baudrate') || '1000000', 10)
  }
}

// 刷新端口列表
async function refreshPorts() {
  loadingPorts.value = true
  try {
    const result = await getAvailablePorts()
    if (result.ok) {
      availablePorts.value = result.ports
    }
  } catch (error) {
    console.error('[McuUriEdit] Failed to fetch ports:', error)
  } finally {
    loadingPorts.value = false
  }
}

// 事件处理
function handleCancel() {
  showModal.value = false
}

function handleConfirm() {
  emit('update:modelValue', generatedUri.value)
  showModal.value = false
}

// 监听 show 变化，初始化数据
watch(() => props.show, (newVal) => {
  if (newVal) {
    parseUri(props.modelValue)
    refreshPorts()
  }
})

onMounted(() => {
  if (props.show) {
    parseUri(props.modelValue)
    refreshPorts()
  }
})
</script>

<style scoped>
.mode-switch {
  margin-bottom: 16px;
}

.form-section {
  margin-top: 8px;
}

.uri-preview {
  margin-top: 16px;
  padding: 10px 12px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: var(--radius-sm);
  font-size: 12px;
}

.uri-preview .label {
  color: var(--text-muted);
  margin-right: 8px;
}

.uri-preview code {
  font-family: var(--font-mono);
  color: var(--primary);
  word-break: break-all;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
