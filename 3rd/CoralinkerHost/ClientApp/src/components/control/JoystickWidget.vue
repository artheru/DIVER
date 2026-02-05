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
    :class="{ focused: focused, 'touch-mode': isTouchDevice }"
    tabindex="0" 
    @focus="onFocus" 
    @blur="onBlur"
    @click="focusSelf"
  >
    <!-- 上键（触摸设备不显示） -->
    <div v-if="!isTouchDevice" class="key-row top">
      <span 
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyUp || ''), empty: !config.keyUp }"
      >{{ formatKey(config.keyUp) || '↑' }}</span>
    </div>
    
    <!-- 中间行：左键 + 摇杆 + 右键 -->
    <div class="middle-row">
      <span 
        v-if="!isTouchDevice"
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyLeft || ''), empty: !config.keyLeft }"
      >{{ formatKey(config.keyLeft) || '←' }}</span>
      
      <div 
        class="joystick-area" 
        ref="areaRef" 
        @mousedown="startDrag"
        @touchstart.prevent="startTouchDrag"
      >
        <div class="joystick-crosshair">
          <div class="cross-h"></div>
          <div class="cross-v"></div>
        </div>
        <div class="joystick-handle" :style="handleStyle"></div>
      </div>
      
      <span 
        v-if="!isTouchDevice"
        class="key-badge" 
        :class="{ active: pressedKeys.has(config.keyRight || ''), empty: !config.keyRight }"
      >{{ formatKey(config.keyRight) || '→' }}</span>
    </div>
    
    <!-- 下键（触摸设备不显示） -->
    <div v-if="!isTouchDevice" class="key-row bottom">
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

// 检测是否是触摸设备
const isTouchDevice = ref(false)
if (typeof window !== 'undefined') {
  isTouchDevice.value = 'ontouchstart' in window || navigator.maxTouchPoints > 0
}

interface JoystickConfig {
  variableX?: string
  variableY?: string
  autoReturnX?: boolean
  autoReturnY?: boolean
  returnToX?: number  // X轴自动回退目标位置（百分比 0-100，默认 50）
  returnToY?: number  // Y轴自动回退目标位置（百分比 0-100，默认 50）
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
  (e: 'change', value: { x?: number; y?: number }): void
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

// X轴回退目标位置（ratio 0-1，默认 0.5 即 50%）
const returnToRatioX = computed(() => {
  const percent = props.config.returnToX ?? 50
  return Math.max(0, Math.min(1, percent / 100))
})

// Y轴回退目标位置（ratio 0-1，默认 0.5 即 50%）
// 注意：Y轴 posY 是反转的（top=0, bottom=1），但百分比是直观的（0%=min, 100%=max）
const returnToRatioY = computed(() => {
  const percent = props.config.returnToY ?? 50
  // 反转：百分比转为 posY（100% 对应 posY=0，0% 对应 posY=1）
  return Math.max(0, Math.min(1, 1 - percent / 100))
})

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
  // 如果焦点在输入框中，不处理键盘事件
  const activeEl = document.activeElement
  if (activeEl && (activeEl.tagName === 'INPUT' || activeEl.tagName === 'TEXTAREA' || activeEl.tagName === 'SELECT')) {
    return
  }
  
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
  const targetX = returnToRatioX.value
  const targetY = returnToRatioY.value
  
  // X 轴
  if (isLeft && !isRight) {
    posX.value = Math.max(0, posX.value - speed)
  } else if (isRight && !isLeft) {
    posX.value = Math.min(1, posX.value + speed)
  } else if (!dragging.value && autoReturnX.value && !isLeft && !isRight) {
    if (Math.abs(posX.value - targetX) > 0.001) {
      posX.value += posX.value < targetX ? Math.min(retSpeed, targetX - posX.value) : -Math.min(retSpeed, posX.value - targetX)
    }
  }
  
  // Y 轴
  if (isUp && !isDown) {
    posY.value = Math.max(0, posY.value - speed)
  } else if (isDown && !isUp) {
    posY.value = Math.min(1, posY.value + speed)
  } else if (!dragging.value && autoReturnY.value && !isUp && !isDown) {
    if (Math.abs(posY.value - targetY) > 0.001) {
      posY.value += posY.value < targetY ? Math.min(retSpeed, targetY - posY.value) : -Math.min(retSpeed, posY.value - targetY)
    }
  }
  
