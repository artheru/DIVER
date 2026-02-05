<!--
  @file components/graph/CoralNodeView.vue
  @description Coral MCU 节点组件 - 使用 vue-flow
  
  布局结构：
  - 标题栏：折叠按钮 + 节点名 + 状态指示器
  - Harness: in/out 连接点
  - Base Config: URI + Logic
  - Port Config: 端口配置列表
  - LowerIO: 只读变量显示
-->

<template>
  <div 
    class="coral-node" 
    :class="{ 
      collapsed: isCollapsed, 
      readonly: !canEdit,
      selected: selected
    }"
  >
    <!-- Handle 连接点 - 始终存在，位置固定在节点边缘 -->
    <Handle 
      type="target" 
      :position="Position.Left" 
      id="in" 
      class="node-handle handle-in handle-visible"
    />
    <Handle 
      type="source" 
      :position="Position.Right" 
      id="out" 
      class="node-handle handle-out handle-visible"
    />

    <!-- 删除按钮 - 左上角 -->
    <button
      v-if="canEdit"
      class="delete-btn"
      @click.stop="confirmDelete"
      title="Delete Node"
    >×</button>

    <!-- 标题栏：名称 + 升级 + 状态 -->
    <div class="node-header" @click="toggleCollapse">
      <span class="collapse-btn">{{ isCollapsed ? '▶' : '▼' }}</span>
      <input
        v-if="!isCollapsed && canEdit"
        class="node-name-input"
        v-model="localNodeName"
        @click.stop
        @blur="updateNodeName"
        @keyup.enter="($event.target as HTMLInputElement)?.blur()"
      />
      <span v-else class="node-name">{{ data.nodeName }}</span>
      <!-- 升级按钮 -->
      <button
        v-if="canEdit"
        class="upgrade-btn"
        @click.stop="openUpgradeDialog"
        :disabled="isProbing"
        title="Firmware Upgrade"
      >{{ isProbing ? '...' : '⬆' }}</button>
      <span class="status-badge" :class="runStateBadgeClass">
        {{ runStateText }}
      </span>
    </div>

    <!-- 展开内容 -->
    <div v-show="!isCollapsed" class="node-content">

      <!-- Base Config -->
      <div class="section">
        <div class="section-title">BASE CONFIG</div>
        
        <!-- URI (只读，添加时确定，不可修改) -->
        <div class="config-row readonly">
          <span class="config-label">URI</span>
          <div class="config-value uri-display">
            <template v-if="parsedUri.mode === 'name'">
              <span class="uri-port">{{ parsedUri.port }}</span>
              <span class="uri-baud">{{ parsedUri.baudrate }}</span>
            </template>
            <template v-else-if="parsedUri.mode === 'vidpid'">
              <span class="uri-vid">V{{ parsedUri.vid }}</span>
              <span class="uri-pid">P{{ parsedUri.pid }}</span>
              <span v-if="parsedUri.serial" class="uri-serial">S{{ parsedUri.serial }}</span>
            </template>
            <span v-else class="uri-unknown">Not Set</span>
          </div>
          <span class="lock-icon" title="URI cannot be changed after node creation">🔒</span>
        </div>

        <!-- Logic -->
        <div class="config-row">
          <span class="config-label">Logic</span>
          <n-select
            v-if="canEdit"
            v-model:value="localLogicName"
            :options="logicOptions"
            size="small"
            placeholder="Select Logic"
            class="logic-select"
            @update:value="updateLogicName"
          />
          <span v-else class="config-value">{{ data.logicName || 'Not Set' }}</span>
        </div>
      </div>

      <!-- Port View (统一显示端口配置和统计) -->
      <PortView
        v-if="hasPortsToShow"
        :uuid="id"
        :port-configs="portConfigs"
        :stats="nodeStats"
        :can-edit="canEdit"
        :app-state="appState"
        @edit="openPortEditor"
      />

      <!-- IO View (LED 样式) -->
      <IOView
        v-if="hasIO"
        :digital-inputs="nodeStats?.digitalInputs ?? 0"
        :digital-outputs="nodeStats?.digitalOutputs ?? 0"
        :input-count="digitalInputCount"
        :output-count="digitalOutputCount"
      />

      <!-- LowerIO Variables -->
      <div class="section" v-if="lowerIOVariables.length > 0">
        <div class="section-title">LOWER I/O</div>
        <div class="variable-list">
          <div v-for="variable in lowerIOVariables" :key="variable.name" class="variable-item">
            <span class="var-type">{{ formatType(variable.type) }}</span>
            <span class="var-name">{{ variable.name }}</span>
            <span class="var-value">{{ formatValue(variable.value) }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Port 配置编辑器 -->
    <PortConfigEdit
      v-model="localPortConfigs"
      v-model:show="showPortEdit"
      :ports="portDescriptors"
      @update:model-value="updatePortConfigs"
    />
    
    <!-- 固件升级对话框 -->
    <UpgradeDialog
      v-model:show="showUpgradeDialog"
      :mcu-uri="data.mcuUri"
      :current-version="probeVersion"
      @complete="handleUpgradeComplete"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import { NSelect, useDialog } from 'naive-ui'
import { useRuntimeStore, useFilesStore, useProjectStore } from '@/stores'
import { storeToRefs } from 'pinia'
import { getLogicList, programNode, configureNode, removeNode, probeNode } from '@/api/device'
import type { PortConfig, RuntimeStats, LogicInfo, LayoutInfo, VersionInfo } from '@/types'
import PortConfigEdit from './PortConfigEdit.vue'
import PortView from './PortView.vue'
import IOView from './IOView.vue'
import UpgradeDialog from './UpgradeDialog.vue'

// Props from vue-flow
const props = defineProps<{
  id: string
  data: {
    nodeName: string
    mcuUri: string
    logicName: string
    runState: string
    isConnected: boolean
    isConfigured: boolean
    isProgrammed: boolean
    ports: PortConfig[]
    layout: LayoutInfo | null
  }
  selected: boolean
}>()

// vue-flow hook
import { useVueFlow } from '@vue-flow/core'
const { updateNodeData, removeNodes } = useVueFlow()

// Dialog
const dialog = useDialog()

// Runtime store
const runtimeStore = useRuntimeStore()
const { isRunning, appState, nodeStates } = storeToRefs(runtimeStore)

// Files store (用于监听 buildVersion)
const filesStore = useFilesStore()
const { buildVersion } = storeToRefs(filesStore)

// Project store (用于保存)
const projectStore = useProjectStore()

// ============================================
// 本地状态
// ============================================

const isCollapsed = ref(false)
const localNodeName = ref(props.data.nodeName)
const localLogicName = ref(props.data.logicName)
const localPortConfigs = ref<PortConfig[]>(props.data.ports || [])

// 编辑器状态
const showPortEdit = ref(false)

// 升级对话框状态
const showUpgradeDialog = ref(false)
const isProbing = ref(false)
const probeVersion = ref<VersionInfo | undefined>(undefined)

// Logic 列表
const logicList = ref<LogicInfo[]>([])

// ============================================
// 计算属性
// ============================================

// 是否可编辑（非运行状态）
const canEdit = computed(() => !isRunning.value)

// 运行状态 Badge 样式
const runStateBadgeClass = computed(() => {
  const state = props.data.runState?.toLowerCase() || 'offline'
  return state
})

const runStateText = computed(() => props.data.runState || 'Offline')

// 解析 URI 显示
const parsedUri = computed(() => {
  const uri = props.data.mcuUri || ''
  
  // serial://name=COM18&baudrate=1000000
  const nameMatch = uri.match(/name=([^&]+)/)
  const baudMatch = uri.match(/baudrate=(\d+)/)
  
  if (nameMatch) {
    return {
      mode: 'name',
      port: nameMatch[1],
      baudrate: baudMatch?.[1] || '1000000'
    }
  }
  
  // serial://vid=XXXX&pid=YYYY&serial=ZZZZ
  const vidMatch = uri.match(/vid=([^&]+)/)
  const pidMatch = uri.match(/pid=([^&]+)/)
  const serialMatch = uri.match(/serial=([^&]+)/)
  
  if (vidMatch && pidMatch) {
    return {
      mode: 'vidpid',
      vid: vidMatch[1],
      pid: pidMatch[1],
      serial: serialMatch?.[1] || '',
      baudrate: baudMatch?.[1] || '1000000'
    }
  }
  
  return { mode: 'unknown' }
})

// 端口配置
const portDescriptors = computed(() => props.data.layout?.ports || [])

// 端口配置（优先使用现有配置，否则从 layout 生成默认配置）
const portConfigs = computed(() => {
  if (props.data.ports && props.data.ports.length > 0) {
    return props.data.ports
  }
  // 从 layout 生成默认配置
  return portDescriptors.value.map(p => ({
    type: p.type,
    name: p.name,
    baud: p.type?.toLowerCase() === 'can' ? 1000000 : 115200,
    receiveFrameMs: p.type?.toLowerCase() === 'serial' ? 0 : undefined,
    retryTimeMs: p.type?.toLowerCase() === 'can' ? 10 : undefined
  }))
})

// 是否有端口可显示
const hasPortsToShow = computed(() => portConfigs.value.length > 0)

// 是否有 IO
const hasIO = computed(() => digitalInputCount.value > 0 || digitalOutputCount.value > 0)

// Logic 下拉选项
const logicOptions = computed(() => 
  logicList.value.map(l => ({ label: l.name, value: l.name }))
)

// 获取该节点的 LowerIO 变量
interface LowerIOVariable {
  name: string
  type: string
  value: unknown
}

const lowerIOVariables = computed((): LowerIOVariable[] => {
  // TODO: 根据节点 ID 过滤变量
  // 暂时返回空数组，等后端支持按节点分组
  return []
})

// 从 runtime store 获取节点统计
const nodeStats = computed((): RuntimeStats | null => {
  const state = nodeStates.value.get(props.id)
  return state?.stats || null
})

// IO 数量（从 layout 获取）
const digitalInputCount = computed(() => {
  return props.data.layout?.digitalInputCount ?? 0
})

const digitalOutputCount = computed(() => {
  return props.data.layout?.digitalOutputCount ?? 0
})

// ============================================
// 方法
// ============================================

/**
 * 打开升级对话框（先 Probe 获取当前版本）
 */
async function openUpgradeDialog() {
  if (isProbing.value) return
  
  isProbing.value = true
  probeVersion.value = undefined
  
  try {
    // Probe 获取当前版本信息
    const result = await probeNode(props.data.mcuUri)
    if (result.ok && result.version) {
      probeVersion.value = result.version
    }
    // 无论 Probe 是否成功都打开对话框
    showUpgradeDialog.value = true
  } catch (error) {
    console.error('[CoralNode] Probe failed:', error)
    // 即使失败也打开对话框，只是没有版本信息
    showUpgradeDialog.value = true
  } finally {
    isProbing.value = false
  }
}

/**
 * 升级完成回调
 */
function handleUpgradeComplete() {
  showUpgradeDialog.value = false
  // 刷新节点信息
  runtimeStore.refreshNodes()
}

function confirmDelete() {
  dialog.warning({
    title: 'Delete Node',
    content: `Are you sure you want to delete "${props.data.nodeName}"?`,
    positiveText: 'Delete',
    negativeText: 'Cancel',
    onPositiveClick: async () => {
      await deleteNode()
    }
  })
}

async function deleteNode() {
  try {
    const result = await removeNode(props.id)
    if (result.ok) {
      console.log(`[CoralNode] Node deleted: ${props.id}`)
      // 从 vue-flow 中移除节点
      removeNodes([props.id])
      // 刷新变量列表
      await runtimeStore.refreshFieldMetas()
      await runtimeStore.refreshVariables()
      // 保存到磁盘
      await projectStore.saveProject({ silent: true })
    } else {
      console.error(`[CoralNode] Failed to delete node`)
    }
  } catch (error) {
    console.error(`[CoralNode] Error deleting node:`, error)
  }
}

function toggleCollapse() {
  isCollapsed.value = !isCollapsed.value
}

async function updateNodeName() {
  if (localNodeName.value !== props.data.nodeName) {
    // 先更新本地 UI
    updateNodeData(props.id, { nodeName: localNodeName.value })
    
    // 保存到后端
    try {
      const result = await configureNode(props.id, { nodeName: localNodeName.value })
      if (result.ok) {
        console.log(`[CoralNode] Node name updated: ${props.id} -> ${localNodeName.value}`)
        // 刷新变量元信息和变量列表（因为 __iteration 变量名包含节点名）
        await runtimeStore.refreshFieldMetas()
        await runtimeStore.refreshVariables()
        // 持久化到磁盘
        await projectStore.saveProject({ silent: true })
      } else {
        console.error(`[CoralNode] Failed to save node name`)
      }
    } catch (error) {
      console.error(`[CoralNode] Error saving node name:`, error)
    }
  }
}

async function updateLogicName(newLogic: string) {
  console.log(`[CoralNode] updateLogicName called: uuid=${props.id}, logicName=${newLogic}`)
  
  // 先更新本地 UI
  updateNodeData(props.id, { logicName: newLogic })
  
  // 调用后端 API 编程节点
  console.log(`[CoralNode] Calling programNode API: uuid=${props.id}, logicName=${newLogic}`)
  try {
    const result = await programNode(props.id, newLogic)
    console.log(`[CoralNode] programNode API response:`, JSON.stringify(result))
    if (result.ok) {
      console.log(`[CoralNode] Programmed ${props.id} with ${newLogic}, size: ${result.programSize}`)
      // 更新 isProgrammed 状态
      updateNodeData(props.id, { isProgrammed: true })
      // 刷新变量列表和字段元信息，更新遥控器绑定的可用变量列表
      await runtimeStore.refreshVariables()
      await runtimeStore.refreshFieldMetas()
      // 持久化到磁盘（保存 logicName 和编译后的程序）
      await projectStore.saveProject({ silent: true })
    } else {
      console.error(`[CoralNode] Failed to program ${props.id}:`, result)
      // 如果失败，清除 isProgrammed 状态
      updateNodeData(props.id, { isProgrammed: false })
    }
  } catch (error) {
    console.error(`[CoralNode] Error programming ${props.id}:`, error)
    updateNodeData(props.id, { isProgrammed: false })
  }
}

function openPortEditor() {
  if (!canEdit.value) return
  
  // 如果有现有配置，使用它
  if (props.data.ports && props.data.ports.length > 0) {
    localPortConfigs.value = [...props.data.ports]
  } else if (portDescriptors.value.length > 0) {
    // 否则从 layout 生成默认配置
    localPortConfigs.value = portDescriptors.value.map(p => {
      const isSerial = p.type.toLowerCase() === 'serial'
      return {
        type: p.type,
        name: p.name,
        baud: isSerial ? 115200 : 1000000,
        receiveFrameMs: isSerial ? 0 : undefined,
        retryTimeMs: isSerial ? undefined : 10
      }
    })
  } else {
    localPortConfigs.value = []
  }
  
  showPortEdit.value = true
}

async function updatePortConfigs(newConfigs: PortConfig[]) {
  // 更新前端显示
  updateNodeData(props.id, { ports: newConfigs })
  
  // 保存到后端
  try {
    const result = await configureNode(props.id, { portConfigs: newConfigs })
    if (result.ok) {
      console.log(`[CoralNode] Port configs saved for ${props.id}`)
      // 持久化到磁盘
      await projectStore.saveProject({ silent: true })
    } else {
      console.error(`[CoralNode] Failed to save port configs for ${props.id}`)
    }
  } catch (error) {
    console.error(`[CoralNode] Error saving port configs:`, error)
  }
}

// 格式化类型名
function formatType(type: string): string {
  const typeMap: Record<string, string> = {
    'Int32': 'i32',
    'UInt32': 'u32',
    'Int16': 'i16',
    'UInt16': 'u16',
    'Single': 'f32',
    'Double': 'f64',
    'Byte': 'u8',
    'SByte': 'i8',
    'Boolean': 'bool',
    'String': 'str'
  }
  return typeMap[type] || type?.toLowerCase() || '?'
}

// 格式化值显示
function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '-'
  if (typeof value === 'number') {
    return Number.isInteger(value) ? String(value) : value.toFixed(2)
  }
  return String(value)
}

