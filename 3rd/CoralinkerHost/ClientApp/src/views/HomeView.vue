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
                <div
                  v-for="tab in tabs" 
                  :key="tab.id"
                  class="tab-wrapper"
                  :class="{ active: activeTabId === tab.id }"
                >
                  <button 
                    class="tab"
                    :class="{ active: activeTabId === tab.id, dirty: tab.dirty, readonly: isReadonlyFile(tab.path) }"
                    @click="switchToTab(tab.id)"
                  >
                    <span v-if="isReadonlyFile(tab.path)" class="readonly-icon" title="Read-only (generated file)">🔒</span>
                    {{ tab.name }}
                    <span v-if="tab.dirty" class="dirty-dot">•</span>
                  </button>
                  <button 
                    v-if="tab.dirty && !isReadonlyFile(tab.path)" 
                    class="tab-save" 
                    @click.stop="handleSaveTab(tab.id)"
                    title="Save (Ctrl+S)"
                  >💾</button>
                  <span class="tab-close" @click.stop="closeTab(tab.id)">×</span>
                </div>
                
                <!-- 工具按钮 -->
                <div class="tab-spacer"></div>
                
                <!-- 运行控制组（始终显示） -->
                <div class="runtime-controls">
                  <!-- 状态指示 -->
                  <div class="runtime-status">
                    <span class="status-dot" :class="statusClass"></span>
                    <span class="status-label">{{ sessionType }}</span>
                    <span class="status-text">{{ statusText }}</span>
                  </div>
                  
                  <button 
                    class="toolbar-btn build" 
                    :disabled="!hasInputFiles || isBuilding || isRunning || isStarting"
                    @click="handleBuild" 
                    :title="isRunning || isStarting ? 'Stop session before building' : 'Compile .cs files in inputs folder'"
                  >
                    <span class="btn-icon">⚙</span>
                    <span class="btn-text">{{ isBuilding ? 'Building...' : 'Build' }}</span>
                  </button>
                  <button 
                    class="toolbar-btn start" 
                    :disabled="!canStart || isStarting || isBuilding"
                    @click="handleStart" 
                    title="Connect, Configure, Program, and Start execution"
                  >
                    <span class="btn-icon">▶</span>
                    <span class="btn-text">{{ isStarting ? 'Starting...' : 'Start' }}</span>
                  </button>
                  <button 
                    class="toolbar-btn stop" 
                    :disabled="!canStop || isStopping"
                    @click="handleStop" 
                    title="Stop execution"
                  >
                    <span class="btn-icon">■</span>
                    <span class="btn-text">{{ isStopping ? 'Stopping...' : 'Stop' }}</span>
                  </button>
                </div>
                
                <div class="toolbar-divider"></div>
                
                <!-- Graph 工具按钮 -->
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
              <div v-show="viewMode === 'editor'" class="editor-container" @keydown.space.stop>
                <CodeEditor 
                  ref="codeEditorRef"
                  v-if="activeTab && !activeTab.isBinary"
                  :content="activeTab.content || ''"
                  :language="getLanguage(activeTab.path)"
                  :readonly="isReadonlyFile(activeTab.path)"
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
              <TerminalPanel @gotoSource="handleGotoSource" />
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
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { NButton, NModal, NCard, NInput } from 'naive-ui'
import { storeToRefs } from 'pinia'
import { Splitpanes, Pane } from 'splitpanes'
import 'splitpanes/dist/splitpanes.css'
import { useFilesStore, useUiStore, useProjectStore, useLogStore, useRuntimeStore } from '@/stores'
import { useAutoSave } from '@/composables'
import * as projectApi from '@/api/project'
import { programNode } from '@/api/device'

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

const { tabs, activeTabId, activeTab, fileTree } = storeToRefs(filesStore)
const { viewMode, sourceJumpRequest } = storeToRefs(uiStore)
const { canEdit, isRunning, isBackendAvailable, sessionType, canStart, canStop } = storeToRefs(runtimeStore)

// 自动保存
useAutoSave()

// ============================================
// 本地状态
// ============================================

const showNewFileDialog = ref(false)
const newFileName = ref('')
const graphCanvasRef = ref<InstanceType<typeof GraphCanvas> | null>(null)
const codeEditorRef = ref<InstanceType<typeof CodeEditor> | null>(null)
const importFileRef = ref<HTMLInputElement | null>(null)
const showAddNodeDialog = ref(false)
const showControlWindow = ref(false)

