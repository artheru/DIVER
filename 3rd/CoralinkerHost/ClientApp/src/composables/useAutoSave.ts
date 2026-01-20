/**
 * @file composables/useAutoSave.ts
 * @description 自动保存组合式函数
 * 
 * 功能：
 * - 防抖保存（避免频繁保存）
 * - 页面关闭前提示未保存更改
 * - 同步状态指示
 */

import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useProjectStore } from '@/stores/project'
import { storeToRefs } from 'pinia'

/** 默认防抖延迟 (毫秒) */
const DEFAULT_DEBOUNCE_MS = 700

export function useAutoSave(debounceMs = DEFAULT_DEBOUNCE_MS) {
  // ============================================
  // Store 引用
  // ============================================
  
  const projectStore = useProjectStore()
  const { dirty, syncState } = storeToRefs(projectStore)
  
  // ============================================
  // 状态
  // ============================================
  
  /** 是否启用自动保存 */
  const enabled = ref(true)
  
  /** 防抖定时器 */
  let saveTimer: ReturnType<typeof setTimeout> | null = null
  
  // ============================================
  // 方法
  // ============================================
  
  /**
   * 调度自动保存
   * 使用防抖机制，避免频繁保存
   */
  function scheduleAutoSave(reason = 'change') {
    if (!enabled.value) return
    
    // 清除之前的定时器
    if (saveTimer) {
      clearTimeout(saveTimer)
    }
    
    // 设置新的定时器
    saveTimer = setTimeout(async () => {
      saveTimer = null
      
      try {
        await projectStore.saveProject({ silent: true })
      } catch (err) {
        console.warn(`[AutoSave] Failed (${reason}):`, err)
      }
    }, debounceMs)
  }
  
  /**
   * 立即保存（取消防抖）
   */
  async function saveNow() {
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
    
    await projectStore.saveProject()
  }
  
  /**
   * 启用自动保存
   */
  function enable() {
    enabled.value = true
  }
  
  /**
   * 禁用自动保存
   */
  function disable() {
    enabled.value = false
    
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
  }
  
  // ============================================
  // 监听 dirty 状态变化
  // ============================================
  
  watch(dirty, (isDirty) => {
    if (isDirty && enabled.value) {
      scheduleAutoSave('dirty')
    }
  })
  
  // ============================================
  // 页面关闭前警告
  // ============================================
  
  function handleBeforeUnload(event: BeforeUnloadEvent) {
    if (dirty.value) {
      // 现代浏览器会忽略自定义消息，但需要设置 returnValue
      event.preventDefault()
      event.returnValue = ''
      return ''
    }
  }
  
  onMounted(() => {
    window.addEventListener('beforeunload', handleBeforeUnload)
  })
  
  onUnmounted(() => {
    window.removeEventListener('beforeunload', handleBeforeUnload)
    
    // 清理定时器
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
  })
  
  return {
    enabled,
    syncState,
    dirty,
    scheduleAutoSave,
    saveNow,
    enable,
    disable
  }
}
