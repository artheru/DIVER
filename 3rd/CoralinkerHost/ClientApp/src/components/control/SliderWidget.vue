<!--
  @file components/control/SliderWidget.vue
  @description 滑块控件
  布局：5列2行（横向）
  [-键] ═══【滑块】═══ [+键]
  绑定变量: 值
-->

<template>
  <div 
    class="slider-widget" 
    :class="[orientation, { focused: focused }]" 
    tabindex="0" 
    @focus="onFocus" 
    @blur="onBlur"
    @click="focusSelf"
  >
    <!-- 滑块主体 -->
    <div class="slider-body">
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyDecrease || ''), empty: !config.keyDecrease }"
      >{{ formatKey(config.keyDecrease) || '-' }}</span>
      
      <div class="slider-track" ref="trackRef" @mousedown="startDrag">
        <div class="slider-fill" :style="fillStyle"></div>
        <div class="slider-thumb" :style="thumbStyle"></div>
      </div>
      
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyIncrease || ''), empty: !config.keyIncrease }"
      >{{ formatKey(config.keyIncrease) || '+' }}</span>
    </div>
    
    <!-- 变量显示 -->
    <div class="var-row">
      <span class="var-name">{{ config.variable || 'Unbound' }}:</span>
      <span class="var-value">{{ displayValue }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

interface SliderConfig {
  variable?: string
  orientation?: 'horizontal' | 'vertical'
  min?: number
  max?: number
  autoReturn?: boolean
  logarithmic?: boolean
  keyIncrease?: string
  keyDecrease?: string
  moveSpeed?: number
  returnSpeed?: number
}

const props = withDefaults(defineProps<{
  config: SliderConfig
  typeId?: number
}>(), {
  typeId: 8
})

const INTEGER_TYPE_IDS = [0, 1, 2, 3, 4, 5, 6, 7]
const isIntegerType = computed(() => INTEGER_TYPE_IDS.includes(props.typeId))

const emit = defineEmits<{
  (e: 'change', value: number): void
}>()

const trackRef = ref<HTMLDivElement | null>(null)
const dragging = ref(false)
const focused = ref(false)

const currentRatio = ref(0.5)

const pressedKeys = ref<Set<string>>(new Set())
let animationFrameId: number | null = null
let lastFrameTime = 0

const orientation = computed(() => props.config.orientation || 'horizontal')
const min = computed(() => props.config.min ?? 0)
const max = computed(() => props.config.max ?? 1)
const moveSpeed = computed(() => (props.config.moveSpeed ?? 100) / 100)
const returnSpeed = computed(() => (props.config.returnSpeed ?? 200) / 100)

const currentValue = computed(() => {
  const range = max.value - min.value
  if (props.config.logarithmic && min.value > 0 && max.value > 0) {
    return min.value * Math.pow(max.value / min.value, currentRatio.value)
  }
  return min.value + currentRatio.value * range
})

const fillStyle = computed(() => {
  if (orientation.value === 'horizontal') {
    return { width: `${currentRatio.value * 100}%` }
  }
  return { height: `${currentRatio.value * 100}%` }
})

const thumbStyle = computed(() => {
  if (orientation.value === 'horizontal') {
    return { left: `${currentRatio.value * 100}%` }
  }
  return { bottom: `${currentRatio.value * 100}%` }
})

const displayValue = computed(() => {
  if (isIntegerType.value) return Math.round(currentValue.value).toString()
  return currentValue.value.toFixed(2)
})

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
  startAnimationLoop()
}

function onBlur() {
  focused.value = false
  pressedKeys.value.clear()
}

function onKeyDown(event: KeyboardEvent) {
  const key = event.key
  const { keyIncrease, keyDecrease } = props.config
  
  if (key === keyIncrease || key === keyDecrease) {
    if (keyIncrease || keyDecrease) {
      event.preventDefault()
      pressedKeys.value.add(key)
      if (animationFrameId === null) {
        startAnimationLoop()
      }
    }
  }
}

function onKeyUp(event: KeyboardEvent) {
  pressedKeys.value.delete(event.key)
}

function startAnimationLoop() {
  if (animationFrameId !== null) return
  lastFrameTime = performance.now()
  animationFrameId = requestAnimationFrame(animationLoop)
}

function stopAnimationLoop() {
  if (animationFrameId !== null) {
    cancelAnimationFrame(animationFrameId)
    animationFrameId = null
  }
}

// 记录上次发送的值，用于检测是否真的变化
let lastSentValue: number | null = null

