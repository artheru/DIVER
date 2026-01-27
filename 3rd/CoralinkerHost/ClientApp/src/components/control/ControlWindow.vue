<!--
  @file components/control/ControlWindow.vue
  @description 浮动遥控器窗口
  
  功能：
  - 可拖动、可调整大小的浮动窗口
  - X×Y 的 32x32 单元格网格布局
  - 支持放置摇杆、滑块、开关控件
  - 支持变量绑定
  - 布局锁定功能
-->

<template>
  <Teleport to="body">
    <div 
      v-if="visible"
      class="control-window"
      :style="windowStyle"
      :class="{ 'is-dragging': isDragging, 'is-resizing': isResizing }"
    >
      <!-- 标题栏 -->
      <div class="window-header" @mousedown="startDrag">
        <span class="window-title">🎮 Control Panel</span>
        <div class="window-actions">
          <!-- 锁定按钮 -->
          <button 
            class="action-btn"
            :class="{ active: isLocked }"
            @click.stop="toggleLock"
            :title="isLocked ? 'Unlock Layout' : 'Lock Layout'"
          >
            {{ isLocked ? '🔒' : '🔓' }}
          </button>
          <!-- 添加控件按钮 -->
          <button 
            class="action-btn"
            :disabled="isLocked"
            @click.stop="showAddWidgetMenu = !showAddWidgetMenu"
            title="Add Widget"
          >
            ➕
          </button>
          <!-- 网格设置按钮 -->
          <button 
            class="action-btn"
            :disabled="isLocked"
            @click.stop="showGridSettings = !showGridSettings"
            title="Grid Settings"
          >
            ⚙️
          </button>
          <!-- 关闭按钮 -->
          <button class="action-btn close" @click.stop="close" title="Close">×</button>
        </div>
      </div>

      <!-- 添加控件菜单 -->
      <div v-if="showAddWidgetMenu" class="dropdown-menu add-widget-menu">
        <button @click="addWidget('joystick')">
          <span class="menu-icon">✛</span>
          <span>Joystick (双轴)</span>
        </button>
        <button @click="addWidget('slider')">
          <span class="menu-icon">━</span>
          <span>Slider (单轴)</span>
        </button>
        <button @click="addWidget('switch')">
          <span class="menu-icon">◉</span>
          <span>Switch (开关)</span>
        </button>
      </div>

      <!-- 网格设置菜单 -->
      <div v-if="showGridSettings" class="dropdown-menu grid-settings-menu">
        <div class="setting-row">
          <label>Columns (X)</label>
          <input 
            type="number" 
            v-model.number="gridCols" 
            min="3" 
            max="20"
            @change="saveLayout"
          />
        </div>
        <div class="setting-row">
          <label>Rows (Y)</label>
          <input 
            type="number" 
            v-model.number="gridRows" 
            min="3" 
            max="20"
            @change="saveLayout"
          />
        </div>
        <div class="setting-info">
          Cell size: 32×32 px
        </div>
      </div>

      <!-- 网格画布 -->
      <div 
        class="grid-canvas" 
        ref="canvasRef"
        :style="canvasStyle"
        @click="closeMenus"
      >
        <!-- 网格背景 -->
        <div class="grid-background" :style="gridBackgroundStyle">
          <div 
            v-for="i in gridCols * gridRows" 
            :key="i" 
            class="grid-cell"
            :class="{ 'drop-target': isDropTarget(i - 1) }"
          ></div>
        </div>

        <!-- 控件容器 -->
        <div 
          v-for="widget in widgets" 
          :key="widget.id"
          class="widget-container"
          :class="{ 
            'is-selected': selectedWidgetId === widget.id,
            'is-dragging': draggingWidgetId === widget.id 
          }"
          :style="getWidgetStyle(widget)"
          @mousedown.stop="selectWidget(widget.id, $event)"
        >
          <!-- 控件内容 -->
          <div class="widget-content">
            <JoystickWidget 
              v-if="widget.type === 'joystick'"
              :config="widget.config"
              @change="(v) => handleJoystickChange(widget, v)"
            />
            <SliderWidget 
              v-else-if="widget.type === 'slider'"
              :config="widget.config"
              @change="(v) => handleSliderChange(widget, v)"
            />
            <SwitchWidget 
              v-else-if="widget.type === 'switch'"
              :config="widget.config"
              @change="(v) => handleSwitchChange(widget, v)"
            />
          </div>

          <!-- 控件工具栏（非锁定时显示） -->
          <div v-if="!isLocked && selectedWidgetId === widget.id" class="widget-toolbar">
            <button @click.stop="openWidgetConfig(widget)" title="Configure">⚙️</button>
            <button @click.stop="removeWidget(widget.id)" title="Delete">🗑️</button>
          </div>

          <!-- 调整大小手柄（非锁定时显示） -->
          <div 
            v-if="!isLocked && selectedWidgetId === widget.id"
            class="resize-handle"
            @mousedown.stop="startWidgetResize(widget, $event)"
          ></div>
        </div>
      </div>

      <!-- 控件配置对话框 -->
      <div v-if="showConfigDialog && editingWidget" class="config-dialog-overlay" @click="closeConfigDialog">
        <div class="config-dialog" @click.stop>
          <div class="config-dialog-header">
            <span>Configure {{ editingWidget.type }}</span>
            <button class="close-btn" @click="closeConfigDialog">×</button>
          </div>
          <div class="config-dialog-body">
            <!-- 通用配置 -->
            <div class="config-section">
              <h4>Grid Position</h4>
              <div class="config-row">
                <label>Column</label>
                <input type="number" v-model.number="editingWidget.gridX" :min="0" :max="gridCols - 1" />
              </div>
              <div class="config-row">
                <label>Row</label>
                <input type="number" v-model.number="editingWidget.gridY" :min="0" :max="gridRows - 1" />
              </div>
              <div class="config-row">
                <label>Width (cells)</label>
                <input type="number" v-model.number="editingWidget.gridW" :min="1" :max="gridCols" />
              </div>
              <div class="config-row">
                <label>Height (cells)</label>
                <input type="number" v-model.number="editingWidget.gridH" :min="1" :max="gridRows" />
              </div>
            </div>

            <!-- 摇杆配置 -->
            <template v-if="editingWidget.type === 'joystick'">
              <div class="config-section">
                <h4>Variable Binding</h4>
                <div class="config-row">
                  <label>X Axis</label>
                  <select v-model="editingWidget.config.variableX">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v" :value="v">{{ v }}</option>
                  </select>
                </div>
                <div class="config-row">
                  <label>Y Axis</label>
                  <select v-model="editingWidget.config.variableY">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v" :value="v">{{ v }}</option>
                  </select>
                </div>
              </div>
              <div class="config-section">
                <h4>Range</h4>
                <div class="config-row">
                  <label>Min X</label>
                  <input type="number" v-model.number="editingWidget.config.minX" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Max X</label>
                  <input type="number" v-model.number="editingWidget.config.maxX" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Min Y</label>
                  <input type="number" v-model.number="editingWidget.config.minY" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Max Y</label>
                  <input type="number" v-model.number="editingWidget.config.maxY" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Auto Return</label>
                  <input type="checkbox" v-model="editingWidget.config.autoReturn" />
                </div>
              </div>
            </template>

            <!-- 滑块配置 -->
            <template v-else-if="editingWidget.type === 'slider'">
              <div class="config-section">
                <h4>Variable Binding</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v" :value="v">{{ v }}</option>
                  </select>
                </div>
              </div>
              <div class="config-section">
                <h4>Settings</h4>
                <div class="config-row">
                  <label>Orientation</label>
                  <select v-model="editingWidget.config.orientation">
                    <option value="horizontal">Horizontal</option>
                    <option value="vertical">Vertical</option>
                  </select>
                </div>
                <div class="config-row">
                  <label>Min</label>
                  <input type="number" v-model.number="editingWidget.config.min" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Max</label>
                  <input type="number" v-model.number="editingWidget.config.max" step="0.1" />
                </div>
                <div class="config-row">
                  <label>Auto Return</label>
                  <input type="checkbox" v-model="editingWidget.config.autoReturn" />
                </div>
              </div>
            </template>

            <!-- 开关配置 -->
            <template v-else-if="editingWidget.type === 'switch'">
              <div class="config-section">
                <h4>Variable Binding</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v" :value="v">{{ v }}</option>
                  </select>
                </div>
              </div>
              <div class="config-section">
                <h4>Settings</h4>
                <div class="config-row">
                  <label>States</label>
                  <select v-model.number="editingWidget.config.states">
                    <option :value="2">2 (OFF/ON)</option>
                    <option :value="3">3 (-1/0/+1)</option>
                  </select>
                </div>
              </div>
            </template>
          </div>
          <div class="config-dialog-footer">
            <button class="btn" @click="closeConfigDialog">Cancel</button>
            <button class="btn primary" @click="saveWidgetConfig">Save</button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore } from '@/stores'
