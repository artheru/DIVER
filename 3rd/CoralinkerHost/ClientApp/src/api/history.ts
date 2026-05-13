import { get, post } from './index'
import type {
  GitCommitInfo,
  GitDiffResult,
  GitFileAtCommitResult,
  GitStatusSnapshot
} from '@/types'

export async function getHistoryStatus(): Promise<{ ok: boolean; status: GitStatusSnapshot }> {
  return get('/api/history/status')
}

export async function getHistoryLog(path?: string, maxCount = 100): Promise<{ ok: boolean; commits: GitCommitInfo[] }> {
  return get('/api/history/log', { params: { path, maxCount } })
}

export async function getHistoryDiff(params: {
  from?: string
  to?: string
  path?: string
}): Promise<{ ok: boolean; diff: GitDiffResult }> {
  return get('/api/history/diff', { params })
}

export async function getHistoryFile(commit: string, path: string): Promise<{ ok: boolean; file: GitFileAtCommitResult }> {
  return get('/api/history/file', { params: { commit, path } })
}

export async function checkoutHistory(commit: string, path?: string): Promise<{ ok: boolean }> {
  return post('/api/history/checkout', { commit, path })
}

export async function revertHistory(commit: string, path?: string): Promise<{ ok: boolean; result: { headAfter?: string; committed: boolean } }> {
  return post('/api/history/revert', { commit, path })
}
