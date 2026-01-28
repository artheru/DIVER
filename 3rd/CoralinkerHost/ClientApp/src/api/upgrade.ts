/**
 * @file api/upgrade.ts
 * @description 固件升级相关 API
 */

const API_BASE = ''

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

  const response = await fetch(`${API_BASE}/api/upgrade/parse`, {
    method: 'POST',
    body: formData
  })

  if (!response.ok) {
    throw new Error(`HTTP error: ${response.status}`)
  }

  return await response.json()
}

/**
 * 开始固件升级
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

  const response = await fetch(`${API_BASE}/api/upgrade/start`, {
    method: 'POST',
    body: formData
  })

  if (!response.ok) {
    throw new Error(`HTTP error: ${response.status}`)
  }

  return await response.json()
}
