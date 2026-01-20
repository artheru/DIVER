<!--
  @file components/control/JoystickWidget.vue
  @description 摇杆控件（双轴）
  
  配置项：
  - autoReturn: 释放后是否归零
  - minX/maxX, minY/maxY: 各轴范围
  - variableX, variableY: 绑定的变量
-->

<template>
  <div class="joystick-widget">
    <div class="joystick-labels">
      <span>X: {{ config.variableX || '-' }}</span>
      <span>Y: {{ config.variableY || '-' }}</span>
    </div>
    
    <div class="joystick-area" ref="areaRef" @mousedown="startDrag">
      <!-- 十字线 -->
      <div class="joystick-crosshair">
        <div class="cross-h"></div>
        <div class="cross-v"></div>
      </div>
      
      <!-- 摇杆手柄 -->
      <div class="joystick-handle" :style="handleStyle"></div>
    </div>
    
    <div class="joystick-values">
      <span>X: {{ valueX.toFixed(2) }}</span>
      <span>Y: {{ valueY.toFixed(2) }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onUnmounted } from 'vue'

// ============================================
// Props 和 Emits
// ============================================

interface JoystickConfig {
  variableX?: string
  variableY?: string
  autoReturn?: boolean
  minX?: number
  maxX?: number
  minY?: number
  maxY?: number
}

const props = defineProps<{
  config: JoystickConfig
}>()

const emit = defineEmits<{
  (e: 'change', value: { x: number; y: number }): void
}>()

// ============================================
// 状态
// ============================================

const areaRef = ref<HTMLDivElement | null>(null)
const dragging = ref(false)

// 0-1 范围内的位置（中心为 0.5）
const posX = ref(0.5)
const posY = ref(0.5)

// ============================================
// 计算属性
// ============================================

const minX = computed(() => props.config.minX ?? -1)
const maxX = computed(() => props.config.maxX ?? 1)
const minY = computed(() => props.config.minY ?? -1)
const maxY = computed(() => props.config.maxY ?? 1)

/** X 轴实际值 */
const valueX = computed(() => {
  return minX.value + posX.value * (maxX.value - minX.value)
})

/** Y 轴实际值（Y 轴向上为正） */
const valueY = computed(() => {
  // posY 0 = 上, 1 = 下，所以需要反转
  const invertedPos = 1 - posY.value
  return minY.value + invertedPos * (maxY.value - minY.value)
})

const handleStyle = computed(() => ({
  left: `${posX.value * 100}%`,
  top: `${posY.value * 100}%`
}))

// ============================================
// 拖拽逻辑
// ============================================

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
  
  // 自动归零
  if (props.config.autoReturn !== false) { // 默认自动归零
    posX.value = 0.5
    posY.value = 0.5
    emitChange()
  }
}

function updatePosition(event: MouseEvent) {
  if (!areaRef.value) return
  
  const rect = areaRef.value.getBoundingClientRect()
  
  let newX = (event.clientX - rect.left) / rect.width
  let newY = (event.clientY - rect.top) / rect.height
  
  // 限制范围
  newX = Math.max(0, Math.min(1, newX))
  newY = Math.max(0, Math.min(1, newY))
  
  posX.value = newX
  posY.value = newY
  
  emitChange()
}

function emitChange() {
  emit('change', { x: valueX.value, y: valueY.value })
}

// ============================================
// 清理
// ============================================

onUnmounted(() => {
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
})
</script>

<style scoped>
.joystick-widget {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 8px;
  user-select: none;
}

.joystick-labels {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: var(--text-muted);
}

.joystick-area {
  position: relative;
  width: 100%;
  aspect-ratio: 1;
  background: var(--body-color);
  border: 2px solid var(--border-color);
  border-radius: var(--radius);
  cursor: crosshair;
}

/* 十字线 */
.joystick-crosshair {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.cross-h,
.cross-v {
  position: absolute;
  background: var(--border-color);
}

.cross-h {
  left: 0;
  right: 0;
  top: 50%;
  height: 1px;
}

.cross-v {
  top: 0;
  bottom: 0;
  left: 50%;
  width: 1px;
}

/* 摇杆手柄 */
.joystick-handle {
  position: absolute;
  width: 32px;
  height: 32px;
  background: radial-gradient(circle at 30% 30%, var(--primary-hover), var(--primary));
  border: 2px solid white;
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: var(--shadow);
  pointer-events: none;
  transition: box-shadow 0.1s;
}

.joystick-area:active .joystick-handle {
  box-shadow: var(--shadow-lg);
}

.joystick-values {
  display: flex;
  justify-content: space-between;
  font-family: var(--font-mono);
  font-size: 12px;
}
</style>
