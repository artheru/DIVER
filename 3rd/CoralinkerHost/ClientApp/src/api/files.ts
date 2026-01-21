/**
 * @file api/files.ts
 * @description 文件操作 API
 * 
 * 处理文件树获取、文件读写、资源上传等操作
 */

import { get, post, del, upload } from './index'
import type { FileNode, FileReadResponse, FileWriteRequest } from '@/types'

/**
 * 后端返回的原始文件节点结构
 */
interface RawFileNode {
  name: string
  path: string
  isDir: boolean
  sizeBytes: number | null
  children: RawFileNode[] | null
}

/**
 * 将后端返回的文件节点转换为前端 FileNode 格式
 * - isDir -> kind
 * - 递归转换 children
 */
function convertFileNode(raw: RawFileNode): FileNode {
  return {
    name: raw.name,
    path: raw.path,
    kind: raw.isDir ? 'folder' : 'file',
    children: raw.children?.map(convertFileNode)
  }
}

/**
 * 获取资源文件树
 * 返回 assets 目录下的文件结构
 * 
 * 注意：后端直接返回根节点对象 { name: "assets", isDir: true, children: [...] }
 * 我们需要提取其 children 并转换为前端格式（isDir -> kind）
 */
export async function getFileTree(): Promise<FileNode[]> {
  const response = await get<RawFileNode>('/api/files/tree')
  
  if (!response) {
    return []
  }
  
  // 兼容两种格式：直接返回根节点 或 { tree: rootNode }
  const rootNode = (response as any).tree || response
  
  // 提取并转换 children
  if (rootNode?.children && rootNode.children.length > 0) {
    return rootNode.children.map(convertFileNode)
  }
  
  return []
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
