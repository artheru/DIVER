/**
 * CiA 402 驱动器配置文件
 * 
 * 定义驱动器和运动控制相关的对象字典
 */

import type { ObjectDictionaryEntry } from './dictionary'

/** CiA 402 设备配置对象 (0x6000 区域) */
export const CiA402Objects: ObjectDictionaryEntry[] = [
  // 控制和状态
  { index: 0x6040, name: 'Controlword', dataType: 'U16', access: 'RW', description: '控制字' },
  { index: 0x6041, name: 'Statusword', dataType: 'U16', access: 'RO', description: '状态字' },
  
  // 运行模式
  { index: 0x6060, name: 'Modes of Operation', dataType: 'I8', access: 'RW', description: '运行模式设置' },
  { index: 0x6061, name: 'Modes of Operation Display', dataType: 'I8', access: 'RO', description: '当前运行模式' },
  
  // 位置相关
  { index: 0x6062, name: 'Position Demand Value', dataType: 'I32', access: 'RO', description: '位置需求值' },
  { index: 0x6063, name: 'Position Actual Internal Value', dataType: 'I32', access: 'RO' },
  { index: 0x6064, name: 'Position Actual Value', dataType: 'I32', access: 'RO', description: '实际位置' },
  { index: 0x6065, name: 'Following Error Window', dataType: 'U32', access: 'RW' },
  { index: 0x6066, name: 'Following Error Time Out', dataType: 'U16', access: 'RW' },
  { index: 0x6067, name: 'Position Window', dataType: 'U32', access: 'RW' },
  { index: 0x6068, name: 'Position Window Time', dataType: 'U16', access: 'RW' },
  
  // 速度相关
  { index: 0x606B, name: 'Velocity Demand Value', dataType: 'I32', access: 'RO', description: '速度需求值' },
  { index: 0x606C, name: 'Velocity Actual Value', dataType: 'I32', access: 'RO', description: '实际速度' },
  { index: 0x606D, name: 'Velocity Window', dataType: 'U16', access: 'RW' },
  { index: 0x606E, name: 'Velocity Window Time', dataType: 'U16', access: 'RW' },
  { index: 0x606F, name: 'Velocity Threshold', dataType: 'U16', access: 'RW' },
  { index: 0x6070, name: 'Velocity Threshold Time', dataType: 'U16', access: 'RW' },
  
  // 扭矩相关
  { index: 0x6071, name: 'Target Torque', dataType: 'I16', access: 'RW', description: '目标扭矩' },
  { index: 0x6072, name: 'Max Torque', dataType: 'U16', access: 'RW' },
  { index: 0x6073, name: 'Max Current', dataType: 'U16', access: 'RW' },
  { index: 0x6074, name: 'Torque Demand Value', dataType: 'I16', access: 'RO' },
  { index: 0x6075, name: 'Motor Rated Current', dataType: 'U32', access: 'RW' },
  { index: 0x6076, name: 'Motor Rated Torque', dataType: 'U32', access: 'RW' },
  { index: 0x6077, name: 'Torque Actual Value', dataType: 'I16', access: 'RO', description: '实际扭矩' },
  { index: 0x6078, name: 'Current Actual Value', dataType: 'I16', access: 'RO' },
  { index: 0x6079, name: 'DC Link Circuit Voltage', dataType: 'U32', access: 'RO' },
  
  // 目标值
  { index: 0x607A, name: 'Target Position', dataType: 'I32', access: 'RW', description: '目标位置' },
  { index: 0x607B, name: 'Position Range Limit', dataType: 'I32[]', access: 'RW' },
  { index: 0x607C, name: 'Home Offset', dataType: 'I32', access: 'RW' },
  { index: 0x607D, name: 'Software Position Limit', dataType: 'I32[]', access: 'RW' },
  { index: 0x607E, name: 'Polarity', dataType: 'U8', access: 'RW' },
  
  // 速度设置
  { index: 0x607F, name: 'Max Profile Velocity', dataType: 'U32', access: 'RW' },
  { index: 0x6080, name: 'Max Motor Speed', dataType: 'U32', access: 'RW' },
  { index: 0x6081, name: 'Profile Velocity', dataType: 'U32', access: 'RW', description: '轮廓速度' },
  { index: 0x6082, name: 'End Velocity', dataType: 'U32', access: 'RW' },
  { index: 0x6083, name: 'Profile Acceleration', dataType: 'U32', access: 'RW', description: '轮廓加速度' },
  { index: 0x6084, name: 'Profile Deceleration', dataType: 'U32', access: 'RW', description: '轮廓减速度' },
  { index: 0x6085, name: 'Quick Stop Deceleration', dataType: 'U32', access: 'RW' },
  { index: 0x6086, name: 'Motion Profile Type', dataType: 'I16', access: 'RW' },
  
  // 回原点
  { index: 0x6098, name: 'Homing Method', dataType: 'I8', access: 'RW', description: '回原点方法' },
  { index: 0x6099, name: 'Homing Speeds', dataType: 'U32[]', access: 'RW' },
  { index: 0x609A, name: 'Homing Acceleration', dataType: 'U32', access: 'RW' },
  
  // 编码器
  { index: 0x608F, name: 'Position Encoder Resolution', dataType: 'U32[]', access: 'RW' },
  { index: 0x6090, name: 'Velocity Encoder Resolution', dataType: 'U32[]', access: 'RW' },
  { index: 0x6091, name: 'Gear Ratio', dataType: 'U32[]', access: 'RW' },
  { index: 0x6092, name: 'Feed Constant', dataType: 'U32[]', access: 'RW' },
  
  // 目标速度（速度模式）
  { index: 0x60FF, name: 'Target Velocity', dataType: 'I32', access: 'RW', description: '目标速度' },
  
  // 支持的驱动模式
  { index: 0x6502, name: 'Supported Drive Modes', dataType: 'U32', access: 'RO' },
  
  // 数字输入输出
  { index: 0x60FD, name: 'Digital Inputs', dataType: 'U32', access: 'RO', description: '数字输入' },
  { index: 0x60FE, name: 'Digital Outputs', dataType: 'U32[]', access: 'RW', description: '数字输出' },
]

