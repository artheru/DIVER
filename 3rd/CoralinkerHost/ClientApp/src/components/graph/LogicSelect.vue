<!--
  @file components/graph/LogicSelect.vue
  @description Logic 选择对话框
  
  只能选择已经编译好的 Logic（generated 目录下有 .bin 和 .bin.json 的）
-->

<template>
  <n-modal v-model:show="showModal" :mask-closable="false">
    <n-card title="Select Logic" style="width: 400px">
      <div v-if="loading" class="loading-state">
        <n-spin size="small" />
        <span>Loading...</span>
      </div>
      
      <div v-else-if="logicList.length === 0" class="empty-state">
        <p>No compiled logic found.</p>
        <p class="hint">Build a .cs file first to generate logic binaries.</p>
      </div>
      
      <div v-else class="logic-list">
        <div
          v-for="logic in logicList"
          :key="logic.name"
          class="logic-item"
          :class="{ selected: selectedLogic === logic.name }"
          @click="selectedLogic = logic.name"
        >
          <div class="logic-name">{{ logic.name }}</div>
          <div class="logic-info">
            <span class="bin-size">{{ formatSize(logic.binSize) }}</span>
          </div>
        </div>
      </div>
      
      <template #footer>
        <div class="dialog-footer">
          <n-button @click="handleCancel">Cancel</n-button>
          <n-button type="primary" @click="handleConfirm" :disabled="!selectedLogic">
            Select
          </n-button>
        </div>
      </template>
    </n-card>
  </n-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { NModal, NCard, NButton, NSpin } from 'naive-ui'
import { getLogicList, type LogicInfo } from '@/api/device'

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

const loading = ref(false)
const logicList = ref<LogicInfo[]>([])
const selectedLogic = ref('')

// 加载 Logic 列表
async function loadLogicList() {
  loading.value = true
  try {
    const result = await getLogicList()
    if (result.ok) {
      logicList.value = result.logics
    }
  } catch (error) {
    console.error('[LogicSelect] Failed to fetch logic list:', error)
  } finally {
    loading.value = false
  }
}

// 格式化文件大小
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

// 事件处理
function handleCancel() {
  showModal.value = false
}

function handleConfirm() {
  if (selectedLogic.value) {
    emit('update:modelValue', selectedLogic.value)
  }
  showModal.value = false
}

// 监听 show 变化，初始化数据
watch(() => props.show, (newVal) => {
  if (newVal) {
    selectedLogic.value = props.modelValue
    loadLogicList()
  }
})

onMounted(() => {
  if (props.show) {
    selectedLogic.value = props.modelValue
    loadLogicList()
  }
})
</script>

<style scoped>
.loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 20px;
  color: var(--text-muted);
}

.empty-state {
  padding: 20px;
  text-align: center;
  color: var(--text-muted);
}

.empty-state .hint {
  font-size: 12px;
  margin-top: 8px;
}

.logic-list {
  max-height: 300px;
  overflow-y: auto;
}

.logic-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 12px;
  margin-bottom: 4px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background var(--transition-fast);
}

.logic-item:hover {
  background: rgba(255, 255, 255, 0.05);
}

.logic-item.selected {
  background: rgba(79, 140, 255, 0.15);
  border: 1px solid var(--primary);
}

.logic-name {
  font-weight: 500;
  color: var(--text-color);
}

.logic-info {
  font-size: 11px;
  color: var(--text-muted);
  font-family: var(--font-mono);
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
