<template>
  <div v-if="show" class="history-overlay" @click.self="emit('close')">
    <div class="history-panel">
      <div class="history-header">
        <div>
          <h3>Input History</h3>
          <div class="history-subtitle">{{ currentPath || 'All input files' }}</div>
        </div>
        <button class="close-btn" @click="emit('close')">×</button>
      </div>

      <div class="history-tabs">
        <span class="scope-label">Scope</span>
        <button class="scope-btn" :class="{ active: scope === 'all' }" @click="setScope('all')">
          All Changes
        </button>
        <button class="scope-btn" :class="{ active: scope === 'file' }" :disabled="!currentPath" @click="setScope('file')">
          Current File
        </button>
        <span class="scope-hint">{{ scopeHint }}</span>
      </div>

      <div class="history-body">
        <aside class="commit-list">
          <div
            v-for="commit in commits"
            :key="commit.hash"
            class="commit-item"
            :class="{ from: fromCommit?.hash === commit.hash, to: toCommit?.hash === commit.hash }"
          >
            <div class="commit-row">
              <span class="hash">{{ commit.shortHash }}</span>
              <span class="time">{{ formatTime(commit.commitTime) }}</span>
            </div>
            <div class="subject">{{ commit.subject }}</div>
            <div class="files">{{ commit.files.join(', ') }}</div>
            <div class="commit-actions">
              <button :class="{ active: fromCommit?.hash === commit.hash }" @click="setFrom(commit)">From</button>
              <button :class="{ active: toCommit?.hash === commit.hash }" @click="setTo(commit)">To</button>
            </div>
          </div>
          <div v-if="commits.length === 0" class="empty">No commits yet</div>
        </aside>

        <section class="diff-area">
          <div v-if="toCommit" class="diff-toolbar">
            <span class="range-label">From {{ fromCommit?.shortHash || 'previous' }} → To {{ toCommit.shortHash }}</span>
            <select v-if="changedFiles.length > 0" v-model="selectedPath" @change="reloadSelectedDiff">
              <option v-for="file in changedFiles" :key="file" :value="file">{{ file }}</option>
            </select>
            <div class="toolbar-spacer"></div>
            <button :disabled="!canCompareUnsaved" @click="compareWithUnsaved">Compare With Unsaved</button>
            <button @click="compareWithHead">Compare With HEAD</button>
            <button @click="checkoutSelected">Checkout Temporarily</button>
            <button class="danger" @click="revertSelected">Revert As Current</button>
          </div>
          <div v-if="diff" class="diff-wrapper">
            <div ref="diffRef" class="monaco-diff"></div>
          </div>
          <div v-if="!toCommit" class="empty">Select a To commit from the left list</div>
        </section>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { monaco } from '@/lib/monaco'
import { useHistoryStore } from '@/stores'
import type { GitCommitInfo, GitDiffResult } from '@/types'

const props = defineProps<{
  show: boolean
  currentPath?: string | null
  currentContent?: string | null
  currentDirty?: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'refresh'): void
}>()

const historyStore = useHistoryStore()
const { commits } = storeToRefs(historyStore)

const scope = ref<'all' | 'file'>('file')
const fromCommit = ref<GitCommitInfo | null>(null)
const toCommit = ref<GitCommitInfo | null>(null)
const diff = ref<GitDiffResult | null>(null)
const selectedPath = ref<string | null>(null)
const diffRef = ref<HTMLDivElement | null>(null)
let diffEditor: monaco.editor.IStandaloneDiffEditor | null = null

const changedFiles = computed(() => {
  const files = new Set<string>()
  fromCommit.value?.files.forEach(file => files.add(file))
  toCommit.value?.files.forEach(file => files.add(file))
  return Array.from(files)
})

const scopeHint = computed(() => {
  if (scope.value === 'file') {
    return props.currentPath ? `showing ${props.currentPath}` : 'no current file'
  }
  return 'showing all input source commits'
})

