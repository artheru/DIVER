<!--
  @file components/logs/CANAggregatedView.vue
  @description CAN 聚合视图组件
  
  功能：
  - 按 (Direction, RTR, DLC, CAN_ID) 聚合显示 CAN 报文
  - 默认只显示最新一条报文数据，点击展开显示最近 5 条
  - 行按最新接收时间排序，并根据活跃度渐变颜色
  - 保留协议解析和 Inspect 功能
-->

<template>
  <div class="can-aggregated-view">
    <div v-if="groups.length === 0" class="empty-log">No CAN data</div>
    <table v-else class="can-table">
      <thead>
        <tr>
          <th class="col-dir">Dir</th>
          <th class="col-id">ID</th>
          <th class="col-dlc">DLC</th>
          <th class="col-type">Type</th>
          <th class="col-rate">Rate</th>
          <th class="col-total">Total</th>
          <th class="col-time">Last</th>
          <th class="col-data">Data</th>
          <th class="col-actions"></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="group in groups" :key="groupKey(group)">
          <!-- Main row -->
          <tr :style="rowStyle(group)" class="can-row">
            <td class="col-dir">
              <span class="dir-badge" :class="group.direction === 0 ? 'rx' : 'tx'">
                {{ group.direction === 0 ? 'RX' : 'TX' }}
              </span>
            </td>
            <td class="col-id can-id-cell">{{ formatCanId(group.canId) }}</td>
            <td class="col-dlc">{{ group.dlc }}</td>
            <td class="col-type">
              <span v-if="group.rtr" class="type-rtr">RTR</span>
              <span v-else class="type-data">DATA</span>
            </td>
            <td class="col-rate">{{ group.frameRate }} fps</td>
            <td class="col-total">{{ group.totalFrames }}</td>
            <td class="col-time">{{ formatTime(group.lastReceived) }} <span class="mcu-time">{{ latestFrame(group) ? formatMcuTimestamp(latestFrame(group)!.mcuTimestampMs) : '' }}</span></td>
            <td class="col-data">
              <div class="data-row">
                <span
                  class="can-data-hex"
                  @mouseup="handleHexSelection($event, group, latestFrame(group))"
                >{{ formatPayload(latestFrame(group)?.data) }}</span>
                <button
                  class="parse-btn"
                  @click="onParseClick($event, group, latestFrame(group))"
                  title="Parse as protocol"
                >🔍</button>
              </div>
            </td>
            <td class="col-actions">
              <button
                v-if="group.recentFrames.length > 1"
                class="expand-btn"
                @click="toggleExpand(groupKey(group))"
              >{{ isExpanded(groupKey(group)) ? '▼' : '▶' }} {{ group.recentFrames.length }}</button>
            </td>
          </tr>
          <!-- Expanded recent frames -->
          <template v-if="isExpanded(groupKey(group))">
            <tr
              v-for="(frame, fi) in group.recentFrames.slice(0, -1).reverse()"
              :key="`${groupKey(group)}-${fi}`"
              class="can-expanded-row"
              :style="rowStyle(group)"
            >
              <td colspan="7" class="expanded-time">{{ frame.timestamp }} <span class="mcu-time">{{ formatMcuTimestamp(frame.mcuTimestampMs) }}</span></td>
              <td class="col-data">
                <div class="data-row">
                  <span
                    class="can-data-hex"
                    @mouseup="handleHexSelection($event, group, frame)"
                  >{{ formatPayload(frame.data) }}</span>
                  <button
                    class="parse-btn"
                    @click="onParseClick($event, group, frame)"
                    title="Parse as protocol"
                  >🔍</button>
                </div>
              </td>
              <td></td>
            </tr>
          </template>
        </template>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useWireTapStore } from '@/stores'
import type { CANAggregatedGroup, WireTapLogEntry } from '@/types'
import { protocolRegistry, type ParseContext } from '@/protocol'