// 加载 Logic 列表
async function loadLogicList() {
  try {
    const result = await getLogicList()
    if (result.ok) {
      logicList.value = result.logics
    }
  } catch (error) {
    console.error('[CoralNode] Failed to load logic list:', error)
  }
}


// ============================================
// 生命周期
// ============================================

onMounted(() => {
  loadLogicList()
})

// 同步 props 到本地状态
watch(() => props.data.nodeName, (v) => { localNodeName.value = v })
watch(() => props.data.logicName, (v) => { localLogicName.value = v })
watch(() => props.data.ports, (v) => { localPortConfigs.value = v || [] })

// 监听 buildVersion 变化，重新加载 Logic 列表
watch(buildVersion, () => {
  console.log('[CoralNode] Build version changed, reloading logic list')
  loadLogicList()
})
</script>

<style scoped>
.coral-node {
  position: relative;
  background: linear-gradient(180deg, #1e293b, #0f172a);
  border: 2px solid #334155;
  border-radius: 8px;
  min-width: 280px;
  font-family: var(--font-family);
  font-size: 12px;
  color: #e2e8f0;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
  transition: border-color 0.2s, box-shadow 0.2s;
}

.coral-node.selected {
  border-color: #4f8cff;
  box-shadow: 0 0 0 2px rgba(79, 140, 255, 0.3), 0 4px 12px rgba(0, 0, 0, 0.4);
}

.coral-node.readonly {
  opacity: 0.85;
}

.coral-node.collapsed {
  min-width: 200px;
}

/* 删除按钮 */
.delete-btn {
  position: absolute;
  top: -8px;
  left: -8px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: none;
  background: #ef4444;
  color: white;
  font-size: 14px;
  font-weight: bold;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.2s, transform 0.2s, background 0.2s;
  z-index: 10;
}

.coral-node:hover .delete-btn {
  opacity: 1;
}

.delete-btn:hover {
  background: #dc2626;
  transform: scale(1.1);
}

/* 标题栏 */
.node-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 6px 6px 0 0;
  cursor: pointer;
  user-select: none;
}

