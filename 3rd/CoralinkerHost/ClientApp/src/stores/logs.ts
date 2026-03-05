/**
 * @file stores/logs.ts
 * @description 日志状态管理
 * 
 * 管理终端日志和节点日志：
 * - 终端日志 (构建输出、系统消息)
 * - 每个节点的独立日志（使用 seq 分页避免重复）
 * - 日志缓冲区管理
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as runtimeApi from '@/api/runtime'

/** 单条日志的最大保留数量 */
const MAX_LOG_LINES = 2000

/** 节点日志条目 */
export interface NodeLogEntry {
  hostTime: string
  mcuTimestampMs: number
  message: string
}

/** 节点日志信息 */
interface NodeLogInfo {
  uuid: string
  nodeName: string
  lastSeq: number
}

export const useLogStore = defineStore('logs', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 终端日志行 */
  const terminalLines = ref<string[]>([])
  
  /** Build 日志行 */
  const buildLines = ref<string[]>([])
  
  /** 节点日志 Map<uuid, NodeLogEntry[]> */
  const nodeLogs = ref<Map<string, NodeLogEntry[]>>(new Map())
  
  /** 节点信息 Map<uuid, NodeLogInfo> */
  const nodeInfos = ref<Map<string, NodeLogInfo>>(new Map())
  
  /** 当前激活的日志标签 ('terminal', 'build' 或 uuid) */
  const activeTab = ref<string>('terminal')
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 当前显示的终端/构建日志行 */
  const currentLines = computed(() => {
    if (activeTab.value === 'terminal') {
      return terminalLines.value
    }
    if (activeTab.value === 'build') {
      return buildLines.value
    }
    return [] as string[]
  })

  /** 当前节点日志条目（activeTab 为 uuid 时有效） */
  const currentNodeEntries = computed((): NodeLogEntry[] => {
    if (activeTab.value === 'terminal' || activeTab.value === 'build') {
      return []
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

  function appendTerminal(line: string) {
    terminalLines.value.push(line)
    if (terminalLines.value.length > MAX_LOG_LINES) {
      terminalLines.value.splice(0, terminalLines.value.length - MAX_LOG_LINES)
    }
  }

  function logUI(line: string) {
    appendTerminal(`[UI][${getTimestamp()}] ${line}`)
  }

  function appendBuild(line: string) {
    buildLines.value.push(line)
    if (buildLines.value.length > MAX_LOG_LINES) {
      buildLines.value.splice(0, buildLines.value.length - MAX_LOG_LINES)
    }
  }

  function logBuild(line: string) {
    appendBuild(`[${getTimestamp()}] ${line}`)
  }

  function clearBuild() {
    buildLines.value.length = 0
  }
  
  /**
   * 添加节点日志条目
   */
  function appendNodeLog(uuid: string, hostTime: string, message: string, mcuTimestampMs: number = 0) {
    ensureNodeTab(uuid)

    let logs = nodeLogs.value.get(uuid)
    if (!logs) {
      logs = []
      nodeLogs.value.set(uuid, logs)
    }

    logs.push({ hostTime, mcuTimestampMs, message })

    if (logs.length > MAX_LOG_LINES) {
      logs.splice(0, logs.length - MAX_LOG_LINES)
    }
  }
  
  function ensureNodeTab(uuid: string, nodeName?: string) {
    if (!nodeInfos.value.has(uuid)) {
      nodeInfos.value.set(uuid, {
        uuid,
        nodeName: nodeName || uuid.slice(0, 8),
        lastSeq: 0
      })
      
      if (!nodeLogs.value.has(uuid)) {
        nodeLogs.value.set(uuid, [])
      }
    } else if (nodeName) {
      const info = nodeInfos.value.get(uuid)!
      info.nodeName = nodeName
    }
  }
  
  function removeNodeTab(uuid: string) {
    nodeInfos.value.delete(uuid)
    nodeLogs.value.delete(uuid)
    
    if (activeTab.value === uuid) {
      activeTab.value = 'terminal'
    }
  }
  
  function switchTab(tabId: string) {
    activeTab.value = tabId
  }
  
  async function loadNodeLogs(uuid: string, maxCount = 500) {
    const info = nodeInfos.value.get(uuid)
    const afterSeq = info?.lastSeq || undefined
    
    try {
      const result = await runtimeApi.getNodeLogs(uuid, afterSeq, maxCount)
      
      if (!result.ok || !result.entries) {
        return
      }
      
      let logs = nodeLogs.value.get(uuid)
      if (!logs) {
        logs = []
        nodeLogs.value.set(uuid, logs)
      }
      
      for (const entry of result.entries) {
        logs.push({ hostTime: entry.timestamp, mcuTimestampMs: 0, message: entry.message })
      }
      
      if (logs.length > MAX_LOG_LINES) {
        logs.splice(0, logs.length - MAX_LOG_LINES)
      }
      
      if (info && result.latestSeq) {
        info.lastSeq = result.latestSeq
      }
      
      console.log(`[Logs] Loaded ${result.entries.length} logs for node ${uuid}, latestSeq=${result.latestSeq}`)
    } catch (error) {
      console.error(`[Logs] Failed to load logs for node ${uuid}:`, error)
    }
  }
  
  async function loadNodeHistory(uuid: string, maxCount = 1000) {
    try {
      const result = await runtimeApi.getNodeLogs(uuid, undefined, maxCount)
      
      if (!result.ok || !result.entries) {
        return
      }
      
      const logs: NodeLogEntry[] = result.entries.map(entry => ({
        hostTime: entry.timestamp,
        mcuTimestampMs: 0,
        message: entry.message
      }))
      
      nodeLogs.value.set(uuid, logs)
      
      const info = nodeInfos.value.get(uuid)
      if (info && result.latestSeq) {
        info.lastSeq = result.latestSeq
      }
      
      console.log(`[Logs] Loaded ${logs.length} history logs for node ${uuid}`)
    } catch (error) {
      console.error(`[Logs] Failed to load history for node ${uuid}:`, error)
    }
  }
  
  async function loadTerminalHistory() {
    try {
      const result = await runtimeApi.getTerminalLogs()
      
      if (!result.ok || !result.lines) {
        return
      }
      
      terminalLines.value = result.lines
      
      console.log(`[Logs] Loaded ${result.lines.length} terminal history logs`)
    } catch (error) {
      console.error(`[Logs] Failed to load terminal history:`, error)
    }
  }

  function clearCurrent() {
    if (activeTab.value === 'terminal') {
      terminalLines.value.length = 0
    } else if (activeTab.value === 'build') {
      buildLines.value.length = 0
    } else {
      const logs = nodeLogs.value.get(activeTab.value)
      if (logs) {
        logs.length = 0
      }

      const info = nodeInfos.value.get(activeTab.value)
      if (info) {
        info.lastSeq = 0
      }
      
      runtimeApi.clearNodeLogs(activeTab.value).catch(console.error)
    }
  }
  
  function clearTerminal() {
    terminalLines.value.length = 0
  }
  
  function clearAll() {
    terminalLines.value.length = 0
    nodeLogs.value.forEach(logs => logs.length = 0)
    nodeInfos.value.forEach(info => info.lastSeq = 0)
    
    runtimeApi.clearAllLogs().catch(console.error)
  }
  
  function clearAllNodeLogs() {
    nodeLogs.value.forEach(logs => logs.length = 0)
    nodeInfos.value.forEach(info => info.lastSeq = 0)
    console.log('[Logs] Cleared all node logs for new run')
  }
  
  async function syncNodeTabs(nodes: Array<{ uuid: string; nodeName: string }>) {
    const currentUuids = new Set(nodes.map(n => n.uuid))
    const newNodes: string[] = []
    
    for (const node of nodes) {
      const isNew = !nodeInfos.value.has(node.uuid)
      ensureNodeTab(node.uuid, node.nodeName)
      if (isNew) {
        newNodes.push(node.uuid)
      }
    }
    
    for (const uuid of nodeInfos.value.keys()) {
      if (!currentUuids.has(uuid)) {
        removeNodeTab(uuid)
      }
    }
    
    for (const uuid of newNodes) {
      await loadNodeHistory(uuid)
    }
  }
  
  return {
    // 状态
    terminalLines,
    buildLines,
    nodeLogs,
    nodeInfos,
    activeTab,
    
    // 计算属性
    currentLines,
    currentNodeEntries,
    nodeTabs,
    
    // 方法
    appendTerminal,
    logUI,
    appendBuild,
    logBuild,
    clearBuild,
    appendNodeLog,
    ensureNodeTab,
    removeNodeTab,
    switchTab,
    loadNodeLogs,
    loadNodeHistory,
    loadTerminalHistory,
    clearCurrent,
    clearTerminal,
    clearAll,
    clearAllNodeLogs,
    syncNodeTabs
  }
})
