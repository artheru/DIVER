<!--
  @file components/graph/RootNodeView.vue
  @description 根节点组件 (PC) - 使用 vue-flow
  
  简单的根节点，只有一个 out 连接点
-->

<template>
  <div class="root-node" :class="{ selected }">
    <!-- Handle 固定在节点右边缘中央 -->
    <Handle type="source" :position="Position.Right" id="out" class="handle-out" />
    
    <div class="node-header">
      <span class="node-icon">🖥️</span>
      <span class="node-name">{{ data.name || 'PC' }}</span>
    </div>
    <div class="node-content">
      <div class="config-row">
        <span class="config-label">Logic</span>
        <n-select
          v-model:value="selectedLogic"
          :options="logicOptions"
          size="small"
          placeholder="No PC Logic"
          clearable
          class="logic-select"
          @update:value="configureRootLogic"
        />
      </div>
      <div class="config-row readonly">
        <span class="config-label">State</span>
        <span class="config-value">{{ rootState?.isRunning ? 'Running' : 'Idle' }}</span>
      </div>
      <div class="config-row readonly">
        <span class="config-label">Build</span>
        <span class="config-value version-display">{{ buildText }}</span>
      </div>
      <div class="config-row readonly">
        <span class="config-label">Status</span>
        <span class="config-value status-text">{{ rootState?.statusText || '/' }}</span>
      </div>
      <div class="harness-out">
        <span class="handle-label">out ●</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import { NSelect } from 'naive-ui'
import * as rootApi from '@/api/root'
import type { RootLogicMetadata, RootRuntimeState } from '@/types'
import { useFilesStore, useRuntimeStore } from '@/stores'
import { storeToRefs } from 'pinia'

// Props from vue-flow
defineProps<{
  id: string
  data: {
    name: string
  }
  selected: boolean
}>()

const rootLogics = ref<RootLogicMetadata[]>([])
const rootState = ref<RootRuntimeState | null>(null)
const selectedLogic = ref<string | null>(null)
const runtimeStore = useRuntimeStore()
const filesStore = useFilesStore()
const { buildVersion } = storeToRefs(filesStore)

const logicOptions = computed(() => {
  return [
    { label: 'None (no PC Logic)', value: '__none__' },
    ...rootLogics.value.map(logic => ({
      label: logic.name,
      value: logic.name
    }))
  ]
})

const buildText = computed(() => {
  if (!rootState.value?.logicName) return 'None'
  const commit = rootState.value.sourceCommitShort || 'unknown'
  const build = rootState.value.buildTime ? new Date(rootState.value.buildTime).toLocaleString() : 'unknown'
  return `Commit: ${commit}  Build: ${build}`
})

async function loadRootInfo() {
  const [logicResult, stateResult] = await Promise.all([
    rootApi.getRootLogics().catch(() => null),
    rootApi.getRootState().catch(() => null)
  ])
  rootLogics.value = logicResult?.ok ? logicResult.logics : []
  rootState.value = stateResult?.ok ? stateResult.state : null
  selectedLogic.value = rootState.value?.logicName || '__none__'
}

async function configureRootLogic(value?: string | null) {
  selectedLogic.value = value || '__none__'
  await rootApi.configureRoot(selectedLogic.value === '__none__' ? null : selectedLogic.value)
  await loadRootInfo()
  await runtimeStore.refreshFieldMetas()
}

onMounted(() => {
  loadRootInfo()
})

watch(buildVersion, () => {
  loadRootInfo()
})
</script>

<style scoped>
.root-node {
  background: linear-gradient(180deg, #1e3a5f, #0d1f33);
  border: 2px solid #2563eb;
  border-radius: 8px;
  min-width: 120px;
  font-family: var(--font-family);
  font-size: 12px;
  color: #e2e8f0;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
  transition: border-color 0.2s, box-shadow 0.2s;
}

.root-node.selected {
  border-color: #4f8cff;
  box-shadow: 0 0 0 2px rgba(79, 140, 255, 0.3), 0 4px 12px rgba(0, 0, 0, 0.4);
}

.node-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  background: rgba(255, 255, 255, 0.05);
  border-radius: 6px 6px 0 0;
}

.node-icon {
  font-size: 16px;
}

.node-name {
  font-weight: 600;
  color: #93c5fd;
}

.node-content {
  padding: 12px;
}

.config-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.config-label {
  width: 44px;
  flex-shrink: 0;
  color: #8b949e;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.4px;
}

.logic-select {
  flex: 1;
  min-width: 160px;
}

.config-value {
  flex: 1;
  color: #cbd5e1;
  font-size: 11px;
}

.status-text {
  color: #a0aec0;
}

.version-display {
  font-family: var(--font-mono);
  color: #a5d6ff;
}

.harness-out {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
}

.handle-label {
  font-size: 11px;
  color: #a0aec0;
}

/* vue-flow Handle 样式 - 固定在节点右边缘中央 */
:deep(.handle-out) {
  width: 12px !important;
  height: 12px !important;
  background: #3b82f6 !important;
  border: 2px solid #1e3a5f !important;
  right: -6px !important;
  top: 50% !important;
  transform: translateY(-50%) !important;
}
</style>
