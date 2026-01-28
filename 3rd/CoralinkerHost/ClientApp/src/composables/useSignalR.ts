/**
 * @file composables/useSignalR.ts
 * @description SignalR 连接管理组合式函数
 * 
 * 关键功能：
 * 1. 建立与后端的 SignalR 连接
 * 2. 自动重连机制
 * 3. 分发实时事件到对应的 Store
 * 
 * 事件类型：
 * - terminalLine: 终端日志行
 * - nodeLogLine: 节点日志行  
 * - variables: 变量值更新
 * - runtimeUpdate: 运行时状态更新
 */

import { ref, onMounted, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import { useLogStore } from '@/stores/logs'
import { useRuntimeStore } from '@/stores/runtime'

/** SignalR 连接状态 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/** 升级进度回调函数类型 */
export type UpgradeProgressCallback = (
  nodeId: string,
  progress: number,
  stage: string,
  message: string | null
) => void

// 升级进度回调列表（全局共享）
const upgradeProgressCallbacks: Set<UpgradeProgressCallback> = new Set()

export function useSignalR() {
  // ============================================
  // 状态
  // ============================================
  
  /** SignalR 连接实例 */
  const connection = ref<signalR.HubConnection | null>(null)
  
  /** 连接状态 */
  const state = ref<ConnectionState>('disconnected')
  
  /** 错误信息 */
  const error = ref<string | null>(null)
  
  // ============================================
  // Store 引用
  // ============================================
  
  const logStore = useLogStore()
  const runtimeStore = useRuntimeStore()
  
  // ============================================
  // 方法
  // ============================================
  
  /**
   * 建立 SignalR 连接
   * 配置自动重连和事件处理器
   */
  async function connect() {
    if (connection.value?.state === signalR.HubConnectionState.Connected) {
      return // 已连接
    }
    
    state.value = 'connecting'
    error.value = null
    
    try {
      // 创建连接，配置自动重连策略
      connection.value = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/terminal')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build()
      
      // 注册事件处理器
      registerEventHandlers()
      
      // 注册连接状态回调
      connection.value.onreconnecting(() => {
        state.value = 'reconnecting'
        runtimeStore.setBackendAvailable(false)
        console.log('[SignalR] Reconnecting...')
      })
      
      connection.value.onreconnected(() => {
        state.value = 'connected'
        runtimeStore.setBackendAvailable(true)
        console.log('[SignalR] Reconnected')
      })
      
      connection.value.onclose((err) => {
        state.value = 'disconnected'
        runtimeStore.setBackendAvailable(false)
        if (err) {
          error.value = err.message
          console.error('[SignalR] Connection closed with error:', err)
        } else {
          console.log('[SignalR] Connection closed')
        }
      })
      
      // 启动连接
      await connection.value.start()
      
      state.value = 'connected'
      runtimeStore.setBackendAvailable(true)
      console.log('[SignalR] Connected to /hubs/terminal')
      
      // 连接成功后加载历史日志
      await loadHistoryLogs()
      
    } catch (err) {
      state.value = 'disconnected'
      error.value = err instanceof Error ? err.message : String(err)
      console.error('[SignalR] Failed to connect:', err)
      throw err
    }
  }
  
  /**
   * 加载历史日志（连接成功后调用）
   * - Terminal 历史日志由 Host 管理
   * - 节点日志由 DIVERSession 管理（在 syncNodeTabs 时自动加载）
   */
  async function loadHistoryLogs() {
    console.log('[SignalR] Loading terminal history logs...')
    
    try {
      // 加载 Terminal 历史日志（Host 管理）
      await logStore.loadTerminalHistory()
      
      // 节点日志在 GraphCanvas.vue 的 syncLogTabs 中自动加载
      // 不需要在这里加载，因为节点可能还未初始化
      
      console.log('[SignalR] Terminal history logs loaded')
    } catch (error) {
      console.error('[SignalR] Failed to load terminal history logs:', error)
    }
  }

  /**
   * 注册 SignalR 事件处理器
   * 将收到的事件分发到对应的 Store
   */
  function registerEventHandlers() {
    if (!connection.value) return
    
    // 终端日志事件
    // 格式: terminalLine(line: string)
    connection.value.on('terminalLine', (line: string) => {
      logStore.appendTerminal(line)
    })
    
    // 节点日志事件
    // 格式: nodeLogLine(uuid: string, message: string)
    connection.value.on('nodeLogLine', (uuid: string, message: string) => {
      logStore.appendNodeLog(uuid, message)
    })
    
    // 变量快照更新事件 (后端发送 varsSnapshot)
    // 格式: varsSnapshot(snapshot: { targetType, fields })
    connection.value.on('varsSnapshot', (snapshot: Record<string, unknown>) => {
      runtimeStore.updateVariables(snapshot)
    })
    
    // 节点状态快照更新事件 (后端发送 nodeSnapshot)
    // 格式: nodeSnapshot(snapshot: { nodes: Array<NodeStateSnapshot> })
    connection.value.on('nodeSnapshot', (snapshot: unknown) => {
      runtimeStore.updateNodeSnapshot(snapshot)
    })
    
    // 固件升级进度事件
    // 格式: upgradeProgress(nodeId: string, progress: number, stage: string, message: string | null)
    connection.value.on('upgradeProgress', (nodeId: string, progress: number, stage: string, message: string | null) => {
      // 通知所有注册的回调
      upgradeProgressCallbacks.forEach(callback => {
        try {
          callback(nodeId, progress, stage, message)
        } catch (err) {
          console.error('[SignalR] upgradeProgress callback error:', err)
        }
      })
    })
  }
  
  /**
   * 注册升级进度回调
   */
  function onUpgradeProgress(callback: UpgradeProgressCallback) {
    upgradeProgressCallbacks.add(callback)
  }
  
  /**
   * 注销升级进度回调
   */
  function offUpgradeProgress(callback: UpgradeProgressCallback) {
    upgradeProgressCallbacks.delete(callback)
  }
  
  /**
   * 断开连接
   */
  async function disconnect() {
    if (connection.value) {
      await connection.value.stop()
      connection.value = null
      state.value = 'disconnected'
      console.log('[SignalR] Disconnected')
    }
  }
  
  /**
   * 检查是否已连接
   */
  function isConnected(): boolean {
    return connection.value?.state === signalR.HubConnectionState.Connected
  }
  
  // ============================================
  // 生命周期
  // ============================================
  
  // 组件挂载时自动连接
  onMounted(() => {
    connect().catch(() => {
      // 连接失败时静默处理，UI 会显示状态
    })
  })
  
  // 组件卸载时断开连接
  onUnmounted(() => {
    disconnect()
  })
  
  return {
    connection,
    state,
    error,
    connect,
    disconnect,
    isConnected,
    onUpgradeProgress,
    offUpgradeProgress
  }
}
