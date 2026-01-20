<!--
  @file components/editor/HexEditor.vue
  @description 十六进制编辑器组件
  
  用于查看/编辑二进制文件 (.bin 等)
-->

<template>
  <div class="hex-editor">
    <!-- 工具栏 -->
    <div class="hex-toolbar">
      <span class="hex-label">Addr</span>
      <input 
        v-model="addressInput" 
        class="hex-addr-input" 
        placeholder="0x00000000"
        @keyup.enter="goToAddress"
      />
      <button class="hex-btn" @click="goToAddress">Go</button>
      
      <span class="hex-label">Cols</span>
      <select v-model="cols" class="hex-select">
        <option :value="8">8</option>
        <option :value="16">16</option>
        <option :value="32">32</option>
      </select>
      
      <div class="hex-spacer"></div>
      
      <span class="hex-status">{{ bytes.length }} bytes</span>
    </div>
    
    <!-- Hex 内容区 -->
    <div class="hex-content" ref="scrollRef">
      <div class="hex-grid">
        <!-- 地址列 -->
        <div class="hex-col hex-addr-col">
          <div 
            v-for="(_, i) in rowCount" 
            :key="i" 
            class="hex-row"
          >
            {{ formatAddress(i * cols) }}
          </div>
        </div>
        
        <!-- Hex 数据列 -->
        <div class="hex-col hex-data-col">
          <div 
            v-for="(row, rowIdx) in hexRows" 
            :key="rowIdx" 
            class="hex-row"
          >
            <span 
              v-for="(byte, byteIdx) in row" 
              :key="byteIdx"
              class="hex-byte"
              :class="{ selected: isSelected(rowIdx * cols + byteIdx) }"
              @click="selectByte(rowIdx * cols + byteIdx)"
            >
              {{ byte }}
            </span>
          </div>
        </div>
        
        <!-- ASCII 列 -->
        <div class="hex-col hex-ascii-col">
          <div 
            v-for="(row, rowIdx) in asciiRows" 
            :key="rowIdx" 
            class="hex-row"
          >
            <span 
              v-for="(char, charIdx) in row" 
              :key="charIdx"
              class="hex-char"
              :class="{ selected: isSelected(rowIdx * cols + charIdx) }"
              @click="selectByte(rowIdx * cols + charIdx)"
            >
              {{ char }}
            </span>
          </div>
        </div>
      </div>
    </div>
    
    <!-- 数据检查器 -->
    <div class="hex-inspector" v-if="selectedIndex >= 0">
      <div class="inspector-title">Inspector @ {{ formatAddress(selectedIndex) }}</div>
      <div class="inspector-row">
        <span class="inspector-label">uint8</span>
        <span class="inspector-value">{{ inspectorValues.uint8 }}</span>
      </div>
      <div class="inspector-row">
        <span class="inspector-label">int8</span>
        <span class="inspector-value">{{ inspectorValues.int8 }}</span>
      </div>
      <div class="inspector-row">
        <span class="inspector-label">uint16 LE</span>
        <span class="inspector-value">{{ inspectorValues.uint16le }}</span>
      </div>
      <div class="inspector-row">
        <span class="inspector-label">int32 LE</span>
        <span class="inspector-value">{{ inspectorValues.int32le }}</span>
      </div>
      <div class="inspector-row">
        <span class="inspector-label">float LE</span>
        <span class="inspector-value">{{ inspectorValues.floatle }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

// ============================================
// Props
// ============================================

const props = defineProps<{
  /** Base64 编码的二进制数据 */
  data: string
}>()

// ============================================
// 状态
// ============================================

const addressInput = ref('0x00000000')
const cols = ref(16)
const scrollRef = ref<HTMLDivElement | null>(null)
const selectedIndex = ref(-1)

// ============================================
// 计算属性
// ============================================

/** 将 Base64 解码为字节数组 */
const bytes = computed<number[]>(() => {
  if (!props.data) return []
  
  try {
    const binary = atob(props.data)
    return Array.from(binary).map(c => c.charCodeAt(0))
  } catch {
    return []
  }
})

/** 行数 */
const rowCount = computed(() => Math.ceil(bytes.value.length / cols.value))

/** Hex 行数据 */
const hexRows = computed(() => {
  const rows: string[][] = []
  const arr = bytes.value
  
  for (let i = 0; i < arr.length; i += cols.value) {
    const row: string[] = []
    for (let j = 0; j < cols.value && i + j < arr.length; j++) {
      const byteVal = arr[i + j]
      if (byteVal !== undefined) {
        row.push(byteVal.toString(16).padStart(2, '0').toUpperCase())
      }
    }
    rows.push(row)
  }
  
  return rows
})

/** ASCII 行数据 */
const asciiRows = computed(() => {
  const rows: string[][] = []
  const arr = bytes.value
  
  for (let i = 0; i < arr.length; i += cols.value) {
    const row: string[] = []
    for (let j = 0; j < cols.value && i + j < arr.length; j++) {
      const byteVal = arr[i + j]
      if (byteVal !== undefined) {
        // 可打印字符范围: 32-126
        row.push(byteVal >= 32 && byteVal <= 126 ? String.fromCharCode(byteVal) : '.')
      }
    }
    rows.push(row)
  }
  
  return rows
})

/** 数据检查器值 */
const inspectorValues = computed(() => {
  const idx = selectedIndex.value
  const arr = bytes.value
  
  if (idx < 0 || idx >= arr.length) {
    return { uint8: '-', int8: '-', uint16le: '-', int32le: '-', floatle: '-' }
  }
  
  const uint8Val = arr[idx]
  if (uint8Val === undefined) {
    return { uint8: '-', int8: '-', uint16le: '-', int32le: '-', floatle: '-' }
  }
  
  const int8 = uint8Val > 127 ? uint8Val - 256 : uint8Val
  
  // 16-bit
  let uint16le = '-'
  const b1 = arr[idx + 1]
  if (idx + 1 < arr.length && b1 !== undefined) {
    uint16le = String(uint8Val | (b1 << 8))
  }
  
  // 32-bit
  let int32le = '-'
  const b2 = arr[idx + 2]
  const b3 = arr[idx + 3]
  if (idx + 3 < arr.length && b1 !== undefined && b2 !== undefined && b3 !== undefined) {
    const u32 = uint8Val | (b1 << 8) | (b2 << 16) | (b3 << 24)
    int32le = String(u32 | 0) // Convert to signed
  }
  
  // Float
  let floatle = '-'
  if (idx + 3 < arr.length && b1 !== undefined && b2 !== undefined && b3 !== undefined) {
    const view = new DataView(new Uint8Array([uint8Val, b1, b2, b3]).buffer)
    floatle = view.getFloat32(0, true).toFixed(6)
  }
  
  return { uint8: String(uint8Val), int8: String(int8), uint16le, int32le, floatle }
})

// ============================================
// 方法
// ============================================

/**
 * 格式化地址
 */
function formatAddress(offset: number): string {
  return offset.toString(16).padStart(8, '0').toUpperCase()
}

/**
 * 跳转到指定地址
 */
function goToAddress() {
  const addr = parseInt(addressInput.value.replace('0x', ''), 16)
  if (isNaN(addr) || addr < 0) return
  
  const row = Math.floor(addr / cols.value)
  if (scrollRef.value) {
    const rowHeight = 22 // 估计行高
    scrollRef.value.scrollTop = row * rowHeight
  }
  
  selectedIndex.value = addr
}

/**
 * 选择字节
 */
function selectByte(index: number) {
  selectedIndex.value = index
}

/**
 * 检查是否选中
 */
function isSelected(index: number): boolean {
  return selectedIndex.value === index
}
</script>

<style scoped>
.hex-editor {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--panel-color);
  border-radius: var(--radius);
  overflow: hidden;
}

