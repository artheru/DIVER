<!--
  @file components/graph/FatalErrorDialog.vue
  @description MCU 致命错误对话框
  
  显示 MCU HardFault 或 ASSERT 失败的详细信息：
  - 节点信息和固件版本
  - 错误类型（字符串错误 / CoreDump）
  - 调试信息（IL偏移量、行号）
  - 支持点击 IL 偏移量跳转到源代码
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false" :close-on-esc="false">
    <n-card 
      title="MCU Fatal Error" 
      style="width: 600px"
      :bordered="false"
      :segmented="{ content: true }"
      closable
      @close="showModal = false"
    >
      <template #header>
        <div class="error-header">
          <span class="error-icon">&#x26A0;</span>
          <span>MCU Fatal Error</span>
        </div>
      </template>

      <div class="error-content" v-if="errorData">
        <!-- 节点信息 -->
        <div class="section">
          <div class="section-title">Node Information</div>
          <div class="info-grid">
            <div class="info-item">
              <span class="label">Node:</span>
              <span class="value">{{ errorData.nodeName }}</span>
            </div>
            <div class="info-item">
              <span class="label">Logic:</span>
              <span class="value">{{ errorData.logicName || '-' }}</span>
            </div>
            <div class="info-item">
              <span class="label">Time:</span>
              <span class="value">{{ errorData.timestamp }}</span>
            </div>
          </div>
        </div>

        <!-- 固件版本 -->
        <div class="section">
          <div class="section-title">Firmware Version</div>
          <div class="info-grid">
            <div class="info-item">
              <span class="label">Product:</span>
              <span class="value">{{ errorData.version.productionName || '-' }}</span>
            </div>
            <div class="info-item">
              <span class="label">Commit:</span>
              <span class="value mono">{{ errorData.version.gitCommit || '-' }}</span>
            </div>
            <div class="info-item">
              <span class="label">Build:</span>
              <span class="value">{{ errorData.version.buildTime || '-' }}</span>
            </div>
          </div>
        </div>

        <!-- 错误信息 -->
        <div class="section error-section">
          <div class="section-title">
            Error Details
            <span class="error-type-badge" :class="errorData.errorType.toLowerCase()">
              {{ errorData.errorType === 'String' ? 'ASSERT' : 'HardFault' }}
            </span>
          </div>
          
          <!-- 字符串错误 -->
          <div v-if="errorData.errorType === 'String'" class="error-message-box">
            <code>{{ errorData.errorString }}</code>
          </div>

          <!-- CoreDump -->
          <div v-else-if="errorData.coreDump" class="coredump-box">
            <div class="register-grid">
              <div 
                v-for="(value, key) in errorData.coreDump" 
                :key="key"
                class="register-item"
              >
                <span class="reg-name">{{ String(key).toUpperCase() }}</span>
                <span class="reg-value">{{ formatHex(value) }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 调试信息 -->
        <div class="section debug-section">
          <div class="section-title">Debug Location</div>
          <div class="debug-info">
            <div class="debug-item">
              <span class="label">IL Offset:</span>
              <span 
                class="value clickable mono"
                @click="handleILOffsetClick"
                title="Click to jump to source"
              >
                {{ errorData.debugInfo.ilOffset }}
              </span>
            </div>
            <div class="debug-item" v-if="sourceLocation">
              <span class="label">Source:</span>
              <span 
                class="value clickable source-link"
                @click="handleSourceClick"
                title="Click to jump to source"
              >
                {{ sourceLocation.sourceFile }}:{{ sourceLocation.sourceLine }}
              </span>
            </div>
            <div class="debug-item" v-if="sourceLocation">
              <span class="label">Method:</span>
              <span class="value mono method-name">{{ sourceLocation.methodName }}</span>
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <div class="dialog-footer">
          <n-button 
            v-if="sourceLocation" 
            type="primary" 
            @click="handleSourceClick"
          >
            Go to Source
          </n-button>
          <n-button @click="showModal = false">
            Close
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { NModal, NCard, NButton } from 'naive-ui'
import type { FatalErrorData } from '@/composables/useSignalR'

// Props
const props = defineProps<{
  show: boolean
  errorData: FatalErrorData | null
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:show', value: boolean): void
  (e: 'gotoSource', file: string, line: number): void
}>()

// Local state
const showModal = computed({
  get: () => props.show,
  set: (v) => emit('update:show', v)
})

// 源码位置映射缓存
const diverMapCache = ref<Map<string, DiverMapEntry[]>>(new Map())

interface DiverMapEntry {
  ilOffset: number
  methodIndex: number
  diverLine: number
  methodName: string
  sourceFile: string
  sourceLine: number
}

// 从 ilOffset 查找源码位置
const sourceLocation = computed<DiverMapEntry | null>(() => {
  if (!props.errorData?.logicName) return null
  
  const mapEntries = diverMapCache.value.get(props.errorData.logicName)
  if (!mapEntries || mapEntries.length === 0) return null

  const ilOffset = props.errorData.debugInfo.ilOffset

  // 查找最接近且不超过 ilOffset 的条目
  let best: DiverMapEntry | null = null
  for (const entry of mapEntries) {
    if (entry.ilOffset <= ilOffset) {
      if (!best || entry.ilOffset > best.ilOffset) {
        best = entry
      }
    }
  }
  
  return best
})

// 加载 diver.map.json
async function loadDiverMap(logicName: string) {
  if (diverMapCache.value.has(logicName)) return
  
  try {
    // 从后端获取 map 文件
    const response = await fetch(`/api/runtime/diver-map/${encodeURIComponent(logicName)}`)
    if (!response.ok) {
      console.warn(`[FatalErrorDialog] Failed to load diver map for ${logicName}`)
      return
    }
    
    const mapData = await response.json() as DiverMapEntry[]
    diverMapCache.value.set(logicName, mapData)
    console.log(`[FatalErrorDialog] Loaded diver map for ${logicName}: ${mapData.length} entries`)
  } catch (err) {
    console.error('[FatalErrorDialog] Error loading diver map:', err)
  }
}

// 格式化十六进制
function formatHex(value: number): string {
  return '0x' + value.toString(16).toUpperCase().padStart(8, '0')
}

// 处理 IL 偏移量点击 - 跳转到源码（不关闭对话框）
function handleILOffsetClick() {
  if (sourceLocation.value) {
    console.log(`[FatalErrorDialog] IL Offset click -> jumping to ${sourceLocation.value.sourceFile}:${sourceLocation.value.sourceLine}`)
    gotoSourceLocation()
  } else {
    console.warn('[FatalErrorDialog] IL Offset click but no source location available')
  }
}

// 处理源码点击 - 跳转到 sourceFile:sourceLine（不关闭对话框）
function handleSourceClick() {
  if (sourceLocation.value) {
    gotoSourceLocation()
  }
}

// 跳转到源码位置（不关闭对话框）
function gotoSourceLocation() {
  if (sourceLocation.value) {
    const { sourceFile, sourceLine } = sourceLocation.value
    console.log(`[FatalErrorDialog] Emitting gotoSource: ${sourceFile}:${sourceLine}`)
    emit('gotoSource', sourceFile, sourceLine)
    // 不关闭对话框，用户需要手动点击 X 关闭
  }
}

// 监听错误数据变化，加载对应的 map
watch(() => props.errorData?.logicName, async (logicName) => {
  if (logicName) {
    await loadDiverMap(logicName)
  }
}, { immediate: true })
</script>

<style scoped>
.error-header {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #f5222d;
}

.error-icon {
  font-size: 20px;
}

.error-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.section {
  background: rgba(255, 255, 255, 0.02);
  border-radius: 8px;
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.section-title {
  font-weight: 600;
  font-size: 13px;
  color: var(--n-text-color-1);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.error-type-badge {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 500;
}

.error-type-badge.string {
  background: #ff7a45;
  color: #fff;
}

.error-type-badge.stm32f4 {
  background: #f5222d;
  color: #fff;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
}

.info-item {
  display: flex;
  gap: 8px;
  font-size: 13px;
}

.info-item .label {
  color: var(--n-text-color-3);
  min-width: 60px;
}

.info-item .value {
  color: var(--n-text-color-1);
  word-break: break-all;
}

.info-item .value.mono {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 12px;
}

.error-section {
  border-color: rgba(245, 34, 45, 0.3);
  background: rgba(245, 34, 45, 0.05);
}

.error-message-box {
  background: rgba(0, 0, 0, 0.4);
  border: 1px solid rgba(255, 122, 69, 0.3);
  border-radius: 6px;
  padding: 14px 16px;
  font-size: 14px;
  font-weight: 500;
  line-height: 1.6;
  color: #ff9a6c;
  word-break: break-word;
}

.error-message-box code {
  font-family: inherit;
  font-size: inherit;
}

.coredump-box {
  background: rgba(0, 0, 0, 0.3);
  border-radius: 6px;
  padding: 12px;
}

.register-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 6px 16px;
}

.register-item {
  display: flex;
  gap: 8px;
  align-items: center;
}

.reg-name {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 11px;
  color: #8b949e;
  min-width: 70px;
  text-align: right;
}

.reg-value {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 12px;
  color: #58a6ff;
}

.debug-section {
  border-color: rgba(88, 166, 255, 0.3);
  background: rgba(88, 166, 255, 0.05);
}

.debug-info {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.debug-item {
  display: flex;
  gap: 8px;
  font-size: 13px;
}

.debug-item .label {
  color: var(--n-text-color-3);
  min-width: 70px;
}

.debug-item .value {
  color: var(--n-text-color-1);
}

.debug-item .value.clickable {
  color: #58a6ff;
  cursor: pointer;
  text-decoration: underline;
}

.debug-item .value.clickable:hover {
  color: #79c0ff;
}

.source-link {
  font-family: 'JetBrains Mono', 'Fira Code', monospace;
  font-size: 12px;
}

.method-name {
  font-size: 11px;
  color: #8b949e !important;
  word-break: break-all;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