// 运行控制状态
const isBuilding = ref(false)
const isStarting = ref(false)
const isStopping = ref(false)

// 状态样式类
const statusClass = computed(() => {
  if (isRunning.value) return 'running'
  if (isBackendAvailable.value) return 'idle'
  return 'offline'
})

// 状态文本
const statusText = computed(() => {
  if (isRunning.value) return 'Running'
  if (isBackendAvailable.value) return 'Idle'
  return 'Offline'
})

// 检查 inputs 目录是否有 .cs 文件
const hasInputFiles = computed(() => {
  const inputsFolder = fileTree.value.find(node => node.name === 'inputs')
  if (!inputsFolder || !inputsFolder.children) return false
  return inputsFolder.children.some(child => 
    child.kind === 'file' && child.name.endsWith('.cs')
  )
})

// ============================================
// 源码跳转处理
// ============================================

/**
 * 监听源码跳转请求
 * 当从错误对话框点击跳转时触发
 */
watch(sourceJumpRequest, async (request) => {
  if (!request) return
  
  console.log(`[HomeView] Source jump request: ${request.file}:${request.line}`)
  
  // 查找匹配的文件
  const targetPath = findSourceFile(request.file)
  if (!targetPath) {
    console.warn(`[HomeView] Source file not found: ${request.file}`)
    uiStore.error('File Not Found', `Cannot find source file: ${request.file}`)
    uiStore.clearSourceJumpRequest()
    return
  }
  
  // 打开文件
  await filesStore.openFile(targetPath)
  
  // 等待 DOM 更新后跳转到行
  await nextTick()
  
  // 再等待一下让 Monaco 初始化
  setTimeout(() => {
    if (codeEditorRef.value) {
      codeEditorRef.value.goToLine(request.line)
    }
    uiStore.clearSourceJumpRequest()
  }, 100)
})

/**
 * 在 assets/inputs 目录中查找源文件
 * @param fileName 源文件名，如 "TestLogic.cs"
 */
function findSourceFile(fileName: string): string | null {
  console.log(`[HomeView] findSourceFile: looking for "${fileName}"`)
  
  // 通过 filesStore 检查文件是否存在
  const fileTree = filesStore.fileTree
  
  // 递归查找文件
  function searchInTree(items: any[], target: string): string | null {
    for (const item of items) {
      // 文件树使用 kind: 'file' | 'folder'，不是 type
      if (item.kind === 'file') {
        // 检查文件名是否匹配
        if (item.name === target) {
          console.log(`[HomeView] Found file by name: ${item.path}`)
          return item.path
        }
        // 检查完整路径结尾
        if (item.path.endsWith('/' + target) || item.path.endsWith('\\' + target)) {
          console.log(`[HomeView] Found file by path suffix: ${item.path}`)
          return item.path
        }
      } else if (item.kind === 'folder' && item.children) {
        const found = searchInTree(item.children, target)
        if (found) return found
      }
    }
    return null
  }
  
  if (fileTree && fileTree.length > 0) {
    console.log(`[HomeView] Searching in file tree with ${fileTree.length} root items`)
    const result = searchInTree(fileTree, fileName)
    if (!result) {
      console.warn(`[HomeView] Source file not found in file tree: "${fileName}"`)
      // 打印文件树结构帮助调试
      console.log('[HomeView] File tree structure:', JSON.stringify(fileTree, null, 2))
    }
    return result
  }
  
  console.warn('[HomeView] File tree not available or empty')
  return null
}

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
 * 处理从 TerminalPanel 的 gotoSource 事件（Build 错误跳转）
 * @param fileName 源文件名，如 "TestLogic.cs"
 * @param line 行号
 */
async function handleGotoSource(fileName: string, line: number) {
  console.log(`[HomeView] handleGotoSource: ${fileName}:${line}`)
  
  // 查找文件路径
  const targetPath = findSourceFile(fileName)
  if (!targetPath) {
    uiStore.error('File Not Found', `Cannot find source file: ${fileName}`)
    return
  }
  
  // 打开文件并切换到编辑器模式
  await filesStore.openFile(targetPath)
  uiStore.setViewMode('editor')
  
  // 等待 DOM 更新后跳转到行
  await nextTick()
  
  // 再等待一下让 Monaco 初始化
  setTimeout(() => {
    if (codeEditorRef.value) {
      codeEditorRef.value.goToLine(line)
    }
  }, 100)
}

