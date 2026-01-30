/**
 * @file stores/runtime.ts
 * @description 运行时状态管理
 * 
 * 管理 MCU 节点的运行时状态：
 * - 应用状态（Offline/Idle/Running）
 * - 节点状态
 * - 变量值
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as runtimeApi from '@/api/runtime'
import * as deviceApi from '@/api/device'
import type { NodeStateSnapshot, NodeFullInfo, CartFieldMeta, VariableValue } from '@/types'

/**
 * 应用状态枚举
 */
export type AppState = 'offline' | 'idle' | 'starting' | 'running' | 'stopping'

export const useRuntimeStore = defineStore('runtime', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 应用状态 */
  const appState = ref<AppState>('offline')
  
  /** 后端服务是否可用 */
  const isBackendAvailable = ref(false)
  
  /** 会话类型 */
  const sessionType = ref('DIVERSession')
  
  /** 节点状态 Map<uuid, NodeStateSnapshot> */
  const nodeStates = ref<Map<string, NodeStateSnapshot>>(new Map())
  
  /** 后端会话状态（作为真实状态来源） */
  const backendSessionRunning = ref(false)
  
  /** 节点完整信息 Map<uuid, NodeFullInfo> */
  const nodeInfos = ref<Map<string, NodeFullInfo>>(new Map())
  
  /** 变量值 Map<name, VariableValue> */
  const variables = ref<Map<string, VariableValue>>(new Map())
  
  /** 变量可控信息 Map<name, controllable> */
  const variableControllable = ref<Map<string, boolean>>(new Map())
  
  /** 字段元信息列表（不需要 Start 就能获取） */
  const fieldMetas = ref<CartFieldMeta[]>([])
  
  /** 正在编辑的变量名 */
  const editingVarName = ref<string | null>(null)
  
  /** 状态轮询定时器 */
  let statePollingTimer: ReturnType<typeof setInterval> | null = null
  const STATE_POLLING_INTERVAL = 2000
  
  // ============================================
  // 计算属性
  // ============================================
  
  /** 是否已连接（任意节点） */
  const isConnected = computed(() => {
    for (const state of nodeStates.value.values()) {
      if (state.isConnected) return true
    }
    return false
  })
  
  /** 是否正在运行 */
  const isRunning = computed(() => appState.value === 'running')
  
  /** 节点列表 */
  const nodeList = computed(() => Array.from(nodeStates.value.values()))
  
  /** 节点信息列表 */
  const nodeInfoList = computed(() => Array.from(nodeInfos.value.values()))
  
  /** 变量列表 */
  const variableList = computed(() => Array.from(variables.value.values()))
  
  /** 可控变量名称集合 */
  const controllableVarNames = computed(() => {
    const names = new Set<string>()
    variableControllable.value.forEach((controllable, name) => {
      if (controllable) names.add(name)
    })
    return names
  })
  
  // ============================================
  // 权限计算属性
  // ============================================
  
  /** 是否可以编辑节点配置 */
  const canEdit = computed(() => appState.value === 'idle' && isBackendAvailable.value)
  
  /** 是否可以启动 */
  const canStart = computed(() => appState.value === 'idle' && isBackendAvailable.value)
  
  /** 是否可以停止 */
  const canStop = computed(() => appState.value === 'running')
  
  // ============================================
  // 操作方法
  // ============================================
  
  /**
   * 设置后端可用状态
   */
  async function setBackendAvailable(available: boolean) {
    isBackendAvailable.value = available
    if (available && appState.value === 'offline') {
      appState.value = 'idle'
      // 同步后端会话状态，确保前端状态与后端一致
      await syncSessionState()
    } else if (!available) {
      appState.value = 'offline'
      backendSessionRunning.value = false
    }
  }
  
  /**
   * 刷新所有节点信息
   */
  async function refreshNodes() {
    try {
      const result = await deviceApi.getAllNodes()
      if (result.ok) {
        nodeInfos.value.clear()
        for (const node of result.nodes) {
          nodeInfos.value.set(node.uuid, node)
        }
      }
    } catch (error) {
      console.error('[Runtime] Failed to refresh nodes:', error)
    }
  }
  
  /**
   * 刷新节点状态
   */
  async function refreshNodeStates() {
    try {
      const result = await deviceApi.getAllNodeStates()
      if (result.ok) {
        for (const state of result.nodes) {
          nodeStates.value.set(state.uuid, state)
        }
      }
    } catch (error) {
      console.error('[Runtime] Failed to refresh node states:', error)
    }
  }
  
  /**
   * 同步后端会话状态（作为状态真实来源）
   */
  async function syncSessionState() {
    try {
      const result = await runtimeApi.getSessionState()
      if (result.ok && result.isRunning !== undefined) {
        backendSessionRunning.value = result.isRunning
        
        // 同步前端状态：后端状态是真实来源
        if (result.isRunning && appState.value !== 'running' && appState.value !== 'stopping') {
          appState.value = 'running'
        } else if (!result.isRunning && appState.value === 'running') {
          appState.value = 'idle'
        }
      }
    } catch (error) {
      console.error('[Runtime] Failed to sync session state:', error)
    }
  }
  
  /**
   * 根据节点状态更新应用状态
   * 注意：后端会话状态优先，此函数仅作为辅助
   */
  function updateAppStateFromNodes() {
    // 如果后端说正在运行，前端必须同步
    if (backendSessionRunning.value) {
      if (appState.value !== 'running' && appState.value !== 'stopping') {
        appState.value = 'running'
      }
      return
    }
    
    // 如果后端没在运行，检查节点状态（用于检测意外启动等情况）
    let anyRunning = false
    for (const state of nodeStates.value.values()) {
      if (state.runState === 'running') {
        anyRunning = true
        break
      }
    }
    
    if (anyRunning && appState.value !== 'running') {
      appState.value = 'running'
    } else if (!anyRunning && appState.value === 'running') {
      appState.value = 'idle'
    }
  }
  
  /**
   * 启动会话
   */
  async function start(): Promise<{ ok: boolean; error?: string }> {
    if (!canStart.value) {
      return { ok: false, error: 'Cannot start in current state' }
    }
    
    try {
      appState.value = 'starting'
      console.log('[Runtime] Starting session...')
      
      const result = await runtimeApi.start()
      
      if (result.ok) {
        // 后端启动成功，标记后端状态
        backendSessionRunning.value = true
        appState.value = 'running'
        
        // 刷新变量列表
        await refreshVariables()
        
        // 启动状态轮询
        startStatePolling()
        
        console.log(`[Runtime] Started: ${result.successNodes}/${result.totalNodes} nodes`)
        return { ok: true }
      } else {
        // 启动失败，同步后端状态确保一致
        await syncSessionState()
        if (!backendSessionRunning.value) {
          appState.value = 'idle'
        }
        const error = result.error || 'Start failed'
        console.error('[Runtime] Start failed:', error)
        return { ok: false, error }
      }
    } catch (error) {
      // 异常情况，同步后端状态确保一致
      await syncSessionState()
      if (!backendSessionRunning.value) {
        appState.value = 'idle'
      }
      const message = error instanceof Error ? error.message : 'Unknown error'
      console.error('[Runtime] Start failed:', message)
      return { ok: false, error: message }
    }
  }
  
  /**
   * 停止会话
   */
  async function stop(): Promise<{ ok: boolean; error?: string }> {
    if (!canStop.value) {
      return { ok: false, error: 'Cannot stop in current state' }
    }
    
    try {
      appState.value = 'stopping'
      console.log('[Runtime] Stopping session...')
      
      // 停止状态轮询
      stopStatePolling()
      
      await runtimeApi.stop()
      
      // 后端已停止，更新状态
      backendSessionRunning.value = false
      appState.value = 'idle'
      
      // 刷新节点状态
      await refreshNodeStates()
      
      console.log('[Runtime] Stopped')
      return { ok: true }
    } catch (error) {
      // 停止失败，同步后端状态确保一致
      await syncSessionState()
      const message = error instanceof Error ? error.message : 'Unknown error'
      console.error('[Runtime] Stop failed:', message)
      return { ok: false, error: message }
    }
  }
  
  /**
   * 刷新字段元信息（不需要 Start，用于遥控器绑定）
   */
  async function refreshFieldMetas() {
    try {
      const result = await runtimeApi.getFieldMetas()
      if (result.ok) {
        fieldMetas.value = result.fields
        // 同时更新 variableControllable
        variableControllable.value.clear()
        for (const f of result.fields) {
          variableControllable.value.set(f.name, !f.isLowerIO)
        }
      }
    } catch (error) {
      console.error('[Runtime] Failed to refresh field metas:', error)
    }
  }
  
  /**
   * 刷新变量列表
   */
  async function refreshVariables() {
    try {
      const result = await runtimeApi.getAllVariables()
      if (result.ok) {
        for (const v of result.variables) {
          variables.value.set(v.name, {
            name: v.name,
            value: v.value,
            type: v.type,
            typeId: v.typeId
          })
          variableControllable.value.set(v.name, !v.isLowerIO)
        }
      }
    } catch (error) {
      console.error('[Runtime] Failed to refresh variables:', error)
    }
  }
  
  /**
   * 设置变量值
   */
  async function setVariable(name: string, value: unknown, typeHint?: string) {
    try {
      // 整数类型四舍五入
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
   * 更新节点状态（由 SignalR 调用）
   */
  function updateNodeState(uuid: string, state: NodeStateSnapshot) {
    nodeStates.value.set(uuid, state)
    updateAppStateFromNodes()
  }
  
  /**
   * 更新变量值（由 SignalR 调用）
   */
  function updateVariables(snapshot: unknown) {
    const data = snapshot as {
      targetType?: string
      fields?: Array<{ name: string; type: string; value: unknown; direction?: string; icon?: string }>
    }
    
    if (!data || !Array.isArray(data.fields)) {
      return
    }
    
    sessionType.value = data.targetType || 'DIVERSession'
    
    for (const field of data.fields) {
      const name = field.name
      
      // 跳过正在编辑的变量
      if (name === editingVarName.value) {
        continue
      }
      
      const existing = variables.value.get(name)
      if (existing) {
        existing.value = field.value
        existing.type = field.type || existing.type
      } else {
        variables.value.set(name, {
          name,
          value: field.value,
          type: field.type || 'unknown',
          typeId: 0
        })
      }
    }
  }
  
  /**
   * 更新节点快照（由 SignalR 调用）
   */
  function updateNodeSnapshot(snapshot: unknown) {
    const data = snapshot as { nodes?: NodeStateSnapshot[] }
    if (!data?.nodes) return
    
    for (const node of data.nodes) {
      // 新格式使用 uuid 而不是 nodeId
      const uuid = (node as any).nodeId || node.uuid
      if (uuid) {
        nodeStates.value.set(uuid, {
          ...node,
          uuid
        })
      }
    }
    
    updateAppStateFromNodes()
  }
  
  /**
   * 设置正在编辑的变量
   */
  function setEditingVar(name: string | null) {
    editingVarName.value = name
  }
  
  /**
   * 启动状态轮询
   */
  function startStatePolling() {
    if (statePollingTimer) return
    
    console.log('[Runtime] Starting state polling')
    statePollingTimer = setInterval(async () => {
      // 同时刷新节点状态和会话状态
      await Promise.all([
        refreshNodeStates(),
        syncSessionState()
      ])
    }, STATE_POLLING_INTERVAL)
  }
  
  /**
   * 停止状态轮询
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
    backendSessionRunning.value = false
    appState.value = isBackendAvailable.value ? 'idle' : 'offline'
    nodeStates.value.clear()
    nodeInfos.value.clear()
    variables.value.clear()
    variableControllable.value.clear()
    editingVarName.value = null
  }
  
  return {
    // 状态
    appState,
    isBackendAvailable,
    backendSessionRunning,
    sessionType,
    nodeStates,
    nodeInfos,
    variables,
    variableControllable,
    fieldMetas,
    editingVarName,
    
    // 计算属性
    isConnected,
    isRunning,
    nodeList,
    nodeInfoList,
    variableList,
    controllableVarNames,
    canEdit,
    canStart,
    canStop,
    
    // 方法
    setBackendAvailable,
    refreshNodes,
    refreshNodeStates,
    syncSessionState,
    start,
    stop,
    refreshFieldMetas,
    refreshVariables,
    setVariable,
    updateNodeState,
    updateVariables,
    updateNodeSnapshot,
    setEditingVar,
    startStatePolling,
    stopStatePolling,
    reset
  }
})
