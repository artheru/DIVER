/**
 * @file stores/runtime.ts
 * @description 运行时状态管理
 * 
 * 管理 MCU 节点的运行时状态：
 * - 节点连接状态
 * - 变量值
 * - 可控变量列表
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as runtimeApi from '@/api/runtime'
import type { NodeSnapshot, VariableInfo, VariableValue } from '@/types'

export const useRuntimeStore = defineStore('runtime', () => {
  // ============================================
  // 状态定义
  // ============================================
  
  /** 是否已连接 */
  const isConnected = ref(false)
  
  /** 是否正在运行 */
  const isRunning = ref(false)
  
  /** 节点快照 Map<nodeId, NodeSnapshot> */
  const nodes = ref<Map<string, NodeSnapshot>>(new Map())
  
  /** 变量值 Map<varName, VariableValue> */
  const variables = ref<Map<string, VariableValue>>(new Map())
  
  /** 可控变量信息 Map<varName, VariableInfo> */
  const variableInfos = ref<Map<string, VariableInfo>>(new Map())
  
  /** 正在编辑的变量名 (编辑时跳过 SignalR 更新) */
  const editingVarName = ref<string | null>(null)
  
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
  // 操作方法
  // ============================================
  
  /**
   * 连接所有节点
   */
  async function connect() {
    try {
      const result = await runtimeApi.connect()
      if (result.ok) {
        isConnected.value = true
        
        // 获取可控变量列表
        await fetchControllableVariables()
        
        console.log('[Runtime] Connected to nodes')
      }
      return result
    } catch (error) {
      console.error('[Runtime] Connect failed:', error)
      throw error
    }
  }
  
  /**
   * 启动执行
   */
  async function start() {
    try {
      const result = await runtimeApi.start()
      if (result.ok) {
        isRunning.value = true
        console.log('[Runtime] Started')
      }
      return result
    } catch (error) {
      console.error('[Runtime] Start failed:', error)
      throw error
    }
  }
  
  /**
   * 停止执行
   */
  async function stop() {
    try {
      await runtimeApi.stop()
      isRunning.value = false
      console.log('[Runtime] Stopped')
    } catch (error) {
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
   */
  function updateNodeSnapshot(snapshot: NodeSnapshot) {
    nodes.value.set(snapshot.nodeId, snapshot)
  }
  
  /**
   * 更新变量值 (由 SignalR 调用)
   * @param snapshot 变量快照对象
   */
  function updateVariables(snapshot: Record<string, unknown>) {
    for (const [name, value] of Object.entries(snapshot)) {
      // 跳过正在编辑的变量
      if (name === editingVarName.value) {
        continue
      }
      
      const existing = variables.value.get(name)
      const info = variableInfos.value.get(name)
      
      if (existing) {
        existing.value = value
      } else {
        variables.value.set(name, {
          name,
          value,
          type: info?.type || 'unknown',
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
   * 重置状态
   */
  function reset() {
    isConnected.value = false
    isRunning.value = false
    nodes.value.clear()
    variables.value.clear()
    variableInfos.value.clear()
    editingVarName.value = null
  }
  
  return {
    // 状态
    isConnected,
    isRunning,
    nodes,
    variables,
    variableInfos,
    editingVarName,
    
    // 计算属性
    nodeList,
    variableList,
    controllableVarNames,
    
    // 方法
    connect,
    start,
    stop,
    fetchControllableVariables,
    setVariable,
    updateNodeSnapshot,
    updateVariables,
    setEditingVar,
    refreshSnapshot,
    reset
  }
})
