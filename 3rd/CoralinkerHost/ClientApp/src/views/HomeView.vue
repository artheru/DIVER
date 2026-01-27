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
    <!-- 可调节分割面板 -->
    <Splitpanes @resize="handleResize">
      <!-- 左侧区域 -->
      <Pane :size="leftPaneSize" :min-size="30">
        <Splitpanes horizontal @resize="handleLeftResize">
          <!-- 左上：图/编辑器区域 -->
          <Pane :size="topLeftPaneSize" :min-size="20">
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
                
                <!-- Graph 工具按钮 -->
                <div class="tab-spacer"></div>
                <div class="graph-toolbar" v-show="viewMode === 'graph'">
                  <button 
                    class="toolbar-btn" 
                    :disabled="!canEdit" 
                    @click="handleNewProject" 
                    title="New Project"
                  >
                    <span class="btn-icon">📄</span>
                    <span class="btn-text">New</span>
                  </button>
                  <button 
                    class="toolbar-btn" 
                    :disabled="!canEdit" 
                    @click="handleSaveProject" 
                    title="Save to ZIP"
                  >
                    <span class="btn-icon">💾</span>
                    <span class="btn-text">Save</span>
                  </button>
                  <button 
                    class="toolbar-btn" 
                    :disabled="!canEdit" 
                    @click="triggerLoadProject" 
                    title="Load from ZIP"
                  >
                    <span class="btn-icon">📂</span>
                    <span class="btn-text">Load</span>
                  </button>
                  <input 
                    ref="importFileRef"
                    type="file" 
                    accept=".zip"
                    style="display: none"
                    @change="handleLoadProject"
                  />
                  <div class="toolbar-divider"></div>
                  <button 
                    class="toolbar-btn add-node" 
                    :disabled="!canEdit" 
                    @click="handleAddNode" 
                    title="Add MCU Node"
                  >
                    <span class="btn-icon">➕</span>
                    <span class="btn-text">Add Node</span>
                  </button>
                </div>
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
          </Pane>
          
          <!-- 左下：终端/日志 -->
          <Pane :min-size="15">
            <div class="panel terminal-panel">
              <TerminalPanel />
            </div>
          </Pane>
        </Splitpanes>
      </Pane>
      
      <!-- 右侧区域 -->
      <Pane :min-size="15" :max-size="50">
        <Splitpanes horizontal @resize="handleRightResize">
          <!-- 右上：资源面板 -->
          <Pane :size="topRightPaneSize" :min-size="20">
            <div class="panel assets-panel">
              <div class="panel-header">
                <span>Assets</span>
                <n-button size="tiny" @click="showNewFileDialog = true">+ New</n-button>
              </div>
              <AssetTree @select="handleFileSelect" />
            </div>
          </Pane>
          
          <!-- 右下：变量面板 -->
          <Pane :min-size="20">
            <div class="panel variables-panel">
              <div class="panel-header">
                <span>Variables</span>
                <button class="control-btn" @click="showControlWindow = true" title="Open Control Panel">🎮</button>
              </div>
              <VariablePanel />
            </div>
          </Pane>
        </Splitpanes>
      </Pane>
    </Splitpanes>
    
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
    
    <!-- 添加节点对话框 -->
    <AddNodeDialog 
      v-model:show="showAddNodeDialog"
      @confirm="handleAddNodeConfirm"
    />
    
    <!-- 遥控器浮动窗口 -->
    <ControlWindow v-model:visible="showControlWindow" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NModal, NCard, NInput } from 'naive-ui'
import { storeToRefs } from 'pinia'
import { Splitpanes, Pane } from 'splitpanes'
import 'splitpanes/dist/splitpanes.css'
import { useFilesStore, useUiStore, useProjectStore, useLogStore, useRuntimeStore } from '@/stores'
import { useAutoSave } from '@/composables'
import * as projectApi from '@/api/project'

