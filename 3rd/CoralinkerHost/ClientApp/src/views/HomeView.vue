<!--
  @file views/HomeView.vue
  @description 主页面视图
  
  布局结构：
  ┌────────────────────────┬──────────────┐
  │                        │   Assets     │
  │    Graph / Editor      │   Panel      │
  │        (左上)          │   (右上)     │
  ├────────────────────────┼──────────────┤
  │    Terminal / Logs     │  Variables   │
  │        (左下)          │   (右下)     │
  └────────────────────────┴──────────────┘
-->

<template>
  <div class="home-layout">
    <!-- 顶部区域：左侧图/编辑器 + 右侧资源面板 -->
    <div class="top-row">
      <!-- 左上：图/编辑器区域 -->
      <div class="panel main-panel">
        <!-- Tab 栏 -->
        <div class="tab-bar">
          <button 
            class="tab" 
            :class="{ active: viewMode === 'graph' }"
            @click="setViewMode('graph')"
          >
            Graph
          </button>
          <button 
            v-for="tab in tabs" 
            :key="tab.id"
            class="tab"
            :class="{ active: activeTabId === tab.id, dirty: tab.dirty }"
            @click="switchToTab(tab.id)"
          >
            {{ tab.name }}
            <span v-if="tab.dirty" class="dirty-dot">•</span>
            <span class="tab-close" @click.stop="closeTab(tab.id)">×</span>
          </button>
        </div>
        
        <!-- 图画布 -->
        <div v-show="viewMode === 'graph'" class="graph-container">
          <GraphCanvas ref="graphCanvasRef" />
        </div>
        
        <!-- 编辑器 -->
        <div v-show="viewMode === 'editor'" class="editor-container">
          <CodeEditor 
            v-if="activeTab && !activeTab.isBinary"
            :content="activeTab.content || ''"
            :language="getLanguage(activeTab.path)"
            @update:content="updateContent"
          />
          <HexEditor 
            v-else-if="activeTab && activeTab.isBinary"
            :data="activeTab.base64 || ''"
          />
          <div v-else class="empty-editor">
            <p>No file selected</p>
          </div>
        </div>
      </div>
      
      <!-- 右上：资源面板 -->
      <div class="panel assets-panel">
        <div class="panel-header">
          <span>Assets</span>
          <n-button size="tiny" @click="showNewFileDialog = true">+ New</n-button>
        </div>
        <AssetTree @select="handleFileSelect" />
      </div>
    </div>
    
    <!-- 底部区域：左侧终端 + 右侧变量 -->
    <div class="bottom-row">
      <!-- 左下：终端/日志 -->
      <div class="panel terminal-panel">
        <TerminalPanel />
      </div>
      
      <!-- 右下：变量面板 -->
      <div class="panel variables-panel">
        <div class="panel-header">
          <span>Variables</span>
          <a href="/control" target="_blank" class="control-link" title="Open Control Panel">🎮</a>
        </div>
        <VariablePanel />
      </div>
    </div>
    
    <!-- 新建文件对话框 -->
    <n-modal v-model:show="showNewFileDialog">
      <n-card title="New Input File" style="width: 400px">
        <n-input 
          v-model:value="newFileName" 
          placeholder="MyLogic"
          @keyup.enter="createNewFile"
        />
        <template #footer>
          <div class="dialog-footer">
            <n-button @click="showNewFileDialog = false">Cancel</n-button>
            <n-button type="primary" @click="createNewFile">Create</n-button>
          </div>
        </template>
      </n-card>
    </n-modal>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NModal, NCard, NInput } from 'naive-ui'
import { storeToRefs } from 'pinia'
import { useFilesStore, useUiStore } from '@/stores'
import { useAutoSave } from '@/composables'

// 子组件（稍后创建）
import GraphCanvas from '@/components/graph/GraphCanvas.vue'
import CodeEditor from '@/components/editor/CodeEditor.vue'
import HexEditor from '@/components/editor/HexEditor.vue'
import AssetTree from '@/components/assets/AssetTree.vue'
import TerminalPanel from '@/components/logs/TerminalPanel.vue'
import VariablePanel from '@/components/variables/VariablePanel.vue'

