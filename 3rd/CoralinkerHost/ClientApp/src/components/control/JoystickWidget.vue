<!--
  @file components/control/JoystickWidget.vue
  @description 摇杆控件（双轴）
  布局：5列7行
       上键
  左键 [摇杆区] 右键
       下键
  X变量: 值
  Y变量: 值
-->

<template>
  <div 
    class="joystick-widget" 
    :class="{ focused: focused }"
    tabindex="0" 
    @focus="onFocus" 
    @blur="onBlur"
    @click="focusSelf"
  >
    <!-- 上键 -->
    <div class="key-row top">
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyUp || ''), empty: !config.keyUp }"
      >{{ formatKey(config.keyUp) || '↑' }}</span>
    </div>
    
    <!-- 中间行：左键 + 摇杆 + 右键 -->
    <div class="middle-row">
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyLeft || ''), empty: !config.keyLeft }"
      >{{ formatKey(config.keyLeft) || '←' }}</span>
      
      <div class="joystick-area" ref="areaRef" @mousedown="startDrag">
        <div class="joystick-crosshair">
          <div class="cross-h"></div>
          <div class="cross-v"></div>
        </div>
        <div class="joystick-handle" :style="handleStyle"></div>
      </div>
      
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyRight || ''), empty: !config.keyRight }"
      >{{ formatKey(config.keyRight) || '→' }}</span>
    </div>
    
    <!-- 下键 -->
    <div class="key-row bottom">
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyDown || ''), empty: !config.keyDown }"
      >{{ formatKey(config.keyDown) || '↓' }}</span>
    </div>
    
    <!-- 变量显示 -->
    <div class="var-row">
      <span class="var-name">{{ config.variableX || 'X' }}:</span>
      <span class="var-value">{{ displayValueX }}</span>
    </div>
    <div class="var-row">
      <span class="var-name">{{ config.variableY || 'Y' }}:</span>
      <span class="var-value">{{ displayValueY }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

interface JoystickConfig {
  variableX?: string
  variableY?: string
  autoReturnX?: boolean
  autoReturnY?: boolean
  minX?: number
  maxX?: number
  minY?: number
  maxY?: number
  keyUp?: string
  keyDown?: string
  keyLeft?: string
  keyRight?: string
  moveSpeed?: number
  returnSpeed?: number
}

const props = withDefaults(defineProps<{
  config: JoystickConfig
  typeIdX?: number
  typeIdY?: number
}>(), {
  typeIdX: 8,
  typeIdY: 8
})

const emit = defineEmits<{
  (e: 'change', value: { x: number; y: number }): void
}>()

const INTEGER_TYPE_IDS = [0, 1, 2, 3, 4, 5, 6, 7]
const isIntegerTypeX = computed(() => INTEGER_TYPE_IDS.includes(props.typeIdX))
const isIntegerTypeY = computed(() => INTEGER_TYPE_IDS.includes(props.typeIdY))

const areaRef = ref<HTMLDivElement | null>(null)
const dragging = ref(false)
const focused = ref(false)

const posX = ref(0.5)
const posY = ref(0.5)

const pressedKeys = ref<Set<string>>(new Set())
let animationFrameId: number | null = null
let lastFrameTime = 0

const minX = computed(() => props.config.minX ?? -1)
const maxX = computed(() => props.config.maxX ?? 1)
const minY = computed(() => props.config.minY ?? -1)
const maxY = computed(() => props.config.maxY ?? 1)

const moveSpeed = computed(() => (props.config.moveSpeed ?? 100) / 100)
const returnSpeed = computed(() => (props.config.returnSpeed ?? 200) / 100)

const autoReturnX = computed(() => props.config.autoReturnX !== false)
const autoReturnY = computed(() => props.config.autoReturnY !== false)

const valueX = computed(() => minX.value + posX.value * (maxX.value - minX.value))
const valueY = computed(() => {
  const invertedPos = 1 - posY.value
  return minY.value + invertedPos * (maxY.value - minY.value)
})

const handleStyle = computed(() => ({
  left: `${posX.value * 100}%`,
  top: `${posY.value * 100}%`
}))

const displayValueX = computed(() => {
  if (isIntegerTypeX.value) return Math.round(valueX.value).toString()
  return valueX.value.toFixed(2)
})

const displayValueY = computed(() => {
  if (isIntegerTypeY.value) return Math.round(valueY.value).toString()
  return valueY.value.toFixed(2)
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
  const { keyUp, keyDown, keyLeft, keyRight } = props.config
  
  if (key === keyUp || key === keyDown || key === keyLeft || key === keyRight) {
    if (keyUp || keyDown || keyLeft || keyRight) {
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
let lastSentX: number | null = null
let lastSentY: number | null = null

function animationLoop(currentTime: number) {
  const deltaTime = (currentTime - lastFrameTime) / 1000
  lastFrameTime = currentTime
  
  const { keyUp, keyDown, keyLeft, keyRight } = props.config
  
  const isUp = keyUp && pressedKeys.value.has(keyUp)
  const isDown = keyDown && pressedKeys.value.has(keyDown)
  const isLeft = keyLeft && pressedKeys.value.has(keyLeft)
  const isRight = keyRight && pressedKeys.value.has(keyRight)
  
  const speed = moveSpeed.value * deltaTime
  const retSpeed = returnSpeed.value * deltaTime
  
  // X 轴
  if (isLeft && !isRight) {
    posX.value = Math.max(0, posX.value - speed)
  } else if (isRight && !isLeft) {
    posX.value = Math.min(1, posX.value + speed)
  } else if (!dragging.value && autoReturnX.value && !isLeft && !isRight) {
    if (Math.abs(posX.value - 0.5) > 0.001) {
      posX.value += posX.value < 0.5 ? Math.min(retSpeed, 0.5 - posX.value) : -Math.min(retSpeed, posX.value - 0.5)
    }
  }
  
  // Y 轴
  if (isUp && !isDown) {
    posY.value = Math.max(0, posY.value - speed)
  } else if (isDown && !isUp) {
    posY.value = Math.min(1, posY.value + speed)
  } else if (!dragging.value && autoReturnY.value && !isUp && !isDown) {
    if (Math.abs(posY.value - 0.5) > 0.001) {
      posY.value += posY.value < 0.5 ? Math.min(retSpeed, 0.5 - posY.value) : -Math.min(retSpeed, posY.value - 0.5)
    }
  }
  
  // 只在值真正变化时才发送
  emitChangeIfNeeded()
  
  const hasActiveKeys = pressedKeys.value.size > 0
  const needReturnX = autoReturnX.value && Math.abs(posX.value - 0.5) > 0.001
  const needReturnY = autoReturnY.value && Math.abs(posY.value - 0.5) > 0.001
  
  if (hasActiveKeys || needReturnX || needReturnY) {
    animationFrameId = requestAnimationFrame(animationLoop)
  } else {
    animationFrameId = null
  }
}

function startDrag(event: MouseEvent) {
  dragging.value = true
  updatePosition(event)
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

function onDrag(event: MouseEvent) {
  if (!dragging.value) return
  updatePosition(event)
}

function stopDrag() {
  dragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  
  if (autoReturnX.value || autoReturnY.value) {
    startAnimationLoop()
  }
}

function updatePosition(event: MouseEvent) {
  if (!areaRef.value) return
  const rect = areaRef.value.getBoundingClientRect()
  posX.value = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width))
  posY.value = Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height))
  emitChangeIfNeeded()
}

function emitChangeIfNeeded() {
  const x = isIntegerTypeX.value ? Math.round(valueX.value) : valueX.value
  const y = isIntegerTypeY.value ? Math.round(valueY.value) : valueY.value
  
  // 检查各轴是否真的变化了
  const xChanged = lastSentX === null || Math.abs(x - lastSentX) > 0.0001
  const yChanged = lastSentY === null || Math.abs(y - lastSentY) > 0.0001
  
  // 只发送变化的轴（undefined 表示没变化）
  if (xChanged || yChanged) {
    if (xChanged) lastSentX = x
    if (yChanged) lastSentY = y
    emit('change', { 
      x: xChanged ? x : undefined, 
      y: yChanged ? y : undefined 
    })
  }
}

onMounted(() => {
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
.joystick-widget {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 4px;
  user-select: none;
  height: 100%;
  outline: none;
  border-radius: var(--radius);
  transition: box-shadow 0.15s;
  gap: 2px;
}

.joystick-widget.focused {
  box-shadow: inset 0 0 0 2px var(--primary);
}

/* 上下按键行 */
.key-row {
  display: flex;
  justify-content: center;
  flex-shrink: 0;
}

/* 中间行 */
.middle-row {
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 1;
  min-height: 0;
  width: 100%;
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

/* 摇杆区域 */
.joystick-area {
  position: relative;
  flex: 1;
  aspect-ratio: 1;
  max-width: 100%;
  max-height: 100%;
  background: var(--body-color);
  border: 2px solid var(--border-color);
  border-radius: var(--radius);
  cursor: crosshair;
}

.joystick-crosshair {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.cross-h, .cross-v {
  position: absolute;
  background: var(--border-color);
  opacity: 0.5;
}

.cross-h { left: 0; right: 0; top: 50%; height: 1px; }
.cross-v { top: 0; bottom: 0; left: 50%; width: 1px; }

.joystick-handle {
  position: absolute;
  width: 20px;
  height: 20px;
  background: radial-gradient(circle at 30% 30%, var(--primary-hover), var(--primary));
  border: 2px solid white;
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: 0 2px 6px rgba(0,0,0,0.4);
  pointer-events: none;
}

/* 变量显示行 */
.var-row {
  display: flex;
  justify-content: space-between;
  width: 100%;
  font-size: 9px;
  padding: 0 2px;
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
