<!--
  @file components/graph/UpgradeDialog.vue
  @description 固件升级对话框
  
  升级流程：
  1. 拖拽或选择 .upg 文件
  2. 解析并显示固件信息
  3. 与当前 MCU 版本对比
  4. 点击升级，显示进度
  5. 完成后显示结果
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false">
    <n-card title="Firmware Upgrade" style="width: 520px">
      <div class="upgrade-form">
        <!-- 文件选择区域 -->
        <div
          class="file-drop-zone"
          :class="{ 'drag-over': isDragOver, 'has-file': !!upgFile }"
          @dragenter.prevent="isDragOver = true"
          @dragleave.prevent="isDragOver = false"
          @dragover.prevent
          @drop.prevent="handleFileDrop"
          @click="openFileDialog"
        >
          <template v-if="!upgFile">
            <div class="drop-icon">📦</div>
            <div class="drop-text">Drag & drop .upg file here</div>
            <div class="drop-hint">or click to select</div>
          </template>
          <template v-else>
            <div class="file-info">
              <div class="file-name">{{ upgFile.name }}</div>
              <div class="file-size">{{ formatSize(upgFile.size) }}</div>
            </div>
            <n-button 
              size="tiny" 
              quaternary 
              @click.stop="clearFile"
              :disabled="isUpgrading"
            >
              Clear
            </n-button>
          </template>
        </div>
        <input
          ref="fileInputRef"
          type="file"
          accept=".upg"
          style="display: none"
          @change="handleFileSelect"
        />

        <!-- 解析中 -->
        <div v-if="isParsing" class="parsing-status">
          <n-spin size="small" />
          <span>Parsing firmware file...</span>
        </div>

        <!-- 解析结果 -->
        <div v-if="parseResult" class="parse-result" :class="{ error: !parseResult.ok }">
          <template v-if="parseResult.ok && parseResult.metadata">
            <div class="result-section">
              <div class="section-title">New Firmware (UPG)</div>
              <div class="info-row">
                <span class="label">Product:</span>
                <span class="value">{{ parseResult.metadata.productName }}</span>
              </div>
              <div class="info-row">
                <span class="label">Version:</span>
                <span class="value">{{ parseResult.metadata.tag }} ({{ parseResult.metadata.commit }})</span>
              </div>
              <div class="info-row">
                <span class="label">Build Time:</span>
                <span class="value">{{ parseResult.metadata.buildTime }}</span>
              </div>
              <div class="info-row">
                <span class="label">Size:</span>
                <span class="value">{{ formatSize(parseResult.metadata.appLength) }}</span>
              </div>
            </div>

            <!-- 当前版本对比 -->
            <div v-if="currentVersion" class="result-section">
              <div class="section-title">Current MCU (Probe)</div>
              <div class="info-row">
                <span class="label">Product:</span>
                <span class="value">{{ currentVersion.productionName || '-' }}</span>
              </div>
              <div class="info-row">
                <span class="label">Version:</span>
                <span class="value">{{ currentVersion.gitTag || '-' }} ({{ currentVersion.gitCommit || '-' }})</span>
              </div>
              <div class="info-row">
                <span class="label">Build Time:</span>
                <span class="value">{{ currentVersion.buildTime || '-' }}</span>
              </div>
            </div>
          </template>
          <template v-else>
            <div class="error-header">✗ Parse Failed</div>
            <div class="error-message">{{ parseResult.error }}</div>
          </template>
        </div>

        <!-- 升级进度 -->
        <div v-if="isUpgrading || upgradeComplete" class="upgrade-progress">
          <div class="progress-header">
            <span>{{ upgradeStageText }}</span>
            <span>{{ upgradeProgress }}%</span>
          </div>
          <n-progress
            type="line"
            :percentage="upgradeProgress"
            :status="upgradeStatus"
            :show-indicator="false"
          />
          <div v-if="upgradeMessage" class="progress-message">{{ upgradeMessage }}</div>
        </div>

        <!-- 升级结果 -->
        <div v-if="upgradeResult" class="upgrade-result" :class="{ error: !upgradeResult.ok }">
          <template v-if="upgradeResult.ok">
            <div class="result-header success">✓ Upgrade Complete</div>
            <div v-if="upgradeResult.upgInfo" class="info-row">
              <span class="label">Installed:</span>
              <span class="value">{{ upgradeResult.upgInfo.productName }} {{ upgradeResult.upgInfo.tag }} ({{ upgradeResult.upgInfo.commit }})</span>
            </div>
          </template>
          <template v-else>
            <div class="result-header error">✗ Upgrade Failed</div>
            <div class="error-message">{{ upgradeResult.error }}</div>
            <div v-if="upgradeResult.mcuInfo" class="info-row" style="margin-top: 8px;">
              <span class="label">MCU:</span>
              <span class="value">{{ upgradeResult.mcuInfo.productName || '-' }}</span>
            </div>
            <div v-if="upgradeResult.upgInfo" class="info-row">
              <span class="label">UPG:</span>
              <span class="value">{{ upgradeResult.upgInfo.productName || '-' }}</span>
            </div>
          </template>
        </div>
      </div>

      <template #footer>
        <div class="dialog-footer">
          <n-button @click="handleCancel" :disabled="isUpgrading">
            {{ upgradeComplete ? 'Close' : 'Cancel' }}
          </n-button>
          <n-button
            type="warning"
            @click="handleUpgrade"
            :loading="isUpgrading"
            :disabled="!canUpgrade"
          >
            Upgrade
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { NModal, NCard, NButton, NSpin, NProgress } from 'naive-ui'
import { parseUpgFile, startUpgrade } from '@/api/upgrade'
import { useSignalR } from '@/composables/useSignalR'
import type { VersionInfo, UpgradeProgress as UpgradeProgressType } from '@/types'