// ============================================
// Store 引用
// ============================================

const filesStore = useFilesStore()
const uiStore = useUiStore()
const { tabs, activeTabId, activeTab } = storeToRefs(filesStore)
const { viewMode } = storeToRefs(uiStore)

// 自动保存
useAutoSave()

// ============================================
// 本地状态
// ============================================

const showNewFileDialog = ref(false)
const newFileName = ref('')

// ============================================
// 方法
// ============================================

/**
 * 切换视图模式
 */
function setViewMode(mode: 'graph' | 'editor') {
  uiStore.setViewMode(mode)
}

/**
 * 切换到指定 Tab
 */
function switchToTab(tabId: string) {
  filesStore.switchToTab(tabId)
  uiStore.setViewMode('editor')
}

/**
 * 关闭 Tab
 */
function closeTab(tabId: string) {
  filesStore.closeTab(tabId)
  
  // 如果没有打开的 Tab，切换回图视图
  if (tabs.value.length === 0) {
    uiStore.setViewMode('graph')
  }
}

/**
 * 处理文件选择
 */
async function handleFileSelect(path: string) {
  await filesStore.openFile(path)
  uiStore.setViewMode('editor')
}

/**
 * 更新编辑器内容
 */
function updateContent(content: string) {
  if (activeTabId.value) {
    filesStore.updateTabContent(activeTabId.value, content)
  }
}

/**
 * 获取文件语言类型
 */
function getLanguage(path: string): string {
  const ext = path.split('.').pop()?.toLowerCase()
  const langMap: Record<string, string> = {
    cs: 'csharp',
    json: 'json',
    xml: 'xml',
    js: 'javascript',
    ts: 'typescript',
    css: 'css',
    html: 'html',
    md: 'markdown'
  }
  return langMap[ext || ''] || 'plaintext'
}

/**
 * 创建新文件
 */
async function createNewFile() {
  if (!newFileName.value.trim()) return
  
  try {
    await filesStore.createNewInput(newFileName.value.trim())
    showNewFileDialog.value = false
    newFileName.value = ''
    uiStore.success('File Created', `${newFileName.value}.cs created`)
  } catch (error) {
    uiStore.error('Failed to Create File', String(error))
  }
}
</script>

<style scoped>
/* 主布局：2x2 网格 */
.home-layout {
  display: grid;
  grid-template-rows: 60% 40%;
  height: 100vh;
  gap: 10px;
  padding: 10px;
  background: var(--body-color);
}

.top-row,
.bottom-row {
  display: grid;
  grid-template-columns: 1fr 400px;
  gap: 10px;
}

/* 面板基础样式 */
.panel {
  background: linear-gradient(180deg, var(--panel-color), var(--panel-color-2));
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

/* 面板头部 */
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border-color);
  font-weight: 500;
}

/* Tab 栏 */
.tab-bar {
  display: flex;
  gap: 2px;
  padding: 8px 10px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  overflow-x: auto;
}

.tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  background: transparent;
  color: var(--text-muted);
  border-radius: var(--radius-sm);
  font-size: 13px;
  transition: all var(--transition-fast);
  white-space: nowrap;
}

.tab:hover {
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-color);
}

.tab.active {
  background: rgba(79, 140, 255, 0.15);
  color: var(--text-color);
}

.tab .dirty-dot {
  color: var(--warning);
}

.tab-close {
  font-size: 16px;
  line-height: 1;
  opacity: 0.5;
  margin-left: 4px;
}

.tab-close:hover {
  opacity: 1;
  color: var(--danger);
}

/* 图画布容器 */
.graph-container {
  flex: 1;
  min-height: 0;
  position: relative;
}

/* 编辑器容器 */
.editor-container {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.empty-editor {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
}

/* 资源面板 */
.assets-panel {
  overflow: hidden;
}

/* 终端面板 */
.terminal-panel {
  min-height: 0;
}

/* 变量面板 */
.variables-panel {
  min-height: 0;
}

.control-link {
  font-size: 18px;
  text-decoration: none;
}

/* 对话框底部 */
.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
