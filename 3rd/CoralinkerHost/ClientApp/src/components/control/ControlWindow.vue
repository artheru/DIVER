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
  <component :is="embedded ? 'div' : Teleport" :to="embedded ? undefined : 'body'">
    <div 
      v-if="visible"
      class="control-window"
      :class="{ 
        'is-dragging': isDragging, 
        'is-resizing': isResizing,
        'is-embedded': embedded,
        'is-readonly': readonly
      }"
      :style="embedded ? embeddedStyle : windowStyle"
    >
      <!-- 标题栏（嵌入模式下不显示） -->
      <div v-if="!embedded" class="window-header" @mousedown="startDrag">
        <span class="window-title">🎮 Control Panel</span>
        <div class="window-actions">
          <!-- 编辑按钮（只读模式下隐藏） -->
          <template v-if="!readonly">
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
          </template>
          <!-- 关闭按钮 -->
          <button class="action-btn close" @click.stop="close" title="Close">×</button>
        </div>
      </div>

      <!-- 添加控件菜单（只读模式下不显示） -->
      <div v-if="showAddWidgetMenu && !readonly" class="dropdown-menu add-widget-menu">
        <div class="menu-section-title">可控控件</div>
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
        <div class="menu-divider"></div>
        <div class="menu-section-title">只读显示</div>
        <button @click="addWidget('gauge')">
          <span class="menu-icon">📊</span>
          <span>Gauge (数显/仪表)</span>
        </button>
        <button @click="addWidget('lamp')">
          <span class="menu-icon">💡</span>
          <span>Lamp (LED指示灯)</span>
        </button>
      </div>

      <!-- 网格设置菜单（只读模式下不显示） -->
      <div v-if="showGridSettings && !readonly" class="dropdown-menu grid-settings-menu">
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
              :type-id-x="getVarTypeId(widget.config.variableX as string)"
              :type-id-y="getVarTypeId(widget.config.variableY as string)"
              @change="(v) => handleJoystickChange(widget, v)"
            />
            <SliderWidget 
              v-else-if="widget.type === 'slider'"
              :config="widget.config"
              :type-id="getVarTypeId(widget.config.variable as string)"
              @change="(v) => handleSliderChange(widget, v)"
            />
            <SwitchWidget 
              v-else-if="widget.type === 'switch'"
              :config="widget.config"
              @change="(v) => handleSwitchChange(widget, v)"
            />
            <GaugeWidget 
              v-else-if="widget.type === 'gauge'"
              :config="widget.config"
            />
            <LampWidget 
              v-else-if="widget.type === 'lamp'"
              :config="widget.config"
            />
          </div>

          <!-- 控件工具栏（非锁定且非只读时显示） -->
          <div v-if="!isLocked && !readonly && selectedWidgetId === widget.id" class="widget-toolbar">
            <button @click.stop="openWidgetConfig(widget)" title="Configure">⚙️</button>
            <button @click.stop="removeWidget(widget.id)" title="Delete">🗑️</button>
          </div>

          <!-- 调整大小手柄（非锁定且非只读时显示） -->
          <div 
            v-if="!isLocked && !readonly && selectedWidgetId === widget.id"
            class="resize-handle"
            @mousedown.stop="startWidgetResize(widget, $event)"
          ></div>
        </div>
      </div>

      <!-- 控件配置对话框（只读模式下不显示） -->
      <div v-if="showConfigDialog && editingWidget && !readonly" class="config-dialog-overlay" @click="closeConfigDialog">
        <div class="config-dialog" @click.stop>
          <div class="config-dialog-header">
            <span>Configure {{ editingWidget.type }}</span>
            <button class="close-btn" @click="closeConfigDialog">×</button>
          </div>
          <div class="config-dialog-body">
            <!-- Position -->
            <div class="config-section">
              <h4>Position</h4>
              <div class="config-row-inline">
                <div class="inline-field">
                  <label>Col</label>
                  <input type="number" v-model.number="editingWidget.gridX" :min="0" :max="gridCols - 1" />
                </div>
                <div class="inline-field">
                  <label>Row</label>
                  <input type="number" v-model.number="editingWidget.gridY" :min="0" :max="gridRows - 1" />
                </div>
                <div class="inline-field">
                  <label>W</label>
                  <input type="number" v-model.number="editingWidget.gridW" :min="1" :max="gridCols" />
                </div>
                <div class="inline-field">
                  <label>H</label>
                  <input type="number" v-model.number="editingWidget.gridH" :min="1" :max="gridRows" />
                </div>
              </div>
            </div>

            <!-- 摇杆配置 -->
            <template v-if="editingWidget.type === 'joystick'">
              <!-- X Axis -->
              <div class="config-section">
                <h4>X Axis (Left/Right)</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variableX" @change="onJoystickVarXChange">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v.name" :value="v.name">
                      {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
                <div class="config-row-inline">
                  <div class="range-field">
                    <label>Range</label>
                    <input type="number" v-model.number="editingWidget.config.minX" step="any" />
                    <span class="range-sep">→</span>
                    <input type="number" v-model.number="editingWidget.config.maxX" step="any" />
                  </div>
                  <div class="toggle-field">
                    <label>Auto Return</label>
                    <button 
                      class="toggle-switch" 
                      :class="{ on: editingWidget.config.autoReturnX }"
                      @click="editingWidget.config.autoReturnX = !editingWidget.config.autoReturnX"
                    >
                      <span class="toggle-slider"></span>
                    </button>
                  </div>
                </div>
                <div v-if="editingWidget.config.autoReturnX" class="config-row">
                  <label>Return To</label>
                  <input type="number" v-model.number="editingWidget.config.returnToX" min="0" max="100" class="small-input" />
                  <span class="unit">%</span>
                </div>
              </div>
              
              <!-- Y Axis -->
              <div class="config-section">
                <h4>Y Axis (Up/Down)</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variableY" @change="onJoystickVarYChange">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v.name" :value="v.name">
                      {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
                <div class="config-row-inline">
                  <div class="range-field">
                    <label>Range</label>
                    <input type="number" v-model.number="editingWidget.config.minY" step="any" />
                    <span class="range-sep">→</span>
                    <input type="number" v-model.number="editingWidget.config.maxY" step="any" />
                  </div>
                  <div class="toggle-field">
                    <label>Auto Return</label>
                    <button 
                      class="toggle-switch" 
                      :class="{ on: editingWidget.config.autoReturnY }"
                      @click="editingWidget.config.autoReturnY = !editingWidget.config.autoReturnY"
                    >
                      <span class="toggle-slider"></span>
                    </button>
                  </div>
                </div>
                <div v-if="editingWidget.config.autoReturnY" class="config-row">
                  <label>Return To</label>
                  <input type="number" v-model.number="editingWidget.config.returnToY" min="0" max="100" class="small-input" />
                  <span class="unit">%</span>
                </div>
              </div>
              
              <!-- Keyboard -->
              <div class="config-section">
                <h4>Keyboard</h4>
                <div class="config-row">
                  <label>Preset</label>
                  <select @change="applyJoystickPreset($event)">
                    <option value="">-- Select --</option>
                    <option value="wasd">WASD</option>
                    <option value="ijkl">IJKL</option>
                    <option value="arrows">Arrow Keys</option>
                  </select>
                </div>
                <div class="key-grid">
                  <div class="key-row">
                    <input type="text" class="key-input" :value="editingWidget.config.keyUp" @keydown.prevent="captureKey($event, 'keyUp')" placeholder="↑" readonly />
                  </div>
                  <div class="key-row">
                    <input type="text" class="key-input" :value="editingWidget.config.keyLeft" @keydown.prevent="captureKey($event, 'keyLeft')" placeholder="←" readonly />
                    <span class="key-center">⊕</span>
                    <input type="text" class="key-input" :value="editingWidget.config.keyRight" @keydown.prevent="captureKey($event, 'keyRight')" placeholder="→" readonly />
                  </div>
                  <div class="key-row">
                    <input type="text" class="key-input" :value="editingWidget.config.keyDown" @keydown.prevent="captureKey($event, 'keyDown')" placeholder="↓" readonly />
                  </div>
                </div>
                <div class="config-row-inline speed-row">
                  <div class="speed-field">
                    <label>Move</label>
                    <input type="number" v-model.number="editingWidget.config.moveSpeed" min="10" max="500" step="10" placeholder="100" />
                    <span class="speed-unit">%/s</span>
                  </div>
                  <div class="speed-field">
                    <label>Return</label>
                    <input type="number" v-model.number="editingWidget.config.returnSpeed" min="10" max="500" step="10" placeholder="200" />
                    <span class="speed-unit">%/s</span>
                  </div>
                </div>
              </div>
            </template>

            <!-- 滑块配置 -->
            <template v-else-if="editingWidget.type === 'slider'">
              <!-- Variable & Settings -->
              <div class="config-section">
                <h4>Variable &amp; Settings</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable" @change="onSliderVarChange">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v.name" :value="v.name">
                      {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
                <div class="config-row-inline">
                  <div class="range-field">
                    <label>Range</label>
                    <input type="number" v-model.number="editingWidget.config.min" step="any" />
                    <span class="range-sep">→</span>
                    <input type="number" v-model.number="editingWidget.config.max" step="any" />
                  </div>
                  <div class="toggle-field">
                    <label>Auto Return</label>
                    <button 
                      class="toggle-switch" 
                      :class="{ on: editingWidget.config.autoReturn }"
                      @click="editingWidget.config.autoReturn = !editingWidget.config.autoReturn"
                    >
                      <span class="toggle-slider"></span>
                    </button>
                  </div>
                </div>
                <div v-if="editingWidget.config.autoReturn" class="config-row">
                  <label>Return To</label>
                  <input type="number" v-model.number="editingWidget.config.returnTo" min="0" max="100" class="small-input" />
                  <span class="unit">%</span>
                </div>
                <div class="config-row">
                  <label>Orientation</label>
                  <select v-model="editingWidget.config.orientation">
                    <option value="horizontal">Horizontal</option>
                    <option value="vertical">Vertical</option>
                  </select>
                </div>
              </div>
              
              <!-- Keyboard -->
              <div class="config-section">
                <h4>Keyboard</h4>
                <div class="config-row">
                  <label>Preset</label>
                  <select @change="applySliderPreset($event)">
                    <option value="">-- Select --</option>
                    <option value="zx">Z / X</option>
                    <option value="nm">N / M</option>
                    <option value="rf">R / F</option>
                    <option value="tg">T / G</option>
                    <option value="yh">Y / H</option>
                  </select>
                </div>
                <div class="key-pair">
                  <div class="key-item">
                    <span class="key-label">−</span>
                    <input type="text" class="key-input" :value="editingWidget.config.keyDecrease" @keydown.prevent="captureKey($event, 'keyDecrease')" placeholder="-" readonly />
                  </div>
                  <div class="key-item">
                    <span class="key-label">+</span>
                    <input type="text" class="key-input" :value="editingWidget.config.keyIncrease" @keydown.prevent="captureKey($event, 'keyIncrease')" placeholder="+" readonly />
                  </div>
                </div>
                <div class="config-row-inline speed-row">
                  <div class="speed-field">
                    <label>Move</label>
                    <input type="number" v-model.number="editingWidget.config.moveSpeed" min="10" max="500" step="10" placeholder="100" />
                    <span class="speed-unit">%/s</span>
                  </div>
                  <div class="speed-field">
                    <label>Return</label>
                    <input type="number" v-model.number="editingWidget.config.returnSpeed" min="10" max="500" step="10" placeholder="200" />
                    <span class="speed-unit">%/s</span>
                  </div>
                </div>
              </div>
            </template>

            <!-- 开关配置 -->
            <template v-else-if="editingWidget.type === 'switch'">
              <!-- Variable & Settings -->
              <div class="config-section">
                <h4>Variable &amp; Settings</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable">
                    <option value="">-- None --</option>
                    <option v-for="v in controllableVarList" :key="v.name" :value="v.name">
                      {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
                <div class="config-row">
                  <label>States</label>
                  <select v-model.number="editingWidget.config.states">
                    <option :value="2">2 (OFF/ON)</option>
                    <option :value="3">3 (-1/0/+1)</option>
                  </select>
                </div>
              </div>
              
              <!-- Keyboard -->
              <div class="config-section">
                <h4>Keyboard</h4>
                <div class="config-row">
                  <label>Preset</label>
                  <select @change="applySwitchPreset($event)">
                    <option value="">-- Select --</option>
                    <option value="c">C</option>
                    <option value="v">V</option>
                    <option value="b">B</option>
                  </select>
                </div>
                <div class="key-single">
                  <span class="key-label">Toggle</span>
                  <input type="text" class="key-input large" :value="editingWidget.config.keyToggle" @keydown.prevent="captureKey($event, 'keyToggle')" placeholder="Press key..." readonly />
                </div>
              </div>
            </template>

            <!-- Gauge 配置 -->
            <template v-else-if="editingWidget.type === 'gauge'">
              <div class="config-section">
                <h4>Variable</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable" class="var-select-mixed">
                    <option value="">-- None --</option>
                    <option 
                      v-for="v in allVarList" 
                      :key="v.name" 
                      :value="v.name"
                      :class="v.isControllable ? 'var-controllable' : 'var-readonly'"
                    >
                      {{ v.isControllable ? '✎' : '👁' }} {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
              </div>
              
              <div class="config-section">
                <h4>Display Style</h4>
                <div class="config-row">
                  <label>Style</label>
                  <select v-model="editingWidget.config.style">
                    <option value="number">Number (数值)</option>
                    <option value="text">Text (文本)</option>
                    <option value="bar-h">Bar Horizontal (水平进度条)</option>
                    <option value="bar-v">Bar Vertical (垂直进度条)</option>
                    <option value="gauge">Gauge (仪表盘)</option>
                  </select>
                </div>
              </div>
              
              <div class="config-section" v-if="editingWidget.config.style !== 'text'">
                <h4>Range &amp; Format</h4>
                <div class="config-row-inline">
                  <div class="range-field">
                    <label>Range</label>
                    <input type="number" v-model.number="editingWidget.config.min" step="any" />
                    <span class="range-sep">→</span>
                    <input type="number" v-model.number="editingWidget.config.max" step="any" />
                  </div>
                </div>
                <div class="config-row">
                  <label>Unit</label>
                  <input type="text" v-model="editingWidget.config.unit" placeholder="e.g. %, °C, rpm" class="text-input" />
                </div>
                <div class="config-row">
                  <label>Decimals</label>
                  <input type="number" v-model.number="editingWidget.config.decimals" min="0" max="6" class="small-input" />
                </div>
              </div>
            </template>

            <!-- Lamp 配置 -->
            <template v-else-if="editingWidget.type === 'lamp'">
              <div class="config-section">
                <h4>Variable</h4>
                <div class="config-row">
                  <label>Variable</label>
                  <select v-model="editingWidget.config.variable" class="var-select-mixed">
                    <option value="">-- None --</option>
                    <option 
                      v-for="v in allVarList" 
                      :key="v.name" 
                      :value="v.name"
                      :class="v.isControllable ? 'var-controllable' : 'var-readonly'"
                    >
                      {{ v.isControllable ? '✎' : '👁' }} {{ v.name }} ({{ v.type }})
                    </option>
                  </select>
                </div>
              </div>
              
              <div class="config-section">
                <h4>LED Settings</h4>
                <div class="config-row">
                  <label>Bits</label>
                  <input type="number" v-model.number="editingWidget.config.bits" min="1" max="32" class="small-input" />
                  <span class="hint">(1-32)</span>
                </div>
                <div class="config-row">
                  <label>Layout</label>
                  <select v-model="editingWidget.config.layout">
                    <option value="horizontal">Horizontal (横排)</option>
                    <option value="vertical">Vertical (竖排)</option>
                  </select>
                </div>
                <div class="config-row">
                  <label>Color</label>
                  <div class="color-picker">
                    <input type="color" v-model="editingWidget.config.color" class="color-input" />
                    <span class="color-value">{{ editingWidget.config.color }}</span>
                  </div>
                </div>
                <div class="config-row">
                  <label>Show Index</label>
                  <button 
                    class="toggle-switch" 
                    :class="{ on: editingWidget.config.showBitIndex }"
                    @click="editingWidget.config.showBitIndex = !editingWidget.config.showBitIndex"
                  >
                    <span class="toggle-slider"></span>
                  </button>
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
  </component>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, Teleport } from 'vue'
import { storeToRefs } from 'pinia'
import { useRuntimeStore, useProjectStore } from '@/stores'
import JoystickWidget from './JoystickWidget.vue'
import SliderWidget from './SliderWidget.vue'
import SwitchWidget from './SwitchWidget.vue'
import GaugeWidget from './GaugeWidget.vue'
import LampWidget from './LampWidget.vue'

// ============================================
// Props 和 Emits
// ============================================

const props = withDefaults(defineProps<{
  visible: boolean
  readonly?: boolean   // 只读模式：只能操控，不能修改布局和参数
  embedded?: boolean   // 嵌入模式：不使用 Teleport，不显示关闭按钮
}>(), {
  readonly: false,
  embedded: false
})

const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
}>()

// ============================================
// Store
// ============================================

const runtimeStore = useRuntimeStore()
const projectStore = useProjectStore()

// ============================================
// 类型定义
// ============================================

interface GridWidget {
  id: string
  type: 'joystick' | 'slider' | 'switch' | 'gauge' | 'lamp'
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

const gridCols = ref(12)
const gridRows = ref(12)
const isLocked = ref(false)
const widgets = ref<GridWidget[]>([])

// UI 状态
const showAddWidgetMenu = ref(false)
const showGridSettings = ref(false)
const showConfigDialog = ref(false)
const editingWidget = ref<GridWidget | null>(null)
const selectedWidgetId = ref<string | null>(null)
const draggingWidgetId = ref<string | null>(null)
// 控件拖动
const widgetDragStartX = ref(0)
const widgetDragStartY = ref(0)
const widgetDragStartGridX = ref(0)
const widgetDragStartGridY = ref(0)

// 控件调整大小
const isWidgetResizing = ref(false)
const resizingWidgetId = ref<string | null>(null)
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

// 嵌入模式下的样式（无定位，自动填充）
const embeddedStyle = computed(() => ({
  width: `${gridCols.value * CELL_SIZE + 18}px`,
  height: `${gridRows.value * CELL_SIZE + 18}px`  // 嵌入模式无 header
}))

const canvasStyle = computed(() => ({
  width: `${gridCols.value * CELL_SIZE}px`,
  height: `${gridRows.value * CELL_SIZE}px`
}))

const gridBackgroundStyle = computed(() => ({
  gridTemplateColumns: `repeat(${gridCols.value}, ${CELL_SIZE}px)`,
  gridTemplateRows: `repeat(${gridRows.value}, ${CELL_SIZE}px)`
}))

// 可控变量列表 - 返回带有类型信息的对象
const { fieldMetas, variableList } = storeToRefs(runtimeStore)

// 支持绑定的类型 ID（数值类型，不包括 string 等）
// 0=bool, 1=byte, 2=sbyte, 3=char, 4=i16, 5=u16, 6=i32, 7=u32, 8=f32
// 注意：只有 controllableVarNames 中的变量才能被控制
const BINDABLE_TYPE_IDS = [0, 1, 2, 3, 4, 5, 6, 7, 8]

// 整数类型 ID（用于决定显示格式）
const INTEGER_TYPE_IDS = [0, 1, 2, 3, 4, 5, 6, 7]

interface BindableVariable {
  name: string
  type: string
  typeId: number
  isInteger: boolean
}

const controllableVarList = computed<BindableVariable[]>(() => {
  // 从 fieldMetas 获取变量元信息，过滤：
  // 1. 必须是可控制的变量（非 LowerIO）
  // 2. 必须是可绑定的数值类型
  // 注意：fieldMetas 不需要 Start 就能获取，在页面加载时获取
  return fieldMetas.value
    .filter(f => 
      !f.isLowerIO && 
      BINDABLE_TYPE_IDS.includes(f.typeId)
    )
    .map(f => ({
      name: f.name,
      type: f.type,
      typeId: f.typeId,
      isInteger: INTEGER_TYPE_IDS.includes(f.typeId)
    }))
})

// 所有变量列表（用于只读 Gauge 控件，混合显示可控和只读）
interface AllVariable extends BindableVariable {
  isControllable: boolean  // 是否可控（用于颜色区分）
}

const allVarList = computed<AllVariable[]>(() => {
  // 从 fieldMetas 获取所有可绑定类型的变量
  return fieldMetas.value
    .filter(f => BINDABLE_TYPE_IDS.includes(f.typeId))
    .map(f => ({
      name: f.name,
      type: f.type,
      typeId: f.typeId,
      isInteger: INTEGER_TYPE_IDS.includes(f.typeId),
      isControllable: !f.isLowerIO  // LowerIO = 只读
    }))
})

// 根据变量名获取类型信息
function getVarTypeId(varName: string): number {
  const v = variableList.value.find(v => v.name === varName)
  return v?.typeId ?? 8 // 默认 f32
}

function isIntegerType(typeId: number): boolean {
  return INTEGER_TYPE_IDS.includes(typeId)
}

/** 获取类型对应的默认范围 */
function getDefaultRange(typeId: number): { min: number; max: number } {
  if (isIntegerType(typeId)) {
    return { min: -100, max: 100 }
  }
  return { min: -1, max: 1 }
}

/** 当 Joystick X 变量改变时更新默认范围 */
function onJoystickVarXChange() {
  if (!editingWidget.value) return
  const typeId = getVarTypeId(String(editingWidget.value.config.variableX || ''))
  const range = getDefaultRange(typeId)
  editingWidget.value.config.minX = range.min
  editingWidget.value.config.maxX = range.max
}

/** 当 Joystick Y 变量改变时更新默认范围 */
function onJoystickVarYChange() {
  if (!editingWidget.value) return
  const typeId = getVarTypeId(String(editingWidget.value.config.variableY || ''))
  const range = getDefaultRange(typeId)
  editingWidget.value.config.minY = range.min
  editingWidget.value.config.maxY = range.max
}

/** 当 Slider 变量改变时更新默认范围 */
function onSliderVarChange() {
  if (!editingWidget.value) return
  const typeId = getVarTypeId(String(editingWidget.value.config.variable || ''))
  const range = getDefaultRange(typeId)
  editingWidget.value.config.min = range.min
  editingWidget.value.config.max = range.max
}

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

function addWidget(type: 'joystick' | 'slider' | 'switch' | 'gauge' | 'lamp') {
  const id = `widget-${Date.now()}`
  
  // 默认配置（float: -1~1, int: -100~100）
  const defaults: Record<string, Partial<GridWidget>> = {
    joystick: { 
      gridW: 5, gridH: 7, 
      config: { 
        variableX: '', variableY: '', 
        minX: -1, maxX: 1, minY: -1, maxY: 1, 
        autoReturnX: true, autoReturnY: true,
        keyUp: '', keyDown: '', keyLeft: '', keyRight: '',
        moveSpeed: 100, returnSpeed: 200
      } 
    },
    slider: { 
      gridW: 5, gridH: 2, 
      config: { 
        variable: '', orientation: 'horizontal', 
        min: -1, max: 1, autoReturn: false,
        keyDecrease: '', keyIncrease: '',
        moveSpeed: 100, returnSpeed: 200
      } 
    },
    switch: { 
      gridW: 3, gridH: 2, 
      config: { variable: '', states: 2, keyToggle: '' } 
    },
    gauge: {
      gridW: 3, gridH: 2,
      config: { 
        variable: '', 
        style: 'number',  // number | text | bar-h | bar-v | gauge
        min: 0, max: 100, 
        unit: '',
        decimals: 2
      }
    },
    lamp: {
      gridW: 3, gridH: 2,
      config: {
        variable: '',
        bits: 1,           // 显示的位数（1-32）
        layout: 'horizontal',  // horizontal | vertical
        color: '#00ff00',  // LED 颜色
        showBitIndex: true // 默认开启（单个 bit 时自动隐藏）
      }
    }
  }
  
  const def = defaults[type]!
  
  // 找到空闲位置
  const pos = findEmptyPosition(def.gridW ?? 2, def.gridH ?? 2)
  
  widgets.value.push({
    id,
    type,
    gridX: pos.x,
    gridY: pos.y,
    gridW: def.gridW ?? 2,
    gridH: def.gridH ?? 2,
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
  
  // 只读模式或锁定状态下不允许拖动
  if (!isLocked.value && !props.readonly) {
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
  event.stopPropagation()
  isWidgetResizing.value = true
  resizingWidgetId.value = widget.id
  widgetResizeStartX.value = event.clientX
  widgetResizeStartY.value = event.clientY
  widgetResizeStartW.value = widget.gridW
  widgetResizeStartH.value = widget.gridH
  
  document.addEventListener('mousemove', onWidgetResizeMove)
  document.addEventListener('mouseup', stopWidgetResizeMove)
}

function onWidgetResizeMove(event: MouseEvent) {
  if (!isWidgetResizing.value || !resizingWidgetId.value) return
  
  const widget = widgets.value.find(w => w.id === resizingWidgetId.value)
  if (!widget) return
  
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

function stopWidgetResizeMove() {
  isWidgetResizing.value = false
  resizingWidgetId.value = null
  document.removeEventListener('mousemove', onWidgetResizeMove)
  document.removeEventListener('mouseup', stopWidgetResizeMove)
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
// 键盘绑定配置
// ============================================

/** 捕获按键 */
function captureKey(event: KeyboardEvent, field: string) {
  if (!editingWidget.value) return
  editingWidget.value.config[field] = event.key
}

/** 应用摇杆预设 */
function applyJoystickPreset(event: Event) {
  if (!editingWidget.value) return
  const preset = (event.target as HTMLSelectElement).value
  
  const presets: Record<string, { up: string; down: string; left: string; right: string }> = {
    wasd: { up: 'w', down: 's', left: 'a', right: 'd' },
    ijkl: { up: 'i', down: 'k', left: 'j', right: 'l' },
    arrows: { up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight' }
  }
  
  const p = presets[preset]
  if (p) {
    editingWidget.value.config.keyUp = p.up
    editingWidget.value.config.keyDown = p.down
    editingWidget.value.config.keyLeft = p.left
    editingWidget.value.config.keyRight = p.right
  }
  
  // 重置 select
  ;(event.target as HTMLSelectElement).value = ''
}

/** 应用滑块预设 */
function applySliderPreset(event: Event) {
  if (!editingWidget.value) return
  const preset = (event.target as HTMLSelectElement).value
  
  const presets: Record<string, { dec: string; inc: string }> = {
    zx: { dec: 'z', inc: 'x' },
    nm: { dec: 'n', inc: 'm' },
    rf: { dec: 'r', inc: 'f' },
    tg: { dec: 't', inc: 'g' },
    yh: { dec: 'y', inc: 'h' }
  }
  
  const p = presets[preset]
  if (p) {
    editingWidget.value.config.keyDecrease = p.dec
    editingWidget.value.config.keyIncrease = p.inc
  }
  
  ;(event.target as HTMLSelectElement).value = ''
}

/** 应用开关预设 */
function applySwitchPreset(event: Event) {
  if (!editingWidget.value) return
  const preset = (event.target as HTMLSelectElement).value
  
  if (preset) {
    editingWidget.value.config.keyToggle = preset
  }
  
  ;(event.target as HTMLSelectElement).value = ''
}

// ============================================
// 控件值变化处理（节流）
// ============================================

// 变量发送节流：100ms 间隔（每秒最多 10 次）
const SEND_THROTTLE_MS = 100
const pendingVars = new Map<string, unknown>()
let sendTimer: ReturnType<typeof setTimeout> | null = null

function scheduleSend() {
  if (sendTimer !== null) return
  sendTimer = setTimeout(flushPendingVars, SEND_THROTTLE_MS)
}

async function flushPendingVars() {
  sendTimer = null
  if (pendingVars.size === 0) return
  
  const toSend = new Map(pendingVars)
  pendingVars.clear()
  
  for (const [name, value] of toSend) {
    try {
      await runtimeStore.setVariable(name, value)
    } catch (error) {
      console.error(`Failed to set variable ${name}:`, error)
    }
  }
}

function handleJoystickChange(widget: GridWidget, value: { x?: number; y?: number }) {
  const varX = widget.config.variableX as string
  const varY = widget.config.variableY as string
  
  // 只发送变化的轴（undefined 表示没变化）
  if (varX && value.x !== undefined) pendingVars.set(varX, value.x)
  if (varY && value.y !== undefined) pendingVars.set(varY, value.y)
  if (pendingVars.size > 0) scheduleSend()
}

function handleSliderChange(widget: GridWidget, value: number) {
  const varName = widget.config.variable as string
  if (!varName) return
  
  pendingVars.set(varName, value)
  scheduleSend()
}

async function handleSwitchChange(widget: GridWidget, value: number) {
  // Switch 不需要节流，立即发送
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

function isDropTarget(_index: number): boolean {
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
  projectStore.updateControlLayout({
    windowX: windowX.value,
    windowY: windowY.value,
    gridCols: gridCols.value,
    gridRows: gridRows.value,
    isLocked: isLocked.value,
    widgets: widgets.value
  })
}

function loadLayout() {
  const layout = projectStore.controlLayout
  windowX.value = layout.windowX ?? 100
  windowY.value = layout.windowY ?? 100
  gridCols.value = layout.gridCols ?? 12
  gridRows.value = layout.gridRows ?? 12
  isLocked.value = layout.isLocked ?? false
  widgets.value = layout.widgets ?? []
}

// ============================================
// 生命周期
// ============================================

onMounted(async () => {
  loadLayout()
  // 刷新字段元信息（用于遥控器绑定）
  await runtimeStore.refreshFieldMetas()
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

/* 嵌入模式：不浮动，无阴影 */
.control-window.is-embedded {
  position: relative;
  box-shadow: none;
  border-radius: var(--radius);
  z-index: auto;
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
  width: 480px;
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
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border-color);
}

.config-section:last-child {
  border-bottom: none;
  margin-bottom: 0;
}

.config-section h4 {
  font-size: 11px;
  font-weight: 600;
  color: var(--primary);
  margin: 0 0 10px 0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

/* 基本行布局 */
.config-row {
  display: flex;
  align-items: center;
  margin-bottom: 8px;
}

.config-row label {
  font-size: 13px;
  color: var(--text-muted);
  width: 80px;
  flex-shrink: 0;
}

.config-row select {
  flex: 1;
  padding: 6px 10px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
}

/* 内联多字段行 */
.config-row-inline {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 8px;
}

.inline-field {
  display: flex;
  align-items: center;
  gap: 6px;
}

.inline-field label {
  font-size: 12px;
  color: var(--text-muted);
}

.inline-field input[type="number"] {
  width: 55px;
  padding: 6px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: center;
}

.inline-field input[type="number"]::-webkit-inner-spin-button {
  margin-left: 6px;
}

/* Range 字段 */
.range-field {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
}

.range-field label {
  font-size: 12px;
  color: var(--text-muted);
  width: 50px;
  flex-shrink: 0;
}

.range-field input[type="number"] {
  width: 90px;
  padding: 6px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: right;
}

.range-field input[type="number"]::-webkit-inner-spin-button {
  margin-left: 6px;
}

.range-sep {
  color: var(--text-muted);
  font-size: 12px;
}

/* Toggle 开关字段 */
.toggle-field {
  display: flex;
  align-items: center;
  gap: 8px;
}

.toggle-field label {
  font-size: 12px;
  color: var(--text-muted);
  white-space: nowrap;
}

.toggle-switch {
  position: relative;
  width: 40px;
  height: 22px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: 11px;
  cursor: pointer;
  transition: all 0.2s;
}

.toggle-switch .toggle-slider {
  position: absolute;
  top: 2px;
  left: 2px;
  width: 16px;
  height: 16px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.2s;
}

.toggle-switch.on {
  background: var(--primary);
  border-color: var(--primary);
}

.toggle-switch.on .toggle-slider {
  left: 20px;
  background: white;
}

/* Speed 字段 */
.speed-row {
  justify-content: flex-end;
  margin-top: 8px;
}

.speed-row {
  justify-content: space-between;
}

.speed-field {
  display: flex;
  align-items: center;
  gap: 8px;
}

.speed-field label {
  font-size: 13px;
  color: var(--text-color);
  min-width: 45px;
}

.speed-field input[type="number"] {
  width: 70px;
  padding: 6px 8px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: right;
}

.speed-field input[type="number"]::-webkit-inner-spin-button {
  margin-left: 6px;
}

.speed-unit {
  font-size: 12px;
  color: var(--text-muted);
}

/* 按键绑定网格（摇杆用） */
.key-grid {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 10px;
  background: var(--body-color);
  border-radius: var(--radius);
  margin-bottom: 8px;
}

.key-grid .key-row {
  display: flex;
  gap: 4px;
  align-items: center;
}

.key-input {
  width: 38px;
  height: 30px;
  text-align: center;
  background: var(--panel-color-2);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  font-weight: 600;
  font-family: var(--font-mono);
  cursor: pointer;
  transition: all 0.15s;
}

.key-input:focus {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px rgba(79, 140, 255, 0.3);
}

.key-input::placeholder {
  color: var(--text-muted);
  font-size: 14px;
}

.key-center {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: 14px;
}

/* 按键对（滑块用） */
.key-pair {
  display: flex;
  justify-content: center;
  gap: 32px;
  padding: 10px;
  background: var(--body-color);
  border-radius: var(--radius);
  margin-bottom: 8px;
}

.key-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.key-label {
  font-size: 16px;
  color: var(--text-muted);
  font-weight: 600;
}

/* 单个按键（开关用） */
.key-single {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 10px;
  background: var(--body-color);
  border-radius: var(--radius);
  margin-bottom: 8px;
}

.key-input.large {
  width: 70px;
  height: 36px;
  font-size: 14px;
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

/* 添加控件菜单样式 */
.menu-section-title {
  padding: 6px 12px 4px;
  font-size: 10px;
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.menu-divider {
  height: 1px;
  background: var(--border-color);
  margin: 4px 0;
}

/* 变量选择器样式 */
.var-select-mixed option.var-controllable {
  background: rgba(79, 140, 255, 0.15);
}

.var-select-mixed option.var-readonly {
  background: rgba(255, 193, 7, 0.15);
}

/* 文本输入框 */
.text-input {
  flex: 1;
  padding: 6px 10px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
}

/* 小输入框 */
.small-input {
  width: 60px;
  padding: 6px 10px;
  background: var(--body-color);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-color);
  font-size: 13px;
  text-align: center;
}

/* 提示文字 */
.hint {
  font-size: 11px;
  color: var(--text-muted);
  margin-left: 8px;
}

/* 颜色选择器 */
.color-picker {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.color-input {
  width: 40px;
  height: 28px;
  padding: 0;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  cursor: pointer;
  background: transparent;
}

.color-input::-webkit-color-swatch-wrapper {
  padding: 2px;
}

.color-input::-webkit-color-swatch {
  border-radius: 2px;
  border: none;
}

.color-value {
  font-size: 12px;
  font-family: var(--font-mono);
  color: var(--text-muted);
  text-transform: uppercase;
}
</style>