/* 工具栏 */
.hex-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
}

.hex-label {
  font-size: 12px;
  color: var(--text-muted);
}

.hex-addr-input {
  width: 100px;
  padding: 4px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-family: var(--font-mono);
  font-size: 12px;
}

.hex-select {
  padding: 4px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 12px;
}

.hex-btn {
  padding: 4px 12px;
  background: var(--primary);
  border-radius: var(--radius-sm);
  color: white;
  font-size: 12px;
}

.hex-spacer {
  flex: 1;
}

.hex-status {
  font-size: 12px;
  color: var(--text-muted);
}

/* 内容区 */
.hex-content {
  flex: 1;
  overflow: auto;
  padding: 8px;
}

.hex-grid {
  display: flex;
  gap: 16px;
  font-family: var(--font-mono);
  font-size: 13px;
  line-height: 22px;
}

.hex-col {
  display: flex;
  flex-direction: column;
}

.hex-addr-col {
  color: var(--text-muted);
  user-select: none;
}

.hex-data-col .hex-row {
  display: flex;
  gap: 6px;
}

.hex-byte {
  padding: 0 2px;
  border-radius: 2px;
  cursor: pointer;
}

.hex-byte:hover {
  background: rgba(79, 140, 255, 0.2);
}

.hex-byte.selected {
  background: var(--primary);
  color: white;
}

.hex-ascii-col {
  color: var(--text-muted);
}

.hex-ascii-col .hex-row {
  display: flex;
}

.hex-char {
  width: 10px;
  text-align: center;
  cursor: pointer;
}

.hex-char:hover {
  background: rgba(79, 140, 255, 0.2);
}

.hex-char.selected {
  background: var(--primary);
  color: white;
}

/* 检查器 */
.hex-inspector {
  padding: 12px;
  background: var(--panel-color-2);
  border-top: 1px solid var(--border-color);
}

.inspector-title {
  font-size: 12px;
  font-weight: 500;
  margin-bottom: 8px;
  color: var(--text-muted);
}

.inspector-row {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  line-height: 20px;
}

.inspector-label {
  color: var(--text-muted);
}

.inspector-value {
  font-family: var(--font-mono);
}
</style>