/**
 * 检查文件路径是否为只读（generated 文件夹下的文件）
 */
function isReadonlyFile(path: string): boolean {
  return filesStore.isReadonlyPath(path)
}

/**
 * 保存指定 Tab
 */
async function handleSaveTab(tabId: string) {
  try {
    const success = await filesStore.saveTab(tabId)
    if (success) {
      uiStore.success('Saved', 'File saved successfully')
    }
  } catch (error) {
    uiStore.error('Save Failed', String(error))
  }
}

/**
 * 保存当前活动 Tab (用于 Ctrl+S)
 */
async function saveActiveTab() {
  if (!activeTabId.value) return
  
  const tab = activeTab.value
  if (!tab || !tab.dirty) return
  
  // 检查是否是只读文件
  if (isReadonlyFile(tab.path)) {
    uiStore.error('Read-only', 'Cannot save generated files')
    return
  }
  
  await handleSaveTab(activeTabId.value)
}

/**
 * 处理键盘快捷键
 */
function handleKeydown(event: KeyboardEvent) {
  // Ctrl+S 或 Cmd+S (Mac)
  if ((event.ctrlKey || event.metaKey) && event.key === 's') {
    event.preventDefault()
    saveActiveTab()
  }
}

// 注册/注销键盘事件监听
onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})

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
  // 同步后端状态，确保前端状态是最新的
  await runtimeStore.syncSessionState()
  
  // 检查是否正在运行
  if (runtimeStore.backendSessionRunning || runtimeStore.isRunning) {
    logStore.logUI('\x1b[31mERROR:\x1b[0m Cannot create new project while session is running. Please stop first.')
    uiStore.error('Cannot Create', 'Please stop the session first')
    return
  }
  
  if (!confirm('Create a new project? This will clear the current graph and assets.')) {
    return
  }
  
  try {
    await projectStore.createNew()
    
    // 清空 runtime store 中的节点数据
    runtimeStore.clearNodeData()
    
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
async function handleAddNodeConfirm(data: AddNodeResult) {
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
    
    // 节点数据由 DIVERSession 管理，需要立即保存到磁盘
    await projectStore.saveProject({ silent: true })
  }
}

// ============================================
// 运行控制方法
// ============================================

/**
 * 执行构建
 */
async function handleBuild() {
  if (isBuilding.value) return
  
  isBuilding.value = true
  
  // 先清空前端 Build 日志，再切换标签
  logStore.clearBuild()
  logStore.switchTab('build')
  
  try {
    const result = await projectStore.build()
    if (result.ok) {
      uiStore.success('Build Success', `Build ID: ${result.buildId}`)
      await filesStore.loadFileTree()
      filesStore.notifyBuildComplete()
      await filesStore.refreshOpenTabs()
      // 先重新编程所有节点，然后再刷新变量和字段元信息
      // 否则会获取到旧的字段定义
      await reprogramAllNodes()
      await runtimeStore.refreshVariables()
      await runtimeStore.refreshFieldMetas()
    } else {
      uiStore.error('Build Failed', result.error || 'Unknown error')
    }
  } catch (error) {
    uiStore.error('Build Failed', String(error))
  } finally {
    isBuilding.value = false
  }
}

/**
 * 重新编程所有已选择 Logic 的节点
 */
async function reprogramAllNodes() {
  await runtimeStore.refreshNodes()
  const nodeInfoList = runtimeStore.nodeInfoList
  const nodesToProgram = nodeInfoList.filter(node => node.logicName)
  
  if (nodesToProgram.length === 0) {
    logStore.logUI('[Build] No nodes with Logic selected')
    return
  }
  
  logStore.logUI(`[Build] Re-programming ${nodesToProgram.length} node(s)...`)
  
  for (const node of nodesToProgram) {
    try {
      logStore.logUI(`[Build] Programming ${node.nodeName} with ${node.logicName}...`)
      const result = await programNode(node.uuid, node.logicName!)
      if (result.ok) {
        logStore.logUI(`[Build] \x1b[32m✓\x1b[0m ${node.nodeName} programmed (${result.programSize} bytes)`)
      } else {
        logStore.logUI(`[Build] \x1b[31m✗\x1b[0m ${node.nodeName} failed to program`)
      }
    } catch (error) {
      logStore.logUI(`[Build] \x1b[31m✗\x1b[0m ${node.nodeName} error: ${error}`)
    }
  }
  
  await runtimeStore.refreshNodes()
  await projectStore.saveProject({ silent: true })
}

