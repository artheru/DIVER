/**
 * CANOpen 对象字典定义
 * 
 * 包含 CiA 301 通信配置（0x1000 区域）和常用设备配置
 */

/** 对象字典条目 */
export interface ObjectDictionaryEntry {
  index: number
  subIndex?: number
  name: string
  dataType?: string
  access?: 'RO' | 'RW' | 'WO' | 'CONST'
  description?: string
}

/** 0x1000 区域 - 通信配置 (CiA 301) */
export const CommunicationProfileObjects: ObjectDictionaryEntry[] = [
  { index: 0x1000, name: 'Device Type', dataType: 'U32', access: 'RO', description: '设备类型和 Profile ID' },
  { index: 0x1001, name: 'Error Register', dataType: 'U8', access: 'RO', description: '错误寄存器' },
  { index: 0x1002, name: 'Manufacturer Status Register', dataType: 'U32', access: 'RO' },
  { index: 0x1003, name: 'Pre-defined Error Field', dataType: 'U32[]', access: 'RO', description: '预定义错误记录' },
  { index: 0x1005, name: 'COB-ID SYNC', dataType: 'U32', access: 'RW', description: 'SYNC 消息 COB-ID' },
  { index: 0x1006, name: 'Communication Cycle Period', dataType: 'U32', access: 'RW', description: '通信周期 (μs)' },
  { index: 0x1007, name: 'Synchronous Window Length', dataType: 'U32', access: 'RW', description: '同步窗口长度 (μs)' },
  { index: 0x1008, name: 'Manufacturer Device Name', dataType: 'STRING', access: 'RO', description: '设备名称' },
  { index: 0x1009, name: 'Manufacturer HW Version', dataType: 'STRING', access: 'RO', description: '硬件版本' },
  { index: 0x100A, name: 'Manufacturer SW Version', dataType: 'STRING', access: 'RO', description: '软件版本' },
  { index: 0x100C, name: 'Guard Time', dataType: 'U16', access: 'RW', description: 'Node Guarding 时间 (ms)' },
  { index: 0x100D, name: 'Life Time Factor', dataType: 'U8', access: 'RW' },
  { index: 0x1010, name: 'Store Parameters', dataType: 'U32', access: 'RW', description: '存储参数 (写 0x65766173="save")' },
  { index: 0x1011, name: 'Restore Default Parameters', dataType: 'U32', access: 'RW', description: '恢复默认 (写 0x64616F6C="load")' },
  { index: 0x1012, name: 'COB-ID Timestamp', dataType: 'U32', access: 'RW' },
  { index: 0x1013, name: 'High Resolution Timestamp', dataType: 'U32', access: 'RW' },
  { index: 0x1014, name: 'COB-ID Emergency', dataType: 'U32', access: 'RW', description: 'Emergency COB-ID' },
  { index: 0x1015, name: 'Inhibit Time Emergency', dataType: 'U16', access: 'RW', description: 'Emergency 抑制时间 (100μs)' },
  { index: 0x1016, name: 'Consumer Heartbeat Time', dataType: 'U32[]', access: 'RW', description: '消费者心跳时间' },
  { index: 0x1017, name: 'Producer Heartbeat Time', dataType: 'U16', access: 'RW', description: '生产者心跳时间 (ms)' },
  // 0x1018 Identity Object
  { index: 0x1018, subIndex: 0, name: 'Identity Object - Number of Entries', dataType: 'U8', access: 'RO' },
  { index: 0x1018, subIndex: 1, name: 'Vendor ID', dataType: 'U32', access: 'RO', description: '厂商 ID' },
  { index: 0x1018, subIndex: 2, name: 'Product Code', dataType: 'U32', access: 'RO', description: '产品代码' },
  { index: 0x1018, subIndex: 3, name: 'Revision Number', dataType: 'U32', access: 'RO', description: '修订号' },
  { index: 0x1018, subIndex: 4, name: 'Serial Number', dataType: 'U32', access: 'RO', description: '序列号' },
  { index: 0x1019, name: 'Sync Counter Overflow Value', dataType: 'U8', access: 'RW' },
  { index: 0x1020, name: 'Verify Configuration', dataType: 'U32[]', access: 'RW' },
  { index: 0x1021, name: 'Store EDS', dataType: 'DOMAIN', access: 'RO' },
  { index: 0x1022, name: 'Store Format', dataType: 'U8', access: 'RO' },
  { index: 0x1023, name: 'OS Command', dataType: 'DOMAIN', access: 'RW' },
  { index: 0x1024, name: 'OS Command Mode', dataType: 'U8', access: 'WO' },
  { index: 0x1025, name: 'OS Debugger Interface', dataType: 'DOMAIN', access: 'RW' },
  { index: 0x1026, name: 'OS Prompt', dataType: 'U8[]', access: 'RO' },
  { index: 0x1027, name: 'Module List', dataType: 'U16[]', access: 'RO' },
  { index: 0x1028, name: 'Emergency Consumer', dataType: 'U32[]', access: 'RW' },
  { index: 0x1029, name: 'Error Behavior', dataType: 'U8[]', access: 'RW' },
  { index: 0x1200, name: 'SDO Server Parameter', dataType: 'RECORD', access: 'RW' },
  { index: 0x1280, name: 'SDO Client Parameter', dataType: 'RECORD', access: 'RW' },
]