import JoystickWidget from './JoystickWidget.vue'
import SliderWidget from './SliderWidget.vue'
import SwitchWidget from './SwitchWidget.vue'

// ============================================
// Props 和 Emits
// ============================================

const props = defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
}>()

// ============================================
// Store
// ============================================

const runtimeStore = useRuntimeStore()

// ============================================
// 类型定义
// ============================================

interface GridWidget {
  id: string
  type: 'joystick' | 'slider' | 'switch'
  gridX: number  // 网格 X 位置
  gridY: number  // 网格 Y 位置
  gridW: number  // 占用网格宽度
  gridH: number  // 占用网格高度
  config: Record<string, unknown>
}

// ============================================
// 常量
// ============================================

const CELL_SIZE = 32
const STORAGE_KEY = 'controlWindowLayout'

// ============================================
// 窗口状态
// ============================================

const windowX = ref(100)
const windowY = ref(100)

// 拖动状态
const isDragging = ref(false)
const dragStartX = ref(0)
const dragStartY = ref(0)
const dragOffsetX = ref(0)
const dragOffsetY = ref(0)

// 不再需要窗口调整大小状态，窗口大小由网格决定
const isResizing = ref(false)

// ============================================
// 网格和控件状态
// ============================================

const gridCols = ref(8)
const gridRows = ref(8)
const isLocked = ref(false)
const widgets = ref<GridWidget[]>([])