/**
 * 启动执行
 */
async function handleStart() {
  if (isStarting.value || !canStart.value) return
  
  isStarting.value = true
  logStore.switchTab('terminal')
  logStore.logUI('Starting execution sequence (Connect → Configure → Program → Start)...')
  
  try {
    const result = await runtimeStore.start()
    if (result.ok) {
      logStore.logUI('\x1b[32mExecution started\x1b[0m')
      uiStore.success('Started', 'Execution started')
    } else {
      logStore.logUI(`[Start] \x1b[31mERROR:\x1b[0m ${(result as { error?: string }).error || 'Start failed'}`)
      uiStore.error('Start Failed', (result as { error?: string }).error || 'Start failed')
    }
  } catch (error) {
    logStore.logUI(`[Start] \x1b[31mERROR:\x1b[0m ${String(error)}`)
    uiStore.error('Start Failed', String(error))
  } finally {
    isStarting.value = false
  }
}

/**
 * 停止执行
 */
async function handleStop() {
  if (isStopping.value || !canStop.value) return
  
  isStopping.value = true
  logStore.logUI('Stopping execution...')
  
  try {
    await runtimeStore.stop()
    logStore.logUI('\x1b[33mExecution stopped\x1b[0m')
    uiStore.success('Stopped', 'Execution stopped')
  } catch (error) {
    logStore.logUI(`[Stop] \x1b[31mERROR:\x1b[0m ${String(error)}`)
    uiStore.error('Stop Failed', String(error))
  } finally {
    isStopping.value = false
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

/* 运行控制组 */
.runtime-controls {
  display: flex;
  align-items: center;
  gap: 4px;
}

/* 运行时状态指示 */
.runtime-status {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: var(--radius-sm);
  margin-right: 8px;
}

.runtime-status .status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.runtime-status .status-dot.idle {
  background: var(--warning);
}

.runtime-status .status-dot.offline {
  background: var(--text-muted);
}

.runtime-status .status-dot.running {
  background: var(--success);
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.runtime-status .status-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-color);
}

.runtime-status .status-text {
  font-size: 11px;
  color: var(--text-muted);
}

/* 特殊按钮颜色 */
.toolbar-btn.build:hover:not(:disabled) {
  background: rgba(79, 140, 255, 0.15);
  color: var(--primary);
}

.toolbar-btn.start:hover:not(:disabled) {
  background: rgba(34, 197, 94, 0.15);
  color: var(--success);
}

.toolbar-btn.stop:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.15);
  color: var(--danger);
}

/* Tab wrapper for grouped tab elements */
.tab-wrapper {
  display: flex;
  align-items: center;
  gap: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  transition: background var(--transition-fast);
}

.tab-wrapper:hover {
  background: rgba(255, 255, 255, 0.03);
}

.tab-wrapper.active {
  background: rgba(79, 140, 255, 0.1);
}

.tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px 6px 12px;
  background: transparent;
  color: var(--text-muted);
  border-radius: var(--radius-sm) 0 0 var(--radius-sm);
  font-size: 13px;
  transition: all var(--transition-fast);
  white-space: nowrap;
}

.tab:hover {
  color: var(--text-color);
}

.tab.active {
  color: var(--text-color);
}

.tab.readonly {
  color: var(--text-muted);
  font-style: italic;
}

.tab .readonly-icon {
  font-size: 10px;
  opacity: 0.7;
}

.tab .dirty-dot {
  color: var(--warning);
}

.tab-save {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4px 6px;
  background: transparent;
  border: none;
  color: var(--text-muted);
  font-size: 12px;
  cursor: pointer;
  transition: all var(--transition-fast);
  opacity: 0.7;
}

.tab-save:hover {
  color: var(--success);
  opacity: 1;
}

.tab-close {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4px 8px 4px 4px;
  font-size: 16px;
  line-height: 1;
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
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
