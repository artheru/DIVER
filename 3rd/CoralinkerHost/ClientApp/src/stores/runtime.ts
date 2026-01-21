/**
 * @file stores/runtime.ts
 * @description 运行时状态管理
 * 
 * 管理 MCU 节点的运行时状态：
 * - 应用状态（Offline/Idle/Connecting/Running）
 * - 节点连接状态
 * - 变量值
 * - 可控变量列表
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as runtimeApi from '@/api/runtime'
import type { NodeSnapshot, VariableInfo, VariableValue, NodeStateInfo } from '@/types'

/**
 * 应用状态枚举
 */
export type AppState = 'offline' | 'idle' | 'connecting' | 'running' | 'stopping'

export const useRuntimeStore = defineStore('runtime', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 应用状态 */
  const appState = ref<AppState>('offline')
  
  /** 是否已连接 */
  const isConnected = ref(false)
  
  /** 是否正在运行 */
  const isRunning = ref(false)
  
  /** 会话类型 (如 DIVERSession) */
  const sessionType = ref('Unknown')
  
  /** 后端服务是否可用 */
  const isBackendAvailable = ref(false)
  
  /** 节点快照 Map<nodeId, NodeSnapshot> */
  const nodes = ref<Map<string, NodeSnapshot>>(new Map())
  
  /** 变量值 Map<varName, VariableValue> */
  const variables = ref<Map<string, VariableValue>>(new Map())
  
  /** 可控变量信息 Map<varName, VariableInfo> */
  const variableInfos = ref<Map<string, VariableInfo>>(new Map())
  
  /** 正在编辑的变量名 (编辑时跳过 SignalR 更新) */
  const editingVarName = ref<string | null>(null)
  
  /** 状态轮询定时器 */
  let statePollingTimer: ReturnType<typeof setInterval> | null = null
  
  /** 状态轮询间隔 (ms) */
  const STATE_POLLING_INTERVAL = 2000
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 节点列表 */
  const nodeList = computed(() => Array.from(nodes.value.values()))
  
  /** 变量列表 */
  const variableList = computed(() => Array.from(variables.value.values()))
  
  /** 可控变量名称集合 */
  const controllableVarNames = computed(() => {
    const names = new Set<string>()
    variableInfos.value.forEach((info, name) => {
      if (info.controllable) {
        names.add(name)
      }
    })
    return names
  })
  
  // ============================================
  // 权限计算属性
  // ============================================
  
  /** 是否可以编辑（New/Save/Load/AddNode/节点配置） */
  const canEdit = computed(() => {
    return appState.value === 'idle' && isBackendAvailable.value
  })
  
  /** 是否可以连接 */
  const canConnect = computed(() => {
    return appState.value === 'idle' && isBackendAvailable.value
  })
  
  /** 是否可以启动 */
  const canStart = computed(() => {
    return appState.value === 'idle' && isBackendAvailable.value
  })
  
  /** 是否可以停止 */
  const canStop = computed(() => {
    return appState.value === 'running'
  })
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 设置后端可用状态
   */
  function setBackendAvailable(available: boolean) {
    isBackendAvailable.value = available
    if (available && appState.value === 'offline') {
      appState.value = 'idle'
    } else if (!available) {
      appState.value = 'offline'
    }
  }
  
  /**
   * 连接所有节点（只负责 Open）
   */
  async function connect() {
    if (!canConnect.value) {
      throw new Error('Cannot connect in current state')
    }
    
    try {
      appState.value = 'connecting'
      const result = await runtimeApi.connect()
      
      if (result.ok) {
        isConnected.value = true
        appState.value = 'idle'
        
        // 获取可控变量列表
        await fetchControllableVariables()
        
        console.log('[Runtime] Connected to nodes')
      } else {
        appState.value = 'idle'
      }
      return result
    } catch (error) {
      appState.value = 'idle'
      console.error('[Runtime] Connect failed:', error)
      throw error
    }
  }
  
  /**
   * 启动执行（后端会处理完整流程：Connect → Configure → Program → Start）
   */
  async function start(): Promise<{ ok: boolean; error?: string }> {
    if (!canStart.value) {
      throw new Error('Cannot start in current state')
    }
    
    try {
      appState.value = 'connecting'
      console.log('[Runtime] Starting full sequence (Connect → Configure → Program → Start)...')
      
      // 后端 /api/start 现在会处理完整流程
      const result = await runtimeApi.start()
      
      if (result.ok) {
        isConnected.value = true
        isRunning.value = true
        appState.value = 'running'
        
        // 获取可控变量列表
        await fetchControllableVariables()
        
        // 启动状态轮询
        startStatePolling()
        
        console.log('[Runtime] Started successfully')
        return { ok: true }
      } else {
        appState.value = 'idle'
        const error = (result as { error?: string }).error || 'Start failed'
        console.error('[Runtime] Start failed:', error)
        return { ok: false, error }
      }
    } catch (error) {
      appState.value = 'idle'
      console.error('[Runtime] Start failed:', error)
      throw error
    }
  }
  
  /**
   * 停止执行
   */
  async function stop() {
    if (!canStop.value) {
      throw new Error('Cannot stop in current state')
    }
    
    try {
      appState.value = 'stopping'
      await runtimeApi.stop()
      
      // 停止状态轮询
      stopStatePolling()
      
      isRunning.value = false
      isConnected.value = false
      appState.value = 'idle'
      console.log('[Runtime] Stopped')
    } catch (error) {
      appState.value = 'running' // 恢复状态
      console.error('[Runtime] Stop failed:', error)
      throw error
    }
  }
  
  /**
   * 获取可控变量列表
   */
  async function fetchControllableVariables() {
    try {
      const result = await runtimeApi.getControllableVariables()
      
      variableInfos.value.clear()
      for (const info of result.variables) {
        variableInfos.value.set(info.name, info)
      }
      
      console.log(`[Runtime] Fetched ${result.variables.length} controllable variables`)
    } catch (error) {
      console.error('[Runtime] Failed to fetch controllable variables:', error)
    }
  }
  
  /**
   * 设置变量值
   * @param name 变量名
   * @param value 新值
   * @param typeHint 类型提示
   */
  async function setVariable(name: string, value: unknown, typeHint?: string) {
    try {
      // 对整数类型进行四舍五入
      let finalValue = value
      const intTypes = ['int', 'byte', 'sbyte', 'short', 'ushort', 'uint', 'long', 'ulong', 'int32', 'int16', 'uint32', 'uint16']
      if (typeHint && intTypes.includes(typeHint.toLowerCase()) && typeof value === 'number') {
        finalValue = Math.round(value)
      }
      
      const result = await runtimeApi.setVariable(name, finalValue, typeHint)
      
      if (result.ok) {
        // 更新本地变量值
        const existing = variables.value.get(name)
        if (existing) {
          existing.value = result.value
        }
        
        console.log(`[Runtime] Set ${name} = ${result.value}`)
      }
      
      return result
    } catch (error) {
      console.error(`[Runtime] Failed to set variable ${name}:`, error)
      throw error
    }
  }
  
  /**
   * 更新节点快照 (由 SignalR 调用)
   * 同时根据节点状态更新连接和运行状态
   */
  function updateNodeSnapshot(snapshot: NodeSnapshot) {
    nodes.value.set(snapshot.nodeId, snapshot)
    
    // 根据节点快照更新连接和运行状态
    updateConnectionState()
  }
  
  /**
   * 根据节点快照更新连接和运行状态
   * 如果有任何节点处于 running 状态，则 isRunning = true
   * 如果有任何节点处于 connected 状态，则 isConnected = true
   */
  function updateConnectionState() {
    let anyConnected = false
    let anyRunning = false
    
    for (const node of nodes.value.values()) {
      if (node.isConnected || node.runState !== 'offline') {
        anyConnected = true
      }
      if (node.runState === 'running') {
        anyRunning = true
      }
    }
    
    isConnected.value = anyConnected
    isRunning.value = anyRunning
  }
  
  /**
   * 更新变量值 (由 SignalR 调用)
   * 后端格式: { targetType: "DIVERSession", fields: [{name, type, value, direction, icon}, ...] }
   */
  function updateVariables(snapshot: unknown) {
    // 解析后端格式
    const data = snapshot as { targetType?: string; fields?: Array<{ name: string; type: string; value: unknown; direction?: string; icon?: string }> }
    
    if (!data || !Array.isArray(data.fields)) {
      console.warn('[Runtime] Invalid varsSnapshot format:', snapshot)
      return
    }
    
    // 更新 targetType (会话类型)
    sessionType.value = data.targetType || 'Unknown'
    
    for (const field of data.fields) {
      const name = field.name
      
      // 跳过正在编辑的变量
      if (name === editingVarName.value) {
        continue
      }
      
      const existing = variables.value.get(name)
      const info = variableInfos.value.get(name)
      
      if (existing) {
        existing.value = field.value
        existing.type = field.type || existing.type
      } else {
        variables.value.set(name, {
          name,
          value: field.value,
          type: field.type || info?.type || 'unknown',
          typeId: info?.typeId || 0
        })
      }
    }
  }
  
  /**
   * 设置正在编辑的变量
   * 编辑时该变量不会被 SignalR 更新覆盖
   */
  function setEditingVar(name: string | null) {
    editingVarName.value = name
  }
  
  /**
   * 刷新运行时快照
   */
  async function refreshSnapshot() {
    try {
      const snapshot = await runtimeApi.getRuntimeSnapshot()
      
      for (const node of snapshot.nodes) {
        nodes.value.set(node.nodeId, node)
      }
    } catch (error) {
      console.error('[Runtime] Failed to refresh snapshot:', error)
    }
  }
  
  /**
   * 更新单个节点状态
   */
  function updateNodeState(nodeId: string, state: NodeStateInfo) {
    const existing = nodes.value.get(nodeId)
    if (existing) {
      existing.runState = state.runState
      existing.isConnected = state.isConnected
      existing.isConfigured = state.isConfigured
      existing.isProgrammed = state.isProgrammed
      existing.mode = state.mode
    } else {
      nodes.value.set(nodeId, {
        nodeId: state.nodeId,
        runState: state.runState,
        isConnected: state.isConnected,
        isConfigured: state.isConfigured,
        isProgrammed: state.isProgrammed,
        mode: state.mode
      })
    }
    
    // 更新连接和运行状态
    updateConnectionState()
  }
  
  /**
   * 启动节点状态轮询
   */
  function startStatePolling() {
    if (statePollingTimer) return
    
    console.log('[Runtime] Starting state polling')
    statePollingTimer = setInterval(async () => {
      try {
        const result = await runtimeApi.getNodeStates()
        if (result.ok && result.nodes) {
          for (const state of result.nodes) {
            updateNodeState(state.nodeId, state)
          }
        }
      } catch (error) {
        console.error('[Runtime] State polling failed:', error)
      }
    }, STATE_POLLING_INTERVAL)
  }
  
  /**
   * 停止节点状态轮询
   */
  function stopStatePolling() {
    if (statePollingTimer) {
      console.log('[Runtime] Stopping state polling')
      clearInterval(statePollingTimer)
      statePollingTimer = null
    }
  }
  
  /**
   * 重置状态
   */
  function reset() {
    stopStatePolling()
    appState.value = isBackendAvailable.value ? 'idle' : 'offline'
    isConnected.value = false
    isRunning.value = false
    sessionType.value = 'Unknown'
    nodes.value.clear()
    variables.value.clear()
    variableInfos.value.clear()
    editingVarName.value = null
  }
  
  return {
    // 状态
    appState,
    isBackendAvailable,
    isConnected,
    isRunning,
    sessionType,
    nodes,
    variables,
    variableInfos,
    editingVarName,
    
    // 计算属性
    nodeList,
    variableList,
    controllableVarNames,
    canEdit,
    canConnect,
    canStart,
    canStop,
    
    // 方法
    setBackendAvailable,
    connect,
    start,
    stop,
    fetchControllableVariables,
    setVariable,
    updateNodeSnapshot,
    updateNodeState,
    updateVariables,
    setEditingVar,
    refreshSnapshot,
    startStatePolling,
    stopStatePolling,
    reset
  }
})
