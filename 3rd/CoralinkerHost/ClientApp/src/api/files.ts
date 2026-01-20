/**
 * @file api/files.ts
 * @description 文件操作 API
 * 
 * 处理文件树获取、文件读写、资源上传等操作
 */

import { get, post, del, upload } from './index'
import type { FileNode, FileReadResponse, FileWriteRequest } from '@/types'

/**
 * 获取资源文件树
 * 返回 assets 目录下的文件结构
 */
export async function getFileTree(): Promise<FileNode[]> {
  const response = await get<{ tree: FileNode[] }>('/api/files/tree')
  return response.tree || []
}

/**
 * 读取文件内容
 * @param path 文件相对路径 (相对于 data 目录)
 * @returns 文件内容，文本文件返回 text，二进制文件返回 base64
 */
export async function readFile(path: string): Promise<FileReadResponse> {
  return get<FileReadResponse>('/api/files/read', {
    params: { path }
  })
}

/**
 * 写入文件内容
 * @param request 写入请求，包含路径、类型和内容
 */
export async function writeFile(request: FileWriteRequest): Promise<void> {
  await post('/api/files/write', request)
}

/**
 * 删除文件
 * @param path 文件相对路径
 */
export async function deleteFile(path: string): Promise<void> {
  await post('/api/files/delete', { path })
}

/**
 * 创建新的输入文件 (.cs)
 * @param name 文件名 (不含扩展名)
 * @param template 可选的初始模板内容
 */
export async function createInputFile(
  name: string, 
  template?: string
): Promise<{ ok: boolean; path: string }> {
  return post('/api/files/newInput', { name, template })
}

/**
 * 上传资源文件
 * @param file 要上传的文件
 */
export async function uploadAsset(file: File): Promise<{ name: string; path: string }> {
  return upload('/api/assets/upload', file)
}

/**
 * 获取资源列表
 */
export async function getAssets(): Promise<Array<{ name: string; size: number }>> {
  return get('/api/assets')
}

/**
 * 删除资源
 * @param name 资源名称
 */
export async function deleteAsset(name: string): Promise<void> {
  await del(`/api/assets/${encodeURIComponent(name)}`)
}
