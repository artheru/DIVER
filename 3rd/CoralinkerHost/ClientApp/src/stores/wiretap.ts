/**
 * @file stores/wiretap.ts
 * @description WireTap 状态管理
 * 
 * 管理 WireTap 配置和日志：
 * - 每个端口的 WireTap 配置（TX/RX）
 * - 每个端口的 WireTap 日志数据
 * - 支持 Serial 和 CAN 两种格式
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { WireTapFlags, type WireTapDataEvent, type WireTapLogEntry, type CANMessageData } from '@/types'
import * as deviceApi from '@/api/device'

/** 单个端口的最大日志条目数 */
const MAX_LOG_ENTRIES = 500

/** 每个端口的 WireTap 配置 */
export interface PortWireTapConfig {
  rx: boolean
  tx: boolean
}

/** 端口 WireTap 日志 */
export interface PortWireTapLog {
  portIndex: number
  portType: string
  portName: string  // 端口名称（如 RS485-1）
  entries: WireTapLogEntry[]
}

/** 节点的 WireTap 状态 */
export interface NodeWireTapState {
  uuid: string
  nodeName: string
  portConfigs: Map<number, PortWireTapConfig>  // portIndex -> config
  portLogs: Map<number, PortWireTapLog>        // portIndex -> logs
}

