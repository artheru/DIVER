<!--
  @file components/control/GaugeWidget.vue
  @description 只读变量显示控件（仪表/数显）
  
  显示样式：
  - number: 纯文本数值
  - text: 纯文本字符串
  - bar-h: 水平进度条
  - bar-v: 垂直进度条
  - gauge: 圆形仪表盘（半圆+指针）
-->

<template>
  <div class="gauge-widget" :class="[`style-${displayStyle}`]">
    <!-- 数值显示 (number / text) -->
    <template v-if="displayStyle === 'number' || displayStyle === 'text'">
      <div class="value-display">
        <span class="value">{{ displayValue }}</span>
        <span v-if="config.unit && displayStyle === 'number'" class="unit">{{ config.unit }}</span>
      </div>
    </template>
    
    <!-- 水平进度条 -->
    <template v-else-if="displayStyle === 'bar-h'">
      <div class="bar-container horizontal">
        <div class="bar-track">
          <div class="bar-fill" :style="{ width: `${fillPercent}%` }"></div>
        </div>
        <div class="bar-value">{{ displayValue }}<span v-if="config.unit" class="unit">{{ config.unit }}</span></div>
      </div>
    </template>
    
    <!-- 垂直进度条 -->
    <template v-else-if="displayStyle === 'bar-v'">
      <div class="bar-container vertical">
        <div class="bar-track">
          <div class="bar-fill" :style="{ height: `${fillPercent}%` }"></div>
        </div>
        <div class="bar-value">{{ displayValue }}<span v-if="config.unit" class="unit">{{ config.unit }}</span></div>
      </div>
    </template>
    
    <!-- 圆形仪表盘（半圆+指针） -->
    <template v-else-if="displayStyle === 'gauge'">
      <div class="gauge-container">
        <svg viewBox="0 0 100 65" class="gauge-svg">
          <!-- 背景弧 -->
          <path 
            class="gauge-bg" 
            d="M 10 50 A 40 40 0 0 1 90 50" 
            fill="none" 
            stroke-width="6"
            stroke-linecap="round"
          />
          <!-- 刻度标记和数字（5个刻度：0%, 25%, 50%, 75%, 100%） -->
          <g class="gauge-ticks">
            <!-- 0%: 180° (min) -->
            <line x1="10" y1="50" x2="16" y2="50" />
            <text x="4" y="58" class="tick-label">{{ formatTickValue(0) }}</text>
            <!-- 25%: 135° -->
            <line x1="21.72" y1="21.72" x2="26.96" y2="26.96" />
            <text x="14" y="16" class="tick-label">{{ formatTickValue(25) }}</text>
            <!-- 50%: 90° (center) -->
            <line x1="50" y1="10" x2="50" y2="16" />
            <text x="50" y="6" class="tick-label center">{{ formatTickValue(50) }}</text>
            <!-- 75%: 45° -->
            <line x1="78.28" y1="21.72" x2="73.04" y2="26.96" />
            <text x="86" y="16" class="tick-label right">{{ formatTickValue(75) }}</text>
            <!-- 100%: 0° (max) -->
            <line x1="90" y1="50" x2="84" y2="50" />
            <text x="96" y="58" class="tick-label right">{{ formatTickValue(100) }}</text>
          </g>
          <!-- 指针 -->
          <g 
            class="gauge-needle" 
            :class="{ 'out-of-range': isOutOfRange, 'severe': isSevereOutOfRange }"
            :style="{ transform: `rotate(${needleAngle}deg)`, transformOrigin: '50px 50px' }"
          >
            <g :class="{ 'needle-shake': isSevereOutOfRange }">
              <polygon points="50,14 47,50 53,50" class="needle-body" />
              <circle cx="50" cy="50" r="5" class="needle-center" />
            </g>
          </g>
        </svg>
        <div class="gauge-value" :class="{ 'out-of-range': isOutOfRange }">
          {{ displayValue }}<span v-if="config.unit" class="unit">{{ config.unit }}</span>
        </div>
      </div>
    </template>
    
    <!-- 变量显示（统一放底部） -->
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

interface GaugeConfig {
  variable?: string
  style?: 'number' | 'text' | 'bar-h' | 'bar-v' | 'gauge'
  min?: number
  max?: number
  unit?: string
  decimals?: number  // 小数位数
}

const props = withDefaults(defineProps<{
  config: GaugeConfig
}>(), {})

const runtimeStore = useRuntimeStore()
const { variableList } = storeToRefs(runtimeStore)

// 显示样式
const displayStyle = computed(() => props.config.style || 'number')

// 从 variableList 获取当前值
const currentVariable = computed(() => {
  if (!props.config.variable) return null
  return variableList.value.find(v => v.name === props.config.variable)
})

const rawValue = computed(() => {
  return currentVariable.value?.value ?? 0
})

// 范围
const min = computed(() => props.config.min ?? 0)
const max = computed(() => props.config.max ?? 100)

// 填充百分比（可以超出 0-100 范围）
const rawPercent = computed(() => {
  const range = max.value - min.value
  if (range === 0) return 0
  return ((Number(rawValue.value) - min.value) / range) * 100
})

// 限制在 0-100 范围内的百分比（用于进度条）
const fillPercent = computed(() => {
  return Math.max(0, Math.min(100, rawPercent.value))
})

// 是否超出范围
const isOutOfRange = computed(() => {
  return rawPercent.value < 0 || rawPercent.value > 100
})

// 是否严重超出范围（超过 ±11%，即指针到达极限）
const isSevereOutOfRange = computed(() => {
  return rawPercent.value < -11 || rawPercent.value > 111
})