/** 运行模式定义 */
export const OperationModes: Record<number, string> = {
  0: 'No mode',
  1: 'Profile Position (PP)',
  2: 'Velocity',
  3: 'Profile Velocity (PV)',
  4: 'Torque Profile (TQ)',
  5: 'Reserved',
  6: 'Homing (HM)',
  7: 'Interpolated Position (IP)',
  8: 'Cyclic Synchronous Position (CSP)',
  9: 'Cyclic Synchronous Velocity (CSV)',
  10: 'Cyclic Synchronous Torque (CST)',
  [-1]: 'Reserved',
  [-2]: 'Reserved',
  [-3]: 'Reserved',
  [-4]: 'Reserved',
  [-5]: 'Reserved',
}

/** Controlword 位定义 (0x6040) */
export const ControlwordBits: Record<number, string> = {
  0: 'Switch On',
  1: 'Enable Voltage',
  2: 'Quick Stop',
  3: 'Enable Operation',
  4: 'Op Mode Specific (4)',
  5: 'Op Mode Specific (5)',
  6: 'Op Mode Specific (6)',
  7: 'Fault Reset',
  8: 'Halt',
  9: 'Op Mode Specific (9)',
  10: 'Reserved',
  11: 'Manufacturer Specific (11)',
  12: 'Manufacturer Specific (12)',
  13: 'Manufacturer Specific (13)',
  14: 'Manufacturer Specific (14)',
  15: 'Manufacturer Specific (15)',
}

/** Statusword 位定义 (0x6041) */
export const StatuswordBits: Record<number, string> = {
  0: 'Ready to Switch On',
  1: 'Switched On',
  2: 'Operation Enabled',
  3: 'Fault',
  4: 'Voltage Enabled',
  5: 'Quick Stop',
  6: 'Switch On Disabled',
  7: 'Warning',
  8: 'Manufacturer Specific (8)',
  9: 'Remote',
  10: 'Target Reached',
  11: 'Internal Limit Active',
  12: 'Op Mode Specific (12) / Set-Point Ack',
  13: 'Op Mode Specific (13) / Following Error',
  14: 'Manufacturer Specific (14)',
  15: 'Manufacturer Specific (15)',
}