const canCompareUnsaved = computed(() => {
  return !!props.currentDirty && !!props.currentPath && !!props.currentContent && selectedPath.value === props.currentPath
})

watch(() => props.show, async visible => {
  if (visible) {
    scope.value = props.currentPath ? 'file' : 'all'
    await loadCommits()
  }
})

watch(() => props.currentPath, async () => {
  if (props.show) {
    scope.value = props.currentPath ? scope.value : 'all'
    await loadCommits()
  }
})

async function setScope(next: 'all' | 'file') {
  if (next === 'file' && !props.currentPath) return
  scope.value = next
  selectedPath.value = null
  await loadCommits()
}

async function loadCommits() {
  fromCommit.value = null
  toCommit.value = null
  diff.value = null
  await historyStore.loadLog(scope.value === 'file' ? props.currentPath || undefined : undefined)
  const latest = commits.value[0]
  if (latest) {
    await setTo(latest)
  }
}

async function setTo(commit: GitCommitInfo) {
  toCommit.value = commit
  if (!fromCommit.value) {
    const idx = commits.value.findIndex(c => c.hash === commit.hash)
    fromCommit.value = commits.value[idx + 1] || null
  }
  selectedPath.value = scope.value === 'file'
    ? props.currentPath || null
    : changedFiles.value[0] || commit.files[0] || null
  await reloadSelectedDiff()
}

async function setFrom(commit: GitCommitInfo) {
  fromCommit.value = commit
  selectedPath.value ||= changedFiles.value[0] || null
  await reloadSelectedDiff()
}

async function compareWithHead() {
  if (!toCommit.value) return
  diff.value = await historyStore.loadDiff(toCommit.value.hash, 'HEAD', selectedPath.value || undefined)
  await renderDiff()
}

async function compareWithUnsaved() {
  if (!toCommit.value || !canCompareUnsaved.value) return
  const base = await historyStore.loadDiff(`${toCommit.value.hash}~1`, toCommit.value.hash, selectedPath.value || undefined)
  diff.value = {
    from: toCommit.value.hash,
    to: 'UNSAVED',
    path: selectedPath.value,
    oldText: base.oldText ?? '',
    newText: props.currentContent ?? '',
    unifiedDiff: ''
  }
  await renderDiff()
}

async function reloadSelectedDiff() {
  if (!toCommit.value) return
  const from = fromCommit.value?.hash || `${toCommit.value.hash}~1`
  diff.value = await historyStore.loadDiff(from, toCommit.value.hash, selectedPath.value || undefined)
  await renderDiff()
}

async function checkoutSelected() {
  if (!toCommit.value) return
  await historyStore.checkout(toCommit.value.hash, selectedPath.value || undefined)
  emit('refresh')
}

async function revertSelected() {
  if (!toCommit.value) return
  if (!confirm('Revert selected version as current and create a new save commit?')) return
  await historyStore.revert(toCommit.value.hash, selectedPath.value || undefined)
  await loadCommits()
  emit('refresh')
}

function formatTime(value?: string | null) {
  if (!value) return ''
  return new Date(value).toLocaleString()
}

async function renderDiff() {
  await nextTick()
  if (!diffRef.value || !diff.value) return

  if (!diffEditor) {
    diffEditor = monaco.editor.createDiffEditor(diffRef.value, {
      theme: 'vs-dark',
      readOnly: true,
      automaticLayout: true,
      renderSideBySide: true,
      originalEditable: false,
      enableSplitViewResizing: true,
      renderOverviewRuler: true,
      scrollBeyondLastLine: false,
      minimap: { enabled: false },
      lineNumbers: 'on',
      glyphMargin: true,
      folding: false,
      wordWrap: 'off',
      fontFamily: "'JetBrains Mono', 'Fira Code', Consolas, monospace",
      fontSize: 13,
      lineHeight: 20
    })
  }

  const oldText = diff.value.oldText ?? ''
  const newText = diff.value.newText ?? ''
  const oldModel = monaco.editor.createModel(oldText, languageFor(selectedPath.value))
  const newModel = monaco.editor.createModel(newText, languageFor(selectedPath.value))
  const previous = diffEditor.getModel()
  diffEditor.setModel({ original: oldModel, modified: newModel })
  previous?.original.dispose()
  previous?.modified.dispose()
  diffEditor.layout()
}

