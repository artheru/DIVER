/**
 * 协议解析器注册中心
 * 
 * 提供：
 * 1. 解析器注册/注销
 * 2. 按端口类型获取可用解析器
 * 3. 自动协议检测
 */

import type { ProtocolParser, ProtocolParserFactory, ParseContext, ParseResult } from './types'

class ProtocolRegistry {
  private parsers = new Map<string, ProtocolParser>()
  private factories = new Map<string, ProtocolParserFactory>()
  
  /**
   * 注册解析器
   */
  register(parser: ProtocolParser): void {
    if (this.parsers.has(parser.id)) {
      console.warn(`[Protocol] Parser "${parser.id}" already registered, overwriting`)
    }
    this.parsers.set(parser.id, parser)
    console.log(`[Protocol] Registered parser: ${parser.name} (${parser.id})`)
  }
  
  /**
   * 注册解析器工厂
   */
  registerFactory(factory: ProtocolParserFactory): void {
    if (this.factories.has(factory.id)) {
      console.warn(`[Protocol] Factory "${factory.id}" already registered, overwriting`)
    }
    this.factories.set(factory.id, factory)
    console.log(`[Protocol] Registered factory: ${factory.name} (${factory.id})`)
  }
  
  /**
   * 注销解析器
   */
  unregister(id: string): boolean {
    return this.parsers.delete(id)
  }
  
  /**
   * 获取解析器
   */
  get(id: string): ProtocolParser | undefined {
    return this.parsers.get(id)
  }
  
  /**
   * 获取所有解析器
   */
  getAll(): ProtocolParser[] {
    return Array.from(this.parsers.values())
  }
  
  /**
   * 获取支持指定端口类型的解析器
   */
  getForPortType(portType: 'serial' | 'can'): ProtocolParser[] {
    return this.getAll().filter(p => p.portTypes.includes(portType))
  }
  
  /**
   * 自动检测协议并解析
   * 返回置信度最高的解析结果
   */
  autoDetectAndParse(data: number[], context: ParseContext): ParseResult | null {
    const candidates = this.getForPortType(context.portType)
    
    if (candidates.length === 0) {
      return null
    }
    
    // 计算每个解析器的置信度
    const scored = candidates.map(parser => ({
      parser,
      confidence: parser.detect(data, context)
    }))
    
    // 按置信度排序
    scored.sort((a, b) => b.confidence - a.confidence)
    
    // 如果有任何解析器有置信度，使用置信度最高的
    // (Raw Serial 后备解析器置信度为 0.1，所以总会有结果)
    if (scored[0].confidence > 0) {
      return scored[0].parser.parse(data, context)
    }
    
    return null
  }
  
  /**
   * 使用指定解析器解析
   */
  parseWith(parserId: string, data: number[], context: ParseContext): ParseResult | null {
    const parser = this.get(parserId)
    if (!parser) {
      console.warn(`[Protocol] Parser "${parserId}" not found`)
      return null
    }
    return parser.parse(data, context)
  }
}

// 单例导出
export const protocolRegistry = new ProtocolRegistry()