const props = defineProps<{
  uuid: string
  portIndex: number
}>()

const emit = defineEmits<{
  (e: 'inspect', event: MouseEvent, entry: WireTapLogEntry, portIndex: number, entryIdx: number): void
  (e: 'parse', result: unknown, position: { x: number; y: number }, portIndex: number, entryIdx: number): void
}>()

const wireTapStore = useWireTapStore()

// Freshness timer
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  timer = setInterval(() => { now.value = Date.now() }, 1000)
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
})

// Expanded groups
const expandedGroups = ref<Set<string>>(new Set())

// Computed groups from store
const groups = ref<CANAggregatedGroup[]>([])

// Use a watcher-like approach via the store's reactive state
import { watch } from 'vue'
watch(
  () => wireTapStore.canAggregatedGroups,
  () => {
    groups.value = wireTapStore.getCanGroupsForPort(props.uuid, props.portIndex)
  },
  { deep: true, immediate: true }
)

function groupKey(g: CANAggregatedGroup): string {
  return `${g.direction}:${g.rtr}:${g.dlc}:${g.canId}`
}

function latestFrame(g: CANAggregatedGroup) {
  return g.recentFrames.length > 0 ? g.recentFrames[g.recentFrames.length - 1] : undefined
}

function toggleExpand(key: string) {
  if (expandedGroups.value.has(key)) {
    expandedGroups.value.delete(key)
  } else {
    expandedGroups.value.add(key)
  }
}

function isExpanded(key: string): boolean {
  return expandedGroups.value.has(key)
}

function formatCanId(id: number): string {
  return '0x' + id.toString(16).toUpperCase().padStart(3, '0')
}

function formatTime(isoString: string): string {
  const d = new Date(isoString)
  if (isNaN(d.getTime())) return isoString
  const HH = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  const ss = String(d.getSeconds()).padStart(2, '0')
  const ms = String(d.getMilliseconds()).padStart(3, '0')
  return `${HH}:${mm}:${ss}.${ms}`
}

function formatMcuTimestamp(ms: number): string {
  const totalSec = Math.floor(ms / 1000)
  const millis = ms % 1000
  const minutes = Math.floor(totalSec / 60)
  const seconds = totalSec % 60
  return `+${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`
}

