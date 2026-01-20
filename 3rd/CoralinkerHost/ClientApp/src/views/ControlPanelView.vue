<!--
  @file views/ControlPanelView.vue
  @description 控制面板页面
  
  独立的遥控器界面，包含：
  - 滑块控件
  - 摇杆控件
  - 开关控件
-->

<template>
  <div class="control-panel-view">
    <!-- 头部工具栏 -->
    <header class="toolbar">
      <h1>Control Panel</h1>
      <div class="toolbar-actions">
        <n-button @click="showAddWidgetDialog = true">+ Add Widget</n-button>
        <n-button @click="saveLayout">Save Layout</n-button>
        <a href="/" class="back-link">← Back to Editor</a>
      </div>
    </header>
    
    <!-- 控件画布 -->
    <main class="widget-canvas" ref="canvasRef">
      <div 
        v-for="widget in widgets" 
        :key="widget.id"
        class="widget-wrapper"
        :style="getWidgetStyle(widget)"
      >
        <!-- 滑块控件 -->
        <SliderWidget 
          v-if="widget.type === 'slider'"
          :config="widget.config"
          @change="(v) => handleWidgetChange(widget, v)"
        />
        
        <!-- 摇杆控件 -->
        <JoystickWidget 
          v-else-if="widget.type === 'joystick'"
          :config="widget.config"
          @change="(v) => handleJoystickChange(widget, v)"
        />
        
        <!-- 开关控件 -->
        <SwitchWidget 
          v-else-if="widget.type === 'switch'"
          :config="widget.config"
          @change="(v) => handleWidgetChange(widget, v)"
        />
        
        <!-- 控件工具栏 -->
        <div class="widget-toolbar">
          <button @click="configureWidget(widget)" title="Configure">⚙</button>
          <button @click="removeWidget(widget.id)" title="Remove">×</button>
        </div>
      </div>
      
      <!-- 空状态 -->
      <div v-if="widgets.length === 0" class="empty-state">
        <p>No widgets yet</p>
        <n-button @click="showAddWidgetDialog = true">Add Your First Widget</n-button>
      </div>
    </main>
    
    <!-- 添加控件对话框 -->
    <n-modal v-model:show="showAddWidgetDialog">
      <n-card title="Add Widget" style="width: 400px">
        <div class="widget-type-list">
          <button 
            v-for="wt in widgetTypes" 
            :key="wt.type"
            class="widget-type-btn"
            @click="addWidget(wt.type)"
          >
            <span class="widget-icon">{{ wt.icon }}</span>
            <span class="widget-name">{{ wt.name }}</span>
            <span class="widget-desc">{{ wt.desc }}</span>
          </button>
        </div>
      </n-card>
    </n-modal>
    
    <!-- 配置控件对话框 -->
    <n-modal v-model:show="showConfigDialog">
      <n-card title="Configure Widget" style="width: 500px">
        <WidgetConfigForm 
          v-if="editingWidget"
          :widget="editingWidget"
          @save="saveWidgetConfig"
          @cancel="showConfigDialog = false"
        />
      </n-card>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NButton, NModal, NCard } from 'naive-ui'
import { useRuntimeStore } from '@/stores'
import { useSignalR } from '@/composables'

// 子组件
import SliderWidget from '@/components/control/SliderWidget.vue'
import JoystickWidget from '@/components/control/JoystickWidget.vue'
import SwitchWidget from '@/components/control/SwitchWidget.vue'
import WidgetConfigForm from '@/components/control/WidgetConfigForm.vue'

// ============================================
// 类型定义
// ============================================

interface WidgetConfig {
  id: string
  type: 'slider' | 'joystick' | 'switch'
  x: number
  y: number
  width: number
  height: number
  config: Record<string, unknown>
}

// ============================================
// Store 和 SignalR
// ============================================

const runtimeStore = useRuntimeStore()

// 初始化 SignalR 连接
useSignalR()

// ============================================
// 本地状态
// ============================================

const widgets = ref<WidgetConfig[]>([])
const showAddWidgetDialog = ref(false)
const showConfigDialog = ref(false)
const editingWidget = ref<WidgetConfig | null>(null)

// 可用的控件类型
const widgetTypes = [
  { type: 'slider', name: 'Slider', icon: '━', desc: 'Single axis control' },
  { type: 'joystick', name: 'Joystick', icon: '✛', desc: 'Dual axis control' },
  { type: 'switch', name: 'Switch', icon: '◉', desc: 'On/Off toggle' }
]

// ============================================
// 方法
// ============================================

/**
 * 获取控件样式
 */
function getWidgetStyle(widget: WidgetConfig) {
  return {
    left: `${widget.x}px`,
    top: `${widget.y}px`,
    width: `${widget.width}px`,
    height: `${widget.height}px`
  }
}

/**
 * 添加控件
 */