// UI 状态
const showAddWidgetMenu = ref(false)
const showGridSettings = ref(false)
const showConfigDialog = ref(false)
const editingWidget = ref<GridWidget | null>(null)
const selectedWidgetId = ref<string | null>(null)
const draggingWidgetId = ref<string | null>(null)
const canvasRef = ref<HTMLDivElement | null>(null)

// 控件拖动
const widgetDragStartX = ref(0)
const widgetDragStartY = ref(0)
const widgetDragStartGridX = ref(0)
const widgetDragStartGridY = ref(0)

// 控件调整大小
const isWidgetResizing = ref(false)
const widgetResizeStartX = ref(0)
const widgetResizeStartY = ref(0)
const widgetResizeStartW = ref(0)
const widgetResizeStartH = ref(0)

// ============================================
// 计算属性
// ============================================

// 窗口大小根据网格自动计算
// header高度约44px，canvas margin 8px*2=16px，border 2px
const windowStyle = computed(() => ({
  left: `${windowX.value}px`,
  top: `${windowY.value}px`,
  width: `${gridCols.value * CELL_SIZE + 18}px`,  // margin 16px + border 2px
  height: `${gridRows.value * CELL_SIZE + 62}px`  // header 44px + margin 16px + border 2px
}))

const canvasStyle = computed(() => ({
  width: `${gridCols.value * CELL_SIZE}px`,
  height: `${gridRows.value * CELL_SIZE}px`
}))

