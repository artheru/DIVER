/**
 * CANOpen 协议解析器
 */

import type { ProtocolParser, ParseContext, ParseResult, ParsedField } from '../types'
import { ByteUtils } from '../types'
import { 
  getMessageTypeFromCobId, 
  NMT_COMMANDS, 
  NMT_STATES, 
  SDO_ABORT_CODES,
  EMERGENCY_ERROR_CODES 
} from './types'
import { lookupObjectDictionary } from './dictionary'
import { 
  lookupCiA402Object, 
  parseStatusword, 
  parseControlword, 
  OperationModes 
} from './cia402'

export class CANopenParser implements ProtocolParser {
  readonly id = 'canopen'
  readonly name = 'CANOpen'
  readonly portTypes: ('serial' | 'can')[] = ['can']
  readonly description = 'CANOpen protocol (CiA 301 + CiA 402)'
  
  detect(data: number[], context: ParseContext): number {
    if (context.portType !== 'can') return 0
    // 注意：canId 可以是 0 (NMT 消息)，所以用 undefined 检查
    if (context.canId === undefined) return 0
    
    const cobId = context.canId
    const msgInfo = getMessageTypeFromCobId(cobId)
    
    // 已知的 CANOpen 消息类型
    if (msgInfo.type !== 'UNKNOWN') {
      return 0.8
    }
    
    return 0.2
  }
  
  parse(data: number[], context: ParseContext): ParseResult {
    const fields: ParsedField[] = []
    const errors: string[] = []
    const warnings: string[] = []
    
    const cobId = context.canId || 0
    const msgInfo = getMessageTypeFromCobId(cobId)
    
    // COB-ID 字段
    fields.push({
      name: 'COB-ID',
      bytes: [(cobId >> 8) & 0xFF, cobId & 0xFF],
      value: `0x${cobId.toString(16).toUpperCase().padStart(3, '0')}`,
      description: msgInfo.type + (msgInfo.nodeId !== undefined ? ` (Node ${msgInfo.nodeId})` : ''),
      highlight: '#4fc3f7'
    })
    
    let messageType = msgInfo.type
    let summary = ''
    
    switch (msgInfo.type) {
      case 'NMT':
        this.parseNMT(data, fields)
        summary = this.getNMTSummary(data)
        break
        
      case 'SYNC':
        this.parseSYNC(data, fields)
        summary = 'SYNC'
        break
        
      case 'EMCY':
        this.parseEmergency(data, fields, msgInfo.nodeId!)
        summary = this.getEmergencySummary(data, msgInfo.nodeId!)
        break
        
      case 'HEARTBEAT':
        this.parseHeartbeat(data, fields, msgInfo.nodeId!)
        summary = this.getHeartbeatSummary(data, msgInfo.nodeId!)
        break
        
      case 'SDO_TX':
      case 'SDO_RX':
        const sdoResult = this.parseSDO(data, fields, msgInfo.type, msgInfo.nodeId!)
        messageType = sdoResult.messageType
        summary = sdoResult.summary
        break
        
      case 'TPDO1':
      case 'TPDO2':
      case 'TPDO3':
      case 'TPDO4':
      case 'RPDO1':
      case 'RPDO2':
      case 'RPDO3':
      case 'RPDO4':
        this.parsePDO(data, fields, msgInfo.type, msgInfo.nodeId!)
        summary = `${msgInfo.type} Node ${msgInfo.nodeId}: ${data.length} bytes`
        break
        
      default:
        // 未知消息
        if (data.length > 0) {
          fields.push({
            name: 'Data',
            bytes: data,
            value: ByteUtils.toHex(data),
            highlight: '#ffb74d'
          })
        }
        summary = `Unknown COB-ID 0x${cobId.toString(16).toUpperCase()}`
    }
    
    return {
      success: errors.length === 0,
      protocol: this.name,
      messageType,
      summary,
      fields,
      errors: errors.length > 0 ? errors : undefined,
      warnings: warnings.length > 0 ? warnings : undefined,
      raw: data
    }
  }
  
  private parseNMT(data: number[], fields: ParsedField[]): void {
    if (data.length >= 2) {
      const command = data[0]
      const nodeId = data[1]
      
      fields.push({
        name: 'Command',
        bytes: [command],
        value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
        description: NMT_COMMANDS[command] || 'Unknown',
        highlight: '#81c784'
      })
      
      fields.push({
        name: 'Target Node',
        bytes: [nodeId],
        value: nodeId,
        description: nodeId === 0 ? 'All Nodes (Broadcast)' : `Node ${nodeId}`,
        highlight: '#ffb74d'
      })
    }
  }
  
  private getNMTSummary(data: number[]): string {
    if (data.length >= 2) {
      const cmd = NMT_COMMANDS[data[0]] || 'Unknown'
      const node = data[1] === 0 ? 'All' : `Node ${data[1]}`
      return `NMT ${cmd} → ${node}`
    }
    return 'NMT'
  }
  
