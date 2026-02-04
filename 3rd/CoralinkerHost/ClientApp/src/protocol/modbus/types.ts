/**
 * MODBUS RTU 类型定义
 */

/** MODBUS 功能码 */
export enum ModbusFunctionCode {
  ReadCoils = 0x01,
  ReadDiscreteInputs = 0x02,
  ReadHoldingRegisters = 0x03,
  ReadInputRegisters = 0x04,
  WriteSingleCoil = 0x05,
  WriteSingleRegister = 0x06,
  WriteMultipleCoils = 0x0F,
  WriteMultipleRegisters = 0x10,
  // 诊断
  ReadExceptionStatus = 0x07,
  Diagnostics = 0x08,
  GetCommEventCounter = 0x0B,
  GetCommEventLog = 0x0C,
  ReportSlaveID = 0x11,
  // 文件操作
  ReadFileRecord = 0x14,
  WriteFileRecord = 0x15,
  // 其他
  MaskWriteRegister = 0x16,
  ReadWriteMultipleRegisters = 0x17,
  ReadFIFOQueue = 0x18
}

/** 功能码名称映射 */
export const FunctionCodeNames: Record<number, string> = {
  [ModbusFunctionCode.ReadCoils]: 'Read Coils',
  [ModbusFunctionCode.ReadDiscreteInputs]: 'Read Discrete Inputs',
  [ModbusFunctionCode.ReadHoldingRegisters]: 'Read Holding Registers',
  [ModbusFunctionCode.ReadInputRegisters]: 'Read Input Registers',
  [ModbusFunctionCode.WriteSingleCoil]: 'Write Single Coil',
  [ModbusFunctionCode.WriteSingleRegister]: 'Write Single Register',
  [ModbusFunctionCode.WriteMultipleCoils]: 'Write Multiple Coils',
  [ModbusFunctionCode.WriteMultipleRegisters]: 'Write Multiple Registers',
  [ModbusFunctionCode.ReadExceptionStatus]: 'Read Exception Status',
  [ModbusFunctionCode.Diagnostics]: 'Diagnostics',
  [ModbusFunctionCode.GetCommEventCounter]: 'Get Comm Event Counter',
  [ModbusFunctionCode.GetCommEventLog]: 'Get Comm Event Log',
  [ModbusFunctionCode.ReportSlaveID]: 'Report Slave ID',
  [ModbusFunctionCode.ReadFileRecord]: 'Read File Record',
  [ModbusFunctionCode.WriteFileRecord]: 'Write File Record',
  [ModbusFunctionCode.MaskWriteRegister]: 'Mask Write Register',
  [ModbusFunctionCode.ReadWriteMultipleRegisters]: 'Read/Write Multiple Registers',
  [ModbusFunctionCode.ReadFIFOQueue]: 'Read FIFO Queue'
}

/** MODBUS 异常码 */
export enum ModbusExceptionCode {
  IllegalFunction = 0x01,
  IllegalDataAddress = 0x02,
  IllegalDataValue = 0x03,
  SlaveDeviceFailure = 0x04,
  Acknowledge = 0x05,
  SlaveDeviceBusy = 0x06,
  NegativeAcknowledge = 0x07,
  MemoryParityError = 0x08,
  GatewayPathUnavailable = 0x0A,
  GatewayTargetDeviceFailedToRespond = 0x0B
}

/** 异常码名称映射 */
export const ExceptionCodeNames: Record<number, string> = {
  [ModbusExceptionCode.IllegalFunction]: 'Illegal Function',
  [ModbusExceptionCode.IllegalDataAddress]: 'Illegal Data Address',
  [ModbusExceptionCode.IllegalDataValue]: 'Illegal Data Value',
  [ModbusExceptionCode.SlaveDeviceFailure]: 'Slave Device Failure',
  [ModbusExceptionCode.Acknowledge]: 'Acknowledge',
  [ModbusExceptionCode.SlaveDeviceBusy]: 'Slave Device Busy',
  [ModbusExceptionCode.NegativeAcknowledge]: 'Negative Acknowledge',
  [ModbusExceptionCode.MemoryParityError]: 'Memory Parity Error',
  [ModbusExceptionCode.GatewayPathUnavailable]: 'Gateway Path Unavailable',
  [ModbusExceptionCode.GatewayTargetDeviceFailedToRespond]: 'Gateway Target Device Failed to Respond'
}