// Props
const props = defineProps<{
  show: boolean
  mcuUri: string
  currentVersion?: VersionInfo
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:show', value: boolean): void
  (e: 'complete'): void
}>()

// SignalR
const { onUpgradeProgress, offUpgradeProgress } = useSignalR()

// 本地状态
const showModal = computed({
  get: () => props.show,
  set: (v) => emit('update:show', v)
})

const fileInputRef = ref<HTMLInputElement | null>(null)
const isDragOver = ref(false)
const upgFile = ref<File | null>(null)
const isParsing = ref(false)
const isUpgrading = ref(false)
const upgradeComplete = ref(false)

interface FirmwareMetadata {
  productName: string
  tag: string
  commit: string
  buildTime: string
  appLength: number
  appCRC32: number
  isValid: boolean
}

interface ParseResultData {
  ok: boolean
  error?: string
  metadata?: FirmwareMetadata
}

interface UpgradeResultData {
  ok: boolean
  error?: string
  mcuInfo?: FirmwareMetadata
  upgInfo?: FirmwareMetadata
}

const parseResult = ref<ParseResultData | null>(null)
const upgradeResult = ref<UpgradeResultData | null>(null)
const upgradeProgress = ref(0)
const upgradeStage = ref('')
const upgradeMessage = ref('')
const nodeId = ref('')

// 计算属性
const canUpgrade = computed(() => {
  return parseResult.value?.ok && !isUpgrading.value && !upgradeComplete.value
})

const upgradeStageText = computed(() => {
  const stageMap: Record<string, string> = {
    Connecting: 'Connecting to MCU...',
    SendingUpgradeCommand: 'Sending upgrade command...',
    WaitingBootloader: 'Waiting for bootloader...',
    ConnectingBootloader: 'Connecting to bootloader...',
    ReadingMcuInfo: 'Reading MCU info...',
    Erasing: 'Erasing firmware...',
    Writing: 'Writing firmware...',
    Verifying: 'Verifying...',
    Complete: 'Complete!',
    Error: 'Error'
  }
  return stageMap[upgradeStage.value] || upgradeStage.value || 'Upgrading...'
})

const upgradeStatus = computed(() => {
  if (upgradeStage.value === 'Error') return 'error'
  if (upgradeStage.value === 'Complete') return 'success'
  return 'default'
})

// 文件操作
function openFileDialog() {
  if (!isUpgrading.value) {
    fileInputRef.value?.click()
  }
}

function handleFileDrop(e: DragEvent) {
  isDragOver.value = false
  if (isUpgrading.value) return
  
  const files = e.dataTransfer?.files
  if (files && files.length > 0) {
    const file = files[0]
    if (file.name.toLowerCase().endsWith('.upg')) {
      processFile(file)
    }
  }
}

function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  if (input.files && input.files.length > 0) {
    processFile(input.files[0])
  }
  input.value = ''
}

function clearFile() {
  upgFile.value = null
  parseResult.value = null
  upgradeResult.value = null
  upgradeComplete.value = false
}

async function processFile(file: File) {
  upgFile.value = file
  parseResult.value = null
  upgradeResult.value = null
  upgradeComplete.value = false
  
  isParsing.value = true
  try {
    const result = await parseUpgFile(file)
    parseResult.value = result
  } catch (err: any) {
    parseResult.value = { ok: false, error: err.message || 'Parse failed' }
  } finally {
    isParsing.value = false
  }
}

