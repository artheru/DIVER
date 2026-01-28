/**
 * @file api/runtime.ts
 * @description 运行时控制 API
 * 
 * 处理会话控制、变量管理、日志获取
 */

import { get, post } from './index'
import type {
  StartResult,
  SessionState,
  CartFieldValue,
  SetVariableRequest,
  LogQueryResult,
  NodeStateSnapshot
} from '@/types'

// ============================================
// 会话控制
// ============================================

/**
 * 启动会话（所有节点）
 */
export async function start(): Promise<{
  ok: boolean
  totalNodes?: number
  successNodes?: number
  errors?: Array<{ uuid: string; nodeName: string; error: string }>
  error?: string
}> {
  return post('/api/start')
}

/**
 * 停止会话
 */
export async function stop(): Promise<{ ok: boolean }> {
  return post('/api/stop')
}

/**
 * 获取会话状态
 */
export async function getSessionState(): Promise<{
  ok: boolean
  state?: string
  isRunning?: boolean
  nodeCount?: number
}> {
  return get('/api/session/state')
}

// ============================================
// 变量管理
// ============================================

/**
 * 获取所有 Cart 变量
 */
export async function getAllVariables(): Promise<{ ok: boolean; variables: CartFieldValue[] }> {
  return get('/api/variables')
}

/**
 * 设置变量值
 * @param name 变量名
 * @param value 新值
 * @param typeHint 类型提示
 */
export async function setVariable(
  name: string,
  value: unknown,
  typeHint?: string
): Promise<{ ok: boolean; name?: string; value?: unknown; error?: string }> {
  const request: SetVariableRequest = { name, value, typeHint }
  return post('/api/variable/set', request)
}

/**
 * 获取单个变量值
 * @param name 变量名
 */
export async function getVariable(name: string): Promise<{ ok: boolean; name?: string; value?: unknown; error?: string }> {
  return get(`/api/variable/${name}`)
}

// ============================================
// 日志管理
// ============================================

// ============================================
// Terminal 日志 API (Host 管理)
// ============================================

/**
 * 获取 Terminal 历史日志
 */
export async function getTerminalLogs(): Promise<{ ok: boolean; lines: string[] }> {
  return get('/api/logs/terminal')
}

/**
 * 清空 Terminal 日志
 */
export async function clearTerminalLogs(): Promise<{ ok: boolean }> {
  return post('/api/logs/terminal/clear')
}

// ============================================
// 节点日志 API (DIVERSession 管理)
// ============================================

/**
 * 获取有日志的节点列表
 */
export async function getLoggedNodes(): Promise<{ ok: boolean; nodes: string[] }> {
  return get('/api/logs/nodes')
}

/**
 * 获取节点日志
 * @param uuid 节点 UUID
 * @param afterSeq 获取 seq 大于此值的日志（可选）
 * @param maxCount 最大返回条数（默认 200）
 */
export async function getNodeLogs(
  uuid: string,
  afterSeq?: number,
  maxCount = 200
): Promise<{
  ok: boolean
  uuid?: string
  latestSeq?: number
  entries?: Array<{ seq: number; timestamp: string; message: string }>
  hasMore?: boolean
  error?: string
}> {
  const params = new URLSearchParams()
  if (afterSeq !== undefined) {
    params.append('afterSeq', afterSeq.toString())
  }
  params.append('maxCount', maxCount.toString())
  
  const queryString = params.toString()
  const url = queryString ? `/api/logs/node/${uuid}?${queryString}` : `/api/logs/node/${uuid}`
  
  return get(url)
}

/**
 * 清空节点日志
 * @param uuid 节点 UUID
 */
export async function clearNodeLogs(uuid: string): Promise<{ ok: boolean }> {
  return post(`/api/logs/node/${uuid}/clear`)
}

/**
 * 清空所有日志
 */
export async function clearAllLogs(): Promise<{ ok: boolean }> {
  return post('/api/logs/clear')
}

// ============================================
// 节点状态轮询
// ============================================

/**
 * 获取所有节点状态（用于轮询）
 */
export async function getNodeStates(): Promise<{ ok: boolean; nodes: NodeStateSnapshot[] }> {
  return get('/api/nodes/state')
}

// ============================================
// 兼容旧接口（逐步废弃）
// ============================================

/**
 * @deprecated 使用 start()
 */
export async function connect(): Promise<{ ok: boolean; nodes: unknown[] }> {
  // 旧的 connect 只是打开连接，新接口 start 会做完整流程
  // 这里返回空结果，前端应该改用 start()
  console.warn('[API] connect() is deprecated, use start() instead')
  return { ok: true, nodes: [] }
}

/**
 * @deprecated 使用 getAllVariables()
 */
export async function getControllableVariables(): Promise<{ variables: Array<{
  name: string
  type: string
  typeId: number
  controllable: boolean
  isLowerIO: boolean
  isUpperIO: boolean
  isMutual: boolean
}> }> {
  const result = await getAllVariables()
  return {
    variables: result.variables.map(v => ({
      name: v.name,
      type: v.type,
      typeId: v.typeId,
      controllable: !v.isLowerIO,
      isLowerIO: v.isLowerIO,
      isUpperIO: v.isUpperIO,
      isMutual: v.isMutual
    }))
  }
}

/**
 * @deprecated 使用 getSessionState()
 */
export async function getRuntimeSnapshot(): Promise<{
  isRunning: boolean
  assetName: string | null
  buildRoot: string | null
}> {
  const result = await getSessionState()
  return {
    isRunning: result.isRunning ?? false,
    assetName: null,
    buildRoot: null
  }
}

/**
 * @deprecated 不再需要，节点统计在 NodeStateSnapshot.stats 中
 */
export async function getNodeStats(nodeId: string): Promise<{ ok: boolean; error?: string }> {
  console.warn('[API] getNodeStats() is deprecated, stats are included in node state')
  return { ok: true }
}

/**
 * @deprecated 使用 configureNode
 */
export async function configureNodePorts(
  nodeId: string,
  ports: Array<{ type: string; baud: number; receiveFrameMs?: number; retryTimeMs?: number }>
): Promise<{ ok: boolean }> {
  const { configureNode } = await import('./device')
  return configureNode(nodeId, { portConfigs: ports })
}

/**
 * @deprecated
 */
export async function sendCommand(command: string): Promise<{ ok: boolean }> {
  return post('/api/command', { command })
}

/**
 * @deprecated 使用 importNodes
 */
export interface RestoreNodeInfo {
  nodeId: string
  mcuUri: string
  success: boolean
  message: string
}

/**
 * @deprecated
 */
export interface RestoreSessionResponse {
  ok: boolean
  total: number
  connected: number
  nodes: RestoreNodeInfo[]
  error?: string
}

/**
 * @deprecated 使用 importNodes
 */
export async function restoreSession(): Promise<RestoreSessionResponse> {
  console.warn('[API] restoreSession() is deprecated, nodes are auto-loaded from project')
  return { ok: true, total: 0, connected: 0, nodes: [] }
}
