import { get } from './index'
import type { HostAboutSnapshot } from '@/types'

export async function getAbout(): Promise<{ ok: boolean; about: HostAboutSnapshot }> {
  return get('/api/about')
}
