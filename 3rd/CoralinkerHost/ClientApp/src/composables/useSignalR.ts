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
import { useWireTapStore } from '@/stores/wiretap'
import type { WireTapDataEvent } from '@/types'

/** SignalR 连接状态 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/** 升级进度回调函数类型 */
export type UpgradeProgressCallback = (
  nodeId: string,
  progress: number,
  stage: string,
  message: string | null
) => void

/** 致命错误数据类型 */
export interface FatalErrorData {
  nodeUuid: string
  nodeName: string
  logicName: string | null
  timestamp: string
  payloadVersion: number
  version: {
    productionName: string | null
    gitCommit: string | null
    buildTime: string | null
  }
  debugInfo: {
    ilOffset: number
    lineNo: number
  }
  errorType: 'String' | 'STM32F4'
  errorString: string | null
  coreDump: {
    r0: number
    r1: number
    r2: number
    r3: number
    r12: number
    lr: number
    pc: number
    psr: number
    msr: number
    cfsr: number
    hfsr: number
    dfsr: number
    afsr: number
    bfar: number
    mmar: number
    msp: number
    stackEnd: number
  } | null
}

/** 致命错误回调函数类型 */
export type FatalErrorCallback = (error: FatalErrorData) => void

// 升级进度回调列表（全局共享）
const upgradeProgressCallbacks: Set<UpgradeProgressCallback> = new Set()

// 致命错误回调列表（全局共享）
const fatalErrorCallbacks: Set<FatalErrorCallback> = new Set()

// ============================================
// 单例状态（全局共享，避免多次连接）
// ============================================

/** SignalR 连接实例（单例） */
const connection = ref<signalR.HubConnection | null>(null)

/** 连接状态（单例） */
const state = ref<ConnectionState>('disconnected')

/** 错误信息（单例） */
const error = ref<string | null>(null)

/** 是否已注册事件处理器（防止重复注册） */
let eventHandlersRegistered = false

/** 引用计数（跟踪有多少组件正在使用连接） */
let connectionRefCount = 0

export function useSignalR() {
  // ============================================
  // Store 引用
  // ============================================
  
  const logStore = useLogStore()
  const runtimeStore = useRuntimeStore()
  const wireTapStore = useWireTapStore()
  
  // ============================================
  // 方法
  // ============================================
  
  /**
   * 建立 SignalR 连接
   * 配置自动重连和事件处理器
   */
  async function connect() {
    // 已连接，直接返回
    if (connection.value?.state === signalR.HubConnectionState.Connected) {
      console.log('[SignalR] Already connected, skipping')
      return
    }
    
    // 正在连接中，避免重复连接
    if (state.value === 'connecting') {
      console.log('[SignalR] Connection in progress, skipping')
      return
    }
    
    state.value = 'connecting'
    error.value = null
    
    try {
      // 只有在没有连接实例时才创建新连接
      if (!connection.value) {
        console.log('[SignalR] Creating new connection...')
        connection.value = new signalR.HubConnectionBuilder()
          .withUrl('/hubs/terminal')
          .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
          .configureLogging(signalR.LogLevel.Warning)
          .build()
        
        // 注册事件处理器（只注册一次）
        registerEventHandlers()
        
        // 注册连接状态回调
        connection.value.onreconnecting(() => {
          state.value = 'reconnecting'
          runtimeStore.setBackendAvailable(false)
          console.log('[SignalR] Reconnecting...')
        })
        
        connection.value.onreconnected(async () => {
          state.value = 'connected'
          runtimeStore.setBackendAvailable(true)
          console.log('[SignalR] Reconnected')
          // 重连后恢复 WireTap 配置
          await wireTapStore.loadFromBackend()
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
      }
      
      // 启动连接
      await connection.value.start()
      
      state.value = 'connected'
      runtimeStore.setBackendAvailable(true)
      console.log('[SignalR] Connected to /hubs/terminal')
      
      // 连接成功后加载历史日志
      await loadHistoryLogs()
      
      // 加载 WireTap 配置（恢复刷新前的状态）
      await wireTapStore.loadFromBackend()
      
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
    
    // 防止重复注册
    if (eventHandlersRegistered) {
      console.log('[SignalR] Event handlers already registered, skipping')
      return
    }
    eventHandlersRegistered = true
    console.log('[SignalR] Registering event handlers...')
    
    // 终端日志事件
    // 格式: terminalLine(line: string)
    connection.value.on('terminalLine', (line: string) => {
      logStore.appendTerminal(line)
    })
    
    // Build 日志事件（专用通道）
    // 格式: buildLine(line: string)
    connection.value.on('buildLine', (line: string) => {
      logStore.appendBuild(line)
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

    // MCU 致命错误事件
    // 格式: fatalError(errorData: FatalErrorData)
    connection.value.on('fatalError', (errorData: FatalErrorData) => {
      console.error('[SignalR] Fatal error received:', errorData)
      // 通知所有注册的回调
      fatalErrorCallbacks.forEach(callback => {
        try {
          callback(errorData)
        } catch (err) {
          console.error('[SignalR] fatalError callback error:', err)
        }
      })
    })

    // WireTap 数据事件
    // 格式: wireTapData(data: WireTapDataEvent)
    connection.value.on('wireTapData', (data: WireTapDataEvent) => {
      wireTapStore.handleWireTapData(data)
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
   * 注册致命错误回调
   */
  function onFatalError(callback: FatalErrorCallback) {
    fatalErrorCallbacks.add(callback)
  }

  /**
   * 注销致命错误回调
   */
  function offFatalError(callback: FatalErrorCallback) {
    fatalErrorCallbacks.delete(callback)
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
  
  // 组件挂载时自动连接（使用引用计数）
  onMounted(() => {
    connectionRefCount++
    connect().catch(() => {
      // 连接失败时静默处理，UI 会显示状态
    })
  })
  
  // 组件卸载时减少引用计数，只有当计数为 0 时才断开连接
  // 注意：通常不需要断开，因为 SignalR 连接是全局的
  onUnmounted(() => {
    connectionRefCount--
    // 不再自动断开连接，保持全局连接活跃
    // 如果需要断开，可以手动调用 disconnect()
  })
  
  return {
    connection,
    state,
    error,
    connect,
    disconnect,
    isConnected,
    onUpgradeProgress,
    offUpgradeProgress,
    onFatalError,
    offFatalError
  }
}
