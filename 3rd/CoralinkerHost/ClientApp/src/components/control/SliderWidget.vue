<!--
  @file components/control/SliderWidget.vue
  @description 滑块控件
  
  配置项：
  - orientation: 'horizontal' | 'vertical'
  - min/max: 数值范围
  - autoReturn: 是否自动归零
  - logarithmic: 是否对数模式
-->

<template>
  <div class="slider-widget" :class="[orientation]">
    <div class="slider-label">{{ config.variable || 'Unbound' }}</div>
    
    <div class="slider-track" ref="trackRef" @mousedown="startDrag">
      <div class="slider-fill" :style="fillStyle"></div>
      <div class="slider-thumb" :style="thumbStyle"></div>
    </div>
    
    <div class="slider-value">{{ displayValue }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onUnmounted } from 'vue'

// ============================================
// Props 和 Emits
// ============================================

interface SliderConfig {
  variable?: string
  orientation?: 'horizontal' | 'vertical'
  min?: number
  max?: number
  autoReturn?: boolean
  logarithmic?: boolean
}

const props = defineProps<{
  config: SliderConfig
}>()

const emit = defineEmits<{
  (e: 'change', value: number): void
}>()

// ============================================
// 状态
// ============================================

const trackRef = ref<HTMLDivElement | null>(null)
const dragging = ref(false)
const currentValue = ref(0)

// ============================================
// 计算属性
// ============================================

const orientation = computed(() => props.config.orientation || 'horizontal')
const min = computed(() => props.config.min ?? 0)
const max = computed(() => props.config.max ?? 1)

/** 当前值在 0-1 范围内的比例 */
const ratio = computed(() => {
  const range = max.value - min.value
  if (range === 0) return 0
  return (currentValue.value - min.value) / range
})

const fillStyle = computed(() => {
  if (orientation.value === 'horizontal') {
    return { width: `${ratio.value * 100}%` }
  }
  return { height: `${ratio.value * 100}%` }
})

const thumbStyle = computed(() => {
  if (orientation.value === 'horizontal') {
    return { left: `${ratio.value * 100}%` }
  }
  return { bottom: `${ratio.value * 100}%` }
})

const displayValue = computed(() => {
  return currentValue.value.toFixed(2)
})

// ============================================
// 拖拽逻辑
// ============================================

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
  
  // 自动归零
  if (props.config.autoReturn) {
    const center = (min.value + max.value) / 2
    currentValue.value = center
    emit('change', center)
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
  
  // 限制范围
  newRatio = Math.max(0, Math.min(1, newRatio))
  
  // 计算实际值
  let newValue: number
  
  if (props.config.logarithmic && min.value > 0 && max.value > 0) {
    // 对数模式
    newValue = min.value * Math.pow(max.value / min.value, newRatio)
  } else {
    // 线性模式
    newValue = min.value + newRatio * (max.value - min.value)
  }
  
  currentValue.value = newValue
  emit('change', newValue)
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
.slider-widget {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px;
  user-select: none;
}

.slider-widget.vertical {
  flex-direction: column;
  height: 100%;
}

.slider-label {
  font-size: 12px;
  color: var(--text-muted);
  min-width: 80px;
}

.slider-track {
  position: relative;
  flex: 1;
  height: 8px;
  background: var(--body-color);
  border-radius: 4px;
  cursor: pointer;
}

.slider-widget.vertical .slider-track {
  width: 8px;
  height: 100%;
  min-height: 100px;
}

.slider-fill {
  position: absolute;
  left: 0;
  top: 0;
  height: 100%;
  background: linear-gradient(90deg, var(--primary), var(--primary-hover));
  border-radius: 4px;
}

.slider-widget.vertical .slider-fill {
  left: 0;
  top: auto;
  bottom: 0;
  width: 100%;
}

.slider-thumb {
  position: absolute;
  top: 50%;
  width: 16px;
  height: 16px;
  background: white;
  border: 2px solid var(--primary);
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: var(--shadow-sm);
}

.slider-widget.vertical .slider-thumb {
  left: 50%;
  top: auto;
  transform: translate(-50%, 50%);
}

.slider-value {
  font-family: var(--font-mono);
  font-size: 14px;
  min-width: 60px;
  text-align: right;
}
</style>
