/**
 * 协议解析基础类型定义
 * 
 * 设计原则：
 * 1. 所有解析器实现统一接口
 * 2. 解析结果采用统一格式
 * 3. 支持运行时注册新解析器
 */

/** 解析字段 */
export interface ParsedField {
  name: string           // 字段名称
  bytes: number[]        // 原始字节
  value: string | number // 解析后的值
  description?: string   // 额外描述（如枚举名称）
  highlight?: string     // 高亮颜色（CSS color）
}

/** 解析结果 */
export interface ParseResult {
  success: boolean
  protocol: string       // 协议名称（如 "MODBUS RTU"）
  messageType: string    // 消息类型（如 "Read Holding Registers"）
  summary: string        // 简短摘要
  fields: ParsedField[]  // 解析出的字段
  errors?: string[]      // 解析错误信息
  warnings?: string[]    // 警告信息
  raw: number[]          // 原始数据
}

/** 解析上下文 */
export interface ParseContext {
  direction: 'tx' | 'rx'  // 数据方向
  portType: 'serial' | 'can'
  portIndex: number
  timestamp?: Date
  // CAN 特有
  canId?: number
  canDlc?: number
  canRtr?: boolean
  canExt?: boolean
}

/** 协议解析器接口 */
export interface ProtocolParser {
  /** 解析器唯一标识 */
  readonly id: string
  
  /** 解析器显示名称 */
  readonly name: string
  
  /** 支持的端口类型 */
  readonly portTypes: ('serial' | 'can')[]
  
  /** 解析器描述 */
  readonly description: string
  
  /**
   * 检查数据是否可能属于该协议
   * 返回置信度 0-1，用于自动检测
   */
  detect(data: number[], context: ParseContext): number
  
  /**
   * 解析数据
   */
  parse(data: number[], context: ParseContext): ParseResult
}

/** 协议解析器工厂（用于创建带配置的解析器） */
export interface ProtocolParserFactory {
  readonly id: string
  readonly name: string
  readonly portTypes: ('serial' | 'can')[]
  readonly description: string
  
  /** 创建解析器实例 */
  create(config?: Record<string, unknown>): ProtocolParser
}

/** 字节工具函数 */
export const ByteUtils = {
  /** 转换为十六进制字符串 */
  toHex(bytes: number[], separator = ' '): string {
    return bytes.map(b => b.toString(16).padStart(2, '0').toUpperCase()).join(separator)
  },
  
  /** 读取 U16 Big Endian */
  readU16BE(bytes: number[], offset: number): number {
    return (bytes[offset] << 8) | bytes[offset + 1]
  },
  
  /** 读取 U16 Little Endian */
  readU16LE(bytes: number[], offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8)
  },
  
  /** 读取 U32 Big Endian */
  readU32BE(bytes: number[], offset: number): number {
    return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]
  },
  
  /** 读取 U32 Little Endian */
  readU32LE(bytes: number[], offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)
  },
  
  /** 读取 I16 Big Endian */
  readI16BE(bytes: number[], offset: number): number {
    const val = ByteUtils.readU16BE(bytes, offset)
    return val > 0x7FFF ? val - 0x10000 : val
  },
  
  /** 读取 I16 Little Endian */
  readI16LE(bytes: number[], offset: number): number {
    const val = ByteUtils.readU16LE(bytes, offset)
    return val > 0x7FFF ? val - 0x10000 : val
  }
}
