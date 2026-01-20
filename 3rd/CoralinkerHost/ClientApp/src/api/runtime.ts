/**
 * @file api/runtime.ts
 * @description 运行时控制 API
 * 
 * 处理 MCU 连接、启动、停止等运行时操作
 */

import { get, post } from './index'
import type { 
  RuntimeSnapshot, 
  VariableInfo, 
  SetVariableRequest,
  NodeLogInfo,
  LogChunkResponse,
  PortConfig
} from '@/types'

// ============================================
// 运行控制
// ============================================

/**
 * 连接所有节点
 * 打开串口并获取 MCU 版本和布局信息
 */
export async function connect(): Promise<{ ok: boolean; nodes: unknown[] }> {
  return post('/api/connect')
}

/**
 * 启动执行
 * 配置 MCU 并开始运行逻辑
 */
export async function start(): Promise<{ ok: boolean }> {
  return post('/api/start')
}

/**
 * 停止执行
 */
export async function stop(): Promise<void> {
  await post('/api/stop')
}

/**
 * 获取运行时快照
 * 包含所有节点的当前状态
 */
export async function getRuntimeSnapshot(): Promise<RuntimeSnapshot> {
  return get<RuntimeSnapshot>('/api/runtime')
}

// ============================================
// 变量控制
// ============================================

/**
 * 获取可控变量列表
 * 返回所有变量及其可控性信息
 */
export async function getControllableVariables(): Promise<{ variables: VariableInfo[] }> {
  return get('/api/variables/controllable')
}

/**
 * 设置变量值
 * 只能设置非 LowerIO 的可控变量
 * 
 * @param name 变量名
 * @param value 新值
 * @param typeHint 类型提示 (如 'int', 'float', 'byte[]')
 */
export async function setVariable(
  name: string, 
  value: unknown, 
  typeHint?: string
): Promise<{ ok: boolean; name: string; value: unknown }> {
  const request: SetVariableRequest = { name, value, typeHint }
  return post('/api/variable/set', request)
}

// ============================================
// 节点配置
// ============================================

/**
 * 配置节点端口
 * @param nodeId 节点 ID
 * @param ports 端口配置数组
 */
export async function configureNodePorts(
  nodeId: string, 
  ports: PortConfig[]
): Promise<{ ok: boolean }> {
  return post(`/api/node/${nodeId}/ports`, { ports })
}

// ============================================
// 日志
// ============================================

/**
 * 获取有日志的节点列表
 */
export async function getLoggedNodes(): Promise<{ nodes: NodeLogInfo[] }> {
  return get('/api/logs/nodes')
}

/**
 * 获取节点日志
 * @param nodeId 节点 ID
 * @param offset 起始偏移
 * @param limit 获取条数
 */
export async function getNodeLogs(
  nodeId: string, 
  offset = 0, 
  limit = 200
): Promise<LogChunkResponse> {
  return get(`/api/logs/node/${nodeId}`, {
    params: { offset, limit }
  })
}

/**
 * 清空节点日志
 * @param nodeId 节点 ID
 */
export async function clearNodeLogs(nodeId: string): Promise<void> {
  await post(`/api/logs/node/${nodeId}/clear`)
}

/**
 * 清空所有日志
 */
export async function clearAllLogs(): Promise<void> {
  await post('/api/logs/clear')
}

// ============================================
// 命令
// ============================================

/**
 * 发送命令到后端
 * @param command 命令字符串
 */
export async function sendCommand(command: string): Promise<{ ok: boolean }> {
  return post('/api/command', { command })
}