.collapse-btn {
  font-size: 10px;
  color: #a0aec0;
  width: 12px;
}

.node-name {
  font-weight: 600;
  color: #f1f5f9;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-name-input {
  width: 120px;
  background: rgba(0, 0, 0, 0.3);
  border: 1px solid #4a5568;
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 12px;
  font-weight: 600;
  color: #f1f5f9;
  outline: none;
}

.node-name-input:focus {
  border-color: #4f8cff;
}

/* 升级按钮 */
.upgrade-btn {
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid #4a5568;
  background: rgba(59, 130, 246, 0.2);
  color: #60a5fa;
  font-size: 10px;
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.upgrade-btn:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.4);
  border-color: #60a5fa;
}

.upgrade-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* 状态徽章 - 右对齐 */
.status-badge {
  margin-left: auto;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 10px;
  font-weight: 500;
  background: rgba(100, 116, 139, 0.3);
  color: #94a3b8;
  white-space: nowrap;
  flex-shrink: 0;
}

.status-badge.active {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
}

/* 运行状态样式 */
.status-badge.offline {
  background: rgba(113, 128, 150, 0.3);
  color: #a0aec0;
}

.status-badge.idle {
  background: rgba(245, 158, 11, 0.2);
  color: #f59e0b;
}

.status-badge.running {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
  animation: badge-pulse 1.5s infinite;
}

