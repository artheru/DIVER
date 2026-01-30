/**
 * @file stores/ui.ts
 * @description UI 状态管理
 * 
 * 管理全局 UI 状态：
 * - 当前视图模式 (图/编辑器)
 * - 对话框状态
 * - 通知消息
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

/** 视图模式 */
export type ViewMode = 'graph' | 'editor'

/** 通知类型 */
export type NotificationType = 'info' | 'success' | 'warning' | 'error'

/** 通知消息 */
export interface Notification {
  id: string
  type: NotificationType
  title: string
  message?: string
  duration?: number
}

export const useUiStore = defineStore('ui', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 当前视图模式 */
  const viewMode = ref<ViewMode>('graph')
  
  /** 是否显示新项目对话框 */
  const showNewProjectDialog = ref(false)
  
  /** 是否显示新文件对话框 */
  const showNewFileDialog = ref(false)
  
  /** 是否显示确认对话框 */
  const showConfirmDialog = ref(false)
  
  /** 确认对话框配置 */
  const confirmConfig = ref<{
    title: string
    message: string
    onConfirm: () => void
    onCancel?: () => void
  } | null>(null)
  
  /** 通知队列 */
  const notifications = ref<Notification[]>([])
  
  /** 应用是否已初始化 */
  const initialized = ref(false)

  /** 源码跳转请求 */
  const sourceJumpRequest = ref<{ file: string; line: number } | null>(null)
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 是否在图视图 */
  const isGraphView = computed(() => viewMode.value === 'graph')
  
  /** 是否在编辑器视图 */
  const isEditorView = computed(() => viewMode.value === 'editor')
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 切换视图模式
   */
  function setViewMode(mode: ViewMode) {
    viewMode.value = mode
  }
  
  /**
   * 切换到图视图
   */
  function showGraph() {
    viewMode.value = 'graph'
  }
  
  /**
   * 切换到编辑器视图
   */
  function showEditor() {
    viewMode.value = 'editor'
  }
  
  /**
   * 显示确认对话框
   */
  function confirm(config: {
    title: string
    message: string
    onConfirm: () => void
    onCancel?: () => void
  }) {
    confirmConfig.value = config
    showConfirmDialog.value = true
  }
  
  /**
   * 关闭确认对话框
   */
  function closeConfirm() {
    showConfirmDialog.value = false
    confirmConfig.value = null
  }
  
  /**
   * 添加通知
   */
  function notify(
    type: NotificationType, 
    title: string, 
    message?: string, 
    duration = 3000
  ) {
    const notification: Notification = {
      id: `notif-${Date.now()}`,
      type,
      title,
      message,
      duration
    }
    
    notifications.value.push(notification)
    
    // 自动移除
    if (duration > 0) {
      setTimeout(() => {
        removeNotification(notification.id)
      }, duration)
    }
    
    return notification.id
  }
  
  /**
   * 移除通知
   */
  function removeNotification(id: string) {
    const index = notifications.value.findIndex(n => n.id === id)
    if (index !== -1) {
      notifications.value.splice(index, 1)
    }
  }
  
  /**
   * 显示成功通知
   */
  function success(title: string, message?: string) {
    return notify('success', title, message)
  }
  
  /**
   * 显示错误通知
   */
  function error(title: string, message?: string) {
    return notify('error', title, message, 5000)
  }
  
  /**
   * 显示警告通知
   */
  function warning(title: string, message?: string) {
    return notify('warning', title, message, 4000)
  }
  
  /**
   * 显示信息通知
   */
  function info(title: string, message?: string) {
    return notify('info', title, message)
  }
  
  /**
   * 标记应用已初始化
   */
  function setInitialized() {
    initialized.value = true
  }

  /**
   * 跳转到源码
   * 切换到编辑器视图并跳转到指定文件和行
   */
  function gotoSource(file: string, line: number) {
    sourceJumpRequest.value = { file, line }
    viewMode.value = 'editor'
  }

  /**
   * 清除源码跳转请求
   */
  function clearSourceJumpRequest() {
    sourceJumpRequest.value = null
  }
  
  return {
    // 状态
    viewMode,
    showNewProjectDialog,
    showNewFileDialog,
    showConfirmDialog,
    confirmConfig,
    notifications,
    initialized,
    sourceJumpRequest,
    
    // 计算属性
    isGraphView,
    isEditorView,
    
    // 方法
    setViewMode,
    showGraph,
    showEditor,
    confirm,
    closeConfirm,
    notify,
    removeNotification,
    success,
    error,
    warning,
    info,
    setInitialized,
    gotoSource,
    clearSourceJumpRequest
  }
})
