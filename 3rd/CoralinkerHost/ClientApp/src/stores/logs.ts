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

/** 节点日志信息 */
interface NodeLogInfo {
  uuid: string
  nodeName: string
  lastSeq: number  // 最后获取的 seq
}

export const useLogStore = defineStore('logs', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 终端日志行 */
  const terminalLines = ref<string[]>([])
  
  /** Build 日志行 */
  const buildLines = ref<string[]>([])
  
  /** 节点日志 Map<uuid, string[]> */
  const nodeLogs = ref<Map<string, string[]>>(new Map())
  
  /** 节点信息 Map<uuid, NodeLogInfo> */
  const nodeInfos = ref<Map<string, NodeLogInfo>>(new Map())
  
  /** 当前激活的日志标签 ('terminal', 'build' 或 uuid) */
  const activeTab = ref<string>('terminal')
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 当前显示的日志行 */
  const currentLines = computed(() => {
    if (activeTab.value === 'terminal') {
      return terminalLines.value
    }
    if (activeTab.value === 'build') {
      return buildLines.value
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
   * 添加 Build 日志行
   * @param line 日志内容
   */
  function appendBuild(line: string) {
    buildLines.value.push(line)
    
    // 限制日志数量
    if (buildLines.value.length > MAX_LOG_LINES) {
      buildLines.value.splice(0, buildLines.value.length - MAX_LOG_LINES)
    }
  }

  /**
   * 添加 Build 日志行 (带时间戳)
   * @param line 日志内容
   */
  function logBuild(line: string) {
    appendBuild(`[${getTimestamp()}] ${line}`)
  }

  /**
   * 清空 Build 日志
   */
  function clearBuild() {
    buildLines.value.length = 0
  }
  
  /**
   * 添加节点日志行（由 SignalR 调用）
   * @param uuid 节点 UUID
   * @param line 日志内容
   */
  function appendNodeLog(uuid: string, line: string) {
    // 确保节点标签存在
    ensureNodeTab(uuid)
    
    let logs = nodeLogs.value.get(uuid)
    
    if (!logs) {
      logs = []
      nodeLogs.value.set(uuid, logs)
    }
    
    logs.push(line)
    
    // 限制日志数量
    if (logs.length > MAX_LOG_LINES) {
      logs.splice(0, logs.length - MAX_LOG_LINES)
    }
  }
  
  /**
   * 确保节点日志标签存在
   * @param uuid 节点 UUID
   * @param nodeName 节点显示名称
   */
  function ensureNodeTab(uuid: string, nodeName?: string) {
    if (!nodeInfos.value.has(uuid)) {
      nodeInfos.value.set(uuid, {
        uuid,
        nodeName: nodeName || uuid.slice(0, 8),
        lastSeq: 0
      })
      
      // 初始化日志数组
      if (!nodeLogs.value.has(uuid)) {
        nodeLogs.value.set(uuid, [])
      }
    } else if (nodeName) {
      // 更新节点名称
      const info = nodeInfos.value.get(uuid)!
      info.nodeName = nodeName
    }
  }
  
  /**
   * 移除节点日志标签
   * @param uuid 节点 UUID
   */
  function removeNodeTab(uuid: string) {
    nodeInfos.value.delete(uuid)
    nodeLogs.value.delete(uuid)
    
    // 如果正在查看被移除的标签，切换回终端
    if (activeTab.value === uuid) {
      activeTab.value = 'terminal'
    }
  }
  
  /**
   * 切换日志标签
   * @param tabId 'terminal' 或 uuid
   */
  function switchTab(tabId: string) {
    activeTab.value = tabId
  }
  
  /**
   * 加载节点日志（使用 seq 分页，避免重复）
   * @param uuid 节点 UUID
   * @param maxCount 最大加载数量
   */
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
      
      // 添加新日志
      for (const entry of result.entries) {
        logs.push(`[${entry.timestamp}] ${entry.message}`)
      }
      
      // 限制日志数量
      if (logs.length > MAX_LOG_LINES) {
        logs.splice(0, logs.length - MAX_LOG_LINES)
      }
      
      // 更新 lastSeq
      if (info && result.latestSeq) {
        info.lastSeq = result.latestSeq
      }
      
      console.log(`[Logs] Loaded ${result.entries.length} logs for node ${uuid}, latestSeq=${result.latestSeq}`)
    } catch (error) {
      console.error(`[Logs] Failed to load logs for node ${uuid}:`, error)
    }
  }
  
  /**
   * 加载节点历史日志（首次加载，不使用 afterSeq）
   * @param uuid 节点 UUID
   * @param maxCount 最大加载数量
   */
  async function loadNodeHistory(uuid: string, maxCount = 1000) {
    try {
      const result = await runtimeApi.getNodeLogs(uuid, undefined, maxCount)
      
      if (!result.ok || !result.entries) {
        return
      }
      
      const logs: string[] = []
      for (const entry of result.entries) {
        logs.push(`[${entry.timestamp}] ${entry.message}`)
      }
      
      nodeLogs.value.set(uuid, logs)
      
      // 更新 lastSeq
      const info = nodeInfos.value.get(uuid)
      if (info && result.latestSeq) {
        info.lastSeq = result.latestSeq
      }
      
      console.log(`[Logs] Loaded ${logs.length} history logs for node ${uuid}`)
    } catch (error) {
      console.error(`[Logs] Failed to load history for node ${uuid}:`, error)
    }
  }
  
  /**
   * 加载 Terminal 历史日志（前端初始化时调用）
   */
  async function loadTerminalHistory() {
    try {
      const result = await runtimeApi.getTerminalLogs()
      
      if (!result.ok || !result.lines) {
        return
      }
      
      // 用历史日志替换当前内容（避免重复）
      terminalLines.value = result.lines
      
      console.log(`[Logs] Loaded ${result.lines.length} terminal history logs`)
    } catch (error) {
      console.error(`[Logs] Failed to load terminal history:`, error)
    }
  }

  /**
   * 清空当前标签的日志
   */
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
      
      // 重置 lastSeq
      const info = nodeInfos.value.get(activeTab.value)
      if (info) {
        info.lastSeq = 0
      }
      
      // 调用后端清空
      runtimeApi.clearNodeLogs(activeTab.value).catch(console.error)
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
    nodeInfos.value.forEach(info => info.lastSeq = 0)
    
    // 调用后端清空
    runtimeApi.clearAllLogs().catch(console.error)
  }
  
  /**
   * 同步节点标签与节点列表
   * @param nodes 节点列表 [{uuid, nodeName}, ...]
   */
  async function syncNodeTabs(nodes: Array<{ uuid: string; nodeName: string }>) {
    const currentUuids = new Set(nodes.map(n => n.uuid))
    const newNodes: string[] = []
    
    // 添加新节点
    for (const node of nodes) {
      const isNew = !nodeInfos.value.has(node.uuid)
      ensureNodeTab(node.uuid, node.nodeName)
      if (isNew) {
        newNodes.push(node.uuid)
      }
    }
    
    // 移除不存在的节点
    for (const uuid of nodeInfos.value.keys()) {
      if (!currentUuids.has(uuid)) {
        removeNodeTab(uuid)
      }
    }
    
    // 为新节点加载历史日志
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
    syncNodeTabs
  }
})