// 子组件
import GraphCanvas from '@/components/graph/GraphCanvas.vue'
import CodeEditor from '@/components/editor/CodeEditor.vue'
import HexEditor from '@/components/editor/HexEditor.vue'
import AssetTree from '@/components/assets/AssetTree.vue'
import TerminalPanel from '@/components/logs/TerminalPanel.vue'
import VariablePanel from '@/components/variables/VariablePanel.vue'
import AddNodeDialog from '@/components/graph/AddNodeDialog.vue'
import ControlWindow from '@/components/control/ControlWindow.vue'
import type { AddNodeResult } from '@/components/graph/AddNodeDialog.vue'

// ============================================
// Store 引用
// ============================================

const filesStore = useFilesStore()
const uiStore = useUiStore()
const projectStore = useProjectStore()
const logStore = useLogStore()
const runtimeStore = useRuntimeStore()

const { tabs, activeTabId, activeTab } = storeToRefs(filesStore)
const { viewMode } = storeToRefs(uiStore)
const { canEdit } = storeToRefs(runtimeStore)

// 自动保存
useAutoSave()

// ============================================
// 本地状态
// ============================================

const showNewFileDialog = ref(false)
const newFileName = ref('')
const graphCanvasRef = ref<InstanceType<typeof GraphCanvas> | null>(null)
const importFileRef = ref<HTMLInputElement | null>(null)
const showAddNodeDialog = ref(false)
const showControlWindow = ref(false)

// Splitpanes 尺寸 (百分比)
const leftPaneSize = ref(75)
const topLeftPaneSize = ref(65)
const topRightPaneSize = ref(50)

/**
 * 处理左右分割调整
 */
function handleResize(event: { size: number }[]) {
  if (event[0]) leftPaneSize.value = event[0].size
}

/**
 * 处理左侧上下分割调整
 */
function handleLeftResize(event: { size: number }[]) {
  if (event[0]) topLeftPaneSize.value = event[0].size
}

/**
 * 处理右侧上下分割调整
 */
function handleRightResize(event: { size: number }[]) {
  if (event[0]) topRightPaneSize.value = event[0].size
}

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

// ============================================
// Graph 工具栏方法
// ============================================

/**
 * 新建项目
 */
async function handleNewProject() {
  if (!confirm('Create a new project? This will clear the current graph and assets.')) {
    return
  }
  
  try {
    await projectStore.createNew()
    
    // 重新加载 Graph
    if (graphCanvasRef.value) {
      graphCanvasRef.value.clearGraph()
      graphCanvasRef.value.ensureRootNode()
    }
    
    // 刷新文件树
    await filesStore.loadFileTree()
    
    logStore.logUI('New project created')
    uiStore.success('New Project', 'Project created successfully')
  } catch (error) {
    logStore.logUI(`\x1b[31mERROR:\x1b[0m Failed to create new project: ${error}`)
    uiStore.error('Failed', String(error))
  }
}

/**
 * 保存项目到 ZIP
 */
async function handleSaveProject() {
  try {
    // 先保存到服务器
    await projectStore.saveProject()
    
    // 然后导出 ZIP
    await projectStore.exportZip()
    
    logStore.logUI('Project exported as ZIP')
    uiStore.success('Saved', 'Project exported as ZIP')
  } catch (error) {
    logStore.logUI(`\x1b[31mERROR:\x1b[0m Export failed: ${error}`)
    uiStore.error('Export Failed', String(error))
  }
}

/**
 * 触发加载项目文件选择
 */
function triggerLoadProject() {
  importFileRef.value?.click()
}

/**
 * 加载项目 ZIP
 */