  // 只在值真正变化时才发送
  emitChangeIfNeeded()
  
  const hasActiveKeys = pressedKeys.value.size > 0
  const needReturnX = autoReturnX.value && Math.abs(posX.value - targetX) > 0.001
  const needReturnY = autoReturnY.value && Math.abs(posY.value - targetY) > 0.001
  
  if (hasActiveKeys || needReturnX || needReturnY) {
    animationFrameId = requestAnimationFrame(animationLoop)
  } else {
    animationFrameId = null
  }
}

function startDrag(event: MouseEvent) {
  dragging.value = true
  updatePositionFromMouse(event)
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

function onDrag(event: MouseEvent) {
  if (!dragging.value) return
  updatePositionFromMouse(event)
}

function stopDrag() {
  dragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  
  if (autoReturnX.value || autoReturnY.value) {
    startAnimationLoop()
  }
}

// 触摸事件处理
function startTouchDrag(event: TouchEvent) {
  dragging.value = true
  updatePositionFromTouch(event)
  document.addEventListener('touchmove', onTouchDrag, { passive: false })
  document.addEventListener('touchend', stopTouchDrag)
  document.addEventListener('touchcancel', stopTouchDrag)
}

function onTouchDrag(event: TouchEvent) {
  if (!dragging.value) return
  event.preventDefault()
  updatePositionFromTouch(event)
}

function stopTouchDrag() {
  dragging.value = false
  document.removeEventListener('touchmove', onTouchDrag)
  document.removeEventListener('touchend', stopTouchDrag)
  document.removeEventListener('touchcancel', stopTouchDrag)
  
  if (autoReturnX.value || autoReturnY.value) {
    startAnimationLoop()
  }
}

function updatePositionFromMouse(event: MouseEvent) {
  if (!areaRef.value) return
  const rect = areaRef.value.getBoundingClientRect()
  posX.value = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width))
  posY.value = Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height))
  emitChangeIfNeeded()
}

function updatePositionFromTouch(event: TouchEvent) {
  const touch = event.touches[0]
  if (!areaRef.value || !touch) return
  const rect = areaRef.value.getBoundingClientRect()
  posX.value = Math.max(0, Math.min(1, (touch.clientX - rect.left) / rect.width))
  posY.value = Math.max(0, Math.min(1, (touch.clientY - rect.top) / rect.height))
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
  // 初始值设为回退目标位置
  posX.value = returnToRatioX.value
  posY.value = returnToRatioY.value
  document.addEventListener('keydown', onKeyDown)
  document.addEventListener('keyup', onKeyUp)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKeyDown)
  document.removeEventListener('keyup', onKeyUp)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  document.removeEventListener('touchmove', onTouchDrag)
  document.removeEventListener('touchend', stopTouchDrag)
  document.removeEventListener('touchcancel', stopTouchDrag)
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
  touch-action: none; /* 禁止浏览器默认触摸行为 */
}

.joystick-widget.focused {
  box-shadow: inset 0 0 0 2px var(--primary);
}

/* 触摸模式：摇杆区域更大，无按键显示 */
.joystick-widget.touch-mode .middle-row {
  flex: 1;
}

.joystick-widget.touch-mode .joystick-area {
  width: 100%;
  height: 100%;
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
  width: 28px;
  height: 28px;
  background: 
    radial-gradient(circle at 35% 35%, rgba(255,255,255,0.8) 0%, transparent 40%),
    radial-gradient(circle at 50% 50%, var(--primary) 0%, var(--primary-hover) 100%);
  border: 3px solid rgba(255,255,255,0.9);
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: 
    0 3px 8px rgba(0,0,0,0.4),
    0 1px 2px rgba(0,0,0,0.2),
    inset 0 -2px 4px rgba(0,0,0,0.15);
  pointer-events: none;
  transition: transform 0.05s ease-out;
}

.joystick-widget:active .joystick-handle {
  transform: translate(-50%, -50%) scale(0.95);
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
