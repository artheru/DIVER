/**
 * @file api/device.ts
 * @description 设备和节点管理 API
 * 
 * 对接 DIVERSession 的节点管理接口
 */

import { get, post } from './index'
import type {
  VersionInfo,
  LayoutInfo,
  PortConfig,
  NodeProbeResult,
  AddNodeResult,
  NodeFullInfo,
  NodeStateSnapshot,
  NodeSettingsRequest,
  NodeExportData,
  LogicInfo
} from '@/types'

// ============================================
// 设备发现
// ============================================

/**
 * 获取可用串口列表
 */
export async function getAvailablePorts(): Promise<{ ok: boolean; ports: string[]; error?: string }> {
  return get('/api/ports')
}

/**
 * 获取已编译的 Logic 列表
 */
export async function getLogicList(): Promise<{ ok: boolean; logics: LogicInfo[]; error?: string }> {
  return get('/api/logic/list')
}

// ============================================
// 节点探测
// ============================================

/**
 * 探测 MCU 节点（只探测，不添加）
 * @param mcuUri MCU 连接 URI
 */
export async function probeNode(mcuUri: string): Promise<NodeProbeResult> {
  return post('/api/node/probe', { mcuUri })
}

// ============================================
// 节点管理
// ============================================

/**
 * 添加节点（探测并添加到 DIVERSession）
 * @param mcuUri MCU 连接 URI
 */
export async function addNode(mcuUri: string): Promise<AddNodeResult> {
  return post('/api/node/add', { mcuUri })
}

/**
 * 删除节点
 * @param uuid 节点 UUID
 */
export async function removeNode(uuid: string): Promise<{ ok: boolean }> {
  return post(`/api/node/${uuid}/remove`)
}

/**
 * 配置节点
 * @param uuid 节点 UUID
 * @param settings 节点设置
 */
export async function configureNode(uuid: string, settings: NodeSettingsRequest): Promise<{ ok: boolean }> {
  return post(`/api/node/${uuid}/configure`, settings)
}

/**
 * 设置节点代码
 * @param uuid 节点 UUID
 * @param logicName Logic 名称（从 generated 目录读取）
 */
export async function programNode(uuid: string, logicName: string): Promise<{ ok: boolean; programSize?: number }> {
  return post(`/api/node/${uuid}/program`, { logicName })
}

/**
 * 获取节点完整信息
 * @param uuid 节点 UUID
 */
export async function getNodeInfo(uuid: string): Promise<{ ok: boolean; node?: NodeFullInfo; error?: string }> {
  return get(`/api/node/${uuid}`)
}

/**
 * 获取节点状态
 * @param uuid 节点 UUID
 */
export async function getNodeState(uuid: string): Promise<{ ok: boolean; state?: NodeStateSnapshot; error?: string }> {
  return get(`/api/node/${uuid}/state`)
}

/**
 * 获取所有节点信息
 */
export async function getAllNodes(): Promise<{ ok: boolean; nodes: NodeFullInfo[] }> {
  return get('/api/nodes')
}

/**
 * 获取所有节点状态
 */
export async function getAllNodeStates(): Promise<{ ok: boolean; nodes: NodeStateSnapshot[] }> {
  return get('/api/nodes/state')
}

/**
 * 导出节点数据
 */
export async function exportNodes(): Promise<{ ok: boolean; nodes: Record<string, NodeExportData> }> {
  return get('/api/nodes/export')
}

/**
 * 导入节点数据
 * @param nodes 节点数据
 */
export async function importNodes(nodes: Record<string, NodeExportData>): Promise<{ ok: boolean; count?: number }> {
  return post('/api/nodes/import', { nodes })
}

/**
 * 清空所有节点
 */
export async function clearAllNodes(): Promise<{ ok: boolean }> {
  return post('/api/nodes/clear')
}

// ============================================
// 兼容旧接口（逐步废弃）
// ============================================

/**
 * @deprecated 使用 getNodeState
 */
export async function pollNodeState(mcuUri: string): Promise<{
  ok: boolean
  mcuUri?: string
  runState?: string
  isConfigured?: boolean
  isProgrammed?: boolean
  mode?: string
  error?: string
}> {
  // 兼容旧接口，通过 mcuUri 查找
  const result = await getAllNodeStates()
  if (!result.ok) {
    return { ok: false, error: 'Failed to get node states' }
  }
  
  const node = result.nodes.find(n => n.mcuUri === mcuUri)
  if (!node) {
    return { ok: true, mcuUri, runState: 'Offline', isConfigured: false, isProgrammed: false, mode: 'Unknown' }
  }
  
  return {
    ok: true,
    mcuUri: node.mcuUri,
    runState: node.runState,
    isConfigured: node.isConfigured,
    isProgrammed: node.isProgrammed,
    mode: 'Unknown'
  }
}

/**
 * @deprecated 使用 configureNode
 */
export async function updateNodePortConfig(nodeId: string, ports: PortConfig[]): Promise<{ ok: boolean; error?: string }> {
  return configureNode(nodeId, { portConfigs: ports })
}