/** SDO 参数范围 */
export const SDO_RANGES = {
  SDO_SERVER_START: 0x1200,
  SDO_SERVER_END: 0x127F,
  SDO_CLIENT_START: 0x1280,
  SDO_CLIENT_END: 0x12FF,
}

/** PDO 参数范围 */
export const PDO_RANGES = {
  RPDO_COMM_START: 0x1400,
  RPDO_COMM_END: 0x15FF,
  RPDO_MAP_START: 0x1600,
  RPDO_MAP_END: 0x17FF,
  TPDO_COMM_START: 0x1800,
  TPDO_COMM_END: 0x19FF,
  TPDO_MAP_START: 0x1A00,
  TPDO_MAP_END: 0x1BFF,
}

/**
 * 根据索引查找对象字典条目
 */
export function lookupObjectDictionary(index: number, subIndex?: number): ObjectDictionaryEntry | undefined {
  // 精确匹配（含 subIndex）
  if (subIndex !== undefined) {
    const exact = CommunicationProfileObjects.find(
      e => e.index === index && e.subIndex === subIndex
    )
    if (exact) return exact
  }
  
  // 仅匹配 index
  const byIndex = CommunicationProfileObjects.find(
    e => e.index === index && e.subIndex === undefined
  )
  if (byIndex) return byIndex
  
  // 范围匹配
  if (index >= SDO_RANGES.SDO_SERVER_START && index <= SDO_RANGES.SDO_SERVER_END) {
    const num = index - SDO_RANGES.SDO_SERVER_START
    return { index, name: `SDO Server ${num} Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  if (index >= SDO_RANGES.SDO_CLIENT_START && index <= SDO_RANGES.SDO_CLIENT_END) {
    const num = index - SDO_RANGES.SDO_CLIENT_START
    return { index, name: `SDO Client ${num} Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  if (index >= PDO_RANGES.RPDO_COMM_START && index <= PDO_RANGES.RPDO_COMM_END) {
    const num = index - PDO_RANGES.RPDO_COMM_START
    return { index, name: `RPDO ${num} Communication Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  if (index >= PDO_RANGES.RPDO_MAP_START && index <= PDO_RANGES.RPDO_MAP_END) {
    const num = index - PDO_RANGES.RPDO_MAP_START
    return { index, name: `RPDO ${num} Mapping Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  if (index >= PDO_RANGES.TPDO_COMM_START && index <= PDO_RANGES.TPDO_COMM_END) {
    const num = index - PDO_RANGES.TPDO_COMM_START
    return { index, name: `TPDO ${num} Communication Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  if (index >= PDO_RANGES.TPDO_MAP_START && index <= PDO_RANGES.TPDO_MAP_END) {
    const num = index - PDO_RANGES.TPDO_MAP_START
    return { index, name: `TPDO ${num} Mapping Parameter`, dataType: 'RECORD', access: 'RW' }
  }
  
  return undefined
}

/** 错误寄存器位定义 (0x1001) */
export const ErrorRegisterBits: Record<number, string> = {
  0: 'Generic Error',
  1: 'Current Error',
  2: 'Voltage Error',
  3: 'Temperature Error',
  4: 'Communication Error',
  5: 'Device Profile Specific',
  6: 'Reserved',
  7: 'Manufacturer Specific'
}

/**
 * 解析错误寄存器
 */
export function parseErrorRegister(value: number): string[] {
  const errors: string[] = []
  for (let i = 0; i < 8; i++) {
    if (value & (1 << i)) {
      errors.push(ErrorRegisterBits[i] || `Bit ${i}`)
    }
  }
  return errors
}
