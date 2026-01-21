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
      <div class="harness-out">
        <span class="handle-label">out ●</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Handle, Position } from '@vue-flow/core'

// Props from vue-flow
defineProps<{
  id: string
  data: {
    name: string
  }
  selected: boolean
}>()
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