const gridBackgroundStyle = computed(() => ({
  gridTemplateColumns: `repeat(${gridCols.value}, ${CELL_SIZE}px)`,
  gridTemplateRows: `repeat(${gridRows.value}, ${CELL_SIZE}px)`
}))

// 可控变量列表 - 同时从 controllableVarNames 和 variableList 获取
const { controllableVarNames, variableList } = storeToRefs(runtimeStore)

const controllableVarList = computed(() => {
  // 首先尝试从 controllableVarNames 获取
  const fromControllable = Array.from(controllableVarNames.value)
  if (fromControllable.length > 0) {
    return fromControllable
  }
  // 如果为空，则从 variableList 获取所有变量名
  return variableList.value.map(v => v.name)
})

// ============================================
// 窗口拖动
// ============================================

function startDrag(event: MouseEvent) {
  if ((event.target as HTMLElement).closest('.window-actions')) return
  
  isDragging.value = true
  dragStartX.value = event.clientX
  dragStartY.value = event.clientY
  dragOffsetX.value = windowX.value
  dragOffsetY.value = windowY.value
  
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

function onDrag(event: MouseEvent) {
  if (!isDragging.value) return
  
  const dx = event.clientX - dragStartX.value
  const dy = event.clientY - dragStartY.value
  
  windowX.value = dragOffsetX.value + dx
  windowY.value = dragOffsetY.value + dy
}

function stopDrag() {
  isDragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  saveLayout()
}

// ============================================
// 控件管理
// ============================================

function addWidget(type: 'joystick' | 'slider' | 'switch') {
  const id = `widget-${Date.now()}`
  
  // 默认配置
  const defaults: Record<string, Partial<GridWidget>> = {
    joystick: { 
      gridW: 4, gridH: 4, 
      config: { variableX: '', variableY: '', minX: -1, maxX: 1, minY: -1, maxY: 1, autoReturn: true } 
    },
    slider: { 
      gridW: 4, gridH: 2, 
      config: { variable: '', orientation: 'horizontal', min: 0, max: 1, autoReturn: false } 
    },
    switch: { 
      gridW: 2, gridH: 2, 
      config: { variable: '', states: 2 } 
    }
  }
  
  const def = defaults[type]
  
  // 找到空闲位置
  const pos = findEmptyPosition(def.gridW || 2, def.gridH || 2)
  
  widgets.value.push({
    id,
    type,
    gridX: pos.x,
    gridY: pos.y,
    gridW: def.gridW || 2,
    gridH: def.gridH || 2,
    config: { ...def.config } as Record<string, unknown>
  })
  
  showAddWidgetMenu.value = false
  saveLayout()
}

function findEmptyPosition(w: number, h: number): { x: number; y: number } {
  // 简单的放置算法：从左上角开始找空位
  for (let y = 0; y <= gridRows.value - h; y++) {
    for (let x = 0; x <= gridCols.value - w; x++) {
      if (!hasCollision(x, y, w, h, null)) {
        return { x, y }
      }
    }
  }
  return { x: 0, y: 0 }
}

function hasCollision(x: number, y: number, w: number, h: number, excludeId: string | null): boolean {
  for (const widget of widgets.value) {
    if (widget.id === excludeId) continue
    
    const wx = widget.gridX
    const wy = widget.gridY
    const ww = widget.gridW
    const wh = widget.gridH
    
    // 检查是否重叠
    if (x < wx + ww && x + w > wx && y < wy + wh && y + h > wy) {
      return true
    }
  }
  return false
}

function removeWidget(id: string) {
  const index = widgets.value.findIndex(w => w.id === id)
  if (index !== -1) {
    widgets.value.splice(index, 1)
    selectedWidgetId.value = null
    saveLayout()
  }
}

function selectWidget(id: string, event: MouseEvent) {
  selectedWidgetId.value = id
  
  if (!isLocked.value) {
    // 开始拖动控件
    const widget = widgets.value.find(w => w.id === id)
    if (widget) {
      draggingWidgetId.value = id
      widgetDragStartX.value = event.clientX
      widgetDragStartY.value = event.clientY
      widgetDragStartGridX.value = widget.gridX
      widgetDragStartGridY.value = widget.gridY
      
      document.addEventListener('mousemove', onWidgetDrag)
      document.addEventListener('mouseup', stopWidgetDrag)
    }
  }
}

function onWidgetDrag(event: MouseEvent) {
  if (!draggingWidgetId.value) return
  
  const widget = widgets.value.find(w => w.id === draggingWidgetId.value)
  if (!widget) return
  
  const dx = event.clientX - widgetDragStartX.value
  const dy = event.clientY - widgetDragStartY.value
  
  // 转换为网格坐标
  const newGridX = Math.round(widgetDragStartGridX.value + dx / CELL_SIZE)
  const newGridY = Math.round(widgetDragStartGridY.value + dy / CELL_SIZE)
  
  // 限制范围
  const clampedX = Math.max(0, Math.min(gridCols.value - widget.gridW, newGridX))
  const clampedY = Math.max(0, Math.min(gridRows.value - widget.gridH, newGridY))
  
  // 检查碰撞
  if (!hasCollision(clampedX, clampedY, widget.gridW, widget.gridH, widget.id)) {
    widget.gridX = clampedX
    widget.gridY = clampedY
  }
}

function stopWidgetDrag() {
  draggingWidgetId.value = null
  document.removeEventListener('mousemove', onWidgetDrag)
  document.removeEventListener('mouseup', stopWidgetDrag)
  saveLayout()
}

// ============================================
// 控件调整大小
// ============================================

function startWidgetResize(widget: GridWidget, event: MouseEvent) {
  isWidgetResizing.value = true
  widgetResizeStartX.value = event.clientX
  widgetResizeStartY.value = event.clientY
  widgetResizeStartW.value = widget.gridW
  widgetResizeStartH.value = widget.gridH
  
  document.addEventListener('mousemove', (e) => onWidgetResize(widget, e))
  document.addEventListener('mouseup', () => stopWidgetResize(widget))
}

function onWidgetResize(widget: GridWidget, event: MouseEvent) {
  if (!isWidgetResizing.value) return
  
  const dx = event.clientX - widgetResizeStartX.value
  const dy = event.clientY - widgetResizeStartY.value
  
  const newW = Math.round(widgetResizeStartW.value + dx / CELL_SIZE)
  const newH = Math.round(widgetResizeStartH.value + dy / CELL_SIZE)
  
  // 限制最小尺寸
  const clampedW = Math.max(1, Math.min(gridCols.value - widget.gridX, newW))
  const clampedH = Math.max(1, Math.min(gridRows.value - widget.gridY, newH))
  
  // 检查碰撞
  if (!hasCollision(widget.gridX, widget.gridY, clampedW, clampedH, widget.id)) {
    widget.gridW = clampedW
    widget.gridH = clampedH
  }
}

function stopWidgetResize(_widget: GridWidget) {
  isWidgetResizing.value = false
  document.removeEventListener('mousemove', onWidgetResize as EventListener)
  document.removeEventListener('mouseup', stopWidgetResize as EventListener)
  saveLayout()
}

// ============================================
// 控件配置
// ============================================

function openWidgetConfig(widget: GridWidget) {
  // 深拷贝
  editingWidget.value = JSON.parse(JSON.stringify(widget))
  showConfigDialog.value = true
}

function closeConfigDialog() {
  showConfigDialog.value = false
  editingWidget.value = null
}

function saveWidgetConfig() {
  if (!editingWidget.value) return
  
  const index = widgets.value.findIndex(w => w.id === editingWidget.value!.id)
  if (index !== -1) {
    widgets.value[index] = { ...editingWidget.value }
  }
  
  closeConfigDialog()
  saveLayout()
}

// ============================================
// 控件值变化处理
// ============================================

async function handleJoystickChange(widget: GridWidget, value: { x: number; y: number }) {
  const varX = widget.config.variableX as string
  const varY = widget.config.variableY as string
  
  try {
    if (varX) await runtimeStore.setVariable(varX, value.x)
    if (varY) await runtimeStore.setVariable(varY, value.y)
  } catch (error) {
    console.error('Failed to set joystick variables:', error)
  }
}

async function handleSliderChange(widget: GridWidget, value: number) {
  const varName = widget.config.variable as string
  if (!varName) return
  
  try {
    await runtimeStore.setVariable(varName, value)
  } catch (error) {
    console.error('Failed to set slider variable:', error)
  }
}

async function handleSwitchChange(widget: GridWidget, value: number) {
  const varName = widget.config.variable as string
  if (!varName) return
  
  try {
    await runtimeStore.setVariable(varName, value)
  } catch (error) {
    console.error('Failed to set switch variable:', error)
  }
}

// ============================================
// 辅助方法
// ============================================

function getWidgetStyle(widget: GridWidget) {
  return {
    left: `${widget.gridX * CELL_SIZE}px`,
    top: `${widget.gridY * CELL_SIZE}px`,
    width: `${widget.gridW * CELL_SIZE}px`,
    height: `${widget.gridH * CELL_SIZE}px`
  }
}

function isDropTarget(index: number): boolean {
  // 暂时不实现拖放高亮
  return false
}

function toggleLock() {
  isLocked.value = !isLocked.value
  selectedWidgetId.value = null
  saveLayout()
}

function close() {
  emit('update:visible', false)
}

function closeMenus() {
  showAddWidgetMenu.value = false
  showGridSettings.value = false
}

// ============================================
// 布局持久化
// ============================================

function saveLayout() {
  const layout = {
    windowX: windowX.value,
    windowY: windowY.value,
    gridCols: gridCols.value,
    gridRows: gridRows.value,
    isLocked: isLocked.value,
    widgets: widgets.value
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(layout))
}

function loadLayout() {
  const saved = localStorage.getItem(STORAGE_KEY)
  if (saved) {
    try {
      const layout = JSON.parse(saved)
      windowX.value = layout.windowX ?? 100
      windowY.value = layout.windowY ?? 100
      gridCols.value = layout.gridCols ?? 8
      gridRows.value = layout.gridRows ?? 8
      isLocked.value = layout.isLocked ?? false
      widgets.value = layout.widgets ?? []
    } catch {
      console.warn('Failed to load control window layout')
    }
  }
}

// ============================================
// 生命周期
// ============================================

onMounted(() => {
  loadLayout()
})

// 点击窗口外部取消选择
function onDocumentClick(event: MouseEvent) {
  const target = event.target as HTMLElement
  if (!target.closest('.control-window')) {
    selectedWidgetId.value = null
    closeMenus()
  }
}

watch(() => props.visible, (visible) => {
  if (visible) {
    document.addEventListener('click', onDocumentClick)
  } else {
    document.removeEventListener('click', onDocumentClick)
  }
})

onUnmounted(() => {
  document.removeEventListener('click', onDocumentClick)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
})
</script>

<style scoped>
.control-window {
  position: fixed;
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  display: flex;
  flex-direction: column;
  z-index: 1000;
  min-width: 300px;
  min-height: 200px;
}

.control-window.is-dragging {
  opacity: 0.9;
  cursor: grabbing;
}

/* 标题栏 */
.window-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  cursor: grab;
  user-select: none;
}

