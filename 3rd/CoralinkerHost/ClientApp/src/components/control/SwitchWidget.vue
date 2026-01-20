<!--
  @file components/control/SwitchWidget.vue
  @description 开关控件
  
  配置项：
  - states: 2 (0/1) 或 3 (-1/0/1)
  - variable: 绑定的变量
-->

<template>
  <div class="switch-widget">
    <div class="switch-label">{{ config.variable || 'Unbound' }}</div>
    
    <div class="switch-container" :class="[`states-${states}`]">
      <!-- 2态开关 -->
      <template v-if="states === 2">
        <button 
          class="switch-btn"
          :class="{ active: currentValue === 0 }"
          @click="setValue(0)"
        >
          OFF
        </button>
        <button 
          class="switch-btn"
          :class="{ active: currentValue === 1 }"
          @click="setValue(1)"
        >
          ON
        </button>
      </template>
      
      <!-- 3态开关 -->
      <template v-else>
        <button 
          class="switch-btn"
          :class="{ active: currentValue === -1 }"
          @click="setValue(-1)"
        >
          -1
        </button>
        <button 
          class="switch-btn center"
          :class="{ active: currentValue === 0 }"
          @click="setValue(0)"
        >
          0
        </button>
        <button 
          class="switch-btn"
          :class="{ active: currentValue === 1 }"
          @click="setValue(1)"
        >
          +1
        </button>
      </template>
    </div>
    
    <div class="switch-value">{{ currentValue }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

// ============================================
// Props 和 Emits
// ============================================

interface SwitchConfig {
  variable?: string
  states?: 2 | 3
}

const props = defineProps<{
  config: SwitchConfig
}>()

const emit = defineEmits<{
  (e: 'change', value: number): void
}>()

// ============================================
// 状态
// ============================================

const currentValue = ref(0)

// ============================================
// 计算属性
// ============================================

const states = computed(() => props.config.states || 2)

// ============================================
// 方法
// ============================================

function setValue(value: number) {
  currentValue.value = value
  emit('change', value)
}
</script>

<style scoped>
.switch-widget {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 12px;
  user-select: none;
}

.switch-label {
  font-size: 12px;
  color: var(--text-muted);
}

.switch-container {
  display: flex;
  gap: 4px;
  padding: 4px;
  background: var(--body-color);
  border-radius: var(--radius);
}

.switch-btn {
  padding: 8px 16px;
  background: var(--panel-color-2);
  border-radius: var(--radius-sm);
  color: var(--text-muted);
  font-size: 12px;
  font-weight: 500;
  transition: all var(--transition-fast);
}

.switch-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-color);
}

.switch-btn.active {
  background: var(--primary);
  color: white;
}

/* 三态中间按钮特殊样式 */
.switch-btn.center.active {
  background: var(--warning);
}

.switch-value {
  font-family: var(--font-mono);
  font-size: 18px;
  font-weight: 600;
}
</style>
