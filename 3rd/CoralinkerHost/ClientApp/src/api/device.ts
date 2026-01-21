/**
 * @file api/device.ts
 * @description 设备相关 API
 * 
 * 包括串口发现、Logic 列表和端口配置
 */

import { get, post } from './index'
import type { PortConfig } from '@/types'

/**
 * 可用串口信息
 */
export interface PortsResponse {
  ok: boolean
  ports: string[]
  error?: string
}

/**
 * Logic 信息
 */
export interface LogicInfo {
  name: string
  binPath: string
  jsonPath: string
  binSize: number
  jsonSize: number
}

/**
 * Logic 列表响应
 */
export interface LogicListResponse {
  ok: boolean
  logics: LogicInfo[]
  error?: string
}

/**
 * 获取可用串口列表
 */
export async function getAvailablePorts(): Promise<PortsResponse> {
  return get<PortsResponse>('/api/ports')
}

/**
 * 获取已编译的 Logic 列表
 */
export async function getLogicList(): Promise<LogicListResponse> {
  return get<LogicListResponse>('/api/logic/list')
}

/**
 * 端口配置请求项
 */
export interface PortConfigItem {
  type: string
  baud: number
  receiveFrameMs?: number
  retryTimeMs?: number
}

/**
 * 端口配置响应
 */
export interface PortConfigResponse {
  ok: boolean
  error?: string
}

/**
 * 更新节点端口配置
 * @param nodeId 节点 ID
 * @param ports 端口配置数组
 */
export async function updateNodePortConfig(nodeId: string, ports: PortConfig[]): Promise<PortConfigResponse> {
  const items: PortConfigItem[] = ports.map(p => ({
    type: p.type,
    baud: p.baud,
    receiveFrameMs: p.receiveFrameMs,
    retryTimeMs: p.retryTimeMs
  }))
  return post<PortConfigResponse>(`/api/node/${nodeId}/ports`, { Ports: items })
}

/**
 * 节点 Probe 请求
 */
export interface NodeProbeRequest {
  mcuUri: string
}

/**
 * 端口布局信息
 */
export interface PortLayoutInfo {
  type: string
  name: string
}

/**
 * 节点 Probe 响应
 */
export interface NodeProbeResponse {
  ok: boolean
  error?: string
  nodeId?: string  // 后端分配的节点 ID（与 DIVERSession 中的 ID 一致）
  mcuUri?: string
  version?: {
    productionName: string
    gitTag: string
    gitCommit: string
    buildTime: string
  }
  state?: {
    runningState: string
    isConfigured: boolean
    isProgrammed: boolean
    mode: string
  }
  layout?: {
    ports: PortLayoutInfo[]
  }
}

/**
 * 探测 MCU 节点
 * 验证 URI 是否有效，返回 MCU 版本、状态和布局信息
 * @param mcuUri MCU 连接 URI
 */
export async function probeNode(mcuUri: string): Promise<NodeProbeResponse> {
  return post<NodeProbeResponse>('/api/node/probe', { McuUri: mcuUri })
}

/**
 * 节点状态轮询响应
 */
export interface NodePollStateResponse {
  ok: boolean
  mcuUri?: string
  runState?: string
  isConfigured?: boolean
  isProgrammed?: boolean
  mode?: string
  error?: string
}

/**
 * 轮询节点状态（轻量级，只获取状态不获取版本/布局）
 * @param mcuUri MCU 连接 URI
 */
export async function pollNodeState(mcuUri: string): Promise<NodePollStateResponse> {
  return post<NodePollStateResponse>('/api/node/poll-state', { McuUri: mcuUri })
}
