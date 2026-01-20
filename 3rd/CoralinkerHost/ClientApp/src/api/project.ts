/**
 * @file api/project.ts
 * @description 项目管理 API
 * 
 * 处理项目的创建、保存、导入、导出等操作
 */

import { get, post, upload } from './index'
import type { ProjectState, BuildResult } from '@/types'

/**
 * 获取当前项目状态
 * 包含节点图数据和选中的资源信息
 */
export async function getProject(): Promise<ProjectState> {
  return get<ProjectState>('/api/project')
}

/**
 * 更新项目状态
 * @param state 要保存的项目状态
 */
export async function updateProject(state: ProjectState): Promise<void> {
  await post('/api/project', state)
}

/**
 * 创建新项目
 * 会清空所有资源文件和节点图
 */
export async function createNewProject(): Promise<void> {
  await post('/api/project/new')
}

/**
 * 保存项目到磁盘
 * 将当前项目状态持久化到 data/project.json
 */
export async function saveProject(): Promise<void> {
  await post('/api/project/save')
}

/**
 * 导出项目为 ZIP 文件
 * @returns Blob 数据，可用于下载
 */
export async function exportProject(): Promise<Blob> {
  const response = await fetch('/api/project/export')
  if (!response.ok) {
    throw new Error('Export failed')
  }
  return response.blob()
}

/**
 * 导入项目 ZIP 文件
 * @param file 要导入的 ZIP 文件
 */
export async function importProject(file: File): Promise<void> {
  await upload('/api/project/import', file)
}

/**
 * 执行构建
 * 编译当前选中的 .cs 文件
 */
export async function build(): Promise<BuildResult> {
  return post<BuildResult>('/api/build')
}