async function handleLoadProject(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  
  if (!file) return
  
  try {
    await projectApi.importProject(file)
    
    // 重新加载项目
    await projectStore.loadProject()
    
    // 重新加载 Graph（从 DIVERSession 获取节点数据）
    if (graphCanvasRef.value) {
      await graphCanvasRef.value.loadFromStore()
    }
    
    // 刷新文件树
    await filesStore.loadFileTree()
    
    logStore.logUI(`Project loaded from ${file.name}`)
    uiStore.success('Loaded', `Project loaded from ${file.name}`)
  } catch (error) {
    logStore.logUI(`\x1b[31mERROR:\x1b[0m Import failed: ${error}`)
    uiStore.error('Import Failed', String(error))
  } finally {
    // 清空 input
    input.value = ''
  }
}

/**
 * 添加节点 - 打开对话框
 */
function handleAddNode() {
  showAddNodeDialog.value = true
}

/**
 * 处理添加节点确认
 * 节点已经在 AddNodeDialog 中通过 addNode API 添加到后端
 * 这里只需要在前端画布上添加节点
 */
function handleAddNodeConfirm(data: AddNodeResult) {
  if (graphCanvasRef.value) {
    graphCanvasRef.value.addNode({
      uuid: data.uuid,  // 使用后端分配的 UUID
      mcuUri: data.mcuUri,
      nodeName: data.nodeName,
      version: data.version,
      layout: data.layout,
      ports: data.ports  // 传递端口配置
    })
    logStore.logUI(`Node added: ${data.nodeName} (${data.version?.productionName || 'Unknown'}) at ${data.mcuUri}`)
  }
}
</script>

<style scoped>
/* 主布局 */
.home-layout {
  height: 100vh;
  background: transparent;
}

/* Splitpanes 样式覆盖 */
:deep(.splitpanes) {
  background: transparent;
}

:deep(.splitpanes__pane) {
  background: transparent;
}

:deep(.splitpanes__splitter) {
  background: transparent;
  position: relative;
}

:deep(.splitpanes--vertical > .splitpanes__splitter) {
  width: 6px;
  margin: 0;
}

:deep(.splitpanes--horizontal > .splitpanes__splitter) {
  height: 6px;
  margin: 0;
}

:deep(.splitpanes__splitter:hover),
:deep(.splitpanes__splitter:active) {
  background: rgba(79, 140, 255, 0.6);
}

:deep(.splitpanes__splitter::before),
:deep(.splitpanes__splitter::after) {
  display: none;
}

/* 面板基础样式 */
.panel {
  background: linear-gradient(180deg, var(--panel-color), var(--panel-color-2));
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  overflow: hidden;
  display: flex;
  flex-direction: column;
  height: calc(100% - 8px);
  margin: 4px;
}

/* Pane 内部 padding，让 panel 的 margin 区域透明 */
:deep(.splitpanes__pane) {
  overflow: visible;
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
  align-items: center;
  gap: 2px;
  padding: 6px 10px;
  background: var(--panel-color-2);
  border-bottom: 1px solid var(--border-color);
  overflow-x: auto;
}

.tab-spacer {
  flex: 1;
}

/* Graph 工具栏 */
.graph-toolbar {
  display: flex;
  align-items: center;
  gap: 4px;
}

.toolbar-btn {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  color: var(--text-muted);
  font-size: 11px;
  white-space: nowrap;
  transition: all var(--transition-fast);
}

.toolbar-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-color);
}

.toolbar-btn .btn-icon {
  font-size: 12px;
}

.toolbar-btn .btn-text {
  font-weight: 500;
}

.toolbar-btn.add-node:hover:not(:disabled) {
  background: rgba(79, 140, 255, 0.15);
  color: var(--primary);
}

.toolbar-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.toolbar-btn:disabled:hover {
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-muted);
}

.toolbar-divider {
  width: 1px;
  height: 20px;
  background: var(--border-color);
  margin: 0 6px;
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

.control-btn {
  font-size: 18px;
  background: transparent;
  border: none;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
  transition: background var(--transition-fast);
}

.control-btn:hover {
  background: rgba(255, 255, 255, 0.1);
}

/* 对话框底部 */
.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
