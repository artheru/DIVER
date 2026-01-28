/**
 * @file types/index.ts
 * @description 全局 TypeScript 类型定义
 * 
 * 与后端 DIVERSession API 对齐的类型定义
 */

// ============================================
// 项目状态相关类型
// ============================================

/**
 * 项目状态 - 对应后端 ProjectState
 * 节点数据由 DIVERSession 管理，不在这里存储
 */
export interface ProjectState {
  /** 当前选中的 .cs 资源文件名 */
  selectedAsset: string | null
  /** 当前在编辑器中打开的文件路径 */
  selectedFile: string | null
  /** 最后一次构建的 ID */
  lastBuildId: string | null
}

// ============================================
// 节点相关类型（对齐 DIVERSession）
// ============================================

/**
 * 版本信息快照
 */
export interface VersionInfo {
  productionName: string
  gitTag: string
  gitCommit: string
  buildTime: string
}

/**
 * 硬件布局快照
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
  type: string
  name: string
}

/**
 * 端口配置快照
 */
export interface PortConfig {
  type: string
  baud: number
  receiveFrameMs?: number
  retryTimeMs?: number
}

/**
 * 端口统计快照
 */
export interface PortStats {
  index: number
  txFrames: number
  rxFrames: number
  txBytes: number
  rxBytes: number
}

/**
 * 运行时统计快照
 */
export interface RuntimeStats {
  uptimeMs: number
  digitalInputs: number
  digitalOutputs: number
  ports: PortStats[]
}

/**
 * Cart 字段快照
 */
export interface CartFieldInfo {
  name: string
  type: string
  typeId: number
  isLowerIO: boolean
  isUpperIO: boolean
  isMutual: boolean
}

/**
 * 节点状态快照 - 对应 NodeStateSnapshot
 */
export interface NodeStateSnapshot {
  uuid: string
  mcuUri: string
  nodeName: string
  isConnected: boolean
  runState: string  // "idle" | "running" | "error" | "offline"
  isConfigured: boolean
  isProgrammed: boolean
  stats?: RuntimeStats
}

/**
 * 节点完整信息 - 对应 NodeFullInfo
 */
export interface NodeFullInfo {
  uuid: string
  mcuUri: string
  nodeName: string
  version?: VersionInfo
  layout?: LayoutInfo
  portConfigs: PortConfig[]
  hasProgram: boolean
  programSize: number
  logicName?: string
  cartFields: CartFieldInfo[]
  extraInfo?: Record<string, unknown>
}

/**
 * 节点设置请求
 */
export interface NodeSettingsRequest {
  nodeName?: string
  portConfigs?: PortConfig[]
  extraInfo?: Record<string, unknown>
}

/**
 * 节点导出数据 - 对应 NodeExportData
 */
export interface NodeExportData {
  mcuUri: string
  nodeName: string
  portConfigs?: PortConfig[]
  programBase64?: string
  metaJson?: string
  logicName?: string
  extraInfo?: Record<string, unknown>
}

// ============================================
// 会话相关类型
// ============================================

/**
 * 启动结果 - 对应 StartResult
 */
export interface StartResult {
  success: boolean
  totalNodes: number
  successNodes: number
  errors: NodeStartError[]
}

/**
 * 节点启动错误
 */
export interface NodeStartError {
  uuid: string
  nodeName: string
  error: string
}

/**
 * 会话状态
 */
export interface SessionState {
  state: string  // "Idle" | "Running"
  isRunning: boolean
  nodeCount: number
}

// ============================================
// 变量相关类型
// ============================================

/**
 * Cart 字段值 - 对应 CartFieldValue
 */
export interface CartFieldValue {
  name: string
  type: string
  typeId: number
  value: unknown
  isLowerIO: boolean
  isUpperIO: boolean
  isMutual: boolean
}

/**
 * 变量信息（用于 UI 显示）
 */
export interface VariableInfo {
  name: string
  type: string
  typeId: number
  controllable: boolean  // !isLowerIO
  isLowerIO: boolean
  isUpperIO: boolean
  isMutual: boolean
}

/**
 * 变量值（用于 store）
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
// 日志相关类型
// ============================================

/**
 * 日志条目 - 对应 LogEntry
 */
export interface LogEntry {
  seq: number
  timestamp: string
  message: string
}

/**
 * 日志查询结果 - 对应 LogQueryResult
 */
export interface LogQueryResult {
  uuid: string
  latestSeq: number
  entries: LogEntry[]
  hasMore: boolean
}

// ============================================
// 文件系统相关类型
// ============================================

/**
 * 文件树节点
 */
export interface FileNode {
  name: string
  path: string
  kind: 'folder' | 'file'
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

// ============================================
// Probe 相关类型
// ============================================

/**
 * 节点探测结果（只探测不添加）
 */
export interface NodeProbeResult {
  ok: boolean
  error?: string
  version?: VersionInfo
  layout?: LayoutInfo
}

/**
 * 添加节点结果（探测并添加）
 */
export interface AddNodeResult {
  ok: boolean
  error?: string
  uuid?: string
  nodeName?: string
  version?: VersionInfo
  layout?: LayoutInfo
}

/**
 * 端口布局信息（从 layout.ports 获取）
 */
export interface PortLayoutInfo {
  type: string
  name: string
}

// ============================================
// 固件升级相关类型
// ============================================

/**
 * 固件元数据（MCU 和 UPG 通用）
 */
export interface FirmwareMetadata {
  productName: string
  tag: string
  commit: string
  buildTime: string
  appLength: number
  appCRC32: number
  isValid: boolean
}

/**
 * 升级进度
 */
export interface UpgradeProgress {
  nodeId: string
  progress: number
  stage: 'Connecting' | 'SendingUpgradeCommand' | 'WaitingBootloader' | 'ConnectingBootloader' | 'ReadingMcuInfo' | 'Erasing' | 'Writing' | 'Verifying' | 'Complete' | 'Error'
  message?: string
}

// ============================================
// 旧类型（兼容，逐步废弃）
// ============================================

/**
 * @deprecated 使用 NodeStateSnapshot
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
 * @deprecated 使用 NodeStateSnapshot
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
 * @deprecated 不再使用
 */
export interface LiteGraphData {
  last_node_id: number
  last_link_id: number
  nodes: unknown[]
  links: unknown[]
  groups?: unknown[]
  config?: Record<string, unknown>
  extra?: Record<string, unknown>
  version?: number
}

/**
 * @deprecated 使用 LogQueryResult
 */
export interface LogChunkResponse {
  lines: string[]
  total: number
  offset: number
}

/**
 * @deprecated
 */
export interface NodeLogInfo {
  nodeId: string
  nodeName?: string
}

/**
 * @deprecated
 */
export interface RuntimeSnapshot {
  nodes: NodeSnapshot[]
}

/**
 * @deprecated
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
 * @deprecated
 */
export interface RootNodeProperties {
  name: string
}