function formatPayload(data: number[] | string | undefined): string {
  if (!data) return ''
  if (typeof data === 'string') {
    try {
      const bytes = atob(data)
      return Array.from(bytes, c => c.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0')).join(' ')
    } catch {
      return data
    }
  }
  if (!Array.isArray(data)) return ''
  return data.map(b => b.toString(16).toUpperCase().padStart(2, '0')).join(' ')
}

function toByteArray(data: number[] | string | undefined): number[] {
  if (!data) return []
  if (Array.isArray(data)) return data
  try {
    const binary = atob(data as string)
    return Array.from(binary, c => c.charCodeAt(0))
  } catch {
    return []
  }
}

// Freshness coloring
function rowStyle(group: CANAggregatedGroup): Record<string, string> {
  const lastMs = new Date(group.lastReceived).getTime()
  const ageMs = now.value - lastMs

  if (ageMs < 500) {
    return { opacity: '1' }
  } else if (ageMs < 3000) {
    const t = (ageMs - 500) / 2500
    const opacity = 1.0 - t * 0.6
    return { opacity: String(opacity.toFixed(2)) }
  } else {
    return { opacity: '0.4', color: '#64748b' }
  }
}

// Protocol parse
function onParseClick(
  event: MouseEvent,
  group: CANAggregatedGroup,
  frame: { data: number[] | string; timestamp: string; mcuTimestampMs: number } | undefined
) {
  if (!frame) return

  const rawBytes = toByteArray(frame.data)
  if (rawBytes.length === 0) return

  const context: ParseContext = {
    direction: group.direction === 0 ? 'rx' : 'tx',
    portType: 'can',
    portIndex: props.portIndex,
    canId: group.canId,
    canDlc: group.dlc,
    canRtr: group.rtr
  }

  const result = protocolRegistry.autoDetectAndParse(rawBytes, context)
  if (result) {
    const rect = (event.target as HTMLElement).getBoundingClientRect()
    emit('parse', result, { x: rect.left, y: rect.bottom + 5 }, props.portIndex, 0)
  }
}

// Hex selection for inspect popup
function handleHexSelection(
  event: MouseEvent,
  group: CANAggregatedGroup,
  frame: { data: number[] | string; timestamp: string; mcuTimestampMs: number } | undefined
) {
  if (!frame) return

  const rawBytes = toByteArray(frame.data)

  // Build a WireTapLogEntry-compatible object for the inspect handler
  const entry: WireTapLogEntry = {
    timestamp: frame.timestamp,
    mcuTimestamp: formatMcuTimestamp(frame.mcuTimestampMs),
    direction: group.direction === 0 ? 'RX' : 'TX',
    hexData: formatPayload(frame.data),
    rawBytes,
    dataLength: rawBytes.length,
    canMessage: {
      id: group.canId,
      dlc: group.dlc,
      rtr: group.rtr,
      data: rawBytes
    }
  }

  emit('inspect', event, entry, props.portIndex, 0)
}
</script>

<style scoped>
.can-aggregated-view {
  height: 100%;
  overflow-y: auto;
  font-family: var(--font-mono);
  font-size: 11px;
}

.can-table {
  width: 100%;
  border-collapse: collapse;
  table-layout: auto;
}

.can-table thead {
  position: sticky;
  top: 0;
  z-index: 1;
}

.can-table th {
  background: #0f172a;
  padding: 5px 6px;
  text-align: left;
  font-weight: 600;
  color: #64748b;
  font-size: 10px;
  text-transform: uppercase;
  border-bottom: 1px solid #334155;
  white-space: nowrap;
}

.can-table td {
  padding: 4px 6px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  vertical-align: middle;
  white-space: nowrap;
}

.can-row {
  transition: opacity 0.5s ease;
}

.can-expanded-row {
  background: rgba(255, 255, 255, 0.02);
}

.can-expanded-row .expanded-time {
  text-align: right;
  color: #64748b;
  font-size: 10px;
  padding-right: 8px;
}

/* Dir badge */
.dir-badge {
  display: inline-block;
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 9px;
  font-weight: 600;
}

.dir-badge.tx {
  background: rgba(251, 146, 60, 0.2);
  color: #fb923c;
}

.dir-badge.rx {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
}

/* CAN ID */
.can-id-cell {
  color: #f59e0b;
  font-weight: 600;
}

/* Type */
.type-data {
  color: #94a3b8;
  font-size: 9px;
}

.type-rtr {
  padding: 1px 4px;
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
  border-radius: 2px;
  font-size: 9px;
}

/* Rate */
.col-rate {
  color: #22d3ee;
  font-size: 10px;
}

/* Total */
.col-total {
  color: #64748b;
  font-size: 10px;
}

/* Time */
.col-time {
  color: #64748b;
  font-size: 10px;
}

.mcu-time {
  color: #8b5cf6;
}

/* Data */
.data-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.can-data-hex {
  color: #94a3b8;
  cursor: text;
  user-select: text;
}

/* Buttons */
.parse-btn {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 11px;
  cursor: pointer;
  padding: 0 2px;
  flex-shrink: 0;
  opacity: 0.6;
  transition: opacity 0.2s;
}

.parse-btn:hover {
  opacity: 1;
  color: #3b82f6;
}

.expand-btn {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 10px;
  cursor: pointer;
  padding: 0 4px;
  flex-shrink: 0;
}

.expand-btn:hover {
  color: #94a3b8;
}

.empty-log {
  color: var(--text-muted);
  text-align: center;
  padding: 20px;
  font-style: italic;
}
</style>
