/**
 * @file api/upgrade.ts
 * @description 固件升级相关 API
 */

import http from './index'

/**
 * 固件元数据（MCU 和 UPG 通用）
 */
export interface FirmwareMetadata {
  productName: string
  tag: string
  commit: string
  buildTime: string
  appLength: number
  appCRC32: number
  isValid: boolean
}

export interface UpgParseResult {
  ok: boolean
  error?: string
  metadata?: FirmwareMetadata
}

export interface UpgradeResult {
  ok: boolean
  error?: string
  mcuInfo?: FirmwareMetadata
  upgInfo?: FirmwareMetadata
}

/**
 * 解析 UPG 文件
 */
export async function parseUpgFile(file: File): Promise<UpgParseResult> {
  const formData = new FormData()
  formData.append('file', file)

  const response = await http.post<UpgParseResult>('/api/upgrade/parse', formData)
  return response.data
}

/**
 * 开始固件升级（烧录耗时较长，超时设为 5 分钟）
 */
export async function startUpgrade(
  mcuUri: string,
  file: File,
  nodeId?: string
): Promise<UpgradeResult> {
  const formData = new FormData()
  formData.append('file', file)
  formData.append('mcuUri', mcuUri)
  if (nodeId) {
    formData.append('nodeId', nodeId)
  }

  const response = await http.post<UpgradeResult>('/api/upgrade/start', formData, {
    timeout: 300_000
  })
  return response.data
}
