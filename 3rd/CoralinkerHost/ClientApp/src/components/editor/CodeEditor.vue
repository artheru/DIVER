<!--
  @file components/editor/CodeEditor.vue
  @description Monaco Editor 代码编辑器组件
  
  关键功能：
  1. 初始化 Monaco Editor
  2. 支持多种语言语法高亮
  3. 响应式尺寸调整
  4. 双向内容绑定
-->

<template>
  <div class="code-editor" ref="containerRef"></div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue'
import * as monaco from 'monaco-editor'

// ============================================
// Props 和 Emits
// ============================================

const props = defineProps<{
  /** 编辑器内容 */
  content: string
  /** 语言类型 (csharp, json, etc.) */
  language?: string
  /** 是否只读 */
  readonly?: boolean
}>()

const emit = defineEmits<{
  /** 内容更新事件 */
  (e: 'update:content', value: string): void
}>()

// ============================================
// 组件状态
// ============================================

const containerRef = ref<HTMLDivElement | null>(null)

let editor: monaco.editor.IStandaloneCodeEditor | null = null
let resizeObserver: ResizeObserver | null = null

// 内部标记：防止 watch 循环触发
let isUpdatingFromProp = false

// ============================================
// Monaco 配置
// ============================================

/**
 * 编辑器主题配置（暗色主题）
 */
function defineCustomTheme() {
  monaco.editor.defineTheme('coralinker-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: [
      { token: 'comment', foreground: '6a9955' },
      { token: 'keyword', foreground: '569cd6' },
      { token: 'string', foreground: 'ce9178' },
      { token: 'number', foreground: 'b5cea8' },
      { token: 'type', foreground: '4ec9b0' }
    ],
    colors: {
      'editor.background': '#0d1117',
      'editor.foreground': '#e6edf3',
      'editor.lineHighlightBackground': '#161b22',
      'editorLineNumber.foreground': '#6e7681',
      'editorLineNumber.activeForeground': '#e6edf3',
      'editor.selectionBackground': '#264f78',
      'editorCursor.foreground': '#58a6ff'
    }
  })
}

// ============================================
// 编辑器初始化
// ============================================

/**
 * 创建 Monaco Editor 实例
 */
function createEditor() {
  if (!containerRef.value) return
  
  // 定义自定义主题
  defineCustomTheme()
  
  // 创建编辑器
  editor = monaco.editor.create(containerRef.value, {
    value: props.content,
    language: props.language || 'plaintext',
    theme: 'coralinker-dark',
    readOnly: props.readonly,
    
    // 外观配置
    fontSize: 13,
    fontFamily: "'JetBrains Mono', 'Fira Code', Consolas, monospace",
    lineHeight: 20,
    padding: { top: 10, bottom: 10 },
    
    // 功能配置
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    wordWrap: 'on',
    automaticLayout: false, // 我们手动处理
    
    // 滚动配置
    scrollbar: {
      vertical: 'auto',
      horizontal: 'auto',
      verticalScrollbarSize: 10,
      horizontalScrollbarSize: 10
    },
    
    // 其他
    renderLineHighlight: 'line',
    cursorBlinking: 'smooth',
    smoothScrolling: true
  })
  
  // 监听内容变化
  editor.onDidChangeModelContent(() => {
    if (isUpdatingFromProp) return
    
    const value = editor?.getValue() || ''
    emit('update:content', value)
  })
  
  console.log('[CodeEditor] Monaco initialized')
}

/**
 * 调整编辑器大小
 */
function resize() {
  if (editor && containerRef.value) {
    const rect = containerRef.value.getBoundingClientRect()
    editor.layout({ width: rect.width, height: rect.height })
  }
}

// ============================================
// 生命周期
// ============================================

onMounted(async () => {
  await nextTick()
  createEditor()
  
  // 监听容器大小变化
  resizeObserver = new ResizeObserver(() => {
    resize()
  })
  
  if (containerRef.value) {
    resizeObserver.observe(containerRef.value)
  }
})

onUnmounted(() => {
  if (resizeObserver) {
    resizeObserver.disconnect()
  }
  
  if (editor) {
    editor.dispose()
    editor = null
  }
})

// ============================================
// 监听 Props 变化
// ============================================

// 内容变化时更新编辑器
watch(() => props.content, (newContent) => {
  if (editor && editor.getValue() !== newContent) {
    isUpdatingFromProp = true
    editor.setValue(newContent)
    isUpdatingFromProp = false
  }
})

// 语言变化时更新模型
watch(() => props.language, (newLang) => {
  if (editor) {
    const model = editor.getModel()
    if (model) {
      monaco.editor.setModelLanguage(model, newLang || 'plaintext')
    }
  }
})

// 只读状态变化
watch(() => props.readonly, (newReadonly) => {
  if (editor) {
    editor.updateOptions({ readOnly: newReadonly })
  }
})

// ============================================
// 暴露方法
// ============================================

defineExpose({
  /** 获取编辑器实例 */
  getEditor: () => editor,
  
  /** 获取当前内容 */
  getValue: () => editor?.getValue() || '',
  
  /** 设置内容 */
  setValue: (value: string) => {
    if (editor) {
      isUpdatingFromProp = true
      editor.setValue(value)
      isUpdatingFromProp = false
    }
  },
  
  /** 聚焦编辑器 */
  focus: () => editor?.focus()
})
</script>

<style scoped>
.code-editor {
  width: 100%;
  height: 100%;
  min-height: 200px;
  border-radius: var(--radius);
  overflow: hidden;
}
</style>