// 仪表盘指针角度（允许超出范围，最大约 ±110 度）
const needleAngle = computed(() => {
  // 0% = -90度（左边），100% = 90度（右边）
  // 允许超出到 -110 ~ 110 度（对应 -11% ~ 111%）
  const clampedPercent = Math.max(-11, Math.min(111, rawPercent.value))
  return -90 + (clampedPercent / 100) * 180
})

// 格式化刻度值
function formatTickValue(percent: number): string {
  const range = max.value - min.value
  const val = min.value + (percent / 100) * range
  // 根据范围决定显示格式
  if (Number.isInteger(val) || Math.abs(range) >= 10) {
    return Math.round(val).toString()
  }
  return val.toFixed(1)
}

// 显示值
const displayValue = computed(() => {
  const val = rawValue.value
  
  // 文本模式直接显示
  if (displayStyle.value === 'text') {
    return String(val)
  }
  
  // 数值模式
  const numVal = Number(val)
  if (isNaN(numVal)) return String(val)
  
  const decimals = props.config.decimals ?? 2
  // 整数不显示小数
  if (Number.isInteger(numVal)) {
    return numVal.toString()
  }
  return numVal.toFixed(decimals)
})
</script>

<style scoped>
.gauge-widget {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 6px;
  height: 100%;
  box-sizing: border-box;
  user-select: none;
  gap: 2px;
}

/* 数值/文本显示 */
.value-display {
  display: flex;
  align-items: baseline;
  justify-content: center;
  gap: 4px;
  flex: 1;
}

.value {
  font-size: 20px;
  font-weight: 600;
  font-family: var(--font-mono);
  color: var(--primary);
}

.unit {
  font-size: 11px;
  color: var(--text-muted);
  margin-left: 2px;
}

/* 进度条容器 */
.bar-container {
  display: flex;
  flex: 1;
  width: 100%;
  gap: 6px;
}

.bar-container.horizontal {
  flex-direction: column;
  justify-content: center;
}

.bar-container.vertical {
  flex-direction: row;
  align-items: stretch;
}

/* 进度条轨道 */
.bar-track {
  position: relative;
  background: var(--body-color);
  border-radius: 4px;
  overflow: hidden;
}

.horizontal .bar-track {
  height: 12px;
  width: 100%;
}

.vertical .bar-track {
  width: 16px;
  flex: 1;
}

/* 进度条填充 */
.bar-fill {
  position: absolute;
  background: linear-gradient(90deg, var(--primary), var(--primary-hover));
  border-radius: 4px;
  transition: width 0.15s ease-out, height 0.15s ease-out;
}

.horizontal .bar-fill {
  left: 0;
  top: 0;
  height: 100%;
}

.vertical .bar-fill {
  left: 0;
  bottom: 0;
  width: 100%;
  background: linear-gradient(0deg, var(--primary), var(--primary-hover));
}

/* 进度条数值 */
.bar-value {
  font-size: 12px;
  font-family: var(--font-mono);
  color: var(--text-color);
  text-align: center;
  flex-shrink: 0;
}

.vertical .bar-value {
  writing-mode: vertical-rl;
  text-orientation: mixed;
  transform: rotate(180deg);
}

/* 仪表盘容器 */
.gauge-container {
  position: relative;
  width: 100%;
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-end;
  min-height: 50px;
}

.gauge-svg {
  width: 100%;
  max-width: 120px;
  height: auto;
}

.gauge-bg {
  stroke: var(--body-color);
}

/* 刻度线 */
.gauge-ticks line {
  stroke: var(--text-muted);
  stroke-width: 1.5;
  opacity: 0.5;
}

/* 刻度标签 */
.tick-label {
  font-size: 7px;
  fill: var(--text-muted);
  text-anchor: start;
}

.tick-label.center {
  text-anchor: middle;
}

.tick-label.right {
  text-anchor: end;
}

/* 指针 */
.gauge-needle {
  transition: transform 0.2s ease-out;
}

.needle-body {
  fill: var(--primary);
}

.needle-center {
  fill: var(--panel-color);
  stroke: var(--primary);
  stroke-width: 2;
}

/* 超出范围时的样式 */
.gauge-needle.out-of-range .needle-body {
  fill: var(--danger, #ff4444);
}

.gauge-needle.out-of-range .needle-center {
  stroke: var(--danger, #ff4444);
}

/* 严重超出范围时的抖动效果 */
.needle-shake {
  animation: needle-shake 0.08s ease-in-out infinite;
  transform-origin: 50px 50px;
}

@keyframes needle-shake {
  0%, 100% { transform: rotate(0deg); }
  25% { transform: rotate(4deg); }
  75% { transform: rotate(-4deg); }
}

.gauge-value {
  font-size: 12px;
  font-weight: 600;
  font-family: var(--font-mono);
  color: var(--primary);
  margin-top: -2px;
}

.gauge-value.out-of-range {
  color: var(--danger, #ff4444);
}

.gauge-value .unit {
  font-size: 10px;
}

/* 变量显示行（统一样式，与其他控件一致） */
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

/* 样式变体调整 */
.style-text .value {
  font-size: 14px;
  font-weight: 400;
  word-break: break-all;
}

.style-bar-v {
  flex-direction: row;
}

.style-bar-v .var-row {
  writing-mode: vertical-rl;
  text-orientation: mixed;
  transform: rotate(180deg);
  width: auto;
  height: 100%;
  flex-direction: column;
}

/* Gauge 模式下不显示底部的 var-row 中的值（已在仪表下方显示） */
.style-gauge .var-row .var-value {
  display: none;
}
</style>
