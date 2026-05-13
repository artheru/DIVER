import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import * as historyApi from '@/api/history'
import type { GitCommitInfo, GitDiffResult, GitStatusSnapshot } from '@/types'

export const useHistoryStore = defineStore('history', () => {
  const status = ref<GitStatusSnapshot | null>(null)
  const commits = ref<GitCommitInfo[]>([])
  const currentDiff = ref<GitDiffResult | null>(null)
  const knownHead = ref<string | null>(null)
  const remoteChanged = ref(false)
  const polling = ref(false)
  let pollTimer: ReturnType<typeof setInterval> | null = null

  const head = computed(() => status.value?.head ?? null)
  const shortHead = computed(() => status.value?.shortHead ?? null)

  async function refreshStatus(markKnown = false) {
    const result = await historyApi.getHistoryStatus()
    const next = result.status
    const oldKnown = knownHead.value
    status.value = next

    if (markKnown || !oldKnown) {
      knownHead.value = next.head ?? null
      remoteChanged.value = false
    } else if ((next.head ?? null) !== oldKnown) {
      remoteChanged.value = true
    }

    return next
  }

  function markCurrentHeadKnown() {
    knownHead.value = status.value?.head ?? null
    remoteChanged.value = false
  }

  function markHeadKnown(head?: string | null) {
    knownHead.value = head ?? null
    if (status.value) {
      status.value = {
        ...status.value,
        head: head ?? null
      }
    }
    remoteChanged.value = false
  }

  async function loadLog(path?: string) {
    const result = await historyApi.getHistoryLog(path)
    commits.value = result.commits
    return result.commits
  }

  async function loadDiff(from?: string, to?: string, path?: string) {
    const result = await historyApi.getHistoryDiff({ from, to, path })
    currentDiff.value = result.diff
    return result.diff
  }

  async function checkout(commit: string, path?: string) {
    await historyApi.checkoutHistory(commit, path)
    await refreshStatus(true)
  }

  async function revert(commit: string, path?: string) {
    await historyApi.revertHistory(commit, path)
    await refreshStatus(true)
  }

  function startPolling() {
    if (pollTimer) return
    polling.value = true
    pollTimer = setInterval(() => {
      refreshStatus(false).catch(err => console.warn('[History] Poll failed:', err))
    }, 10000)
  }

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    polling.value = false
  }

  return {
    status,
    commits,
    currentDiff,
    knownHead,
    remoteChanged,
    polling,
    head,
    shortHead,
    refreshStatus,
    markCurrentHeadKnown,
    markHeadKnown,
    loadLog,
    loadDiff,
    checkout,
    revert,
    startPolling,
    stopPolling
  }
})
