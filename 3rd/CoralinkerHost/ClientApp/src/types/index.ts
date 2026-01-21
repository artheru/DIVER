/**
 * @file types/index.ts
 * @description 全局 TypeScript 类型定义
 * 
 * 这里定义了与后端 API 交互的所有数据结构，
 * 确保前后端类型一致性。
 */

// ============================================
// 项目状态相关类型
// ============================================

/**
 * LiteGraph 序列化数据结构
 * 直接存储为 JSON 对象，避免双重序列化
 */
export interface LiteGraphData {
  last_node_id: number
  last_link_id: number
  nodes: any[]
  links: any[]
  groups?: any[]
  config?: Record<string, any>
  extra?: Record<string, any>
  version?: number
}

/**
 * 项目状态 - 对应后端 ProjectState
 * 包含节点图序列化数据和当前选中的资源信息
 */
export interface ProjectState {
  /** LiteGraph 序列化的 JSON 对象 (直接存储，非字符串) */
  nodeMap: LiteGraphData | null
  /** 当前选中的 .cs 资源文件名 */
  selectedAsset: string | null
  /** 当前在编辑器中打开的文件路径 */
  selectedFile: string | null
  /** 最后一次构建的 ID */
  lastBuildId: string | null
}

// ============================================
// 文件系统相关类型
// ============================================

/**
 * 文件树节点 - 对应后端 FileNode
 * 用于资源管理器的树形结构
 */
export interface FileNode {
  /** 文件/文件夹名称 */
  name: string
  /** 相对路径 (相对于 data 目录) */
  path: string
  /** 节点类型 */
  kind: 'folder' | 'file'
  /** 子节点 (仅文件夹有) */
  children?: FileNode[]
}

/**
 * 文件读取响应
 */
export interface FileReadResponse {
  path: string
  kind: 'text' | 'binary'
  text?: string
  base64?: string
  sizeBytes: number
}

/**
 * 文件写入请求
 */
export interface FileWriteRequest {
  path: string
  kind: 'text' | 'binary'
  text?: string
  base64?: string
}

// ============================================
// 运行时相关类型
// ============================================

/**
 * MCU 节点版本信息
 */
export interface VersionInfo {
  productionName: string
  gitTag: string
  gitCommit: string
  buildTime: string
}

/**
 * MCU 节点布局信息 - 描述硬件 I/O 配置
 */
export interface LayoutInfo {
  digitalInputCount: number
  digitalOutputCount: number
  portCount: number
  ports: PortDescriptor[]
}

/**
 * 端口描述符
 */
export interface PortDescriptor {
  index: number
  type: 'Serial' | 'CAN' | 'Unknown'
  name: string
}

/**
 * 端口配置
 */
export interface PortConfig {
  type: string
  name?: string   // 端口名称（来自 MCU Layout）
  baud: number
  receiveFrameMs?: number
  retryTimeMs?: number
}

/**
 * 节点运行时快照
 */
export interface NodeSnapshot {
  nodeId: string
  runState: string
  isConnected: boolean
  isConfigured: boolean
  isProgrammed: boolean
  mode: string
  version?: VersionInfo
  layout?: LayoutInfo
  ports?: PortConfig[]
}

/**
 * 节点状态信息（用于轮询）
 */
export interface NodeStateInfo {
  nodeId: string
  isConnected: boolean
  runState: string
  isConfigured: boolean
  isProgrammed: boolean
  mode: string
}

/**
 * 运行时整体快照
 */
export interface RuntimeSnapshot {
  nodes: NodeSnapshot[]
}

// ============================================
// 变量相关类型
// ============================================

/**
 * Cart 变量类型 ID 映射
 * 对应 DiverCompiler 输出的 typeid
 */
export const VariableTypeId = {
  Bool: 0,
  Byte: 1,
  SByte: 2,
  Int16: 3,
  UInt16: 4,
  Int32: 5,
  UInt32: 6,
  Int64: 7,
  UInt64: 8,
  Float: 9,
  Double: 10,
  Char: 11,
  String: 12,
  IntArray: 16,
  ByteArray: 17,
  FloatArray: 18
} as const

export type VariableTypeId = (typeof VariableTypeId)[keyof typeof VariableTypeId]

/**
 * 变量信息 - 来自 /api/variables/controllable
 */
export interface VariableInfo {
  name: string
  type: string
  typeId: number
  /** 是否可由 Host 控制 (非 LowerIO) */
  controllable: boolean
  /** 是否为 LowerIO (MCU 控制) */
  isLowerIO: boolean
  /** 是否为 UpperIO (MCU 输出) */
  isUpperIO: boolean
  /** 是否为 Mutual (双向) */
  isMutual: boolean
}

/**
 * 变量值快照 - SignalR 推送
 */
export interface VariableValue {
  name: string
  value: unknown
  type: string
  typeId: number
}

/**
 * 设置变量请求
 */
export interface SetVariableRequest {
  name: string
  value: unknown
  typeHint?: string
}

// ============================================
// 构建相关类型
// ============================================

/**
 * 构建结果
 */
export interface BuildResult {
  ok: boolean
  buildRoot?: string
  buildId?: string
  artifacts?: string[]
  error?: string
  exitCode?: number
  tail?: string
}

// ============================================
// 日志相关类型
// ============================================

/**
 * 节点日志信息
 */
export interface NodeLogInfo {
  nodeId: string
  nodeName?: string
}

/**
 * 日志分页响应
 */
export interface LogChunkResponse {
  lines: string[]
  total: number
  offset: number
}

// ============================================
// LiteGraph 扩展类型
// ============================================

/**
 * Coral MCU 节点属性
 */
export interface CoralNodeProperties {
  nodeName: string
  mcuUri: string
  logicName: string
  runState?: string
  isConfigured?: boolean
  isProgrammed?: boolean
  mode?: string
  version?: string
  info?: string
  versionInfo?: VersionInfo
  layout?: LayoutInfo
  ports?: PortConfig[]
}

/**
 * 根节点属性
 */
export interface RootNodeProperties {
  name: string
}
