<!--
  @file components/graph/IOView.vue
  @description 数字 IO 状态显示组件（LED 样式）
  
  - 显示输入（绿色）和输出（红色）LED
  - 每 4 个一组，16 个一行
  - 显示位索引
-->

<template>
  <div class="io-view" v-if="hasIO">
    <div class="section-title">DIGITAL I/O</div>
    
    <div class="io-container">
      <!-- 输入 -->
      <div class="io-group" v-if="inputCount > 0">
        <span class="io-label">IN</span>
        <div class="led-rows">
          <div class="led-row" v-for="(row, rowIdx) in inputRows" :key="`in-row-${rowIdx}`">
            <template v-for="(group, groupIdx) in row" :key="`in-group-${rowIdx}-${groupIdx}`">
              <div class="led-group" :class="{ 'group-gap': groupIdx > 0 }">
                <div class="led-cell" v-for="bitIdx in group" :key="`in-${bitIdx}`">
                  <div 
                    class="led input" 
                    :class="{ on: isInputActive(bitIdx) }"
                    :title="`DI${bitIdx}: ${isInputActive(bitIdx) ? 'ON' : 'OFF'}`"
                  >
                    <div class="led-highlight"></div>
                  </div>
                  <span class="led-index">{{ bitIdx }}</span>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
      
      <!-- 输出 -->
      <div class="io-group" v-if="outputCount > 0">
        <span class="io-label">OUT</span>
        <div class="led-rows">
          <div class="led-row" v-for="(row, rowIdx) in outputRows" :key="`out-row-${rowIdx}`">
            <template v-for="(group, groupIdx) in row" :key="`out-group-${rowIdx}-${groupIdx}`">
              <div class="led-group" :class="{ 'group-gap': groupIdx > 0 }">
                <div class="led-cell" v-for="bitIdx in group" :key="`out-${bitIdx}`">
                  <div 
                    class="led output" 
                    :class="{ on: isOutputActive(bitIdx) }"
                    :title="`DO${bitIdx}: ${isOutputActive(bitIdx) ? 'ON' : 'OFF'}`"
                  >
                    <div class="led-highlight"></div>
                  </div>
                  <span class="led-index">{{ bitIdx }}</span>
                </div>
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  /** 数字输入值（位掩码） */
  digitalInputs: number
  /** 数字输出值（位掩码） */
  digitalOutputs: number
  /** 数字输入数量 */
  inputCount: number
  /** 数字输出数量 */
  outputCount: number
}>()

// 是否有 IO
const hasIO = computed(() => props.inputCount > 0 || props.outputCount > 0)

// 将 LED 分成行和组：每行 16 个，每 4 个一组
// 返回格式: number[][][] - [行][组][位索引]
function buildRows(count: number): number[][][] {
  const rows: number[][][] = []
  const LEDS_PER_ROW = 16
  const LEDS_PER_GROUP = 4
  
  for (let i = 0; i < count; i += LEDS_PER_ROW) {
    const row: number[][] = []
    const rowEnd = Math.min(i + LEDS_PER_ROW, count)
    
    for (let j = i; j < rowEnd; j += LEDS_PER_GROUP) {
      const group: number[] = []
      const groupEnd = Math.min(j + LEDS_PER_GROUP, rowEnd)
      
      for (let k = j; k < groupEnd; k++) {
        group.push(k)
      }
      row.push(group)
    }
    rows.push(row)
  }
  return rows
}

// 输入行
const inputRows = computed(() => buildRows(props.inputCount))

// 输出行
const outputRows = computed(() => buildRows(props.outputCount))

// 检查输入是否激活
function isInputActive(bitIndex: number): boolean {
  return (props.digitalInputs & (1 << bitIndex)) !== 0
}

// 检查输出是否激活
function isOutputActive(bitIndex: number): boolean {
  return (props.digitalOutputs & (1 << bitIndex)) !== 0
}
</script>

<style scoped>
.io-view {
  margin: 0 8px 8px 8px;
}

.section-title {
  font-size: 10px;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 6px 4px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  margin-bottom: 6px;
}

.io-container {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 6px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 4px;
}

.io-group {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

.io-label {
  font-size: 10px;
  font-weight: 600;
  color: #64748b;
  min-width: 28px;
  padding-top: 2px;
}

.led-rows {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.led-row {
  display: flex;
  align-items: flex-start;
}

.led-group {
  display: flex;
  gap: 2px;
}

.led-group.group-gap {
  margin-left: 8px;
}

.led-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1px;
  width: 16px;
}

/* LED 灯 */
.led {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  position: relative;
  transition: all 0.1s ease-out;
}

/* 输入 LED - 绿色 */
.led.input {
  background: radial-gradient(circle at 35% 35%, #333, #1a1a1a);
  border: 1px solid #333;
}

.led.input.on {
  background: radial-gradient(circle at 35% 35%, #6ee7b7, #22c55e);
  border-color: rgba(34, 197, 94, 0.5);
  box-shadow: 0 0 6px 1px rgba(34, 197, 94, 0.6);
}

/* 输出 LED - 红色 */
.led.output {
  background: radial-gradient(circle at 35% 35%, #333, #1a1a1a);
  border: 1px solid #333;
}

.led.output.on {
  background: radial-gradient(circle at 35% 35%, #fca5a5, #ef4444);
  border-color: rgba(239, 68, 68, 0.5);
  box-shadow: 0 0 6px 1px rgba(239, 68, 68, 0.6);
}

/* LED 高光 */
.led-highlight {
  position: absolute;
  top: 2px;
  left: 2px;
  width: 3px;
  height: 2px;
  background: rgba(255, 255, 255, 0.3);
  border-radius: 50%;
}

.led.on .led-highlight {
  background: rgba(255, 255, 255, 0.5);
}

/* 位索引 */
.led-index {
  font-size: 7px;
  color: #64748b;
  font-family: var(--font-mono);
  line-height: 1;
}
</style>