// 升级操作
async function handleUpgrade() {
  if (!upgFile.value || !parseResult.value?.ok) return
  
  isUpgrading.value = true
  upgradeProgress.value = 0
  upgradeStage.value = 'Connecting'
  upgradeMessage.value = ''
  upgradeResult.value = null
  nodeId.value = crypto.randomUUID()
  
  try {
    const result = await startUpgrade(props.mcuUri, upgFile.value, nodeId.value)
    upgradeResult.value = result
    if (result.ok) {
      upgradeComplete.value = true
      emit('complete')
    }
  } catch (err: any) {
    upgradeResult.value = { ok: false, error: err.message || 'Upgrade failed' }
    upgradeStage.value = 'Error'
  } finally {
    isUpgrading.value = false
  }
}

function handleCancel() {
  if (!isUpgrading.value) {
    showModal.value = false
    // 重置状态
    setTimeout(() => {
      clearFile()
    }, 300)
  }
}

// SignalR 进度回调
function handleUpgradeProgress(
  receivedNodeId: string,
  progress: number,
  stage: string,
  message: string | null
) {
  if (receivedNodeId === nodeId.value) {
    upgradeProgress.value = progress
    upgradeStage.value = stage
    if (message) {
      upgradeMessage.value = message
    }
  }
}

// 格式化文件大小
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}

// 生命周期
onMounted(() => {
  onUpgradeProgress(handleUpgradeProgress)
})

onUnmounted(() => {
  offUpgradeProgress(handleUpgradeProgress)
})

// 监听显示状态
watch(() => props.show, (newVal) => {
  if (newVal) {
    // 打开时重置
    clearFile()
  }
})
</script>

<style scoped>
.upgrade-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.file-drop-zone {
  border: 2px dashed var(--n-border-color);
  border-radius: 8px;
  padding: 24px;
  text-align: center;
  cursor: pointer;
  transition: all 0.2s;
  background: var(--n-color-modal);
}

.file-drop-zone:hover {
  border-color: var(--n-primary-color);
  background: var(--n-color-hover);
}

.file-drop-zone.drag-over {
  border-color: var(--n-primary-color);
  background: var(--n-primary-color-hover);
}

.file-drop-zone.has-file {
  border-style: solid;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
}

.drop-icon {
  font-size: 32px;
  margin-bottom: 8px;
}

.drop-text {
  font-size: 14px;
  color: var(--n-text-color-1);
}

.drop-hint {
  font-size: 12px;
  color: var(--n-text-color-3);
  margin-top: 4px;
}

.file-info {
  text-align: left;
}

.file-name {
  font-weight: 500;
  color: var(--n-text-color-1);
}

.file-size {
  font-size: 12px;
  color: var(--n-text-color-3);
}

.parsing-status,
.probing-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px;
  background: var(--n-color-modal);
  border-radius: 6px;
  color: var(--n-text-color-2);
}

.parse-result,
.upgrade-result {
  padding: 12px;
  border-radius: 6px;
  background: var(--n-color-modal);
  border: 1px solid var(--n-border-color);
}

.parse-result.error,
.upgrade-result.error {
  border-color: var(--n-error-color);
  background: rgba(var(--n-error-color-rgb), 0.1);
}

.result-section {
  margin-bottom: 12px;
}

.result-section:last-child {
  margin-bottom: 0;
}

.section-title {
  font-weight: 600;
  font-size: 13px;
  color: var(--n-text-color-1);
  margin-bottom: 8px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--n-border-color);
}

.info-row {
  display: flex;
  font-size: 13px;
  margin-bottom: 4px;
}

.info-row .label {
  color: var(--n-text-color-3);
  width: 90px;
  flex-shrink: 0;
}

.info-row .value {
  color: var(--n-text-color-1);
  word-break: break-all;
}

.error-header,
.result-header {
  font-weight: 600;
  margin-bottom: 8px;
}

.result-header.success {
  color: var(--n-success-color);
}

.result-header.error,
.error-header {
  color: var(--n-error-color);
}

.error-message {
  font-size: 13px;
  color: var(--n-text-color-2);
}

.upgrade-progress {
  padding: 12px;
  background: var(--n-color-modal);
  border-radius: 6px;
  border: 1px solid var(--n-border-color);
}

.progress-header {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  margin-bottom: 8px;
  color: var(--n-text-color-1);
}

.progress-message {
  font-size: 12px;
  color: var(--n-text-color-3);
  margin-top: 8px;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
