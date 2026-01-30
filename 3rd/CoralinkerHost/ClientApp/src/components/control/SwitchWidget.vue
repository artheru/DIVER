<!--
  @file components/control/SwitchWidget.vue
  @description 开关控件
  布局：3列2行
  OFF | ON  [按键]
  绑定变量: 值
-->

<template>
  <div 
    class="switch-widget" 
    :class="{ focused: focused }"
    tabindex="0" 
    @focus="onFocus" 
    @blur="onBlur"
    @click="focusSelf"
  >
    <!-- 开关 + 按键行 -->
    <div class="switch-row">
      <div class="switch-container" :class="[`states-${states}`]">
        <template v-if="states === 2">
          <button class="switch-btn" :class="{ active: currentValue === 0 }" @click.stop="setValue(0)">OFF</button>
          <button class="switch-btn" :class="{ active: currentValue === 1 }" @click.stop="setValue(1)">ON</button>
        </template>
        <template v-else>
          <button class="switch-btn" :class="{ active: currentValue === -1 }" @click.stop="setValue(-1)">-1</button>
          <button class="switch-btn center" :class="{ active: currentValue === 0 }" @click.stop="setValue(0)">0</button>
          <button class="switch-btn" :class="{ active: currentValue === 1 }" @click.stop="setValue(1)">+1</button>
        </template>
      </div>
      
      <span 
        v-if="config.keyToggle"
        class="key-badge" 
        :class="{ active: isKeyPressed }"
      >{{ formatKey(config.keyToggle) }}</span>
      <span v-else class="key-badge empty">·</span>
    </div>
    
    <!-- 变量显示 -->
    <div class="var-row">
      <span class="var-name">{{ config.variable || 'Unbound' }}:</span>
      <span class="var-value">{{ currentValue }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

interface SwitchConfig {
  variable?: string
  states?: 2 | 3
  keyToggle?: string
}

const props = defineProps<{
  config: SwitchConfig
}>()

const emit = defineEmits<{
  (e: 'change', value: number): void
}>()

const currentValue = ref(0)
const focused = ref(false)
const isKeyPressed = ref(false)

const states = computed(() => props.config.states || 2)

function formatKey(key?: string): string {
  if (!key) return ''
  const keyMap: Record<string, string> = {
    'ArrowUp': '↑', 'ArrowDown': '↓', 'ArrowLeft': '←', 'ArrowRight': '→',
    ' ': '␣', 'Space': '␣'
  }
  return keyMap[key] || key.toUpperCase()
}

function focusSelf(event: MouseEvent) {
  const target = event.currentTarget as HTMLElement
  target?.focus()
}

function onFocus() {
  focused.value = true
}

function onBlur() {
  focused.value = false
  isKeyPressed.value = false
}

function onKeyDown(event: KeyboardEvent) {
  if (event.key === props.config.keyToggle && props.config.keyToggle) {
    event.preventDefault()
    if (!isKeyPressed.value) {
      isKeyPressed.value = true
      toggleValue()
    }
  }
}

function onKeyUp(event: KeyboardEvent) {
  if (event.key === props.config.keyToggle) {
    isKeyPressed.value = false
  }
}

function setValue(value: number) {
  currentValue.value = value
  emit('change', value)
}

function toggleValue() {
  if (states.value === 2) {
    setValue(currentValue.value === 0 ? 1 : 0)
  } else {
    if (currentValue.value === -1) setValue(0)
    else if (currentValue.value === 0) setValue(1)
    else setValue(-1)
  }
}

onMounted(() => {
  document.addEventListener('keydown', onKeyDown)
  document.addEventListener('keyup', onKeyUp)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKeyDown)
  document.removeEventListener('keyup', onKeyUp)
})
</script>

<style scoped>
.switch-widget {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  padding: 6px;
  user-select: none;
  height: 100%;
  outline: none;
  border-radius: var(--radius);
  transition: box-shadow 0.15s;
}

.switch-widget.focused {
  box-shadow: inset 0 0 0 2px var(--primary);
}

/* 开关 + 按键行 */
.switch-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  flex: 1;
}

.switch-container {
  display: flex;
  gap: 2px;
  padding: 3px;
  background: var(--body-color);
  border-radius: var(--radius);
}

.switch-btn {
  padding: 6px 12px;
  background: var(--panel-color-2);
  border-radius: var(--radius-sm);
  color: var(--text-muted);
  font-size: 11px;
  font-weight: 600;
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

.switch-btn.center.active {
  background: var(--warning);
}

/* 按键徽章 */
.key-badge {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 24px;
  height: 24px;
  padding: 0 6px;
  background: var(--primary);
  border-radius: 4px;
  font-size: 12px;
  font-weight: 700;
  font-family: var(--font-mono);
  color: white;
  transition: all 0.1s;
  box-shadow: 0 2px 4px rgba(0,0,0,0.3);
}

.key-badge.empty {
  background: var(--panel-color-2);
  color: var(--text-muted);
  box-shadow: none;
}

.key-badge.active {
  background: #ffcc00;
  color: #000;
  transform: scale(1.15);
  box-shadow: 0 0 12px #ffcc00;
}

/* 变量显示行 */
.var-row {
  display: flex;
  justify-content: space-between;
  font-size: 10px;
  flex-shrink: 0;
}

.var-name {
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.var-value {
  font-family: var(--font-mono);
  color: var(--primary);
  flex-shrink: 0;
  margin-left: 4px;
}
</style>
