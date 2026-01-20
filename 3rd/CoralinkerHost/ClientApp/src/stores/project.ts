/**
 * @file stores/project.ts
 * @description 项目状态管理
 * 
 * 管理项目的核心状态：
 * - 节点图数据 (nodeMap)
 * - 当前选中的资源和文件
 * - 构建状态
 * - 自动保存逻辑
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as projectApi from '@/api/project'
import type { ProjectState, BuildResult } from '@/types'

export const useProjectStore = defineStore('project', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** LiteGraph 序列化的节点图数据 */
  const nodeMap = ref<string | null>(null)
  
  /** 当前选中的 .cs 资源文件名 */
  const selectedAsset = ref<string | null>(null)
  
  /** 当前在编辑器中打开的文件路径 */
  const selectedFile = ref<string | null>(null)
  
  /** 最后一次构建的 ID */
  const lastBuildId = ref<string | null>(null)
  
  /** 是否正在加载 */
  const loading = ref(false)
  
  /** 是否有未保存的更改 */
  const dirty = ref(false)
  
  /** 同步状态: 'synced' | 'syncing' | 'error' */
  const syncState = ref<'synced' | 'syncing' | 'error'>('synced')
  
  /** 最后一次构建结果 */
  const lastBuildResult = ref<BuildResult | null>(null)
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 是否有选中的资源 */
  const hasSelectedAsset = computed(() => !!selectedAsset.value)
  
  /** 是否已构建过 */
  const hasBuilt = computed(() => !!lastBuildId.value)
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 从服务器加载项目
   * 在应用启动时调用
   */
  async function loadProject() {
    loading.value = true
    try {
      const state = await projectApi.getProject()
      
      // 更新本地状态
      nodeMap.value = state.nodeMap
      selectedAsset.value = state.selectedAsset
      selectedFile.value = state.selectedFile
      lastBuildId.value = state.lastBuildId
      
      dirty.value = false
      syncState.value = 'synced'
      
      console.log('[Project] Loaded from server')
    } catch (error) {
      console.error('[Project] Failed to load:', error)
      syncState.value = 'error'
      throw error
    } finally {
      loading.value = false
    }
  }
  
  /**
   * 保存项目到服务器
   * @param options.silent 是否静默保存 (不显示日志)
   */
  async function saveProject(options?: { silent?: boolean }) {
    if (!dirty.value && syncState.value === 'synced') {
      return // 没有更改，跳过保存
    }
    
    syncState.value = 'syncing'
    
    try {
      // 构建要保存的状态
      const state: ProjectState = {
        nodeMap: nodeMap.value,
        selectedAsset: selectedAsset.value,
        selectedFile: selectedFile.value,
        lastBuildId: lastBuildId.value
      }
      
      // 更新项目状态
      await projectApi.updateProject(state)
      
      // 持久化到磁盘
      await projectApi.saveProject()
      
      dirty.value = false
      syncState.value = 'synced'
      
      if (!options?.silent) {
        console.log('[Project] Saved to server')
      }
    } catch (error) {
      console.error('[Project] Failed to save:', error)
      syncState.value = 'error'
      throw error
    }
  }
  
  /**
   * 更新节点图数据
   * 会标记为 dirty 并触发自动保存
   */
  function setNodeMap(data: string | null) {
    nodeMap.value = data
    dirty.value = true
  }
  
  /**
   * 设置选中的资源文件
   */
  function setSelectedAsset(asset: string | null) {
    selectedAsset.value = asset
    dirty.value = true
  }
  
  /**
   * 设置当前打开的文件
   */
  function setSelectedFile(file: string | null) {
    selectedFile.value = file
    dirty.value = true
  }
  
  /**
   * 创建新项目
   * 会清空所有数据
   */
  async function createNew() {
    loading.value = true
    try {
      await projectApi.createNewProject()
      
      // 重置本地状态
      nodeMap.value = null
      selectedAsset.value = null
      selectedFile.value = null
      lastBuildId.value = null
      lastBuildResult.value = null
      
      dirty.value = false
      syncState.value = 'synced'
      
      console.log('[Project] Created new project')
    } catch (error) {
      console.error('[Project] Failed to create new:', error)
      throw error
    } finally {
      loading.value = false
    }
  }
  
  /**
   * 导出项目为 ZIP
   */
  async function exportZip() {
    const blob = await projectApi.exportProject()
    
    // 创建下载链接
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `coralinker-project-${new Date().toISOString().slice(0, 10)}.zip`
    a.click()
    URL.revokeObjectURL(url)
    
    console.log('[Project] Exported as ZIP')
  }
  
  /**
   * 执行构建
   */
  async function build() {
    loading.value = true
    try {
      const result = await projectApi.build()
      lastBuildResult.value = result
      
      if (result.ok && result.buildId) {
        lastBuildId.value = result.buildId
        dirty.value = true
      }
      
      return result
    } finally {
      loading.value = false
    }
  }
  
  return {
    // 状态
    nodeMap,
    selectedAsset,
    selectedFile,
    lastBuildId,
    loading,
    dirty,
    syncState,
    lastBuildResult,
    
    // 计算属性
    hasSelectedAsset,
    hasBuilt,
    
    // 方法
    loadProject,
    saveProject,
    setNodeMap,
    setSelectedAsset,
    setSelectedFile,
    createNew,
    exportZip,
    build
  }
})
