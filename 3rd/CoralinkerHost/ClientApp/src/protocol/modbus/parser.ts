/**
 * MODBUS RTU 协议解析器
 */

import type { ProtocolParser, ParseContext, ParseResult, ParsedField } from '../types'
import { ByteUtils } from '../types'
import { ModbusFunctionCode, FunctionCodeNames, ExceptionCodeNames } from './types'
import { verifyCRC16, calculateCRC16 } from './crc'

export class ModbusRtuParser implements ProtocolParser {
  readonly id = 'modbus-rtu'
  readonly name = 'MODBUS RTU'
  readonly portTypes: ('serial' | 'can')[] = ['serial']
  readonly description = 'MODBUS RTU protocol over RS-485/RS-232'
  
  detect(data: number[], context: ParseContext): number {
    // 最小帧长度：地址(1) + 功能码(1) + CRC(2) = 4
    if (data.length < 4) return 0
    
    // 检查从机地址是否有效 (1-247)
    const slaveAddr = data[0]
    if (slaveAddr === 0 || slaveAddr > 247) return 0.15
    
    // 检查功能码是否在常见范围
    const funcCode = data[1] & 0x7F
    const isCommonFuncCode = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x0F, 0x10].includes(funcCode)
    
    if (funcCode < 1 || funcCode > 0x18) return 0.2
    
    // CRC 校验
    if (verifyCRC16(data)) {
      return 0.95  // 高置信度
    }
    