.window-header:active {
  cursor: grabbing;
}

.window-title {
  font-weight: 500;
  font-size: 14px;
}

.window-actions {
  display: flex;
  gap: 4px;
}

.action-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border-radius: var(--radius-sm);
  font-size: 14px;
  transition: background var(--transition-fast);
}

.action-btn:hover {
  background: rgba(255, 255, 255, 0.1);
}

.action-btn.active {
  background: var(--primary);
  color: white;
}

.action-btn.close:hover {
  background: var(--danger);
  color: white;
}

.action-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* 下拉菜单 */
.dropdown-menu {
  position: absolute;
  top: 44px;
  right: 8px;
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  box-shadow: var(--shadow-lg);
  z-index: 10;
  min-width: 160px;
}

.dropdown-menu button {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 10px 12px;
  background: transparent;
  text-align: left;
  font-size: 13px;
  color: var(--text-color);
  transition: background var(--transition-fast);
}

.dropdown-menu button:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-color);
}

.menu-icon {
  width: 20px;
  text-align: center;
}

/* 网格设置菜单 */
.grid-settings-menu {
  padding: 12px;
}

.setting-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.setting-row label {
  font-size: 13px;
  color: var(--text-muted);
}

.setting-row input {
  width: 60px;
  padding: 4px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: center;
}

