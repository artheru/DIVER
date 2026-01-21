<!--
  @file components/graph/AddNodeDialog.vue
  @description 添加节点对话框
  
  添加节点前需要：
  1. 选择 MCU URI（COM 端口和波特率）
  2. 验证连接（调用后端 probe API）
  3. 验证成功后返回节点信息
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false">
    <n-card title="Add MCU Node" style="width: 480px">
      <div class="add-node-form">
        <!-- URI 模式选择 -->
        <n-form-item label="Connection Mode">
          <n-radio-group v-model:value="uriMode" :disabled="isProbing">
            <n-radio-button value="name">COM Port</n-radio-button>
            <n-radio-button value="vidpid">VID/PID</n-radio-button>
          </n-radio-group>
        </n-form-item>

        <!-- COM 端口模式 -->
        <template v-if="uriMode === 'name'">
          <n-form-item label="COM Port">
            <n-select
              v-model:value="selectedPort"
              :options="portOptions"
              :loading="loadingPorts"
              placeholder="Select COM port"
              :disabled="isProbing"
            />
            <n-button 
              size="small" 
              quaternary 
              @click="refreshPorts" 
              :loading="loadingPorts"
              style="margin-left: 8px"
            >
              Refresh
            </n-button>
          </n-form-item>
        </template>

        <!-- VID/PID 模式 -->
        <template v-else>
          <n-form-item label="VID">
            <n-input v-model:value="vid" placeholder="e.g. 1234" :disabled="isProbing" />
          </n-form-item>
          <n-form-item label="PID">
            <n-input v-model:value="pid" placeholder="e.g. 5678" :disabled="isProbing" />
          </n-form-item>
          <n-form-item label="Serial (optional)">
            <n-input v-model:value="serial" placeholder="Device serial number" :disabled="isProbing" />
          </n-form-item>
        </template>

        <!-- 波特率 -->
        <n-form-item label="Baud Rate">
          <n-select
            v-model:value="baudrate"
            :options="baudOptions"
            :disabled="isProbing"
          />
        </n-form-item>

        <!-- 生成的 URI 预览 -->
        <n-form-item label="URI Preview">
          <code class="uri-preview">{{ generatedUri }}</code>
        </n-form-item>

        <!-- 探测结果 -->
        <div v-if="probeResult" class="probe-result" :class="{ success: probeResult.ok, error: !probeResult.ok }">
          <template v-if="probeResult.ok">
            <div class="result-header">✓ MCU Connected</div>
            <div class="result-info">
              <span class="label">Product:</span>
              <span class="value">{{ probeResult.version?.productionName || 'Unknown' }}</span>
            </div>
            <div class="result-info">
              <span class="label">Commit:</span>
              <span class="value">{{ probeResult.version?.gitCommit || '-' }}{{ probeResult.version?.gitTag ? ` (${probeResult.version.gitTag})` : '' }}</span>
            </div>
            <div class="result-info">
              <span class="label">Build Time:</span>
              <span class="value">{{ probeResult.version?.buildTime || '-' }}</span>
            </div>
            <div class="result-info" v-if="probeResult.layout?.ports?.length">
              <span class="label">Ports:</span>
              <span class="value ports-list">
                <span v-for="(port, idx) in probeResult.layout.ports" :key="idx" class="port-tag" :class="port.type.toLowerCase()">
                  {{ port.name }}
                </span>
              </span>
            </div>
          </template>
          <template v-else>
            <div class="result-header">✗ Connection Failed</div>
            <div class="result-error">{{ probeResult.error }}</div>
          </template>
        </div>

        <!-- 探测进度 -->
        <div v-if="isProbing" class="probing-status">
          <n-spin size="small" />
          <span>Connecting to MCU...</span>
        </div>
      </div>

      <template #footer>
        <div class="dialog-footer">
          <n-button @click="handleCancel" :disabled="isProbing">Cancel</n-button>
          <n-button 
            type="primary" 
            @click="handleProbe" 
            :loading="isProbing"
            :disabled="!canProbe"
          >
            {{ probeResult?.ok ? 'Re-probe' : 'Probe' }}
          </n-button>
          <n-button 
            type="success" 
            @click="handleConfirm" 
            :disabled="!probeResult?.ok"
          >
            Add Node
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { NModal, NCard, NButton, NFormItem, NSelect, NRadioGroup, NRadioButton, NInput, NSpin } from 'naive-ui'
import { getAvailablePorts, probeNode, type NodeProbeResponse, type PortLayoutInfo } from '@/api/device'

// Props
const props = defineProps<{
  show: boolean
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:show', value: boolean): void
  (e: 'confirm', data: AddNodeResult): void
}>()

// 端口配置（添加节点时生成）
export interface PortConfigData {
  type: string
  name: string
  baud: number
  receiveFrameMs?: number
  retryTimeMs?: number
}

// 返回的节点数据
export interface AddNodeResult {
  nodeId: string  // 后端分配的节点 ID（与 DIVERSession 中的 ID 一致）
  mcuUri: string
  version: {
    productionName: string
    gitTag: string
  }
  layout?: {
    ports: PortLayoutInfo[]
  }
  // 根据 layout 生成的默认端口配置
  ports: PortConfigData[]
}