  /**
   * 查找对象字典条目（带来源标识）
   */
  private lookupObject(index: number, subIndex?: number): { 
    entry: ReturnType<typeof lookupObjectDictionary>
    source: string | null 
  } {
    // 先查找 CiA 301 通信配置
    const cia301Entry = lookupObjectDictionary(index, subIndex)
    if (cia301Entry) {
      return { entry: cia301Entry, source: 'CiA 301' }
    }
    
    // 再查找 CiA 402 驱动器配置
    const cia402Entry = lookupCiA402Object(index, subIndex)
    if (cia402Entry) {
      return { entry: cia402Entry, source: 'CiA 402' }
    }
    
    return { entry: undefined, source: null }
  }
  
  private parseSYNC(data: number[], fields: ParsedField[]): void {
    if (data.length >= 1) {
      fields.push({
        name: 'Counter',
        bytes: [data[0]],
        value: data[0],
        highlight: '#ce93d8'
      })
    }
  }
  
  private parseEmergency(data: number[], fields: ParsedField[], nodeId: number): void {
    if (data.length >= 8) {
      // Emergency Error Code (bytes 0-1)
      const errorCode = ByteUtils.readU16LE(data, 0)
      const errorName = this.lookupEmergencyError(errorCode)
      
      fields.push({
        name: 'Error Code',
        bytes: data.slice(0, 2),
        value: `0x${errorCode.toString(16).toUpperCase().padStart(4, '0')}`,
        description: errorName,
        highlight: '#ef5350'
      })
      
      // Error Register (byte 2)
      fields.push({
        name: 'Error Register',
        bytes: [data[2]],
        value: `0x${data[2].toString(16).toUpperCase().padStart(2, '0')}`,
        highlight: '#ffb74d'
      })
      
      // Manufacturer Specific (bytes 3-7)
      const mfgData = data.slice(3, 8)
      fields.push({
        name: 'Manufacturer Data',
        bytes: mfgData,
        value: ByteUtils.toHex(mfgData),
        highlight: '#90caf9'
      })
    }
  }
  
  private lookupEmergencyError(code: number): string {
    // 精确匹配
    if (EMERGENCY_ERROR_CODES[code]) {
      return EMERGENCY_ERROR_CODES[code]
    }
    // 按类别匹配（高字节）
    const category = code & 0xFF00
    if (EMERGENCY_ERROR_CODES[category]) {
      return EMERGENCY_ERROR_CODES[category]
    }
    return 'Unknown'
  }
  
  private getEmergencySummary(data: number[], nodeId: number): string {
    if (data.length >= 2) {
      const errorCode = ByteUtils.readU16LE(data, 0)
      const errorName = this.lookupEmergencyError(errorCode)
      return `EMCY Node ${nodeId}: ${errorName}`
    }
    return `EMCY Node ${nodeId}`
  }
  
  private parseHeartbeat(data: number[], fields: ParsedField[], nodeId: number): void {
    if (data.length >= 1) {
      const state = data[0]
      const stateName = NMT_STATES[state] || 'Unknown'
      
      fields.push({
        name: 'NMT State',
        bytes: [state],
        value: `0x${state.toString(16).toUpperCase().padStart(2, '0')}`,
        description: stateName,
        highlight: state === 0x05 ? '#81c784' : '#ffb74d'
      })
    }
  }
  
  private getHeartbeatSummary(data: number[], nodeId: number): string {
    if (data.length >= 1) {
      const stateName = NMT_STATES[data[0]] || 'Unknown'
      return `Heartbeat Node ${nodeId}: ${stateName}`
    }
    return `Heartbeat Node ${nodeId}`
  }
  
