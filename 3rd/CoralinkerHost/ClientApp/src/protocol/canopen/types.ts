/**
 * CANOpen 协议类型定义
 */

/** COB-ID 范围定义 */
export const COB_ID_RANGES = {
  NMT: { start: 0x000, end: 0x000 },
  SYNC: { start: 0x080, end: 0x080 },
  EMERGENCY: { start: 0x081, end: 0x0FF },
  TIMESTAMP: { start: 0x100, end: 0x100 },
  TPDO1: { start: 0x180, end: 0x1FF },
  RPDO1: { start: 0x200, end: 0x27F },
  TPDO2: { start: 0x280, end: 0x2FF },
  RPDO2: { start: 0x300, end: 0x37F },
  TPDO3: { start: 0x380, end: 0x3FF },
  RPDO3: { start: 0x400, end: 0x47F },
  TPDO4: { start: 0x480, end: 0x4FF },
  RPDO4: { start: 0x500, end: 0x57F },
  SDO_TX: { start: 0x580, end: 0x5FF },
  SDO_RX: { start: 0x600, end: 0x67F },
  HEARTBEAT: { start: 0x700, end: 0x77F },
  LSS_TX: { start: 0x7E4, end: 0x7E4 },
  LSS_RX: { start: 0x7E5, end: 0x7E5 },
}

/** NMT 命令 */
export const NMT_COMMANDS: Record<number, string> = {
  0x01: 'Start (Operational)',
  0x02: 'Stop (Pre-operational)',
  0x80: 'Enter Pre-operational',
  0x81: 'Reset Node',
  0x82: 'Reset Communication',
}

/** NMT 状态 (Heartbeat) */
export const NMT_STATES: Record<number, string> = {
  0x00: 'Boot-up',
  0x04: 'Stopped',
  0x05: 'Operational',
  0x7F: 'Pre-operational',
}

/** SDO 命令说明符 */
export const SDO_COMMAND_SPECIFIERS = {
  // Client -> Server (Request)
  DOWNLOAD_INIT_REQ: 0x20,      // 0010 0xxx - Download initiate
  DOWNLOAD_SEG_REQ: 0x00,       // 000x xxxx - Download segment
  UPLOAD_INIT_REQ: 0x40,        // 0100 0000 - Upload initiate
  UPLOAD_SEG_REQ: 0x60,         // 011x xxxx - Upload segment
  ABORT: 0x80,                  // 1000 0000 - Abort
  BLOCK_DOWNLOAD_INIT: 0xC0,    // 1100 0xxx - Block download initiate
  BLOCK_UPLOAD_INIT: 0xA0,      // 1010 0xxx - Block upload initiate
  
  // Server -> Client (Response)
  DOWNLOAD_INIT_RESP: 0x60,     // 0110 0000 - Download response
  DOWNLOAD_SEG_RESP: 0x20,      // 001x xxxx - Download segment response
  UPLOAD_INIT_RESP: 0x40,       // 0100 0xxx - Upload response (with data)
  UPLOAD_SEG_RESP: 0x00,        // 000x xxxx - Upload segment response
}

/** SDO Abort 错误码 */
export const SDO_ABORT_CODES: Record<number, string> = {
  0x05030000: 'Toggle bit not alternated',
  0x05040000: 'SDO protocol timed out',
  0x05040001: 'Client/server command specifier not valid',
  0x05040002: 'Invalid block size',
  0x05040003: 'Invalid sequence number',
  0x05040004: 'CRC error',
  0x05040005: 'Out of memory',
  0x06010000: 'Unsupported access to object',
  0x06010001: 'Attempt to read write-only object',
  0x06010002: 'Attempt to write read-only object',
  0x06020000: 'Object does not exist',
  0x06040041: 'Object cannot be mapped to PDO',
  0x06040042: 'Number/length of objects exceeds PDO length',
  0x06040043: 'General parameter incompatibility',
  0x06040047: 'General internal incompatibility',
  0x06060000: 'Access failed due to hardware error',
  0x06070010: 'Data type mismatch, length of service parameter does not match',
  0x06070012: 'Data type mismatch, length too high',
  0x06070013: 'Data type mismatch, length too low',
  0x06090011: 'Sub-index does not exist',
  0x06090030: 'Invalid value for parameter (download only)',
  0x06090031: 'Value too high',
  0x06090032: 'Value too low',
  0x06090036: 'Maximum value is less than minimum value',
  0x060A0023: 'Resource not available: SDO connection',
  0x08000000: 'General error',
  0x08000020: 'Data cannot be transferred or stored',
  0x08000021: 'Data cannot be transferred due to local control',
  0x08000022: 'Data cannot be transferred due to device state',
  0x08000023: 'Object dictionary dynamic generation fails',
  0x08000024: 'No data available',
}