function addWidget(type: string) {
  const id = `widget-${Date.now()}`
  
  // 默认配置
  const defaults: Record<string, Partial<WidgetConfig>> = {
    slider: { width: 300, height: 80, config: { orientation: 'horizontal', min: 0, max: 1, autoReturn: false, variable: '' } },
    joystick: { width: 200, height: 200, config: { autoReturn: true, minX: -1, maxX: 1, minY: -1, maxY: 1, variableX: '', variableY: '' } },
    switch: { width: 100, height: 100, config: { states: 2, variable: '' } }
  }
  
  const def = defaults[type] || { width: 150, height: 150, config: {} }
  
  widgets.value.push({
    id,
    type: type as WidgetConfig['type'],
    x: 100 + Math.random() * 200,
    y: 100 + Math.random() * 200,
    width: def.width || 150,
    height: def.height || 150,
    config: def.config || {}
  })
  
  showAddWidgetDialog.value = false
  saveLayout()
}

/**
 * 移除控件
 */
function removeWidget(id: string) {
  const index = widgets.value.findIndex(w => w.id === id)
  if (index !== -1) {
    widgets.value.splice(index, 1)
    saveLayout()
  }
}

/**
 * 配置控件
 */
function configureWidget(widget: WidgetConfig) {
  editingWidget.value = widget
  showConfigDialog.value = true
}

/**
 * 保存控件配置
 */
function saveWidgetConfig(config: Record<string, unknown>) {
  if (editingWidget.value) {
    editingWidget.value.config = config
    showConfigDialog.value = false
    saveLayout()
  }
}

/**
 * 处理控件值变化
 */
async function handleWidgetChange(widget: WidgetConfig, value: number) {
  const varName = widget.config.variable as string
  if (!varName) return
  
  try {
    await runtimeStore.setVariable(varName, value)
  } catch (error) {
    console.error('Failed to set variable:', error)
  }
}

/**
 * 处理摇杆值变化
 */
async function handleJoystickChange(widget: WidgetConfig, value: { x: number; y: number }) {
  const varX = widget.config.variableX as string
  const varY = widget.config.variableY as string
  
  try {
    if (varX) await runtimeStore.setVariable(varX, value.x)
    if (varY) await runtimeStore.setVariable(varY, value.y)
  } catch (error) {
    console.error('Failed to set joystick variables:', error)
  }
}

/**
 * 保存布局到 localStorage
 */
function saveLayout() {
  localStorage.setItem('controlPanelLayout', JSON.stringify(widgets.value))
}

/**
 * 加载布局
 */
function loadLayout() {
  const saved = localStorage.getItem('controlPanelLayout')
  if (saved) {
    try {
      widgets.value = JSON.parse(saved)
    } catch {
      widgets.value = []
    }
  }
}

// ============================================
// 生命周期
// ============================================

onMounted(() => {
  loadLayout()
})
</script>

<style scoped>
.control-panel-view {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: var(--body-color);
}

/* 工具栏 */
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 20px;
  background: var(--panel-color);
  border-bottom: 1px solid var(--border-color);
}

.toolbar h1 {
  font-size: 18px;
  font-weight: 600;
  margin: 0;
}

.toolbar-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.back-link {
  color: var(--text-muted);
  font-size: 13px;
}

.back-link:hover {
  color: var(--primary);
}

/* 控件画布 */
.widget-canvas {
  flex: 1;
  position: relative;
  overflow: hidden;
}

/* 控件容器 */
.widget-wrapper {
  position: absolute;
  background: var(--panel-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  padding: 10px;
}

.widget-toolbar {
  position: absolute;
  top: 4px;
  right: 4px;
  display: flex;
  gap: 4px;
  opacity: 0;
  transition: opacity var(--transition-fast);
}

.widget-wrapper:hover .widget-toolbar {
  opacity: 1;
}

.widget-toolbar button {
  width: 24px;
  height: 24px;
  border-radius: var(--radius-sm);
  background: rgba(0, 0, 0, 0.5);
  color: var(--text-muted);
  font-size: 14px;
}

.widget-toolbar button:hover {
  background: var(--primary);
  color: white;
}

/* 空状态 */
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  gap: 16px;
  color: var(--text-muted);
}

/* 控件类型列表 */
.widget-type-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.widget-type-btn {
  display: grid;
  grid-template-columns: 40px 1fr;
  grid-template-rows: auto auto;
  gap: 0 12px;
  padding: 12px;
  background: var(--panel-color-2);
  border: 1px solid var(--border-color);
  border-radius: var(--radius);
  text-align: left;
  transition: all var(--transition-fast);
}

.widget-type-btn:hover {
  background: var(--card-color);
  border-color: var(--primary);
}

.widget-icon {
  grid-row: span 2;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
}

.widget-name {
  font-weight: 500;
  color: var(--text-color);
}

.widget-desc {
  font-size: 12px;
  color: var(--text-muted);
}
</style>