.status-badge.error {
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
}

@keyframes badge-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.7; }
}

/* 内容区 */
.node-content {
  padding: 8px 0;
}

/* Harness 区域 */
.harness-section {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: rgba(0, 0, 0, 0.2);
  margin: 0 8px 8px 8px;
  border-radius: 4px;
}

.harness-in, .harness-out {
  display: flex;
  align-items: center;
  gap: 6px;
}

.handle-label {
  font-size: 11px;
  color: #a0aec0;
}

/* vue-flow Handle 样式 - 固定位置在节点边缘 */
.node-handle {
  width: 12px !important;
  height: 12px !important;
  background: transparent !important;
  border: 2px solid transparent !important;
  transition: background 0.2s, border-color 0.2s;
}

/* 收缩状态时显示 Handle */
.node-handle.handle-visible {
  background: #4f8cff !important;
  border-color: #1e293b !important;
}

:deep(.handle-in) {
  left: -6px !important;
  top: 50% !important;
  transform: translateY(-50%) !important;
}

:deep(.handle-out) {
  right: -6px !important;
  top: 50% !important;
  transform: translateY(-50%) !important;
}

/* 连接时高亮 */
:deep(.vue-flow__handle.connecting),
:deep(.vue-flow__handle.valid) {
  background: #22c55e !important;
  border-color: #1e293b !important;
}