/** 状态机状态 */
export const DriveState = {
  NotReadyToSwitchOn: 'Not Ready to Switch On',
  SwitchOnDisabled: 'Switch On Disabled',
  ReadyToSwitchOn: 'Ready to Switch On',
  SwitchedOn: 'Switched On',
  OperationEnabled: 'Operation Enabled',
  QuickStopActive: 'Quick Stop Active',
  FaultReactionActive: 'Fault Reaction Active',
  Fault: 'Fault',
  Unknown: 'Unknown'
} as const

export type DriveState = typeof DriveState[keyof typeof DriveState]

/**
 * 从 Statusword 解析驱动状态
 */
export function parseStatusword(statusword: number): {
  state: DriveState
  bits: string[]
  warnings: string[]
} {
  const bits: string[] = []
  const warnings: string[] = []
  
  // 解析各个位
  for (let i = 0; i < 16; i++) {
    if (statusword & (1 << i)) {
      bits.push(StatuswordBits[i] || `Bit ${i}`)
    }
  }
  
  // 解析状态机状态 (bits 0-3, 5, 6)
  const rtso = (statusword >> 0) & 1  // Ready to Switch On
  const so = (statusword >> 1) & 1    // Switched On
  const oe = (statusword >> 2) & 1    // Operation Enabled
  const fault = (statusword >> 3) & 1 // Fault
  const qs = (statusword >> 5) & 1    // Quick Stop
  const sod = (statusword >> 6) & 1   // Switch On Disabled
  
  let state: DriveState
  
  if (fault) {
    state = DriveState.Fault
  } else if (!rtso && !so && !oe && sod) {
    state = DriveState.SwitchOnDisabled
  } else if (rtso && !so && !oe && !qs) {
    state = DriveState.QuickStopActive
  } else if (rtso && !so && !oe && qs) {
    state = DriveState.ReadyToSwitchOn
  } else if (rtso && so && !oe && qs) {
    state = DriveState.SwitchedOn
  } else if (rtso && so && oe && qs) {
    state = DriveState.OperationEnabled
  } else if (!rtso && !so && !oe && !sod) {
    state = DriveState.NotReadyToSwitchOn
  } else {
    state = DriveState.Unknown
  }
  
  // 检查警告
  if (statusword & (1 << 7)) warnings.push('Warning active')
  if (statusword & (1 << 11)) warnings.push('Internal limit active')
  if (statusword & (1 << 13)) warnings.push('Following error')
  
  return { state, bits, warnings }
}

/**
 * 解析 Controlword
 */
export function parseControlword(controlword: number): {
  command: string
  bits: string[]
} {
  const bits: string[] = []
  
  for (let i = 0; i < 16; i++) {
    if (controlword & (1 << i)) {
      bits.push(ControlwordBits[i] || `Bit ${i}`)
    }
  }
  
  // 解析命令
  const so = (controlword >> 0) & 1   // Switch On
  const ev = (controlword >> 1) & 1   // Enable Voltage
  const qs = (controlword >> 2) & 1   // Quick Stop
  const eo = (controlword >> 3) & 1   // Enable Operation
  const fr = (controlword >> 7) & 1   // Fault Reset
  
  let command: string
  
  if (fr) {
    command = 'Fault Reset'
  } else if (!qs) {
    command = 'Quick Stop'
  } else if (!ev) {
    command = 'Disable Voltage'
  } else if (!so) {
    command = 'Shutdown'
  } else if (!eo) {
    command = 'Switch On'
  } else {
    command = 'Enable Operation'
  }
  
  return { command, bits }
}

/**
 * 查找 CiA 402 对象
 */
export function lookupCiA402Object(index: number, subIndex?: number): ObjectDictionaryEntry | undefined {
  // 如果对象定义中没有指定 subIndex，则匹配任何 subIndex
  return CiA402Objects.find(e => 
    e.index === index && 
    (e.subIndex === undefined || subIndex === undefined || e.subIndex === subIndex)
  )
}