  private parseSDO(
    data: number[], 
    fields: ParsedField[], 
    type: string, 
    nodeId: number
  ): { messageType: string; summary: string } {
    if (data.length < 1) {
      return { messageType: 'SDO', summary: `SDO Node ${nodeId}: Empty` }
    }
    
    const command = data[0]
    const isRequest = type === 'SDO_RX'  // RX = Client → Server = Request
    
    // 解析命令说明符
    const ccs = (command >> 5) & 0x07  // Client Command Specifier
    
    let messageType = 'SDO'
    let summary = ''
    
    // Abort Transfer
    if ((command & 0xE0) === 0x80) {
      return this.parseSDOAbort(data, fields, nodeId)
    }
    
    // 解析索引和子索引（如果存在）
    if (data.length >= 4) {
      const index = ByteUtils.readU16LE(data, 1)
      const subIndex = data[3]
      
      // 查找对象字典（带来源标识）
      const { entry: objEntry, source } = this.lookupObject(index, subIndex)
      
      const indexStr = `0x${index.toString(16).toUpperCase().padStart(4, '0')}`
      const objName = objEntry?.name || 'Unknown'
      // 添加协议来源标识
      const objDesc = objEntry 
        ? (source ? `[${source}] ${objName}` : objName)
        : 'Unknown'
      
      fields.push({
        name: 'Index',
        bytes: data.slice(1, 3),
        value: indexStr,
        description: objDesc,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Sub-Index',
        bytes: [subIndex],
        value: subIndex,
        highlight: '#ce93d8'
      })
      
      // 解析命令类型和数据
      if (isRequest) {
        // Upload Request (Read)
        if ((command & 0xE0) === 0x40) {
          messageType = 'SDO Upload Request'
          fields.unshift({
            name: 'Command',
            bytes: [command],
            value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
            description: 'Upload Initiate (Read Request)',
            highlight: '#81c784'
          })
          // 对于 Upload Request，后面 4 字节是保留的（未使用）
          if (data.length >= 8) {
            const reserved = data.slice(4, 8)
            fields.push({
              name: 'Reserved',
              bytes: reserved,
              value: ByteUtils.toHex(reserved),
              description: '(unused in request)',
              highlight: '#64748b'
            })
          }
          summary = `SDO Read Node ${nodeId}: ${indexStr}:${subIndex} (${objName})`
        }
        // Download Request (Write)
        else if ((command & 0xE0) === 0x20) {
          const expedited = (command & 0x02) !== 0
          const sizeIndicated = (command & 0x01) !== 0
          let dataSize = 4
          if (expedited && sizeIndicated) {
            dataSize = 4 - ((command >> 2) & 0x03)
          }
          
          messageType = 'SDO Download Request'
          fields.unshift({
            name: 'Command',
            bytes: [command],
            value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
            description: expedited ? `Download ${dataSize} bytes (Write)` : 'Download Initiate (Write)',
            highlight: '#81c784'
          })
          
          // 解析数据
          if (expedited && data.length >= 8) {
            const sdoData = data.slice(4, 4 + dataSize)
            const dataResult = this.parseSDOData(index, subIndex, sdoData, objEntry)
            fields.push(dataResult.field)
            summary = `SDO Write Node ${nodeId}: ${indexStr}:${subIndex} = ${dataResult.valueStr} (${objName})`
          } else {
            summary = `SDO Write Node ${nodeId}: ${indexStr}:${subIndex} (${objName})`
          }
        }
      } else {
        // Upload Response (Read Response)
        if ((command & 0xE0) === 0x40) {
          const expedited = (command & 0x02) !== 0
          const sizeIndicated = (command & 0x01) !== 0
          let dataSize = 4
          if (expedited && sizeIndicated) {
            dataSize = 4 - ((command >> 2) & 0x03)
          }
          
          messageType = 'SDO Upload Response'
          fields.unshift({
            name: 'Command',
            bytes: [command],
            value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
            description: expedited ? `Upload ${dataSize} bytes` : 'Upload Initiate',
            highlight: '#81c784'
          })
          
          // 解析数据
          if (expedited && data.length >= 8) {
            const sdoData = data.slice(4, 4 + dataSize)
            const dataResult = this.parseSDOData(index, subIndex, sdoData, objEntry)
            fields.push(dataResult.field)
            summary = `SDO Response Node ${nodeId}: ${indexStr}:${subIndex} = ${dataResult.valueStr} (${objName})`
          } else {
            summary = `SDO Response Node ${nodeId}: ${indexStr}:${subIndex} (${objName})`
          }
        }
        // Download Response (Write ACK)
        else if ((command & 0xE0) === 0x60) {
          messageType = 'SDO Download Response'
          fields.unshift({
            name: 'Command',
            bytes: [command],
            value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
            description: 'Download Confirmed (Write ACK)',
            highlight: '#81c784'
          })
          // 对于 Download Response，后面 4 字节是保留的
          if (data.length >= 8) {
            const reserved = data.slice(4, 8)
            fields.push({
              name: 'Reserved',
              bytes: reserved,
              value: ByteUtils.toHex(reserved),
              description: '(unused in response)',
              highlight: '#64748b'
            })
          }
          summary = `SDO Write ACK Node ${nodeId}: ${indexStr}:${subIndex} (${objName})`
        }
      }
    } else {
      // 数据太短
      fields.push({
        name: 'Command',
        bytes: [command],
        value: `0x${command.toString(16).toUpperCase().padStart(2, '0')}`,
        highlight: '#81c784'
      })
      
      if (data.length > 1) {
        fields.push({
          name: 'Data',
          bytes: data.slice(1),
          value: ByteUtils.toHex(data.slice(1)),
          highlight: '#90caf9'
        })
      }
      
      summary = `SDO Node ${nodeId}`
    }
    
    return { messageType, summary }
  }
  
