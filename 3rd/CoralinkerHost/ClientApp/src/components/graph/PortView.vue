<!--
  @file components/graph/PortView.vue
  @description 端口统一显示组件（合并配置和统计）
  
  显示格式：【Type】【Name】【Baud】【其他属性】 [TX监听] TX=? [RX监听] RX=?
  - 停止时和运行时显示相同宽度
  - 运行时显示实时 TX/RX 统计
  - 停止时 TX/RX 显示为 "-"
  - WireTap 监听勾选框允许开启/关闭端口数据监听
    - 在 idle 状态下可点击：修改预设配置，Start 时自动应用
    - 在 running 状态下可点击：实时修改 DIVERSession 中的 WireTap 状态
    - 在 starting/stopping/offline 状态下禁用
    - WireTap 配置不会持久化到项目文件
-->

<template>
  <div class="port-view">
    <!-- 标题行 -->
    <div class="section-title">
      PORTS
      <span class="edit-btn" v-if="canEdit" @click="$emit('edit')">Edit</span>
      <span class="uptime" v-if="stats">{{ formatUptime(stats.uptimeMs) }}</span>
    </div>
    
    <!-- 端口列表 -->
    <div class="port-list" v-if="displayPorts.length > 0">
      <div v-for="(port, idx) in displayPorts" :key="idx" class="port-item">
        <span class="port-type" :class="port.type?.toLowerCase()">{{ port.type }}</span>
        <span class="port-name">{{ port.name || `Port${idx}` }}</span>
        <span class="port-baud">{{ formatBaud(port.baud) }}</span>
        <span class="port-extra">{{ formatExtra(port) }}</span>
        <span class="spacer"></span>
        <!-- TX 统计和 WireTap -->
        <span class="port-stat tx">
          <button 
            class="wiretap-btn" 
            :class="{ active: getWireTapTx(idx) }"
            :title="getWireTapTx(idx) ? '停止监听 TX' : '监听 TX 数据'"
            :disabled="!canToggleWireTap"
            @click.stop="toggleWireTap(idx, 'tx', !getWireTapTx(idx))"
          >
            <span class="tap-dot"></span>
          </button>
          <span class="stat-label">TX</span>
          <span class="stat-value">{{ formatFrames(port.txFrames) }}</span>
        </span>
        <!-- RX 统计和 WireTap -->
        <span class="port-stat rx">
          <button 
            class="wiretap-btn" 
            :class="{ active: getWireTapRx(idx) }"
            :title="getWireTapRx(idx) ? '停止监听 RX' : '监听 RX 数据'"
            :disabled="!canToggleWireTap"
            @click.stop="toggleWireTap(idx, 'rx', !getWireTapRx(idx))"
          >
            <span class="tap-dot"></span>
          </button>
          <span class="stat-label">RX</span>
          <span class="stat-value">{{ formatFrames(port.rxFrames) }}</span>
        </span>
      </div>
    </div>
    <div v-else class="no-data">No ports configured</div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { RuntimeStats, PortConfig } from '@/types'
import { useWireTapStore } from '@/stores'
import type { AppState } from '@/stores/runtime'

const props = defineProps<{
  /** 节点 UUID */
  uuid: string
  /** 端口配置列表 */
  portConfigs: PortConfig[]
  /** 运行时统计数据（可选） */
  stats: RuntimeStats | null
  /** 是否可编辑 */
  canEdit: boolean
  /** 应用状态（用于 WireTap 控制） */
  appState: AppState
}>()

// WireTap 复选框启用条件：仅在 idle 或 running 状态下可点击
// 在 starting/stopping/offline 状态下禁用
const canToggleWireTap = computed(() => 
  props.appState === 'idle' || props.appState === 'running'
)

defineEmits<{
  (e: 'edit'): void
}>()

const wireTapStore = useWireTapStore()

// 获取端口的 TX WireTap 状态
function getWireTapTx(portIndex: number): boolean {
  return wireTapStore.getPortWireTap(props.uuid, portIndex).tx
}

// 获取端口的 RX WireTap 状态
function getWireTapRx(portIndex: number): boolean {
  return wireTapStore.getPortWireTap(props.uuid, portIndex).rx
}

// 切换 WireTap 状态
async function toggleWireTap(portIndex: number, direction: 'tx' | 'rx', enabled: boolean) {
  const current = wireTapStore.getPortWireTap(props.uuid, portIndex)
  const newConfig = {
    tx: direction === 'tx' ? enabled : current.tx,
    rx: direction === 'rx' ? enabled : current.rx
  }
  // 获取端口类型和名称
  const port = displayPorts.value[portIndex]
  const portType = port?.type || 'Serial'
  const portName = port?.name || `Port ${portIndex}`
  await wireTapStore.setPortWireTap(props.uuid, portIndex, newConfig.rx, newConfig.tx, portType, portName)
}

// 合并端口配置和统计数据
interface DisplayPort {
  type: string
  name: string
  baud: number
  receiveFrameMs?: number
  retryTimeMs?: number
  txFrames?: number
  rxFrames?: number
}

