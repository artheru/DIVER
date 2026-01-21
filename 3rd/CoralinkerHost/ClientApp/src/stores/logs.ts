/**
 * @file stores/logs.ts
 * @description 日志状态管理
 * 
 * 管理终端日志和节点日志：
 * - 终端日志 (构建输出、系统消息)
 * - 每个节点的独立日志
 * - 日志缓冲区管理
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as runtimeApi from '@/api/runtime'
import type { NodeLogInfo } from '@/types'

/** 单条日志的最大保留数量 */
const MAX_LOG_LINES = 2000

export const useLogStore = defineStore('logs', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 终端日志行 */
  const terminalLines = ref<string[]>([])
  
  /** 节点日志 Map<nodeId, string[]> */
  const nodeLogs = ref<Map<string, string[]>>(new Map())
  
  /** 节点信息 Map<nodeId, NodeLogInfo> */
  const nodeInfos = ref<Map<string, NodeLogInfo>>(new Map())
  
  /** 当前激活的日志标签 ('terminal' 或 nodeId) */
  const activeTab = ref<string>('terminal')
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 当前显示的日志行 */
  const currentLines = computed(() => {
    if (activeTab.value === 'terminal') {
      return terminalLines.value
    }
    return nodeLogs.value.get(activeTab.value) || []
  })
  
  /** 节点标签列表 */
  const nodeTabs = computed(() => {
    return Array.from(nodeInfos.value.values())
  })
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 生成时间戳字符串
   */
  function getTimestamp(): string {
    const now = new Date()
    const MM = String(now.getMonth() + 1).padStart(2, '0')
    const DD = String(now.getDate()).padStart(2, '0')
    const HH = String(now.getHours()).padStart(2, '0')
    const mm = String(now.getMinutes()).padStart(2, '0')
    const ss = String(now.getSeconds()).padStart(2, '0')
    const sss = String(now.getMilliseconds()).padStart(3, '0')
    return `${MM}-${DD} ${HH}:${mm}:${ss}.${sss}`
  }

  /**
   * 添加终端日志行 (原始，不加时间戳，用于接收后端日志)
   * @param line 日志内容
   */
  function appendTerminal(line: string) {
    terminalLines.value.push(line)
    
    // 限制日志数量，防止内存溢出
    if (terminalLines.value.length > MAX_LOG_LINES) {
      terminalLines.value.splice(0, terminalLines.value.length - MAX_LOG_LINES)
    }
  }

  /**
   * 添加前端本地日志行 (带 [UI] 前缀和时间戳)
   * @param line 日志内容
   */
  function logUI(line: string) {
    appendTerminal(`[UI][${getTimestamp()}] ${line}`)
  }
  
  /**
   * 添加节点日志行
   * @param nodeId 节点 ID
   * @param line 日志内容
   */
  function appendNodeLog(nodeId: string, line: string) {
    let logs = nodeLogs.value.get(nodeId)
    
    if (!logs) {
      logs = []
      nodeLogs.value.set(nodeId, logs)
    }
    
    logs.push(line)
    
    // 限制日志数量
    if (logs.length > MAX_LOG_LINES) {
      logs.splice(0, logs.length - MAX_LOG_LINES)
    }
  }
  
  /**
   * 确保节点日志标签存在
   * @param nodeId 节点 ID
   * @param nodeName 节点显示名称
   */
  function ensureNodeTab(nodeId: string, nodeName?: string) {
    if (!nodeInfos.value.has(nodeId)) {
      nodeInfos.value.set(nodeId, {
        nodeId,
        nodeName: nodeName || nodeId
      })
      
      // 初始化日志数组
      if (!nodeLogs.value.has(nodeId)) {
        nodeLogs.value.set(nodeId, [])
      }
    } else if (nodeName) {
      // 更新节点名称
      const info = nodeInfos.value.get(nodeId)!
      info.nodeName = nodeName
    }
  }
  
  /**
   * 移除节点日志标签
   * @param nodeId 节点 ID
   */
  function removeNodeTab(nodeId: string) {
    nodeInfos.value.delete(nodeId)
    nodeLogs.value.delete(nodeId)
    
    // 如果正在查看被移除的标签，切换回终端
    if (activeTab.value === nodeId) {
      activeTab.value = 'terminal'
    }
  }
  
  /**
   * 切换日志标签
   * @param tabId 'terminal' 或 nodeId
   */
  function switchTab(tabId: string) {
    activeTab.value = tabId
  }
  
  /**
   * 加载节点历史日志
   * @param nodeId 节点 ID
   * @param limit 加载数量
   */
  async function loadNodeHistory(nodeId: string, limit = 1000) {
    try {
      const result = await runtimeApi.getNodeLogs(nodeId, 0, limit)
      
      const logs = nodeLogs.value.get(nodeId) || []
      logs.length = 0 // 清空现有
      logs.push(...result.lines)
      
      nodeLogs.value.set(nodeId, logs)
      
      console.log(`[Logs] Loaded ${result.lines.length} history lines for node ${nodeId}`)
    } catch (error) {
      console.error(`[Logs] Failed to load history for node ${nodeId}:`, error)
    }
  }
  
  /**
   * 清空当前标签的日志
   */
  function clearCurrent() {
    if (activeTab.value === 'terminal') {
      terminalLines.value.length = 0
    } else {
      const logs = nodeLogs.value.get(activeTab.value)
      if (logs) {
        logs.length = 0
      }
    }
  }
  
  /**
   * 清空终端日志
   */
  function clearTerminal() {
    terminalLines.value.length = 0
  }
  
  /**
   * 清空所有日志
   */
  function clearAll() {
    terminalLines.value.length = 0
    nodeLogs.value.forEach(logs => logs.length = 0)
  }
  
  /**
   * 同步节点标签与图中的节点
   * @param nodeIds 当前图中的节点 ID 列表
   * @param nodeNames 节点名称映射
   */
  function syncNodeTabs(nodeIds: string[], nodeNames: Map<string, string>) {
    // 添加新节点
    for (const nodeId of nodeIds) {
      ensureNodeTab(nodeId, nodeNames.get(nodeId))
    }
    
    // 移除不存在的节点
    const currentIds = new Set(nodeIds)
    for (const nodeId of nodeInfos.value.keys()) {
      if (!currentIds.has(nodeId)) {
        removeNodeTab(nodeId)
      }
    }
  }
  
  return {
    // 状态
    terminalLines,
    nodeLogs,
    nodeInfos,
    activeTab,
    
    // 计算属性
    currentLines,
    nodeTabs,
    
    // 方法
    appendTerminal,
    logUI,
    appendNodeLog,
    ensureNodeTab,
    removeNodeTab,
    switchTab,
    loadNodeHistory,
    clearCurrent,
    clearTerminal,
    clearAll,
    syncNodeTabs
  }
})
