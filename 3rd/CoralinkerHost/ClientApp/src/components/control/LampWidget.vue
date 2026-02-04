<!--
  @file components/control/LampWidget.vue
  @description LED 指示灯控件（显示二进制位状态）
  
  功能：
  - 绑定数值变量，显示其二进制位状态
  - 支持 1-32 位显示
  - 横排或竖排排列，每 4 位换行
  - 可选灯的颜色
-->

<template>
  <div class="lamp-widget" :class="[`layout-${layout}`]">
    <!-- LED 灯组（从 bit 0 开始递增排列） -->
    <div class="lamp-grid" :style="gridStyle">
      <div 
        v-for="i in bitCount" 
        :key="i"
        class="lamp-cell"
        :title="`Bit ${i - 1}: ${getBit(i - 1) ? '1' : '0'}`"
      >
        <div 
          class="lamp-led" 
          :class="{ on: getBit(i - 1) }"
          :style="ledStyle(getBit(i - 1))"
        >
          <div class="lamp-highlight"></div>
        </div>
        <span v-if="shouldShowIndex" class="bit-index">{{ i - 1 }}</span>
      </div>
    </div>
    
    <!-- 变量显示 -->
    <div class="var-row">
      <span class="var-name">{{ config.variable || 'Unbound' }}:</span>
      <span class="var-value">{{ displayValue }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore } from '@/stores'

interface LampConfig {
  variable?: string
  bits?: number        // 显示的位数（1-32，默认 1）
  layout?: 'horizontal' | 'vertical'  // 排列方向
  color?: string       // 亮灯颜色（默认绿色 #00ff00）
  showBitIndex?: boolean  // 是否显示位索引
}

const props = withDefaults(defineProps<{
  config: LampConfig
}>(), {})

const runtimeStore = useRuntimeStore()
const { variableList } = storeToRefs(runtimeStore)

// 配置
const bitCount = computed(() => Math.max(1, Math.min(32, props.config.bits ?? 1)))
const layout = computed(() => props.config.layout || 'horizontal')
const ledColor = computed(() => props.config.color || '#00ff00')

// 是否显示位索引：默认开启，但只有 1 位时不显示
const shouldShowIndex = computed(() => {
  if (bitCount.value === 1) return false
  return props.config.showBitIndex !== false  // 默认 true
})

// 从 variableList 获取当前值
const currentVariable = computed(() => {
  if (!props.config.variable) return null
  return variableList.value.find(v => v.name === props.config.variable)
})

const rawValue = computed(() => {
  const val = currentVariable.value?.value ?? 0
  // 处理布尔类型
  if (typeof val === 'boolean') {
    return val ? 1 : 0
  }
  if (typeof val === 'string') {
    const lower = val.toLowerCase()
    if (lower === 'true') return 1
    if (lower === 'false') return 0
  }
  const num = Number(val)
  return isNaN(num) ? 0 : Math.floor(num)
})

// 获取指定位的值（0 或 1）
function getBit(bitIndex: number): boolean {
  return ((rawValue.value >> bitIndex) & 1) === 1
}

// 显示值（十进制 + 十六进制）
const displayValue = computed(() => {
  const val = rawValue.value
  if (bitCount.value <= 8) {
    return `${val} (0x${(val & 0xFF).toString(16).toUpperCase().padStart(2, '0')})`
  } else if (bitCount.value <= 16) {
    return `${val} (0x${(val & 0xFFFF).toString(16).toUpperCase().padStart(4, '0')})`
  } else {
    return `${val} (0x${(val >>> 0).toString(16).toUpperCase().padStart(8, '0')})`
  }
})

// 网格样式（每 8 位换行）
const gridStyle = computed(() => {
  const bits = bitCount.value
  if (layout.value === 'horizontal') {
    // 横排：每行最多 8 个
    const cols = Math.min(bits, 8)
    return {
      gridTemplateColumns: `repeat(${cols}, 1fr)`
    }
  } else {
    // 竖排：每列最多 8 个
    const rows = Math.min(bits, 8)
    return {
      gridTemplateRows: `repeat(${rows}, 1fr)`,
      gridAutoFlow: 'column'
    }
  }
})

// LED 样式
function ledStyle(isOn: boolean) {
  if (isOn) {
    return {
      background: `radial-gradient(circle at 35% 35%, ${lightenColor(ledColor.value, 50)}, ${ledColor.value})`,
      boxShadow: `0 0 8px 2px ${ledColor.value}, inset 0 0 4px rgba(255,255,255,0.3)`
    }
  }
  return {}
}

// 颜色变亮
function lightenColor(hex: string, percent: number): string {
  const num = parseInt(hex.replace('#', ''), 16)
  const r = Math.min(255, ((num >> 16) & 0xFF) + Math.floor(255 * percent / 100))
  const g = Math.min(255, ((num >> 8) & 0xFF) + Math.floor(255 * percent / 100))
  const b = Math.min(255, (num & 0xFF) + Math.floor(255 * percent / 100))
  return `rgb(${r}, ${g}, ${b})`
}
</script>

<style scoped>
.lamp-widget {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 6px;
  height: 100%;
  box-sizing: border-box;
  user-select: none;
  gap: 4px;
}

/* 横排布局 */
.layout-horizontal {
  flex-direction: column;
}

/* 竖排布局 */
.layout-vertical {
  flex-direction: row;
}

.layout-vertical .var-row {
  writing-mode: vertical-rl;
  text-orientation: mixed;
  transform: rotate(180deg);
  width: auto;
  height: 100%;
  flex-direction: column;
}

/* LED 网格 */
.lamp-grid {
  display: grid;
  gap: 4px;
  flex: 1;
  align-content: center;
  justify-content: center;
}

/* LED 单元格 */
.lamp-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

/* LED 灯 */
.lamp-led {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: radial-gradient(circle at 35% 35%, #555, #222);
  border: 1px solid #333;
  position: relative;
  transition: all 0.1s ease-out;
}

.lamp-led.on {
  border-color: rgba(255, 255, 255, 0.3);
}

/* LED 高光 */
.lamp-highlight {
  position: absolute;
  top: 2px;
  left: 3px;
  width: 5px;
  height: 4px;
  background: rgba(255, 255, 255, 0.4);
  border-radius: 50%;
}

.lamp-led.on .lamp-highlight {
  background: rgba(255, 255, 255, 0.6);
}

/* 位索引 */
.bit-index {
  font-size: 8px;
  color: var(--text-muted);
  font-family: var(--font-mono);
}

/* 变量显示行 */
.var-row {
  display: flex;
  justify-content: space-between;
  width: 100%;
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