    // 格式看起来像 MODBUS 但 CRC 错误
    // （可能是部分数据，或者实际是其他协议）
    return isCommonFuncCode ? 0.5 : 0.35
  }
  
  parse(data: number[], context: ParseContext): ParseResult {
    const fields: ParsedField[] = []
    const errors: string[] = []
    const warnings: string[] = []
    
    if (data.length < 4) {
      return {
        success: false,
        protocol: this.name,
        messageType: 'Invalid',
        summary: 'Frame too short',
        fields: [],
        errors: ['Frame length must be at least 4 bytes'],
        raw: data
      }
    }
    
    // 解析从机地址
    const slaveAddr = data[0]
    fields.push({
      name: 'Slave Address',
      bytes: [slaveAddr],
      value: slaveAddr,
      description: slaveAddr === 0 ? 'Broadcast' : undefined,
      highlight: '#4fc3f7'
    })
    
    // 解析功能码
    const funcByte = data[1]
    const isException = (funcByte & 0x80) !== 0
    const funcCode = funcByte & 0x7F
    const funcName = FunctionCodeNames[funcCode] || `Unknown (0x${funcCode.toString(16).toUpperCase()})`
    
    fields.push({
      name: 'Function Code',
      bytes: [funcByte],
      value: `0x${funcByte.toString(16).padStart(2, '0').toUpperCase()}`,
      description: isException ? `Exception: ${funcName}` : funcName,
      highlight: isException ? '#ef5350' : '#81c784'
    })
    
    // 解析数据部分
    const dataBytes = data.slice(2, -2)
    const crcBytes = data.slice(-2)
    
    if (isException) {
      // 异常响应
      this.parseException(dataBytes, fields)
    } else {
      // 正常消息
      this.parseNormalMessage(funcCode, dataBytes, fields, context, errors)
    }
    
    // CRC 校验
    const receivedCRC = crcBytes[0] | (crcBytes[1] << 8)
    const calculatedCRC = calculateCRC16(data.slice(0, -2))
    const crcValid = receivedCRC === calculatedCRC
    
    fields.push({
      name: 'CRC',
      bytes: crcBytes,
      value: `0x${receivedCRC.toString(16).padStart(4, '0').toUpperCase()}`,
      description: crcValid ? '✓ Valid' : `✗ Invalid (expected 0x${calculatedCRC.toString(16).padStart(4, '0').toUpperCase()})`,
      highlight: crcValid ? '#81c784' : '#ef5350'
    })
    
    if (!crcValid) {
      errors.push('CRC checksum mismatch')
    }
    
    // 生成摘要
    const summary = this.generateSummary(slaveAddr, funcCode, isException, dataBytes, context)
    
    return {
      success: errors.length === 0,
      protocol: this.name,
      messageType: isException ? `Exception: ${funcName}` : funcName,
      summary,
      fields,
      errors: errors.length > 0 ? errors : undefined,
      warnings: warnings.length > 0 ? warnings : undefined,
      raw: data
    }
  }
  
  private parseException(dataBytes: number[], fields: ParsedField[]): void {
    if (dataBytes.length >= 1) {
      const exCode = dataBytes[0]
      const exName = ExceptionCodeNames[exCode] || 'Unknown'
      fields.push({
        name: 'Exception Code',
        bytes: [exCode],
        value: exCode,
        description: exName,
        highlight: '#ef5350'
      })
    }
  }
  
  private parseNormalMessage(
    funcCode: number,
    dataBytes: number[],
    fields: ParsedField[],
    context: ParseContext,
    errors: string[]
  ): void {
    switch (funcCode) {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
        this.parseReadBitsRequest(dataBytes, fields, context)
        break
        
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        this.parseReadRegistersRequest(dataBytes, fields, context)
        break
        
      case ModbusFunctionCode.WriteSingleCoil:
        this.parseWriteSingleCoil(dataBytes, fields)
        break
        
      case ModbusFunctionCode.WriteSingleRegister:
        this.parseWriteSingleRegister(dataBytes, fields)
        break
        
      case ModbusFunctionCode.WriteMultipleCoils:
        this.parseWriteMultipleCoils(dataBytes, fields, context)
        break
        
      case ModbusFunctionCode.WriteMultipleRegisters:
        this.parseWriteMultipleRegisters(dataBytes, fields, context)
        break
        
      default:
        // 未知功能码，显示原始数据
        if (dataBytes.length > 0) {
          fields.push({
            name: 'Data',
            bytes: dataBytes,
            value: ByteUtils.toHex(dataBytes),
            highlight: '#ffb74d'
          })
        }
    }
  }
  
  private parseReadBitsRequest(dataBytes: number[], fields: ParsedField[], context: ParseContext): void {
    if (dataBytes.length === 4) {
      // 请求：起始地址 + 数量
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        description: `0x${startAddr.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        highlight: '#ce93d8'
      })
    } else if (dataBytes.length >= 1) {
      // 响应：字节数 + 数据
      const byteCount = dataBytes[0]
      fields.push({
        name: 'Byte Count',
        bytes: [byteCount],
        value: byteCount,
        highlight: '#ce93d8'
      })
      
      if (dataBytes.length > 1) {
        const coilData = dataBytes.slice(1)
        fields.push({
          name: 'Coil Status',
          bytes: coilData,
          value: ByteUtils.toHex(coilData),
          description: this.formatCoilBits(coilData),
          highlight: '#90caf9'
        })
      }
    }
  }
  
  private parseReadRegistersRequest(dataBytes: number[], fields: ParsedField[], context: ParseContext): void {
    if (dataBytes.length === 4) {
      // 请求：起始地址 + 数量
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        description: `0x${startAddr.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        description: `${quantity} registers`,
        highlight: '#ce93d8'
      })
    } else if (dataBytes.length >= 1) {
      // 响应：字节数 + 寄存器值
      const byteCount = dataBytes[0]
      fields.push({
        name: 'Byte Count',
        bytes: [byteCount],
        value: byteCount,
        highlight: '#ce93d8'
      })
      
      // 解析寄存器值
      const regData = dataBytes.slice(1)
      for (let i = 0; i < regData.length; i += 2) {
        if (i + 1 < regData.length) {
          const regValue = ByteUtils.readU16BE(regData, i)
          fields.push({
            name: `Register ${i / 2}`,
            bytes: regData.slice(i, i + 2),
            value: regValue,
            description: `0x${regValue.toString(16).toUpperCase()}`,
            highlight: '#90caf9'
          })
        }
      }
    }
  }
  
  private parseWriteSingleCoil(dataBytes: number[], fields: ParsedField[]): void {
    if (dataBytes.length >= 4) {
      const address = ByteUtils.readU16BE(dataBytes, 0)
      const value = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Coil Address',
        bytes: dataBytes.slice(0, 2),
        value: address,
        description: `0x${address.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Value',
        bytes: dataBytes.slice(2, 4),
        value: value === 0xFF00 ? 'ON' : 'OFF',
        description: `0x${value.toString(16).toUpperCase()}`,
        highlight: value === 0xFF00 ? '#81c784' : '#ef5350'
      })
    }
  }
  
  private parseWriteSingleRegister(dataBytes: number[], fields: ParsedField[]): void {
    if (dataBytes.length >= 4) {
      const address = ByteUtils.readU16BE(dataBytes, 0)
      const value = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Register Address',
        bytes: dataBytes.slice(0, 2),
        value: address,
        description: `0x${address.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Value',
        bytes: dataBytes.slice(2, 4),
        value: value,
        description: `0x${value.toString(16).toUpperCase()}`,
        highlight: '#90caf9'
      })
    }
  }
  
  private parseWriteMultipleCoils(dataBytes: number[], fields: ParsedField[], context: ParseContext): void {
    if (dataBytes.length >= 5) {
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      const byteCount = dataBytes[4]
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        description: `0x${startAddr.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        highlight: '#ce93d8'
      })
      
      fields.push({
        name: 'Byte Count',
        bytes: [byteCount],
        value: byteCount,
        highlight: '#ce93d8'
      })
      
      if (dataBytes.length > 5) {
        const coilData = dataBytes.slice(5)
        fields.push({
          name: 'Coil Values',
          bytes: coilData,
          value: ByteUtils.toHex(coilData),
          highlight: '#90caf9'
        })
      }
    } else if (dataBytes.length === 4) {
      // 响应
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        highlight: '#ce93d8'
      })
    }
  }
  
  private parseWriteMultipleRegisters(dataBytes: number[], fields: ParsedField[], context: ParseContext): void {
    if (dataBytes.length >= 5) {
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      const byteCount = dataBytes[4]
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        description: `0x${startAddr.toString(16).toUpperCase()}`,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        description: `${quantity} registers`,
        highlight: '#ce93d8'
      })
      
      fields.push({
        name: 'Byte Count',
        bytes: [byteCount],
        value: byteCount,
        highlight: '#ce93d8'
      })
      
      // 解析寄存器值
      const regData = dataBytes.slice(5)
      for (let i = 0; i < regData.length && i < byteCount; i += 2) {
        if (i + 1 < regData.length) {
          const regValue = ByteUtils.readU16BE(regData, i)
          fields.push({
            name: `Value ${i / 2}`,
            bytes: regData.slice(i, i + 2),
            value: regValue,
            description: `0x${regValue.toString(16).toUpperCase()}`,
            highlight: '#90caf9'
          })
        }
      }
    } else if (dataBytes.length === 4) {
      // 响应
      const startAddr = ByteUtils.readU16BE(dataBytes, 0)
      const quantity = ByteUtils.readU16BE(dataBytes, 2)
      
      fields.push({
        name: 'Start Address',
        bytes: dataBytes.slice(0, 2),
        value: startAddr,
        highlight: '#ffb74d'
      })
      
      fields.push({
        name: 'Quantity Written',
        bytes: dataBytes.slice(2, 4),
        value: quantity,
        highlight: '#ce93d8'
      })
    }
  }
  
  private formatCoilBits(data: number[]): string {
    const bits: string[] = []
    for (let i = 0; i < data.length; i++) {
      for (let j = 0; j < 8; j++) {
        bits.push((data[i] >> j) & 1 ? '1' : '0')
      }
    }
    return bits.join('')
  }
  
  private generateSummary(
    slaveAddr: number,
    funcCode: number,
    isException: boolean,
    dataBytes: number[],
    context: ParseContext
  ): string {
    const funcName = FunctionCodeNames[funcCode] || 'Unknown'
    
    if (isException) {
      const exCode = dataBytes[0] || 0
      const exName = ExceptionCodeNames[exCode] || 'Unknown'
      return `Slave ${slaveAddr}: Exception ${exName}`
    }
    
    switch (funcCode) {
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        if (dataBytes.length === 4) {
          const addr = ByteUtils.readU16BE(dataBytes, 0)
          const qty = ByteUtils.readU16BE(dataBytes, 2)
          return `Slave ${slaveAddr}: Read ${qty} regs @ 0x${addr.toString(16).toUpperCase()}`
        } else if (dataBytes.length >= 1) {
          const count = dataBytes[0] / 2
          return `Slave ${slaveAddr}: Response ${count} registers`
        }
        break
        
      case ModbusFunctionCode.WriteSingleRegister:
        if (dataBytes.length >= 4) {
          const addr = ByteUtils.readU16BE(dataBytes, 0)
          const val = ByteUtils.readU16BE(dataBytes, 2)
          return `Slave ${slaveAddr}: Write ${val} @ 0x${addr.toString(16).toUpperCase()}`
        }
        break
        
      case ModbusFunctionCode.WriteMultipleRegisters:
        if (dataBytes.length >= 4) {
          const addr = ByteUtils.readU16BE(dataBytes, 0)
          const qty = ByteUtils.readU16BE(dataBytes, 2)
          return `Slave ${slaveAddr}: Write ${qty} regs @ 0x${addr.toString(16).toUpperCase()}`
        }
        break
    }
    
    return `Slave ${slaveAddr}: ${funcName}`
  }
}

// 导出单例
export const modbusRtuParser = new ModbusRtuParser()
