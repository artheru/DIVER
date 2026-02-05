<!--
  @file components/logs/WireTapLogView.vue
  @description WireTap 日志显示组件
  
  功能：
  - 左侧显示 Console 日志
  - 右侧显示各端口的 WireTap 数据
  - 列可调整宽度
  - Serial: 显示 HEX + Unicode 解码
  - CAN: 显示格式化的 CAN 消息
-->

<template>
  <div class="wiretap-log-view">
    <!-- Console 列 -->
    <div class="log-column console-column" :style="{ flex: columnFlexValues[0] }">
      <div class="column-header">
        <span class="column-title">Console</span>
        <button class="clear-btn" @click="clearConsole" title="Clear">🗑</button>
      </div>
      <div class="column-content" ref="consoleRef">
        <div 
          v-for="(parsed, idx) in parsedConsoleLines" 
          :key="idx" 
          class="log-entry console-entry"
        >
          <span v-if="parsed.time" class="entry-time">{{ parsed.time }}</span>
          <span v-html="formatLine(parsed.message)"></span>
        </div>
        <div v-if="parsedConsoleLines.length === 0" class="empty-log">No logs yet</div>
      </div>
    </div>

    <!-- 可调整大小的分隔条 -->
    <div 
      v-for="(port, idx) in activePorts" 
      :key="port.portIndex"
      class="column-group"
      :style="{ flex: columnFlexValues[idx + 1] }"
    >
      <div 
        class="resize-handle"
        @mousedown="startResize($event, idx)"
      ></div>
      
      <!-- Port 列 -->
      <div class="log-column port-column">
        <div class="column-header">
          <span class="column-title">
            <span class="port-badge" :class="port.portType?.toLowerCase()">{{ port.portType }}</span>
            {{ port.portName }}
          </span>
          <button class="clear-btn" @click="clearPortLog(port.portIndex)" title="Clear">🗑</button>
        </div>
        <div class="column-content" ref="portRefs">
          <template v-if="port.portType === 'CAN'">
            <div 
              v-for="(entry, entryIdx) in getPortEntries(port.portIndex)" 
              :key="entryIdx" 
              class="log-entry can-entry"
              :class="{ highlighted: highlightedEntry?.portIndex === port.portIndex && highlightedEntry?.entryIdx === entryIdx }"
            >
              <span class="entry-time">{{ entry.timestamp }}</span>
              <span class="entry-dir" :class="entry.direction.toLowerCase()">{{ entry.direction }}</span>
              <button 
                class="parse-btn" 
                @click="showProtocolParse($event, entry, port.portType, port.portIndex, entryIdx)"
                title="Parse as protocol"
              >🔍</button>
              <div v-if="entry.canMessage" class="can-message">
                <span class="can-id">ID:{{ formatCanId(entry.canMessage.id) }}</span>
                <span class="can-dlc">DLC:{{ entry.canMessage.dlc }}</span>
                <span v-if="entry.canMessage.rtr" class="can-rtr">RTR</span>
                <span 
                  class="can-data" 
                  @mouseup="handleHexSelection($event, entry, port.portIndex, entryIdx)"
                >{{ formatCanData(entry.canMessage.data) }}</span>
              </div>
            </div>
          </template>
          <template v-else>
            <div 
              v-for="(entry, entryIdx) in getPortEntries(port.portIndex)" 
              :key="entryIdx" 
              class="log-entry serial-entry"
              :class="{ highlighted: highlightedEntry?.portIndex === port.portIndex && highlightedEntry?.entryIdx === entryIdx }"
            >
              <span class="entry-time">{{ entry.timestamp }}</span>
              <span class="entry-dir" :class="entry.direction.toLowerCase()">{{ entry.direction }}</span>
              <button 
                class="parse-btn" 
                @click="showProtocolParse($event, entry, port.portType, port.portIndex, entryIdx)"
                title="Parse as protocol"
              >🔍</button>
              <span class="entry-length">[{{ entry.dataLength }}]</span>
              <span 
                class="entry-hex" 
                :class="{ collapsed: !isExpanded(port.portIndex, entryIdx) && entry.dataLength > 16 }"
                @mouseup="handleHexSelection($event, entry, port.portIndex, entryIdx)"
              >
                {{ getDisplayHex(entry, port.portIndex, entryIdx) }}
              </span>
              <span 
                v-if="entry.textData" 
                class="entry-text"
                :class="{ collapsed: !isExpanded(port.portIndex, entryIdx) && entry.dataLength > 16 }"
              >
                {{ getDisplayText(entry, port.portIndex, entryIdx) }}
              </span>
              <button 
                v-if="entry.dataLength > 16" 
                class="expand-btn"
                @click="toggleExpand(port.portIndex, entryIdx)"
              >
                {{ isExpanded(port.portIndex, entryIdx) ? '▼' : '▶' }}
              </button>
            </div>
          </template>
          <div v-if="getPortEntries(port.portIndex).length === 0" class="empty-log">No data</div>
        </div>
      </div>
    </div>
    <!-- Inspect 弹窗 -->
    <div 
      v-if="inspectData" 
      class="inspect-popup"
      :class="{ dragging: isDragging && dragTarget === 'inspect' }"
      :style="{ left: inspectPosition.x + 'px', top: inspectPosition.y + 'px' }"
    >
      <div class="inspect-header" @mousedown="startDrag($event, 'inspect')">
        <span class="inspect-title">Inspect: {{ inspectData.hex }}</span>
        <button class="inspect-close" @click.stop="closeInspect">×</button>
      </div>
      <div class="inspect-content">
        <div v-for="(item, idx) in inspectData.interpretations" :key="idx" class="inspect-row">
          <span class="inspect-type">{{ item.type }}</span>
          <span class="inspect-value">{{ item.value }}</span>
        </div>
      </div>
    </div>

    <!-- Protocol Parse 弹窗 -->
    <div 
      v-if="parseResult" 
      class="protocol-popup"
      :class="{ dragging: isDragging && dragTarget === 'parse' }"
      :style="{ left: parsePosition.x + 'px', top: parsePosition.y + 'px' }"
    >
      <div class="protocol-header" :class="{ success: parseResult.success, error: !parseResult.success }" @mousedown="startDrag($event, 'parse')">
        <span class="protocol-title">{{ parseResult.protocol }} - {{ parseResult.messageType }}</span>
        <button class="protocol-close" @click.stop="closeProtocolParse">×</button>
      </div>
      <div class="protocol-summary">{{ parseResult.summary }}</div>
      <div class="protocol-content">
        <table class="protocol-table">
          <thead>
            <tr>
              <th>Field</th>
              <th>Bytes</th>
              <th>Value</th>
              <th>Description</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(field, idx) in parseResult.fields" :key="idx">
              <td class="field-name">{{ field.name }}</td>
              <td class="field-bytes" :style="{ color: field.highlight }">
                {{ formatFieldBytes(field.bytes) }}
              </td>
              <td class="field-value">{{ field.value }}</td>
              <td class="field-desc">{{ field.description || '' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="parseResult.errors?.length" class="protocol-errors">
        <div v-for="(err, idx) in parseResult.errors" :key="idx" class="error-item">⚠ {{ err }}</div>
      </div>
      <div v-if="parseResult.warnings?.length" class="protocol-warnings">
        <div v-for="(warn, idx) in parseResult.warnings" :key="idx" class="warning-item">⚡ {{ warn }}</div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useWireTapStore, useLogStore } from '@/stores'
import type { WireTapLogEntry, CANMessageData } from '@/types'
import { protocolRegistry, type ParseResult, type ParseContext } from '@/protocol'

const props = defineProps<{
  /** 节点 UUID */
  uuid: string
  /** 节点名称 */
  nodeName: string
  /** 自动滚动 */
  autoScroll: boolean
}>()

const emit = defineEmits<{
  (e: 'clearConsole'): void
}>()

const wireTapStore = useWireTapStore()
const logStore = useLogStore()

// Refs
const consoleRef = ref<HTMLDivElement | null>(null)
const portRefs = ref<HTMLDivElement[]>([])

// 列 flex 值状态 (索引 0 是 Console，后续是各 Port)
const columnFlexValues = ref<number[]>([1])

// 当前正在调整大小的列索引
const resizingIndex = ref<number | null>(null)
const startX = ref(0)
const startWidth = ref(0)

// 展开的条目 (key: "portIndex-entryIdx")
const expandedEntries = ref<Set<string>>(new Set())

// Inspect 弹窗状态
interface InspectInterpretation {
  type: string
  value: string
}
interface InspectData {
  hex: string
  interpretations: InspectInterpretation[]
}
const inspectData = ref<InspectData | null>(null)
const inspectPosition = ref({ x: 0, y: 0 })
const inspectJustOpened = ref(false)  // 防止立即关闭

// Protocol Parse 弹窗状态
const parseResult = ref<ParseResult | null>(null)
const parsePosition = ref({ x: 0, y: 0 })
const parseJustOpened = ref(false)

// 高亮显示的条目（inspect 或 parse 打开时）
const highlightedEntry = ref<{ portIndex: number; entryIdx: number } | null>(null)

// 弹窗拖动状态
const isDragging = ref(false)
const dragTarget = ref<'inspect' | 'parse' | null>(null)
const dragOffset = ref({ x: 0, y: 0 })

// ============================================
// 工具函数
// ============================================

// 计算弹窗位置，确保不超出视口
function constrainPopupPosition(
  anchorX: number,
  anchorY: number,
  popupWidth: number,
  popupHeight: number,
  padding: number = 10
): { x: number; y: number } {
  const vw = window.innerWidth
  const vh = window.innerHeight
  
  // X 坐标：优先显示在锚点右侧，如果超出则左移
  let x = anchorX
  if (x + popupWidth + padding > vw) {
    x = vw - popupWidth - padding
  }
  x = Math.max(padding, x)
  
  // Y 坐标：优先显示在锚点下方，如果超出则上移
  let y = anchorY
  if (y + popupHeight + padding > vh) {
    // 尝试显示在锚点上方
    y = anchorY - popupHeight - 10
    if (y < padding) {
      // 如果上方也放不下，就贴着底部显示
      y = vh - popupHeight - padding
    }
  }
  y = Math.max(padding, y)
  
  return { x, y }
}

// ============================================
// 计算属性
// ============================================

// Console 日志行
const consoleLines = computed(() => {
  const logs = logStore.nodeLogs.get(props.uuid)
  return logs || []
})

// 解析后的 Console 日志（分离时间戳和消息）
interface ParsedLogLine {
  time: string | null
  message: string
}

const parsedConsoleLines = computed((): ParsedLogLine[] => {
  return consoleLines.value.map(line => {
    // 尝试匹配时间戳格式: [HH:MM:SS.mmm] 或 [HH:MM:SS]
    const timeMatch = line.match(/^\[(\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]\s*/)
    if (timeMatch) {
      return {
        time: timeMatch[1],
        message: line.slice(timeMatch[0].length)
      }
    }
    return {
      time: null,
      message: line
    }
  })
})

// 活动的端口列表
const activePorts = computed(() => {
  const ports = wireTapStore.getActivePortsForNode(props.uuid)
  const state = wireTapStore.nodeStates.get(props.uuid)
  
  return ports.map(portIndex => {
    const portLog = state?.portLogs.get(portIndex)
    return {
      portIndex,
      portType: portLog?.portType || 'Serial',
      portName: portLog?.portName || `Port ${portIndex}`
    }
  })
})

// ============================================
// 方法
// ============================================

// 获取端口的日志条目
function getPortEntries(portIndex: number): WireTapLogEntry[] {
  return wireTapStore.getPortLogs(props.uuid, portIndex)
}

// 格式化 Console 日志行
function formatLine(line: string): string {
  return line
    .replace(/\x1b\[32m/g, '<span class="log-green">')
    .replace(/\x1b\[33m/g, '<span class="log-yellow">')
    .replace(/\x1b\[31m/g, '<span class="log-red">')
    .replace(/\x1b\[36m/g, '<span class="log-cyan">')
    .replace(/\x1b\[0m/g, '</span>')
    .replace(/\x1b\[\d+m/g, '')
}

// 格式化 CAN ID
function formatCanId(id: number): string {
  return '0x' + id.toString(16).toUpperCase().padStart(3, '0')
}

// 格式化 CAN 数据
function formatCanData(data: number[] | string | undefined): string {
  if (!data) return ''
  // 如果是 base64 字符串，解码它
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

// 清空 Console
function clearConsole() {
  emit('clearConsole')
}

// 清空端口日志
function clearPortLog(portIndex: number) {
  wireTapStore.clearPortLogs(props.uuid, portIndex)
}

// ============================================
// 展开/折叠功能
// ============================================

function getExpandKey(portIndex: number, entryIdx: number): string {
  return `${portIndex}-${entryIdx}`
}

function isExpanded(portIndex: number, entryIdx: number): boolean {
  return expandedEntries.value.has(getExpandKey(portIndex, entryIdx))
}

function toggleExpand(portIndex: number, entryIdx: number): void {
  const key = getExpandKey(portIndex, entryIdx)
  if (expandedEntries.value.has(key)) {
    expandedEntries.value.delete(key)
  } else {
    expandedEntries.value.add(key)
  }
}

function getDisplayHex(entry: WireTapLogEntry, portIndex: number, entryIdx: number): string {
  if (entry.dataLength <= 16 || isExpanded(portIndex, entryIdx)) {
    return entry.hexData
  }
  // 折叠显示：前8字节 + ... + 后8字节
  const bytes = entry.hexData.split(' ')
  const first8 = bytes.slice(0, 8).join(' ')
  const last8 = bytes.slice(-8).join(' ')
  return `${first8} ··· ${last8}`
}

function getDisplayText(entry: WireTapLogEntry, portIndex: number, entryIdx: number): string {
  if (!entry.textData) return ''
  if (entry.dataLength <= 16 || isExpanded(portIndex, entryIdx)) {
    return entry.textData
  }
  // 折叠显示：前8字符 + ... + 后8字符
  const text = entry.textData
  if (text.length <= 20) return text
  const first8 = text.slice(0, 8)
  const last8 = text.slice(-8)
  return `${first8}···${last8}`
}

// ============================================
// Inspect 功能
// ============================================

function handleHexSelection(event: MouseEvent, _entry: WireTapLogEntry, portIndex: number, entryIdx: number): void {
  // 延迟处理，确保选择已完成
  const x = event.clientX
  const y = event.clientY
  
  setTimeout(() => {
    const selection = window.getSelection()
    if (!selection || selection.isCollapsed) return
    
    const selectedText = selection.toString().trim()
    if (!selectedText) return
    
    // 解析选中的十六进制字节（支持空格或无空格分隔）
    let hexBytes: string[] = []
    
    // 尝试按空格分割
    const spaceSplit = selectedText.split(/\s+/).filter(h => /^[0-9A-Fa-f]{2}$/.test(h))
    if (spaceSplit.length > 0) {
      hexBytes = spaceSplit
    } else {
      // 尝试解析连续的十六进制字符串（如 "0010"）
      const noSpaceMatch = selectedText.match(/^[0-9A-Fa-f]+$/)
      if (noSpaceMatch && selectedText.length % 2 === 0 && selectedText.length >= 2) {
        for (let i = 0; i < selectedText.length; i += 2) {
          hexBytes.push(selectedText.slice(i, i + 2))
        }
      }
    }
    
    // 限制 1-8 字节
    if (hexBytes.length === 0 || hexBytes.length > 8) return
    
    // 将十六进制转换为字节数组
    const bytes = hexBytes.map(h => parseInt(h, 16))
    
    // 生成各种解析结果
    const interpretations = interpretBytes(bytes)
    
    if (interpretations.length > 0) {
      // 设置高亮
      highlightedEntry.value = { portIndex, entryIdx }
      
      inspectData.value = {
        hex: hexBytes.join(' '),
        interpretations
      }
      // 使用约束函数计算位置，inspect-popup 宽度约 280px，高度约 200px
      inspectPosition.value = constrainPopupPosition(x + 10, y + 10, 280, 200)
      
      // 设置标志防止立即关闭
      inspectJustOpened.value = true
      setTimeout(() => {
        inspectJustOpened.value = false
      }, 100)
    }
  }, 10)
}

function interpretBytes(bytes: number[]): InspectInterpretation[] {
  const results: InspectInterpretation[] = []
  const len = bytes.length
  
  // 1 字节
  if (len === 1) {
    const u8 = bytes[0]
    const i8 = u8 > 127 ? u8 - 256 : u8
    results.push({ type: 'u8', value: `${u8}` })
    results.push({ type: 'i8', value: `${i8}` })
    results.push({ type: 'hex', value: `0x${u8.toString(16).toUpperCase().padStart(2, '0')}` })
    if (u8 >= 32 && u8 < 127) {
      results.push({ type: 'char', value: `'${String.fromCharCode(u8)}'` })
    }
  }
  
  // 2 字节
  if (len === 2) {
    const buffer = new ArrayBuffer(2)
    const view = new DataView(buffer)
    view.setUint8(0, bytes[0])
    view.setUint8(1, bytes[1])
    
    results.push({ type: 'u16 LE', value: `${view.getUint16(0, true)}` })
    results.push({ type: 'u16 BE', value: `${view.getUint16(0, false)}` })
    results.push({ type: 'i16 LE', value: `${view.getInt16(0, true)}` })
    results.push({ type: 'i16 BE', value: `${view.getInt16(0, false)}` })
    results.push({ type: 'hex LE', value: `0x${view.getUint16(0, true).toString(16).toUpperCase().padStart(4, '0')}` })
    results.push({ type: 'hex BE', value: `0x${view.getUint16(0, false).toString(16).toUpperCase().padStart(4, '0')}` })
  }
  
  // 4 字节
  if (len === 4) {
    const buffer = new ArrayBuffer(4)
    const view = new DataView(buffer)
    for (let i = 0; i < 4; i++) view.setUint8(i, bytes[i])
    
    results.push({ type: 'u32 LE', value: `${view.getUint32(0, true)}` })
    results.push({ type: 'u32 BE', value: `${view.getUint32(0, false)}` })
    results.push({ type: 'i32 LE', value: `${view.getInt32(0, true)}` })
    results.push({ type: 'i32 BE', value: `${view.getInt32(0, false)}` })
    results.push({ type: 'f32 LE', value: `${view.getFloat32(0, true).toPrecision(7)}` })
    results.push({ type: 'f32 BE', value: `${view.getFloat32(0, false).toPrecision(7)}` })
    results.push({ type: 'hex LE', value: `0x${view.getUint32(0, true).toString(16).toUpperCase().padStart(8, '0')}` })
    results.push({ type: 'hex BE', value: `0x${view.getUint32(0, false).toString(16).toUpperCase().padStart(8, '0')}` })
  }
  
  // 8 字节
  if (len === 8) {
    const buffer = new ArrayBuffer(8)
    const view = new DataView(buffer)
    for (let i = 0; i < 8; i++) view.setUint8(i, bytes[i])
    
    results.push({ type: 'u64 LE', value: `${view.getBigUint64(0, true)}` })
    results.push({ type: 'u64 BE', value: `${view.getBigUint64(0, false)}` })
    results.push({ type: 'i64 LE', value: `${view.getBigInt64(0, true)}` })
    results.push({ type: 'i64 BE', value: `${view.getBigInt64(0, false)}` })
    results.push({ type: 'f64 LE', value: `${view.getFloat64(0, true).toPrecision(15)}` })
    results.push({ type: 'f64 BE', value: `${view.getFloat64(0, false).toPrecision(15)}` })
  }
  
  // 任意长度：显示十六进制值
  if (len >= 2 && len <= 8 && len !== 2 && len !== 4 && len !== 8) {
    let leVal = BigInt(0)
    let beVal = BigInt(0)
    for (let i = 0; i < len; i++) {
      leVal |= BigInt(bytes[i]) << BigInt(i * 8)
      beVal = (beVal << BigInt(8)) | BigInt(bytes[i])
    }
    results.push({ type: `${len}B LE`, value: `${leVal}` })
    results.push({ type: `${len}B BE`, value: `${beVal}` })
  }
  
  return results
}

function closeInspect(): void {
  inspectData.value = null
  highlightedEntry.value = null
}

// 弹窗拖动功能
function startDrag(event: MouseEvent, target: 'inspect' | 'parse'): void {
  isDragging.value = true
  dragTarget.value = target
  
  const position = target === 'inspect' ? inspectPosition.value : parsePosition.value
  dragOffset.value = {
    x: event.clientX - position.x,
    y: event.clientY - position.y
  }
  
  // 阻止文本选择
  event.preventDefault()
}

function onDrag(event: MouseEvent): void {
  if (!isDragging.value || !dragTarget.value) return
  
  const newX = event.clientX - dragOffset.value.x
  const newY = event.clientY - dragOffset.value.y
  
  if (dragTarget.value === 'inspect') {
    inspectPosition.value = { x: newX, y: newY }
  } else {
    parsePosition.value = { x: newX, y: newY }
  }
}

function stopDrag(): void {
  isDragging.value = false
  dragTarget.value = null
}

// 点击外部关闭 Inspect 弹窗
function handleClickOutside(event: MouseEvent): void {
  // 如果刚刚打开，忽略这次点击
  if (inspectJustOpened.value || parseJustOpened.value) return
  
  if (inspectData.value) {
    const popup = document.querySelector('.inspect-popup')
    if (popup && !popup.contains(event.target as Node)) {
      closeInspect()
    }
  }
  
  if (parseResult.value) {
    const popup = document.querySelector('.protocol-popup')
    if (popup && !popup.contains(event.target as Node)) {
      closeProtocolParse()
    }
  }
}

// ============================================
// Protocol Parse 功能
// ============================================

function showProtocolParse(event: MouseEvent, entry: WireTapLogEntry, portType: string, portIndex: number, entryIdx: number): void {
  // 设置高亮
  highlightedEntry.value = { portIndex, entryIdx }
  
  // 获取原始字节数据
  let rawBytes: number[] = []
  
  if (entry.rawBytes && entry.rawBytes.length > 0) {
    rawBytes = entry.rawBytes
  } else if (entry.hexData) {
    // 从 hexData 解析
    rawBytes = entry.hexData.split(' ').map(h => parseInt(h, 16)).filter(n => !isNaN(n))
  }
  
  if (rawBytes.length === 0) {
    console.warn('[Protocol] No data to parse')
    return
  }
  
  // 构建解析上下文
  const context: ParseContext = {
    direction: entry.direction.toLowerCase() as 'tx' | 'rx',
    portType: portType.toLowerCase() === 'can' ? 'can' : 'serial',
    portIndex,
    timestamp: entry.timestamp ? new Date() : undefined
  }
  
  // 如果是 CAN，添加 CAN 特有信息
  if (portType.toLowerCase() === 'can' && entry.canMessage) {
    context.canId = entry.canMessage.id
    context.canDlc = entry.canMessage.dlc
    context.canRtr = entry.canMessage.rtr
    context.canExt = entry.canMessage.ext
    
    // CAN 数据可能在 canMessage.data 中
    if (entry.canMessage.data) {
      if (Array.isArray(entry.canMessage.data)) {
        rawBytes = entry.canMessage.data
      } else if (typeof entry.canMessage.data === 'string') {
        // base64 解码
        try {
          const bytes = atob(entry.canMessage.data)
          rawBytes = Array.from(bytes, c => c.charCodeAt(0))
        } catch {
          // 忽略解码错误
        }
      }
    }
  }
  
  // 自动检测协议并解析
  const result = protocolRegistry.autoDetectAndParse(rawBytes, context)
  
  if (result) {
    parseResult.value = result
    
    // 计算弹窗位置，protocol-popup 宽度约 500px，高度约 400px
    const rect = (event.target as HTMLElement).getBoundingClientRect()
    parsePosition.value = constrainPopupPosition(rect.left, rect.bottom + 5, 500, 400)
    
    // 防止立即关闭
    parseJustOpened.value = true
    setTimeout(() => {
      parseJustOpened.value = false
    }, 100)
  } else {
    console.warn('[Protocol] No parser could handle this data')
  }
}

function closeProtocolParse(): void {
  parseResult.value = null
  highlightedEntry.value = null
}

function formatFieldBytes(bytes: number[]): string {
  if (!bytes || bytes.length === 0) return ''
  return bytes.map(b => b.toString(16).toUpperCase().padStart(2, '0')).join(' ')
}

// 开始调整大小 (index 是 Port 在 activePorts 中的索引)
function startResize(event: MouseEvent, index: number) {
  resizingIndex.value = index
  startX.value = event.clientX
  // 记录左列和右列的初始 flex 值
  // index=0 表示 Console 和第一个 Port 之间的分隔条
  // 左列索引 = index，右列索引 = index + 1
  startWidth.value = columnFlexValues.value[index] || 1
  
  document.addEventListener('mousemove', handleResize)
  document.addEventListener('mouseup', stopResize)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

// 处理调整大小
function handleResize(event: MouseEvent) {
  if (resizingIndex.value === null) return
  
  const delta = event.clientX - startX.value
  // 每 100px 移动改变 0.5 个 flex 单位
  const flexDelta = delta / 200
  
  const leftIdx = resizingIndex.value
  const rightIdx = resizingIndex.value + 1
  
  // 获取初始总 flex（两列共享）
  const leftFlex = Math.max(0.2, startWidth.value + flexDelta)
  const rightFlex = Math.max(0.2, (columnFlexValues.value[rightIdx] || 1) - flexDelta)
  
  columnFlexValues.value[leftIdx] = leftFlex
  columnFlexValues.value[rightIdx] = rightFlex
}

// 停止调整大小
function stopResize() {
  resizingIndex.value = null
  document.removeEventListener('mousemove', handleResize)
  document.removeEventListener('mouseup', stopResize)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

// 滚动到底部
function scrollToBottom() {
  if (!props.autoScroll) return
  
  nextTick(() => {
    if (consoleRef.value) {
      consoleRef.value.scrollTop = consoleRef.value.scrollHeight
    }
    // 所有端口列也滚动到底部
    portRefs.value.forEach(ref => {
      if (ref) {
        ref.scrollTop = ref.scrollHeight
      }
    })
  })
}

// ============================================
// 生命周期
// ============================================

// 初始化列 flex 值（Console + 各 Port 平分）
watch(activePorts, (ports) => {
  const totalColumns = 1 + ports.length  // Console + Ports
  while (columnFlexValues.value.length < totalColumns) {
    columnFlexValues.value.push(1)
  }
  // 裁剪多余的值
  if (columnFlexValues.value.length > totalColumns) {
    columnFlexValues.value.length = totalColumns
  }
}, { immediate: true })

// 自动滚动
watch([parsedConsoleLines, () => wireTapStore.nodeStates.get(props.uuid)], () => {
  scrollToBottom()
}, { deep: true })

onMounted(() => {
  scrollToBottom()
  document.addEventListener('click', handleClickOutside)
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
})

onUnmounted(() => {
  // 清理事件监听器
  document.removeEventListener('mousemove', handleResize)
  document.removeEventListener('mouseup', stopResize)
  document.removeEventListener('click', handleClickOutside)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
})
</script>

<style scoped>
.wiretap-log-view {
  display: flex;
  height: 100%;
  background: #0a0e14;
  overflow: hidden;
}

.column-group {
  display: flex;
  min-width: 100px;
}

.log-column {
  display: flex;
  flex-direction: column;
  min-width: 0;
  border-right: 1px solid var(--border-color);
}

.console-column {
  min-width: 100px;
  /* flex 由内联样式控制 */
}

.port-column {
  flex: 1;
}

.column-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}

.column-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-color);
}

.port-badge {
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 9px;
  font-weight: 500;
  text-transform: uppercase;
}

.port-badge.serial {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.port-badge.can {
  background: rgba(59, 130, 246, 0.15);
  color: #3b82f6;
}

.port-badge.unknown {
  background: rgba(100, 116, 139, 0.15);
  color: #94a3b8;
}

.clear-btn {
  padding: 2px 6px;
  background: transparent;
  border: none;
  color: var(--text-muted);
  font-size: 12px;
  cursor: pointer;
  border-radius: 3px;
}

.clear-btn:hover {
  background: rgba(255, 255, 255, 0.1);
}

.column-content {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
  font-family: var(--font-mono);
  font-size: 11px;
  line-height: 1.5;
}

.resize-handle {
  width: 4px;
  cursor: col-resize;
  background: transparent;
  transition: background 0.2s;
  flex-shrink: 0;
}

.resize-handle:hover {
  background: var(--primary);
}

/* Console 日志条目 */
.console-entry {
  display: flex;
  gap: 6px;
  padding: 2px 0;
  white-space: pre-wrap;
  word-break: break-all;
  color: var(--text-color);
}

.console-entry .entry-time {
  flex-shrink: 0;
}

/* WireTap 日志条目 */
.log-entry {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 4px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.log-entry.highlighted {
  background: rgba(59, 130, 246, 0.2);
  border-radius: 4px;
  padding: 4px 6px;
  margin: 0 -6px;
  border-left: 3px solid #3b82f6;
}

.entry-time {
  color: #64748b;
  font-size: 10px;
  flex-shrink: 0;
}

.entry-dir {
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 9px;
  font-weight: 600;
  flex-shrink: 0;
}

.entry-dir.tx {
  background: rgba(251, 146, 60, 0.2);
  color: #fb923c;
}

.entry-dir.rx {
  background: rgba(34, 197, 94, 0.2);
  color: #22c55e;
}

/* Serial 条目 */
.serial-entry .entry-length {
  color: #64748b;
  font-size: 10px;
  flex-shrink: 0;
}

.serial-entry .entry-hex {
  color: #94a3b8;
  word-break: break-all;
  cursor: text;
  user-select: text;
}

.serial-entry .entry-hex.collapsed {
  color: #78909c;
}

.serial-entry .expand-btn {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 10px;
  cursor: pointer;
  padding: 0 4px;
  flex-shrink: 0;
}

.serial-entry .expand-btn:hover {
  color: #94a3b8;
}

.serial-entry .entry-text {
  color: #22d3ee;
  background: rgba(34, 211, 238, 0.1);
  padding: 1px 4px;
  border-radius: 2px;
  word-break: break-all;
}

.serial-entry .entry-text.collapsed {
  color: #0e7490;
}

/* CAN 条目 */
.can-message {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.can-id {
  color: #f59e0b;
  font-weight: 600;
}

.can-dlc {
  color: #64748b;
}

.can-rtr {
  padding: 1px 4px;
  background: rgba(239, 68, 68, 0.2);
  color: #ef4444;
  border-radius: 2px;
  font-size: 9px;
}

.can-data {
  color: #94a3b8;
  cursor: text;
  user-select: text;
}

.empty-log {
  color: var(--text-muted);
  text-align: center;
  padding: 20px;
  font-style: italic;
}

/* ANSI 颜色 */
:deep(.log-green) {
  color: #22c55e;
}

:deep(.log-yellow) {
  color: #f59e0b;
}

:deep(.log-red) {
  color: #ef4444;
}

:deep(.log-cyan) {
  color: #22d3ee;
}

/* Inspect 弹窗 */
.inspect-popup {
  position: fixed;
  z-index: 1000;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 6px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.5);
  min-width: 200px;
  max-width: 300px;
  font-family: var(--font-mono);
  font-size: 11px;
}

.inspect-popup.dragging {
  opacity: 0.9;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.7);
}

.inspect-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 10px;
  background: #0f172a;
  border-bottom: 1px solid #334155;
  border-radius: 6px 6px 0 0;
  cursor: move;
  user-select: none;
}

.inspect-title {
  color: #94a3b8;
  font-weight: 600;
}

.inspect-close {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 16px;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
}

.inspect-close:hover {
  color: #ef4444;
}

.inspect-content {
  padding: 8px 10px;
}

.inspect-row {
  display: flex;
  justify-content: space-between;
  padding: 3px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.inspect-row:last-child {
  border-bottom: none;
}

.inspect-type {
  color: #64748b;
  font-size: 10px;
}

.inspect-value {
  color: #22d3ee;
  font-weight: 500;
}

/* Parse 按钮 */
.parse-btn {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 11px;
  cursor: pointer;
  padding: 0 4px;
  flex-shrink: 0;
  opacity: 0.6;
  transition: opacity 0.2s;
}

.parse-btn:hover {
  opacity: 1;
  color: #3b82f6;
}

/* Protocol Parse 弹窗 */
.protocol-popup {
  position: fixed;
  z-index: 1001;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
  min-width: 400px;
  max-width: 600px;
  max-height: 500px;
  font-family: var(--font-mono);
  font-size: 11px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.protocol-popup.dragging {
  opacity: 0.9;
  box-shadow: 0 12px 48px rgba(0, 0, 0, 0.8);
}

.protocol-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 12px;
  background: #0f172a;
  border-bottom: 1px solid #334155;
  cursor: move;
  user-select: none;
}

.protocol-header.success {
  border-left: 3px solid #22c55e;
}

.protocol-header.error {
  border-left: 3px solid #ef4444;
}

.protocol-title {
  color: #e2e8f0;
  font-weight: 600;
  font-size: 12px;
}

.protocol-close {
  background: transparent;
  border: none;
  color: #64748b;
  font-size: 18px;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
}

.protocol-close:hover {
  color: #ef4444;
}

.protocol-summary {
  padding: 8px 12px;
  background: rgba(59, 130, 246, 0.1);
  color: #93c5fd;
  font-size: 11px;
  border-bottom: 1px solid #334155;
}

.protocol-content {
  flex: 1;
  overflow-y: auto;
  padding: 0;
}

.protocol-table {
  width: 100%;
  border-collapse: collapse;
}

.protocol-table th {
  position: sticky;
  top: 0;
  background: #1e293b;
  padding: 8px 10px;
  text-align: left;
  font-weight: 600;
  color: #64748b;
  font-size: 10px;
  text-transform: uppercase;
  border-bottom: 1px solid #334155;
}

.protocol-table td {
  padding: 6px 10px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  vertical-align: top;
}

.protocol-table tr:hover {
  background: rgba(255, 255, 255, 0.02);
}

.protocol-table .field-name {
  color: #94a3b8;
  font-weight: 500;
  white-space: nowrap;
}

.protocol-table .field-bytes {
  font-family: var(--font-mono);
  white-space: nowrap;
}

.protocol-table .field-value {
  color: #e2e8f0;
}

.protocol-table .field-desc {
  color: #64748b;
  font-size: 10px;
}

.protocol-errors,
.protocol-warnings {
  padding: 8px 12px;
  border-top: 1px solid #334155;
}

.protocol-errors {
  background: rgba(239, 68, 68, 0.1);
}

.protocol-warnings {
  background: rgba(245, 158, 11, 0.1);
}

.error-item {
  color: #fca5a5;
  padding: 2px 0;
}

.warning-item {
  color: #fcd34d;
  padding: 2px 0;
}
</style>