const displayPorts = computed((): DisplayPort[] => {
  return props.portConfigs.map((config, idx) => {
    const stat = props.stats?.ports?.[idx]
    return {
      type: config.type || 'Unknown',
      name: config.name || `Port${idx}`,
      baud: config.baud || 0,
      receiveFrameMs: config.receiveFrameMs,
      retryTimeMs: config.retryTimeMs,
      txFrames: stat?.txFrames,
      rxFrames: stat?.rxFrames
    }
  })
})

// 格式化波特率
function formatBaud(baud: number): string {
  if (!baud) return '-'
  if (baud >= 1000000) return `${baud / 1000000}M`
  if (baud >= 1000) return `${baud / 1000}K`
  return String(baud)
}

// 格式化额外参数
function formatExtra(port: DisplayPort): string {
  if (port.type?.toLowerCase() === 'serial') {
    return `F${port.receiveFrameMs ?? 0}ms`
  }
  if (port.type?.toLowerCase() === 'can') {
    return `R${port.retryTimeMs ?? 10}ms`
  }
  return ''
}

// 格式化帧数
function formatFrames(frames: number | undefined): string {
  if (frames === undefined || frames === null) return '-'
  return String(frames)
}

// 格式化运行时间
function formatUptime(ms: number): string {
  if (!ms) return ''
  if (ms < 1000) return `${ms}ms`
  const seconds = Math.floor(ms / 1000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m${seconds % 60}s`
  const hours = Math.floor(minutes / 60)
  return `${hours}h${minutes % 60}m`
}
</script>

<style scoped>
.port-view {
  margin: 0 8px 8px 8px;
  pointer-events: auto; /* 确保在运行时也能接收点击 */
}

.section-title {
  display: flex;
  align-items: center;
  gap: 8px;
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

.uptime {
  margin-left: auto;
  font-weight: 400;
  color: #94a3b8;
  text-transform: none;
  font-family: var(--font-mono);
}

.port-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.port-item {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 5px 6px;
  font-size: 11px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 4px;
  border: 1px solid rgba(255, 255, 255, 0.05);
}

/* 左侧固定宽度列 */
.port-type {
  width: 42px;
  padding: 2px 4px;
  border-radius: 3px;
  font-size: 9px;
  font-weight: 500;
  text-transform: uppercase;
  text-align: center;
  flex-shrink: 0;
  box-sizing: border-box;
}

.port-type.serial {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.port-type.can {
  background: rgba(59, 130, 246, 0.15);
  color: #3b82f6;
}

.port-type.unknown {
  background: rgba(100, 116, 139, 0.15);
  color: #94a3b8;
}

.port-name {
  width: 56px;
  color: #e2e8f0;
  font-size: 10px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex-shrink: 0;
}

.port-baud {
  width: 36px;
  font-family: var(--font-mono);
  color: #94a3b8;
  font-size: 10px;
  text-align: right;
  flex-shrink: 0;
}

.port-extra {
  width: 48px;
  font-family: var(--font-mono);
  font-size: 9px;
  color: #64748b;
  text-align: right;
  flex-shrink: 0;
}

/* 中间弹性填充 */
.spacer {
  flex: 1;
}

/* 右侧固定宽度列 */
.port-stat {
  display: flex;
  align-items: center;
  width: 72px;
  font-family: var(--font-mono);
  font-size: 10px;
  padding: 1px 4px;
  border-radius: 2px;
  flex-shrink: 0;
  box-sizing: border-box;
  pointer-events: auto; /* 确保在运行时也能点击 */
}

.port-stat .stat-label {
  flex-shrink: 0;
}

.port-stat .stat-value {
  flex: 1;
  text-align: right;
}

.port-stat.tx {
  background: rgba(251, 146, 60, 0.15);
  color: #fb923c;
}

.port-stat.rx {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

/* WireTap 监听按钮 - TX 橙色 */
.port-stat.tx .wiretap-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  padding: 0;
  margin-right: 3px;
  border: 1.5px solid #fb923c;
  border-radius: 50%;
  background: transparent;
  cursor: pointer;
  opacity: 0.55;
  transition: all 0.15s ease;
  pointer-events: auto;
}

/* WireTap 监听按钮 - RX 绿色 */
.port-stat.rx .wiretap-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  padding: 0;
  margin-right: 3px;
  border: 1.5px solid #22c55e;
  border-radius: 50%;
  background: transparent;
  cursor: pointer;
  opacity: 0.55;
  transition: all 0.15s ease;
  pointer-events: auto;
}

.wiretap-btn .tap-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: transparent;
  transition: all 0.15s ease;
}

.wiretap-btn:hover:not(:disabled) {
  opacity: 0.8;
}

.wiretap-btn.active {
  opacity: 1;
}

/* TX 激活状态 - 橙色 */
.port-stat.tx .wiretap-btn.active .tap-dot {
  background: #fb923c;
  box-shadow: 0 0 4px rgba(255, 255, 255, 0.8);
}

/* RX 激活状态 - 绿色 */
.port-stat.rx .wiretap-btn.active .tap-dot {
  background: #22c55e;
  box-shadow: 0 0 4px rgba(255, 255, 255, 0.8);
}

.wiretap-btn:disabled {
  opacity: 0.2;
  cursor: not-allowed;
}

.no-data {
  padding: 8px 4px;
  color: #64748b;
  font-size: 11px;
  font-style: italic;
}
</style>
