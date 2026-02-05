/**
 * 协议解析模块
 * 
 * 架构说明：
 * 
 * protocol/
 * ├── index.ts              # 主入口，注册所有解析器
 * ├── types.ts              # 基础类型定义
 * ├── registry.ts           # 解析器注册中心
 * ├── modbus/               # MODBUS 协议
 * │   ├── index.ts
 * │   ├── types.ts          # 功能码、异常码定义
 * │   ├── crc.ts            # CRC16 计算
 * │   └── parser.ts         # MODBUS RTU 解析器
 * └── canopen/              # CANOpen 协议
 *     ├── index.ts
 *     ├── types.ts          # COB-ID、NMT 等定义
 *     ├── dictionary.ts     # 对象字典 (CiA 301)
 *     ├── cia402.ts         # CiA 402 驱动器配置
 *     └── parser.ts         # CANOpen 解析器
 * 
 * 添加新协议步骤：
 * 1. 在 protocol/ 下创建新目录（如 protocol/j1939/）
 * 2. 实现 ProtocolParser 接口
 * 3. 在此文件中导入并注册
 * 
 * 使用示例：
 * ```typescript
 * import { protocolRegistry, ParseContext } from '@/protocol'
 * 
 * const context: ParseContext = {
 *   direction: 'rx',
 *   portType: 'serial',
 *   portIndex: 0
 * }
 * 
 * // 自动检测协议
 * const result = protocolRegistry.autoDetectAndParse(data, context)
 * 
 * // 指定协议解析
 * const result = protocolRegistry.parseWith('modbus-rtu', data, context)
 * ```
 */

// 导出类型
export * from './types'
export { protocolRegistry } from './registry'

// 导出子模块
export * as modbus from './modbus'
export * as canopen from './canopen'
export * as serial from './serial'

// 导入解析器
import { modbusRtuParser } from './modbus'
import { canopenParser } from './canopen'
import { rawSerialParser } from './serial'
import { protocolRegistry } from './registry'

// 注册所有内置解析器
function registerBuiltinParsers() {
  // 特定协议解析器（高优先级）
  protocolRegistry.register(modbusRtuParser)
  protocolRegistry.register(canopenParser)
  // 通用解析器（低优先级，作为后备）
  protocolRegistry.register(rawSerialParser)
  
  console.log('[Protocol] Registered built-in parsers:', 
    protocolRegistry.getAll().map(p => p.id).join(', ')
  )
}

// 自动注册
registerBuiltinParsers()