  private parseSDOAbort(
    data: number[], 
    fields: ParsedField[], 
    nodeId: number
  ): { messageType: string; summary: string } {
    fields.push({
      name: 'Command',
      bytes: [data[0]],
      value: '0x80',
      description: 'Abort Transfer',
      highlight: '#ef5350'
    })
    
    if (data.length >= 4) {
      const index = ByteUtils.readU16LE(data, 1)
      const subIndex = data[3]
      
      fields.push({
        name: 'Index',
        bytes: data.slice(1, 3),
        value: `0x${index.toString(16).toUpperCase().padStart(4, '0')}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Sub-Index',
        bytes: [subIndex],
        value: subIndex,
        highlight: '#ce93d8'
      })
    }
    
    if (data.length >= 8) {
      const abortCode = ByteUtils.readU32LE(data, 4)
      const abortName = SDO_ABORT_CODES[abortCode] || 'Unknown'
      
      fields.push({
        name: 'Abort Code',
        bytes: data.slice(4, 8),
        value: `0x${abortCode.toString(16).toUpperCase().padStart(8, '0')}`,
        description: abortName,
        highlight: '#ef5350'
      })
      
      return {
        messageType: 'SDO Abort',
        summary: `SDO Abort Node ${nodeId}: ${abortName}`
      }
    }
    
    return { messageType: 'SDO Abort', summary: `SDO Abort Node ${nodeId}` }
  }
  
  private parseSDOData(
    index: number, 
    subIndex: number, 
    data: number[],
    objEntry?: { name?: string; dataType?: string }
  ): { field: ParsedField; valueStr: string } {
    let value: number | string
    let description: string | undefined
    let valueStr: string
    
    // 特殊处理 CiA 402 对象
    if (index === 0x6040 && data.length >= 2) {
      // Controlword
      const cwValue = ByteUtils.readU16LE(data, 0)
      const cwResult = parseControlword(cwValue)
      value = cwValue
      description = cwResult.command
      valueStr = `${cwValue} (${cwResult.command})`
    } else if (index === 0x6041 && data.length >= 2) {
      // Statusword
      const swValue = ByteUtils.readU16LE(data, 0)
      const swResult = parseStatusword(swValue)
      value = swValue
      description = swResult.state
      valueStr = `${swValue} (${swResult.state})`
    } else if ((index === 0x6060 || index === 0x6061) && data.length >= 1) {
      // Modes of Operation
      const mode = data[0] > 127 ? data[0] - 256 : data[0]
      const modeName = OperationModes[mode] || 'Unknown'
      value = mode
      description = modeName
      valueStr = `${mode} (${modeName})`
    } else if (index === 0x1017 && data.length >= 2) {
      // Producer Heartbeat Time
      const ms = ByteUtils.readU16LE(data, 0)
      value = ms
      description = `${ms} ms`
      valueStr = `${ms} ms`
    } else {
      // 通用解析
      if (data.length === 1) {
        value = data[0]
        valueStr = `${value}`
      } else if (data.length === 2) {
        value = ByteUtils.readU16LE(data, 0)
        valueStr = `${value}`
      } else if (data.length === 4) {
        value = ByteUtils.readU32LE(data, 0)
        valueStr = `${value}`
      } else {
        value = ByteUtils.toHex(data)
        valueStr = value as string
      }
    }
    
    return {
      field: {
        name: 'Data',
        bytes: data,
        value,
        description,
        highlight: '#90caf9'
      },
      valueStr
    }
  }
  
  private parsePDO(data: number[], fields: ParsedField[], type: string, nodeId: number): void {
    // PDO 数据是映射定义的，这里只显示原始字节
    // 可以扩展为根据 PDO 映射配置解析
    
    if (data.length > 0) {
      fields.push({
        name: 'PDO Data',
        bytes: data,
        value: ByteUtils.toHex(data),
        description: `${data.length} bytes`,
        highlight: '#90caf9'
      })
      
      // 尝试解析常见的数据格式
      if (data.length >= 2) {
        const u16 = ByteUtils.readU16LE(data, 0)
        const i16 = ByteUtils.readI16LE(data, 0)
        fields.push({
          name: 'As U16 LE',
          bytes: data.slice(0, 2),
          value: u16,
          highlight: '#e0e0e0'
        })
        if (i16 !== u16) {
          fields.push({
            name: 'As I16 LE',
            bytes: data.slice(0, 2),
            value: i16,
            highlight: '#e0e0e0'
          })
        }
      }
      
      if (data.length >= 4) {
        const u32 = ByteUtils.readU32LE(data, 0)
        fields.push({
          name: 'As U32 LE',
          bytes: data.slice(0, 4),
          value: u32,
          highlight: '#e0e0e0'
        })
      }
    }
  }
}

// 导出单例
export const canopenParser = new CANopenParser()
