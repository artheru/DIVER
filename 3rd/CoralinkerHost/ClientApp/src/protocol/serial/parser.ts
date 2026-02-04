/**
 * 通用 Serial 数据解析器
 * 作为后备，当没有特定协议匹配时显示原始数据和基本解析
 */

import type { ProtocolParser, ParseContext, ParseResult, ParsedField } from '../types'
import { ByteUtils } from '../types'

export class RawSerialParser implements ProtocolParser {
  readonly id = 'raw-serial'
  readonly name = 'Raw Serial'
  readonly portTypes: ('serial' | 'can')[] = ['serial']
  readonly description = 'Raw serial data display with basic analysis'
  
  detect(data: number[], context: ParseContext): number {
    // 始终返回低置信度，作为后备解析器
    if (context.portType !== 'serial') return 0
    if (data.length === 0) return 0
    return 0.1
  }
  
  parse(data: number[], context: ParseContext): ParseResult {
    const fields: ParsedField[] = []
    
    // 基本信息
    fields.push({
      name: 'Length',
      bytes: [],
      value: `${data.length} bytes`,
      highlight: '#4fc3f7'
    })
    
    // 原始十六进制
    fields.push({
      name: 'Hex Data',
      bytes: data,
      value: ByteUtils.toHex(data),
      highlight: '#94a3b8'
    })
    
    // 尝试 ASCII 解析
    const ascii = this.tryDecodeAscii(data)
    if (ascii) {
      fields.push({
        name: 'ASCII',
        bytes: [],
        value: ascii,
        description: 'Printable characters',
        highlight: '#22d3ee'
      })
    }
    
    // 数值解析（如果长度合适）
    if (data.length >= 2) {
      const u16le = ByteUtils.readU16LE(data, 0)
      const u16be = ByteUtils.readU16BE(data, 0)
      fields.push({
        name: 'First 2 bytes (U16 LE)',
        bytes: data.slice(0, 2),
        value: u16le,
        description: `0x${u16le.toString(16).toUpperCase().padStart(4, '0')}`,
        highlight: '#e0e0e0'
      })
      if (u16le !== u16be) {
        fields.push({
          name: 'First 2 bytes (U16 BE)',
          bytes: data.slice(0, 2),
          value: u16be,
          description: `0x${u16be.toString(16).toUpperCase().padStart(4, '0')}`,
          highlight: '#e0e0e0'
        })
      }
    }
    
    if (data.length >= 4) {
      const u32le = ByteUtils.readU32LE(data, 0)
      const u32be = ByteUtils.readU32BE(data, 0)
      fields.push({
        name: 'First 4 bytes (U32 LE)',
        bytes: data.slice(0, 4),
        value: u32le,
        description: `0x${u32le.toString(16).toUpperCase().padStart(8, '0')}`,
        highlight: '#e0e0e0'
      })
    }
    
    // 分析特征
    const analysis = this.analyzeData(data)
    if (analysis.length > 0) {
      fields.push({
        name: 'Analysis',
        bytes: [],
        value: analysis.join(', '),
        highlight: '#fbbf24'
      })
    }
    
    return {
      success: true,
      protocol: this.name,
      messageType: 'Raw Data',
      summary: `${data.length} bytes ${context.direction.toUpperCase()}`,
      fields,
      raw: data
    }
  }
  
  private tryDecodeAscii(data: number[]): string | null {
    let result = ''
    let printableCount = 0
    
    for (const b of data) {
      if (b >= 32 && b < 127) {
        result += String.fromCharCode(b)
        printableCount++
      } else if (b === 0x0D || b === 0x0A || b === 0x09) {
        // CR, LF, TAB
        result += b === 0x0D ? '\\r' : (b === 0x0A ? '\\n' : '\\t')
        printableCount++
      } else {
        result += '.'
      }
    }
    
    // 如果超过 50% 是可打印字符，返回结果
    if (printableCount / data.length >= 0.5) {
      return result
    }
    return null
  }
  
  private analyzeData(data: number[]): string[] {
    const hints: string[] = []
    
    // 检查是否全是 ASCII 可打印字符
    const allPrintable = data.every(b => (b >= 32 && b < 127) || b === 0x0D || b === 0x0A)
    if (allPrintable && data.length > 0) {
      hints.push('All printable ASCII')
    }
    
    // 检查是否以常见结束符结尾
    if (data.length >= 2) {
      const last2 = data.slice(-2)
      if (last2[0] === 0x0D && last2[1] === 0x0A) {
        hints.push('Ends with CRLF')
      }
    }
    if (data.length >= 1 && data[data.length - 1] === 0x0A) {
      hints.push('Ends with LF')
    }
    
    // 检查是否有空字节
    if (data.includes(0x00)) {
      hints.push('Contains NULL bytes')
    }
    
    // 检查可能的帧头
    if (data.length >= 2 && data[0] === 0x7E) {
      hints.push('Possible HDLC frame (0x7E)')
    }
    if (data.length >= 2 && data[0] === 0xAA && data[1] === 0x55) {
      hints.push('Common sync pattern (0xAA55)')
    }
    if (data.length >= 2 && data[0] === 0x55 && data[1] === 0xAA) {
      hints.push('Common sync pattern (0x55AA)')
    }
    
    return hints
  }
}

export const rawSerialParser = new RawSerialParser()