export const useWireTapStore = defineStore('wiretap', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 节点 WireTap 状态 Map<uuid, NodeWireTapState> */
  const nodeStates = ref<Map<string, NodeWireTapState>>(new Map())
  
  // ============================================
  // 计算属性
  // ============================================
  
  /**
   * 获取所有有活动 WireTap 的节点
   */
  const activeNodes = computed(() => {
    const result: Array<{ uuid: string; nodeName: string; activePorts: number[] }> = []
    
    for (const [uuid, state] of nodeStates.value) {
      const activePorts: number[] = []
      for (const [portIndex, config] of state.portConfigs) {
        if (config.rx || config.tx) {
          activePorts.push(portIndex)
        }
      }
      if (activePorts.length > 0) {
        result.push({ uuid, nodeName: state.nodeName, activePorts })
      }
    }
    
    return result
  })
  
  // ============================================
  // 辅助函数
  // ============================================
  
  /**
   * 确保节点状态存在
   */
  function ensureNodeState(uuid: string, nodeName?: string): NodeWireTapState {
    let state = nodeStates.value.get(uuid)
    if (!state) {
      state = {
        uuid,
        nodeName: nodeName || uuid.slice(0, 8),
        portConfigs: new Map(),
        portLogs: new Map()
      }
      nodeStates.value.set(uuid, state)
    } else if (nodeName) {
      state.nodeName = nodeName
    }
    return state
  }
  
  /**
   * 将原始数据转换为字节数组
   * C# byte[] 在 JSON 中序列化为 base64 字符串
   */
  function toByteArray(data: number[] | string): number[] {
    if (Array.isArray(data)) {
      return data
    }
    // base64 字符串 -> 字节数组
    try {
      const binary = atob(data)
      return Array.from(binary, c => c.charCodeAt(0))
    } catch {
      console.warn('[WireTap] Failed to decode base64 data:', data)
      return []
    }
  }
  
  /**
   * 将字节数组格式化为十六进制字符串
   */
  function formatHex(data: number[]): string {
    return data.map(b => b.toString(16).padStart(2, '0').toUpperCase()).join(' ')
  }
  
  /**
   * 尝试将字节数组解码为 UTF-8 字符串
   */
  function tryDecodeUtf8(data: number[] | string): string | undefined {
    try {
      const bytes = toByteArray(data)
      const uint8Array = new Uint8Array(bytes)
      const decoded = new TextDecoder('utf-8', { fatal: false }).decode(uint8Array)
      // 检查是否有可打印字符
      const printable = decoded.replace(/[\x00-\x1F\x7F-\x9F]/g, '')
      if (printable.length > 0 && printable.length >= decoded.length * 0.5) {
        return decoded.replace(/[\x00-\x1F\x7F-\x9F]/g, '·')  // 用点替换不可打印字符
      }
      return undefined
    } catch {
      return undefined
    }
  }
  
  /**
   * 格式化时间戳
   */
  function formatTimestamp(isoString: string): string {
    const date = new Date(isoString)
    const HH = String(date.getHours()).padStart(2, '0')
    const mm = String(date.getMinutes()).padStart(2, '0')
    const ss = String(date.getSeconds()).padStart(2, '0')
    const sss = String(date.getMilliseconds()).padStart(3, '0')
    return `${HH}:${mm}:${ss}.${sss}`
  }
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 设置端口 WireTap 配置
   * @param uuid 节点 UUID
   * @param portIndex 端口索引
   * @param rx 是否监听 RX
   * @param tx 是否监听 TX
   * @param portType 端口类型（Serial/CAN）
   * @param portName 端口名称（如 RS485-1）
   */
  async function setPortWireTap(uuid: string, portIndex: number, rx: boolean, tx: boolean, portType?: string, portName?: string): Promise<boolean> {
    const state = ensureNodeState(uuid)
    
    // 计算 flags
    let flags = WireTapFlags.None
    if (rx) flags |= WireTapFlags.RX
    if (tx) flags |= WireTapFlags.TX
    
    try {
      const result = await deviceApi.setNodeWireTap(uuid, portIndex, flags)
      
      if (result.ok) {
        // 更新本地状态
        state.portConfigs.set(portIndex, { rx, tx })
        
        // 如果是启用，确保日志数组存在
        if (rx || tx) {
          const existingLog = state.portLogs.get(portIndex)
          if (!existingLog) {
            state.portLogs.set(portIndex, {
              portIndex,
              portType: portType || 'Serial',
              portName: portName || `Port ${portIndex}`,
              entries: []
            })
          } else {
            // 更新端口类型和名称（如果提供了）
            if (portType) existingLog.portType = portType
            if (portName) existingLog.portName = portName
          }
        }
      }
      
      return result.ok
    } catch (error) {
      console.error(`[WireTap] Failed to set wiretap for ${uuid} port ${portIndex}:`, error)
      return false
    }
  }
  
  /**
   * 获取端口 WireTap 配置
   */
  function getPortWireTap(uuid: string, portIndex: number): PortWireTapConfig {
    const state = nodeStates.value.get(uuid)
    return state?.portConfigs.get(portIndex) || { rx: false, tx: false }
  }
  
  /**
   * 获取端口 WireTap 日志
   */
  function getPortLogs(uuid: string, portIndex: number): WireTapLogEntry[] {
    const state = nodeStates.value.get(uuid)
    return state?.portLogs.get(portIndex)?.entries || []
  }
  
  /**
   * 获取节点所有有日志的端口
   */
  function getActivePortsForNode(uuid: string): number[] {
    const state = nodeStates.value.get(uuid)
    if (!state) return []
    
    const ports: number[] = []
    for (const [portIndex, config] of state.portConfigs) {
      if (config.rx || config.tx) {
        ports.push(portIndex)
      }
    }
    return ports.sort((a, b) => a - b)
  }
  
  /**
   * 处理 WireTap 数据事件（由 SignalR 调用）
   */
  function handleWireTapData(event: WireTapDataEvent) {
    const state = ensureNodeState(event.uuid, event.nodeName)
    
    // 确保端口日志存在
    let portLog = state.portLogs.get(event.portIndex)
    if (!portLog) {
      portLog = {
        portIndex: event.portIndex,
        portType: event.portType,
        portName: `Port ${event.portIndex}`,  // 默认名称，后续通过 loadFromBackend 更新
        entries: []
      }
      state.portLogs.set(event.portIndex, portLog)
    }
    
    // 处理 CAN 消息（解码 data 字段）
    let canMessage = event.canMessage
    if (canMessage && canMessage.data) {
      canMessage = {
        ...canMessage,
        data: toByteArray(canMessage.data)
      }
    }
    
    // 转换原始数据
    const rawBytes = toByteArray(event.rawData)
    
    // 创建日志条目
    const entry: WireTapLogEntry = {
      timestamp: formatTimestamp(event.timestamp),
      direction: event.direction === 0 ? 'RX' : 'TX',
      hexData: formatHex(rawBytes),
      rawBytes,
      dataLength: rawBytes.length,
      textData: event.portType === 'Serial' ? tryDecodeUtf8(rawBytes) : undefined,
      canMessage
    }
    
    // 添加日志
    portLog.entries.push(entry)
    
    // 限制日志数量
    if (portLog.entries.length > MAX_LOG_ENTRIES) {
      portLog.entries.splice(0, portLog.entries.length - MAX_LOG_ENTRIES)
    }
  }
  
  /**
   * 清空端口 WireTap 日志
   */
  function clearPortLogs(uuid: string, portIndex: number) {
    const state = nodeStates.value.get(uuid)
    const portLog = state?.portLogs.get(portIndex)
    if (portLog) {
      portLog.entries.length = 0
    }
  }
  
  /**
   * 清空节点所有 WireTap 日志
   */
  function clearNodeLogs(uuid: string) {
    const state = nodeStates.value.get(uuid)
    if (state) {
      for (const portLog of state.portLogs.values()) {
        portLog.entries.length = 0
      }
    }
  }
  
  /**
   * 清空所有节点的 WireTap 日志（Start 时调用）
   */
  function clearAllLogs() {
    for (const state of nodeStates.value.values()) {
      for (const portLog of state.portLogs.values()) {
        portLog.entries.length = 0
      }
    }
    console.log('[WireTap] Cleared all logs')
  }
  
  /**
   * 移除节点状态
   */
  function removeNode(uuid: string) {
    nodeStates.value.delete(uuid)
  }
  
  /**
   * 同步节点列表
   * @param nodes 节点列表
   */
  function syncNodes(nodes: Array<{ uuid: string; nodeName: string }>) {
    const currentUuids = new Set(nodes.map(n => n.uuid))
    
    // 更新/添加节点
    for (const node of nodes) {
      ensureNodeState(node.uuid, node.nodeName)
    }
    
    // 移除不存在的节点
    for (const uuid of nodeStates.value.keys()) {
      if (!currentUuids.has(uuid)) {
        nodeStates.value.delete(uuid)
      }
    }
  }
  
  /**
   * 从后端加载 WireTap 配置和日志（页面刷新时恢复状态）
   */
  async function loadFromBackend(): Promise<void> {
    try {
      // 并行获取 WireTap 配置、节点信息和日志
      const [wireTapResult, nodesResult, logsResult] = await Promise.all([
        deviceApi.getAllWireTapConfigs(),
        deviceApi.getAllNodes(),
        deviceApi.getAllWireTapLogs()
      ])
      
      // 构建节点信息映射（uuid -> portIndex -> {type, name}）
      interface PortInfo { type: string; name: string }
      const nodePortInfos = new Map<string, Map<number, PortInfo>>()
      if (nodesResult.ok && nodesResult.nodes) {
        for (const node of nodesResult.nodes) {
          const portInfos = new Map<number, PortInfo>()
          // 优先从 layout.ports 获取名称，其次从 portConfigs 获取
          const layoutPorts = node.layout?.ports || []
          node.portConfigs?.forEach((cfg, idx) => {
            const layoutPort = layoutPorts[idx]
            portInfos.set(idx, {
              type: layoutPort?.type || cfg.type || 'Serial',
              name: layoutPort?.name || cfg.name || `Port ${idx}`
            })
          })
          // 如果 portConfigs 为空但有 layout，也要处理
          if (!node.portConfigs?.length && layoutPorts.length > 0) {
            layoutPorts.forEach((p, idx) => {
              portInfos.set(idx, {
                type: p.type || 'Serial',
                name: p.name || `Port ${idx}`
              })
            })
          }
          nodePortInfos.set(node.uuid, portInfos)
        }
      }
      
      // 遍历所有节点的 WireTap 配置
      if (wireTapResult.ok && wireTapResult.configs) {
        for (const [uuid, portConfigs] of Object.entries(wireTapResult.configs)) {
          const state = ensureNodeState(uuid)
          const portInfos = nodePortInfos.get(uuid)
          
          // 应用每个端口的配置
          for (const portConfig of portConfigs) {
            const rx = (portConfig.flags & WireTapFlags.RX) !== 0
            const tx = (portConfig.flags & WireTapFlags.TX) !== 0
            
            state.portConfigs.set(portConfig.portIndex, { rx, tx })
            
            // 确保日志数组存在
            if (rx || tx) {
              // 从节点配置获取端口类型和名称
              const info = portInfos?.get(portConfig.portIndex)
              const portType = info?.type || 'Serial'
              const portName = info?.name || `Port ${portConfig.portIndex}`
              
              if (!state.portLogs.has(portConfig.portIndex)) {
                state.portLogs.set(portConfig.portIndex, {
                  portIndex: portConfig.portIndex,
                  portType,
                  portName,
                  entries: []
                })
              } else {
                // 更新已有日志的端口类型和名称
                const existingLog = state.portLogs.get(portConfig.portIndex)
                if (existingLog) {
                  existingLog.portType = portType
                  existingLog.portName = portName
                }
              }
            }
          }
        }
        console.log(`[WireTap] Loaded configs for ${Object.keys(wireTapResult.configs).length} nodes from backend`)
      }
      
      // 加载历史日志
      if (logsResult.ok && logsResult.logs) {
        let totalLogs = 0
        for (const [uuid, logEntries] of Object.entries(logsResult.logs)) {
          const state = ensureNodeState(uuid)
          const portInfos = nodePortInfos.get(uuid)
          
          for (const logEntry of logEntries) {
            // 确保端口日志存在
            let portLog = state.portLogs.get(logEntry.portIndex)
            if (!portLog) {
              const info = portInfos?.get(logEntry.portIndex)
              const portType = info?.type || logEntry.portType || 'Serial'
              const portName = info?.name || `Port ${logEntry.portIndex}`
              portLog = {
                portIndex: logEntry.portIndex,
                portType,
                portName,
                entries: []
              }
              state.portLogs.set(logEntry.portIndex, portLog)
            }
            
            // 转换并添加日志条目
            const rawBytes = toByteArray(logEntry.rawData)
            
            // 处理 CAN 消息
            let canMessage = logEntry.canMessage
            if (canMessage && canMessage.data) {
              canMessage = {
                ...canMessage,
                data: toByteArray(canMessage.data)
              }
            }
            
            const entry: WireTapLogEntry = {
              timestamp: logEntry.timestamp,
              direction: logEntry.direction === 0 ? 'RX' : 'TX',
              hexData: formatHex(rawBytes),
              rawBytes,
              dataLength: rawBytes.length,
              textData: logEntry.portType === 'Serial' ? tryDecodeUtf8(rawBytes) : undefined,
              canMessage: canMessage as WireTapLogEntry['canMessage']
            }
            
            portLog.entries.push(entry)
            totalLogs++
          }
        }
        console.log(`[WireTap] Loaded ${totalLogs} log entries from backend`)
      }
    } catch (error) {
      console.error('[WireTap] Failed to load configs from backend:', error)
    }
  }
  
  /**
   * 重置所有状态
   */
  function reset() {
    nodeStates.value.clear()
  }
  
  return {
    // 状态
    nodeStates,
    
    // 计算属性
    activeNodes,
    
    // 方法
    ensureNodeState,
    setPortWireTap,
    getPortWireTap,
    getPortLogs,
    getActivePortsForNode,
    handleWireTapData,
    clearPortLogs,
    clearNodeLogs,
    clearAllLogs,
    removeNode,
    syncNodes,
    loadFromBackend,
    reset
  }
})
