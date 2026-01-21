<!--
  @file components/graph/PortStatsView.vue
  @description 端口统计和 IO 状态显示组件
  
  显示：
  - 端口统计：Baud, TX Frames, RX Frames
  - 数字 IO 状态：输入（绿色）、输出（红色）
-->

<template>
  <div class="port-stats-view" v-if="hasStats || hasIO">
    <!-- 端口统计 -->
    <div class="section" v-if="hasStats">
      <div class="section-title">
        PORT STATS
        <span class="uptime" v-if="stats">Uptime: {{ formatUptime(stats.uptimeMs) }}</span>
      </div>
      <div class="stats-list" v-if="stats && stats.ports">
        <div 
          v-for="(port, idx) in mergedPorts" 
          :key="idx" 
          class="stats-item"
        >
          <span class="port-index">Port {{ idx }}</span>
          <span class="port-type" :class="port.type?.toLowerCase()">{{ port.type }}</span>
          <span class="port-name">{{ port.name || `Port${idx}` }}</span>
          <span class="port-baud">{{ port.baud }}</span>
          <span class="port-stat tx">TX {{ port.txFrames ?? 0 }}</span>
          <span class="port-stat rx">RX {{ port.rxFrames ?? 0 }}</span>
        </div>
      </div>
      <div v-else class="no-data">No statistics available</div>
    </div>

    <!-- IO 状态 -->
    <div class="section" v-if="hasIO">
      <div class="section-title">DIGITAL I/O</div>
      <div class="io-grid">
        <!-- 输入 -->
        <div class="io-row" v-if="digitalInputCount > 0">
          <span class="io-label">IN</span>
          <div class="io-dots">
            <span 
              v-for="i in digitalInputCount" 
              :key="`in-${i}`"
              class="io-dot input"
              :class="{ active: isInputActive(i - 1) }"
              :title="`DI${i - 1}: ${isInputActive(i - 1) ? 'ON' : 'OFF'}`"
            />
          </div>
        </div>
        <!-- 输出 -->
        <div class="io-row" v-if="digitalOutputCount > 0">
          <span class="io-label">OUT</span>
          <div class="io-dots">
            <span 
              v-for="i in digitalOutputCount" 
              :key="`out-${i}`"
              class="io-dot output"
              :class="{ active: isOutputActive(i - 1) }"
              :title="`DO${i - 1}: ${isOutputActive(i - 1) ? 'ON' : 'OFF'}`"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { RuntimeStats, PortConfig } from '@/types'

const props = defineProps<{
  /** 运行时统计数据 */
  stats: RuntimeStats | null
  /** 端口配置（用于获取类型和波特率） */
  portConfigs: PortConfig[]
  /** 数字输入数量 */
  digitalInputCount: number
  /** 数字输出数量 */
  digitalOutputCount: number
}>()

// 是否有统计数据
const hasStats = computed(() => {
  return props.stats && props.stats.ports && props.stats.ports.length > 0
})

// 是否有 IO 数据
const hasIO = computed(() => {
  return props.digitalInputCount > 0 || props.digitalOutputCount > 0
})

// 合并端口配置和统计数据
interface MergedPort {
  type: string
  name: string
  baud: number
  txFrames: number
  rxFrames: number
}

const mergedPorts = computed((): MergedPort[] => {
  if (!props.stats?.ports) return []
  
  return props.stats.ports.map((stat, idx) => {
    const config = props.portConfigs[idx]
    return {
      type: config?.type || 'Unknown',
      name: config?.name || `Port${idx}`,
      baud: config?.baud || 0,
      txFrames: stat.txFrames,
      rxFrames: stat.rxFrames
    }
  })
})

// 检查输入是否激活
function isInputActive(bitIndex: number): boolean {
  if (!props.stats) return false
  return (props.stats.digitalInputs & (1 << bitIndex)) !== 0
}

// 检查输出是否激活
function isOutputActive(bitIndex: number): boolean {
  if (!props.stats) return false
  return (props.stats.digitalOutputs & (1 << bitIndex)) !== 0
}

// 格式化运行时间
function formatUptime(ms: number): string {
  if (!ms) return '0ms'
  if (ms < 1000) return `${ms}ms`
  const seconds = Math.floor(ms / 1000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`
  const hours = Math.floor(minutes / 60)
  return `${hours}h ${minutes % 60}m`
}
</script>

<style scoped>
.port-stats-view {
  margin: 0 8px 8px 8px;
}

.section {
  margin-bottom: 8px;
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

.uptime {
  font-weight: 400;
  color: #94a3b8;
  text-transform: none;
  font-family: var(--font-mono);
}

/* 统计列表 - 与 PortConfigEdit 布局一致 */
.stats-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.stats-item {
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

.port-type.unknown {
  background: rgba(100, 116, 139, 0.15);
  color: #94a3b8;
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

.port-stat {
  font-family: var(--font-mono);
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 3px;
  min-width: 56px;
}

.port-stat.tx {
  background: rgba(251, 146, 60, 0.15);
  color: #fb923c;
}

.port-stat.rx {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.no-data {
  padding: 8px 4px;
  color: #64748b;
  font-size: 11px;
  font-style: italic;
}

/* IO 状态 */
.io-grid {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 4px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 4px;
}

.io-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.io-label {
  font-size: 10px;
  font-weight: 600;
  color: #64748b;
  width: 28px;
}

.io-dots {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.io-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  cursor: default;
  transition: all 0.15s ease;
}

/* 输入点 - 绿色 */
.io-dot.input {
  background: rgba(34, 197, 94, 0.2);
  border: 1px solid rgba(34, 197, 94, 0.4);
}

.io-dot.input.active {
  background: #22c55e;
  border-color: #22c55e;
  box-shadow: 0 0 6px rgba(34, 197, 94, 0.6);
}

/* 输出点 - 红色 */
.io-dot.output {
  background: rgba(239, 68, 68, 0.2);
  border: 1px solid rgba(239, 68, 68, 0.4);
}

.io-dot.output.active {
  background: #ef4444;
  border-color: #ef4444;
  box-shadow: 0 0 6px rgba(239, 68, 68, 0.6);
}
</style>