.setting-info {
  font-size: 11px;
  color: var(--text-muted);
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--border-color);
}

/* 网格画布 */
.grid-canvas {
  position: relative;
  overflow: hidden;
  margin: 8px;
  background: var(--body-color);
  border-radius: var(--radius);
}

.grid-background {
  display: grid;
  position: absolute;
  inset: 0;
}

.grid-cell {
  border: 1px dashed rgba(255, 255, 255, 0.08);
  box-sizing: border-box;
}

.grid-cell.drop-target {
  background: rgba(79, 140, 255, 0.1);
}

/* 控件容器 */
.widget-container {
  position: absolute;
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  overflow: hidden;
  transition: box-shadow var(--transition-fast);
}

.widget-container.is-selected {
  border-color: var(--primary);
  box-shadow: 0 0 0 2px rgba(79, 140, 255, 0.3);
}

.widget-container.is-dragging {
  opacity: 0.8;
  z-index: 10;
}

.widget-content {
  width: 100%;
  height: 100%;
  overflow: hidden;
}

/* 控件工具栏 */
.widget-toolbar {
  position: absolute;
  top: 4px;
  right: 4px;
  display: flex;
  gap: 2px;
  z-index: 5;
}

.widget-toolbar button {
  width: 24px;
  height: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.6);
  border-radius: var(--radius-sm);
  font-size: 12px;
  transition: background var(--transition-fast);
}

