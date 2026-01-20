/**
 * @file stores/files.ts
 * @description 文件系统状态管理
 * 
 * 管理资源文件树和编辑器 Tab：
 * - 文件树结构
 * - 打开的 Tab 列表
 * - 当前编辑的文件内容
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as filesApi from '@/api/files'
import type { FileNode } from '@/types'

/**
 * 编辑器 Tab 信息
 */
export interface EditorTab {
  /** Tab 唯一 ID */
  id: string
  /** 文件路径 */
  path: string
  /** 显示名称 */
  name: string
  /** 文件内容 (文本) */
  content?: string
  /** 文件内容 (二进制 base64) */
  base64?: string
  /** 是否为二进制文件 */
  isBinary: boolean
  /** 是否有未保存的更改 */
  dirty: boolean
  /** 文件大小 */
  size: number
}

export const useFilesStore = defineStore('files', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 文件树根节点 */
  const fileTree = ref<FileNode[]>([])
  
  /** 打开的 Tab 列表 */
  const tabs = ref<EditorTab[]>([])
  
  /** 当前激活的 Tab ID */
  const activeTabId = ref<string | null>(null)
  
  /** 文件树加载状态 */
  const loading = ref(false)
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 当前激活的 Tab */
  const activeTab = computed(() => {
    if (!activeTabId.value) return null
    return tabs.value.find(t => t.id === activeTabId.value) || null
  })
  
  /** 是否有打开的 Tab */
  const hasTabs = computed(() => tabs.value.length > 0)
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 加载文件树
   */
  async function loadFileTree() {
    loading.value = true
    try {
      fileTree.value = await filesApi.getFileTree()
      console.log('[Files] File tree loaded')
    } catch (error) {
      console.error('[Files] Failed to load file tree:', error)
      throw error
    } finally {
      loading.value = false
    }
  }
  
  /**
   * 打开文件到 Tab
   * @param path 文件路径
   */
  async function openFile(path: string) {
    // 检查是否已打开
    const existing = tabs.value.find(t => t.path === path)
    if (existing) {
      activeTabId.value = existing.id
      return existing
    }
    
    // 读取文件内容
    const response = await filesApi.readFile(path)
    
    // 创建新 Tab
    const tab: EditorTab = {
      id: `tab-${Date.now()}`,
      path: response.path,
      name: path.split('/').pop() || path,
      content: response.text,
      base64: response.base64,
      isBinary: response.kind === 'binary',
      dirty: false,
      size: response.sizeBytes
    }
    
    tabs.value.push(tab)
    activeTabId.value = tab.id
    
    console.log(`[Files] Opened file: ${path}`)
    return tab
  }
  
  /**
   * 关闭 Tab
   * @param tabId Tab ID
   */
  function closeTab(tabId: string) {
    const index = tabs.value.findIndex(t => t.id === tabId)
    if (index === -1) return
    
    tabs.value.splice(index, 1)
    
    // 如果关闭的是当前 Tab，切换到相邻的
    if (activeTabId.value === tabId) {
      if (tabs.value.length > 0) {
        const newIndex = Math.min(index, tabs.value.length - 1)
        const tab = tabs.value[newIndex]
        activeTabId.value = tab?.id ?? null
      } else {
        activeTabId.value = null
      }
    }
  }
  
  /**
   * 切换到指定 Tab
   * @param tabId Tab ID
   */
  function switchToTab(tabId: string) {
    const tab = tabs.value.find(t => t.id === tabId)
    if (tab) {
      activeTabId.value = tabId
    }
  }
  
  /**
   * 更新 Tab 内容
   * @param tabId Tab ID
   * @param content 新内容
   */
  function updateTabContent(tabId: string, content: string) {
    const tab = tabs.value.find(t => t.id === tabId)
    if (tab && !tab.isBinary) {
      tab.content = content
      tab.dirty = true
    }
  }
  
  /**
   * 保存当前 Tab
   */
  async function saveCurrentTab() {
    const tab = activeTab.value
    if (!tab || !tab.dirty) return
    
    if (tab.isBinary) {
      await filesApi.writeFile({
        path: tab.path,
        kind: 'binary',
        base64: tab.base64
      })
    } else {
      await filesApi.writeFile({
        path: tab.path,
        kind: 'text',
        text: tab.content
      })
    }
    
    tab.dirty = false
    console.log(`[Files] Saved: ${tab.path}`)
  }
  
  /**
   * 创建新的输入文件
   * @param name 文件名
   */
  async function createNewInput(name: string) {
    const result = await filesApi.createInputFile(name)
    
    // 刷新文件树
    await loadFileTree()
    
    // 打开新文件
    if (result.path) {
      await openFile(result.path)
    }
    
    return result
  }
  
  /**
   * 删除文件
   * @param path 文件路径
   */
  async function deleteFile(path: string) {
    await filesApi.deleteFile(path)
    
    // 关闭相关 Tab
    const tab = tabs.value.find(t => t.path === path)
    if (tab) {
      closeTab(tab.id)
    }
    
    // 刷新文件树
    await loadFileTree()
    
    console.log(`[Files] Deleted: ${path}`)
  }
  
  /**
   * 上传资源文件
   * @param file 文件对象
   */
  async function uploadAsset(file: File) {
    const result = await filesApi.uploadAsset(file)
    
    // 刷新文件树
    await loadFileTree()
    
    console.log(`[Files] Uploaded: ${result.name}`)
    return result
  }
  
  return {
    // 状态
    fileTree,
    tabs,
    activeTabId,
    loading,
    
    // 计算属性
    activeTab,
    hasTabs,
    
    // 方法
    loadFileTree,
    openFile,
    closeTab,
    switchToTab,
    updateTabContent,
    saveCurrentTab,
    createNewInput,
    deleteFile,
    uploadAsset
  }
})