function languageFor(path?: string | null) {
  if (path?.endsWith('.cs')) return 'csharp'
  if (path?.endsWith('.json')) return 'json'
  return 'plaintext'
}

onBeforeUnmount(() => {
  const model = diffEditor?.getModel()
  model?.original.dispose()
  model?.modified.dispose()
  diffEditor?.dispose()
})
</script>

<style scoped>
.history-overlay {
  position: fixed;
  inset: 0;
  z-index: 1000;
  background: rgba(0, 0, 0, 0.35);
  display: flex;
  justify-content: flex-end;
}
.history-panel {
  width: min(1100px, 86vw);
  height: 100%;
  background: #0d1117;
  color: #e6edf3;
  border-left: 1px solid #30363d;
  display: flex;
  flex-direction: column;
  font-family: Inter, "Segoe UI", Arial, sans-serif;
}
.history-header,
.history-tabs,
.diff-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-bottom: 1px solid #30363d;
}
.history-header h3 {
  margin: 0;
}
.history-subtitle,
.files,
.time {
  color: #8b949e;
  font-size: 12px;
}
.close-btn {
  margin-left: auto;
}
.scope-label {
  color: #8b949e;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.scope-btn {
  border: 1px solid #30363d;
  border-radius: 999px;
  padding: 5px 12px;
  color: #c9d1d9;
  background: #161b22;
}
.scope-btn.active {
  color: #ffffff;
  background: rgba(56, 139, 253, 0.28);
  border-color: #388bfd;
}
.scope-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
.scope-hint {
  color: #8b949e;
  font-size: 12px;
}
.history-body {
  flex: 1;
  display: grid;
  grid-template-columns: 320px 1fr;
  min-height: 0;
}
.commit-list {
  border-right: 1px solid #30363d;
  overflow: auto;
}
.commit-item {
  width: 100%;
  text-align: left;
  padding: 10px;
  background: transparent;
  color: inherit;
  border-bottom: 1px solid #21262d;
}
.commit-item.from {
  box-shadow: inset 3px 0 0 #f2cc60;
}
.commit-item.to {
  box-shadow: inset 3px 0 0 #58a6ff;
}
.commit-item.from.to {
  box-shadow: inset 3px 0 0 #a371f7;
}
.commit-item:hover {
  background: #161b22;
}
.commit-row {
  display: flex;
  justify-content: space-between;
}
.hash {
  font-family: monospace;
  color: #79c0ff;
}
.subject {
  margin-top: 4px;
}
.diff-area {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.commit-actions {
  display: flex;
  gap: 6px;
  margin-top: 8px;
}
.commit-actions button {
  border: 1px solid #30363d;
  border-radius: 999px;
  padding: 2px 8px;
  background: #0d1117;
  color: #8b949e;
  font-size: 12px;
  cursor: pointer;
}
.commit-actions button.active {
  border-color: #58a6ff;
  color: #ffffff;
  background: rgba(56, 139, 253, 0.22);
}
.range-label {
  font-family: 'JetBrains Mono', Consolas, monospace;
  color: #c9d1d9;
}
.toolbar-spacer {
  flex: 1;
}
.danger {
  color: #ff7b72;
}
.diff-wrapper {
  flex: 1;
  min-height: 0;
}
.monaco-diff {
  width: 100%;
  height: 100%;
  min-height: 0;
}
.diff-toolbar select {
  max-width: 360px;
  background: #0d1117;
  color: #e6edf3;
  border: 1px solid #30363d;
  border-radius: 4px;
  padding: 4px 6px;
}
.empty {
  padding: 16px;
  color: #8b949e;
}
</style>