// 本地状态
const showModal = computed({
  get: () => props.show,
  set: (v) => emit('update:show', v)
})

const uriMode = ref<'name' | 'vidpid'>('name')
const selectedPort = ref<string | null>(null)
const vid = ref('')
const pid = ref('')
const serial = ref('')
const baudrate = ref(1000000)

const loadingPorts = ref(false)
const availablePorts = ref<string[]>([])
const isProbing = ref(false)
const probeResult = ref<NodeProbeResponse | null>(null)

// 波特率选项
const baudOptions = [
  { label: '115200', value: 115200 },
  { label: '230400', value: 230400 },
  { label: '460800', value: 460800 },
  { label: '921600', value: 921600 },
  { label: '1000000', value: 1000000 },
  { label: '2000000', value: 2000000 },
]

// 端口选项
const portOptions = computed(() => 
  availablePorts.value.map(p => ({ label: p, value: p }))
)

// 生成的 URI
const generatedUri = computed(() => {
  if (uriMode.value === 'name') {
    if (!selectedPort.value) return 'serial://name=???&baudrate=' + baudrate.value
    return `serial://name=${selectedPort.value}&baudrate=${baudrate.value}`
  } else {
    if (!vid.value || !pid.value) return 'serial://vid=???&pid=???&baudrate=' + baudrate.value
    let uri = `serial://vid=${vid.value}&pid=${pid.value}`
    if (serial.value) uri += `&serial=${serial.value}`
    uri += `&baudrate=${baudrate.value}`
    return uri
  }
})

// 是否可以探测
const canProbe = computed(() => {
  if (uriMode.value === 'name') {
    return !!selectedPort.value
  } else {
    return !!vid.value && !!pid.value
  }
})

// 刷新端口列表
async function refreshPorts() {
  loadingPorts.value = true
  try {
    const result = await getAvailablePorts()
    if (result.ok) {
      availablePorts.value = result.ports
    }
  } catch (error) {
    console.error('[AddNodeDialog] Failed to load ports:', error)
  } finally {
    loadingPorts.value = false
  }
}

// 探测 MCU
async function handleProbe() {
  if (!canProbe.value || isProbing.value) return
  
  isProbing.value = true
  probeResult.value = null
  
  try {
    const result = await probeNode(generatedUri.value)
    probeResult.value = result
  } catch (error) {
    probeResult.value = {
      ok: false,
      error: String(error)
    }
  } finally {
    isProbing.value = false
  }
}

// 取消
function handleCancel() {
  showModal.value = false
  resetForm()
}

// 确认添加
function handleConfirm() {
  if (!probeResult.value?.ok || !probeResult.value.nodeId) return
  
  // 根据 layout.ports 生成默认端口配置
  const ports: PortConfigData[] = (probeResult.value.layout?.ports || []).map(p => {
    const isSerial = p.type.toLowerCase() === 'serial'
    return {
      type: p.type,
      name: p.name,
      baud: isSerial ? 115200 : 1000000, // Serial 默认 115200, CAN 默认 1000000
      receiveFrameMs: isSerial ? 0 : undefined,
      retryTimeMs: isSerial ? undefined : 10
    }
  })
  
  emit('confirm', {
    nodeId: probeResult.value.nodeId,  // 使用后端分配的 ID
    mcuUri: generatedUri.value,
    version: {
      productionName: probeResult.value.version?.productionName || 'Unknown',
      gitTag: probeResult.value.version?.gitTag || ''
    },
    layout: probeResult.value.layout,
    ports
  })
  
  showModal.value = false
  resetForm()
}

// 重置表单
function resetForm() {
  probeResult.value = null
  selectedPort.value = null
  vid.value = ''
  pid.value = ''
  serial.value = ''
}

// 监听 show 变化
watch(() => props.show, (newVal) => {
  if (newVal) {
    refreshPorts()
    probeResult.value = null
  }
})

onMounted(() => {
  if (props.show) {
    refreshPorts()
  }
})
</script>

<style scoped>
.add-node-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.uri-preview {
  display: block;
  padding: 8px 12px;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 4px;
  font-family: var(--font-mono);
  font-size: 12px;
  color: #94a3b8;
  word-break: break-all;
}

.probe-result {
  padding: 12px;
  border-radius: 6px;
  margin-top: 8px;
}

.probe-result.success {
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.probe-result.error {
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.result-header {
  font-weight: 600;
  margin-bottom: 8px;
}

.probe-result.success .result-header {
  color: #22c55e;
}

.probe-result.error .result-header {
  color: #ef4444;
}

.result-info {
  display: flex;
  gap: 8px;
  font-size: 12px;
  margin-top: 4px;
}

.result-info .label {
  color: #64748b;
  min-width: 70px;
}

.result-info .value {
  color: #e2e8f0;
}

.result-error {
  font-size: 12px;
  color: #fca5a5;
}

.ports-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.port-tag {
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 10px;
  font-weight: 500;
}

.port-tag.serial {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.port-tag.can {
  background: rgba(59, 130, 246, 0.15);
  color: #3b82f6;
}

.probing-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px;
  color: #94a3b8;
  font-size: 13px;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
