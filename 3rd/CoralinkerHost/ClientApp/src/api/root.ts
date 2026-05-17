import { get, post } from './index'
import type { RootLogicMetadata, RootRuntimeState } from '@/types'

export async function getRootLogics(): Promise<{ ok: boolean; logics: RootLogicMetadata[] }> {
  return get('/api/root/logics')
}

export async function configureRoot(logicName: string | null): Promise<{ ok: boolean }> {
  return post('/api/root/configure', { logicName })
}

export async function getRootState(): Promise<{ ok: boolean; state: RootRuntimeState }> {
  return get('/api/root/state')
}

export async function getRootControlMeta(): Promise<{ ok: boolean; fields: RootFieldMetadata[] }> {
  return get('/api/root/control/meta')
}

export async function setRootControl(name: string, value: unknown): Promise<{ ok: boolean }> {
  return post('/api/root/control/set', { name, value })
}

export interface RootFieldMetadata {
  name: string
  type: string
  typeId: number
  direction: string
}