.widget-toolbar button:hover {
  background: var(--primary);
}

/* 调整大小手柄 */
.resize-handle {
  position: absolute;
  right: 0;
  bottom: 0;
  width: 16px;
  height: 16px;
  cursor: se-resize;
  background: linear-gradient(135deg, transparent 50%, var(--primary) 50%);
  border-radius: 0 0 var(--radius) 0;
}

/* 配置对话框 */
.config-dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1100;
}

.config-dialog {
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  width: 400px;
  max-height: 80vh;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.config-dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-color);
  font-weight: 500;
}

.close-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border-radius: var(--radius-sm);
  font-size: 18px;
  color: var(--text-muted);
}

.close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-color);
}

.config-dialog-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
}

.config-section {
  margin-bottom: 16px;
}

.config-section h4 {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-muted);
  margin: 0 0 8px 0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.config-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.config-row label {
  font-size: 13px;
  color: var(--text-muted);
}

.config-row input[type="number"],
.config-row input[type="text"],
.config-row select {
  width: 140px;
  padding: 6px 10px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
}

.config-row input[type="checkbox"] {
  width: 18px;
  height: 18px;
}

.config-dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--border-color);
}

.btn {
  padding: 8px 16px;
  background: var(--panel-color-2);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  color: var(--text-color);
  font-size: 13px;
  cursor: pointer;
  transition: all var(--transition-fast);
}

.btn:hover {
  background: rgba(255, 255, 255, 0.1);
}

.btn.primary {
  background: var(--primary);
  border-color: var(--primary);
  color: white;
}

.btn.primary:hover {
  background: var(--primary-hover);
}
</style>