/* Section 通用样式 */
.section {
  margin: 0 8px 8px 8px;
}

.section-title {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 10px;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 6px 4px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  margin-bottom: 6px;
}

.edit-btn {
  font-size: 10px;
  color: #4f8cff;
  cursor: pointer;
  text-transform: none;
}

.edit-btn:hover {
  text-decoration: underline;
}

/* 配置行 */
.config-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 4px;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.15s;
}

.config-row:hover {
  background: rgba(255, 255, 255, 0.05);
}

.config-row.readonly {
  cursor: default;
}

.config-row.readonly:hover {
  background: transparent;
}

.config-label {
  width: 50px;
  font-size: 11px;
  color: #94a3b8;
}

.config-value {
  flex: 1;
  color: #e2e8f0;
  font-size: 11px;
}

.uri-display {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.uri-port, .uri-vid {
  font-weight: 600;
  color: #22c55e;
}

.uri-baud, .uri-pid, .uri-serial {
  font-size: 10px;
  color: #94a3b8;
}

.uri-unknown {
  color: #64748b;
  font-style: italic;
}

.edit-icon {
  font-size: 12px;
  opacity: 0.6;
}

.lock-icon {
  font-size: 10px;
  opacity: 0.5;
}

.logic-select {
  flex: 1;
  max-width: 180px;
}

/* 统一下拉框字体大小 */
.logic-select :deep(.n-base-selection-label) {
  font-size: 11px !important;
}

.logic-select :deep(.n-base-selection-input) {
  font-size: 11px !important;
}

/* 端口列表 */
.port-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.port-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  font-size: 11px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 4px;
  border: 1px solid rgba(255, 255, 255, 0.05);
}