/** Emergency 错误码 (通用部分) */
export const EMERGENCY_ERROR_CODES: Record<number, string> = {
  0x0000: 'Error Reset / No Error',
  0x1000: 'Generic Error',
  0x2000: 'Current',
  0x2100: 'Current, device input side',
  0x2200: 'Current inside the device',
  0x2300: 'Current, device output side',
  0x3000: 'Voltage',
  0x3100: 'Mains Voltage',
  0x3200: 'Voltage inside the device',
  0x3300: 'Output Voltage',
  0x4000: 'Temperature',
  0x4100: 'Ambient Temperature',
  0x4200: 'Device Temperature',
  0x5000: 'Device Hardware',
  0x6000: 'Device Software',
  0x6100: 'Internal Software',
  0x6200: 'User Software',
  0x6300: 'Data Set',
  0x7000: 'Additional Modules',
  0x8000: 'Monitoring',
  0x8100: 'Communication',
  0x8110: 'CAN Overrun',
  0x8120: 'CAN in Error Passive Mode',
  0x8130: 'Life Guard Error / Heartbeat Error',
  0x8140: 'Recovered from Bus Off',
  0x8150: 'CAN-ID Collision',
  0x8200: 'Protocol Error',
  0x8210: 'PDO not processed due to length error',
  0x8220: 'PDO length exceeded',
  0x8230: 'DAM MPDO not processed, destination object not available',
  0x8240: 'Unexpected SYNC data length',
  0x8250: 'RPDO timeout',
  0x9000: 'External Error',
  0xF000: 'Additional Functions',
  0xFF00: 'Device Specific',
}

/**
 * 从 COB-ID 获取消息类型
 */
export function getMessageTypeFromCobId(cobId: number): {
  type: string
  nodeId?: number
  baseId?: number
} {
  if (cobId === 0x000) {
    return { type: 'NMT' }
  }
  
  if (cobId === 0x080) {
    return { type: 'SYNC' }
  }
  
  if (cobId === 0x100) {
    return { type: 'TIME' }
  }
  
  if (cobId >= 0x081 && cobId <= 0x0FF) {
    return { type: 'EMCY', nodeId: cobId - 0x080, baseId: 0x080 }
  }
  
  if (cobId >= 0x180 && cobId <= 0x1FF) {
    return { type: 'TPDO1', nodeId: cobId - 0x180, baseId: 0x180 }
  }
  
  if (cobId >= 0x200 && cobId <= 0x27F) {
    return { type: 'RPDO1', nodeId: cobId - 0x200, baseId: 0x200 }
  }
  
  if (cobId >= 0x280 && cobId <= 0x2FF) {
    return { type: 'TPDO2', nodeId: cobId - 0x280, baseId: 0x280 }
  }
  
  if (cobId >= 0x300 && cobId <= 0x37F) {
    return { type: 'RPDO2', nodeId: cobId - 0x300, baseId: 0x300 }
  }
  
  if (cobId >= 0x380 && cobId <= 0x3FF) {
    return { type: 'TPDO3', nodeId: cobId - 0x380, baseId: 0x380 }
  }
  
  if (cobId >= 0x400 && cobId <= 0x47F) {
    return { type: 'RPDO3', nodeId: cobId - 0x400, baseId: 0x400 }
  }
  
  if (cobId >= 0x480 && cobId <= 0x4FF) {
    return { type: 'TPDO4', nodeId: cobId - 0x480, baseId: 0x480 }
  }
  
  if (cobId >= 0x500 && cobId <= 0x57F) {
    return { type: 'RPDO4', nodeId: cobId - 0x500, baseId: 0x500 }
  }
  
  if (cobId >= 0x580 && cobId <= 0x5FF) {
    return { type: 'SDO_TX', nodeId: cobId - 0x580, baseId: 0x580 }
  }
  
  if (cobId >= 0x600 && cobId <= 0x67F) {
    return { type: 'SDO_RX', nodeId: cobId - 0x600, baseId: 0x600 }
  }
  
  if (cobId >= 0x700 && cobId <= 0x77F) {
    return { type: 'HEARTBEAT', nodeId: cobId - 0x700, baseId: 0x700 }
  }
  
  if (cobId === 0x7E4) {
    return { type: 'LSS_TX' }
  }
  
  if (cobId === 0x7E5) {
    return { type: 'LSS_RX' }
  }
  
  return { type: 'UNKNOWN' }
}