function animationLoop(currentTime: number) {
  const deltaTime = (currentTime - lastFrameTime) / 1000
  lastFrameTime = currentTime
  
  const { keyIncrease, keyDecrease, autoReturn } = props.config
  
  const isIncrease = keyIncrease && pressedKeys.value.has(keyIncrease)
  const isDecrease = keyDecrease && pressedKeys.value.has(keyDecrease)
  
  const speed = moveSpeed.value * deltaTime
  const retSpeed = returnSpeed.value * deltaTime
  
  if (isIncrease && !isDecrease) {
    currentRatio.value = Math.min(1, currentRatio.value + speed)
  } else if (isDecrease && !isIncrease) {
    currentRatio.value = Math.max(0, currentRatio.value - speed)
  } else if (!dragging.value && autoReturn && !isIncrease && !isDecrease) {
    if (Math.abs(currentRatio.value - 0.5) > 0.001) {
      currentRatio.value += currentRatio.value < 0.5 
        ? Math.min(retSpeed, 0.5 - currentRatio.value) 
        : -Math.min(retSpeed, currentRatio.value - 0.5)
    }
  }
  
  // 只在值真正变化时才发送
  emitChangeIfNeeded()
  
  const hasActiveKeys = pressedKeys.value.size > 0
  const needReturn = autoReturn && Math.abs(currentRatio.value - 0.5) > 0.001
  
  if (hasActiveKeys || needReturn) {
    animationFrameId = requestAnimationFrame(animationLoop)
  } else {
    animationFrameId = null
  }
}

function startDrag(event: MouseEvent) {
  dragging.value = true
  updateValue(event)
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

function onDrag(event: MouseEvent) {
  if (!dragging.value) return
  updateValue(event)
}

function stopDrag() {
  dragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  
  if (props.config.autoReturn) {
    startAnimationLoop()
  }
}

function updateValue(event: MouseEvent) {
  if (!trackRef.value) return
  const rect = trackRef.value.getBoundingClientRect()
  let newRatio: number
  
  if (orientation.value === 'horizontal') {
    newRatio = (event.clientX - rect.left) / rect.width
  } else {
    newRatio = 1 - (event.clientY - rect.top) / rect.height
  }
  
  currentRatio.value = Math.max(0, Math.min(1, newRatio))
  emitChangeIfNeeded()
}

function emitChangeIfNeeded() {
  const val = isIntegerType.value ? Math.round(currentValue.value) : currentValue.value
  
  // 检查值是否真的变化了
  if (lastSentValue === null || Math.abs(val - lastSentValue) > 0.0001) {
    lastSentValue = val
    emit('change', val)
  }
}

onMounted(() => {
  currentRatio.value = 0.5
  document.addEventListener('keydown', onKeyDown)
  document.addEventListener('keyup', onKeyUp)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKeyDown)
  document.removeEventListener('keyup', onKeyUp)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  stopAnimationLoop()
})
</script>

<style scoped>
.slider-widget {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px;
  user-select: none;
  height: 100%;
  box-sizing: border-box;
  outline: none;
  border-radius: var(--radius);
  transition: box-shadow 0.15s;
}

.slider-widget.focused {
  box-shadow: inset 0 0 0 2px var(--primary);
}

/* 滑块主体 */
.slider-body {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
}

.slider-widget.vertical .slider-body {
  flex-direction: column-reverse;
}

/* 按键徽章 */
.key-badge {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 22px;
  padding: 0 4px;
  background: var(--primary);
  border-radius: 4px;
  font-size: 11px;
  font-weight: 700;
  font-family: var(--font-mono);
  color: white;
  transition: all 0.1s;
  box-shadow: 0 2px 4px rgba(0,0,0,0.3);
  flex-shrink: 0;
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

/* 滑轨 */
.slider-track {
  position: relative;
  flex: 1;
  height: 12px;
  background: var(--body-color);
  border-radius: 6px;
  cursor: pointer;
}

.slider-widget.vertical .slider-track {
  width: 12px;
  height: auto;
  flex: 1;
}

.slider-fill {
  position: absolute;
  left: 0;
  top: 0;
  height: 100%;
  background: linear-gradient(90deg, var(--primary), var(--primary-hover));
  border-radius: 6px;
}

.slider-widget.vertical .slider-fill {
  left: 0;
  top: auto;
  bottom: 0;
  width: 100%;
  height: auto;
}

.slider-thumb {
  position: absolute;
  top: 50%;
  width: 18px;
  height: 18px;
  background: white;
  border: 2px solid var(--primary);
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: 0 2px 4px rgba(0,0,0,0.3);
}

.slider-widget.vertical .slider-thumb {
  left: 50%;
  top: auto;
  transform: translate(-50%, 50%);
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