.port-index {
  font-weight: 600;
  font-family: var(--font-mono);
  color: #e2e8f0;
  font-size: 11px;
  min-width: 42px;
}

.port-type {
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 9px;
  font-weight: 500;
  text-transform: uppercase;
}

.port-type.serial {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.port-type.can {
  background: rgba(59, 130, 246, 0.15);
  color: #3b82f6;
}

.port-name {
  color: #64748b;
  font-size: 11px;
  flex: 1;
}

.port-baud {
  font-family: var(--font-mono);
  color: #94a3b8;
  font-size: 11px;
  min-width: 56px;
  text-align: right;
}

.port-extra {
  font-family: var(--font-mono);
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 3px;
  background: rgba(100, 116, 139, 0.15);
  color: #94a3b8;
}

/* 变量列表 */
.variable-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.variable-item {
  display: grid;
  grid-template-columns: 32px 1fr 60px;
  gap: 6px;
  padding: 3px 4px;
  font-size: 11px;
  background: rgba(0, 0, 0, 0.15);
  border-radius: 2px;
}

.var-type {
  font-family: var(--font-mono);
  color: #64748b;
  font-size: 10px;
}

.var-name {
  color: #94a3b8;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.var-value {
  font-family: var(--font-mono);
  color: #22c55e;
  text-align: right;
}
</style>
