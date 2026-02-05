/**
 * MODBUS CRC16 计算
 * 使用多项式 0x8005 (反转为 0xA001)
 */

// 预计算的 CRC 查找表
const CRC_TABLE = new Uint16Array(256)

// 初始化查找表
;(function initCRCTable() {
  const polynomial = 0xA001
  for (let i = 0; i < 256; i++) {
    let crc = i
    for (let j = 0; j < 8; j++) {
      if (crc & 1) {
        crc = (crc >> 1) ^ polynomial
      } else {
        crc = crc >> 1
      }
    }
    CRC_TABLE[i] = crc
  }
})()

/**
 * 计算 MODBUS CRC16
 * @param data 数据字节数组
 * @returns CRC16 值 (Little Endian: 低字节在前)
 */
export function calculateCRC16(data: number[]): number {
  let crc = 0xFFFF
  for (const byte of data) {
    crc = (crc >> 8) ^ CRC_TABLE[(crc ^ byte) & 0xFF]
  }
  return crc
}

/**
 * 验证 MODBUS CRC16
 * @param data 包含 CRC 的完整数据（CRC 在末尾两字节，Little Endian）
 * @returns 是否校验通过
 */
export function verifyCRC16(data: number[]): boolean {
  if (data.length < 3) return false
  
  const payload = data.slice(0, -2)
  const receivedCRC = data[data.length - 2] | (data[data.length - 1] << 8)
  const calculatedCRC = calculateCRC16(payload)
  
  return receivedCRC === calculatedCRC
}

/**
 * 获取 CRC 字节（Little Endian）
 */
export function getCRCBytes(crc: number): [number, number] {
  return [crc & 0xFF, (crc >> 8) & 0xFF]
}
