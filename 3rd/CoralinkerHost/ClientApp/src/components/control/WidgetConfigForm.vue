<!--
  @file components/control/WidgetConfigForm.vue
  @description 控件配置表单
  
  根据控件类型动态显示配置选项
-->

<template>
  <div class="widget-config-form">
    <!-- 通用配置 -->
    <div class="config-section">
      <h4>General</h4>
      
      <div class="config-row">
        <label>Position X</label>
        <n-input-number v-model:value="localConfig.x" :min="0" />
      </div>
      
      <div class="config-row">
        <label>Position Y</label>
        <n-input-number v-model:value="localConfig.y" :min="0" />
      </div>
      
      <div class="config-row">
        <label>Width</label>
        <n-input-number v-model:value="localConfig.width" :min="50" />
      </div>
      
      <div class="config-row">
        <label>Height</label>
        <n-input-number v-model:value="localConfig.height" :min="50" />
      </div>
    </div>
    
    <!-- 滑块配置 -->
    <template v-if="widget.type === 'slider'">
      <div class="config-section">
        <h4>Slider Settings</h4>
        
        <div class="config-row">
          <label>Variable</label>
          <n-input v-model:value="sliderConfig.variable" placeholder="varName" />
        </div>
        
        <div class="config-row">
          <label>Orientation</label>
          <n-select 
            v-model:value="sliderConfig.orientation"
            :options="[
              { label: 'Horizontal', value: 'horizontal' },
              { label: 'Vertical', value: 'vertical' }
            ]"
          />
        </div>
        
        <div class="config-row">
          <label>Min Value</label>
          <n-input-number v-model:value="sliderConfig.min" />
        </div>
        
        <div class="config-row">
          <label>Max Value</label>
          <n-input-number v-model:value="sliderConfig.max" />
        </div>
        
        <div class="config-row">
          <label>Auto Return</label>
          <n-switch v-model:value="sliderConfig.autoReturn" />
        </div>
        
        <div class="config-row">
          <label>Logarithmic</label>
          <n-switch v-model:value="sliderConfig.logarithmic" />
        </div>
      </div>
    </template>
    
    <!-- 摇杆配置 -->
    <template v-else-if="widget.type === 'joystick'">
      <div class="config-section">
        <h4>Joystick Settings</h4>
        
        <div class="config-row">
          <label>Variable X</label>
          <n-input v-model:value="joystickConfig.variableX" placeholder="X axis var" />
        </div>
        
        <div class="config-row">
          <label>Variable Y</label>
          <n-input v-model:value="joystickConfig.variableY" placeholder="Y axis var" />
        </div>
        
        <div class="config-row">
          <label>Min X</label>
          <n-input-number v-model:value="joystickConfig.minX" />
        </div>
        
        <div class="config-row">
          <label>Max X</label>
          <n-input-number v-model:value="joystickConfig.maxX" />
        </div>
        
        <div class="config-row">
          <label>Min Y</label>
          <n-input-number v-model:value="joystickConfig.minY" />
        </div>
        
        <div class="config-row">
          <label>Max Y</label>
          <n-input-number v-model:value="joystickConfig.maxY" />
        </div>
        
        <div class="config-row">
          <label>Auto Return</label>
          <n-switch v-model:value="joystickConfig.autoReturn" />
        </div>
      </div>
    </template>
    
    <!-- 开关配置 -->
    <template v-else-if="widget.type === 'switch'">
      <div class="config-section">
        <h4>Switch Settings</h4>
        
        <div class="config-row">
          <label>Variable</label>
          <n-input v-model:value="switchConfig.variable" placeholder="varName" />
        </div>
        
        <div class="config-row">
          <label>States</label>
          <n-select 
            v-model:value="switchConfig.states"
            :options="[
              { label: '2 (0/1)', value: 2 },
              { label: '3 (-1/0/1)', value: 3 }
            ]"
          />
        </div>
      </div>
    </template>
    
    <!-- 操作按钮 -->
    <div class="form-actions">
      <n-button @click="$emit('cancel')">Cancel</n-button>
      <n-button type="primary" @click="handleSave">Save</n-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive } from 'vue'
import { NInput, NInputNumber, NSelect, NSwitch, NButton } from 'naive-ui'

// ============================================
// Props 和 Emits
// ============================================

interface WidgetData {
  id: string
  type: 'slider' | 'joystick' | 'switch'
  x: number
  y: number
  width: number
  height: number
  config: Record<string, unknown>
}

const props = defineProps<{
  widget: WidgetData
}>()

const emit = defineEmits<{
  (e: 'save', config: Record<string, unknown>): void
  (e: 'cancel'): void
}>()

// ============================================
// 本地状态
// ============================================

const localConfig = reactive({
  x: props.widget.x,
  y: props.widget.y,
  width: props.widget.width,
  height: props.widget.height
})

// 类型化的配置对象
const sliderConfig = reactive({
  variable: (props.widget.config.variable as string) || '',
  orientation: (props.widget.config.orientation as string) || 'horizontal',
  min: (props.widget.config.min as number) ?? 0,
  max: (props.widget.config.max as number) ?? 1,
  autoReturn: (props.widget.config.autoReturn as boolean) || false,
  logarithmic: (props.widget.config.logarithmic as boolean) || false
})

const joystickConfig = reactive({
  variableX: (props.widget.config.variableX as string) || '',
  variableY: (props.widget.config.variableY as string) || '',
  minX: (props.widget.config.minX as number) ?? -1,
  maxX: (props.widget.config.maxX as number) ?? 1,
  minY: (props.widget.config.minY as number) ?? -1,
  maxY: (props.widget.config.maxY as number) ?? 1,
  autoReturn: (props.widget.config.autoReturn as boolean) ?? true
})

const switchConfig = reactive({
  variable: (props.widget.config.variable as string) || '',
  states: (props.widget.config.states as 2 | 3) || 2
})

// ============================================
// 方法
// ============================================

function handleSave() {
  // 更新 widget 位置和尺寸
  props.widget.x = localConfig.x
  props.widget.y = localConfig.y
  props.widget.width = localConfig.width
  props.widget.height = localConfig.height
  
  // 根据控件类型返回对应配置
  let config: Record<string, unknown>
  
  if (props.widget.type === 'slider') {
    config = { ...sliderConfig }
  } else if (props.widget.type === 'joystick') {
    config = { ...joystickConfig }
  } else {
    config = { ...switchConfig }
  }
  
  emit('save', config)
}
</script>

<style scoped>
.widget-config-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.config-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.config-section h4 {
  margin: 0;
  font-size: 13px;
  font-weight: 500;
  color: var(--text-muted);
  border-bottom: 1px solid var(--border-color);
  padding-bottom: 8px;
}

.config-row {
  display: grid;
  grid-template-columns: 100px 1fr;
  gap: 12px;
  align-items: center;
}

.config-row label {
  font-size: 13px;
  color: var(--text-muted);
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
  padding-top: 16px;
  border-top: 1px solid var(--border-color);
}
</style>
