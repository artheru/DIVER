<template>
  <div class="vm-load">
    <!-- CPU LOAD -->
    <div class="vm-load-row">
      <div class="vm-load-header">
        <span class="vm-load-title">CPU LOAD <span class="vm-load-win">{{ windowSec }}s</span></span>
        <span class="vm-load-metrics">
          <span class="num num-cyc">{{ latest ? formatCycles(latest.cycles) : '--' }}</span>
          <span class="num num-time">{{ latest ? formatTime(latest) : '--' }}</span>
          <span class="num num-pct" :class="cpuClass">{{ latest ? latest.loadPercent.toFixed(0) + '%' : '--' }}</span>
        </span>
      </div>
      <svg class="vm-load-svg" :viewBox="`0 0 ${width} ${height}`" preserveAspectRatio="none">
        <line :x1="0" :y1="height * 0.1" :x2="width" :y2="height * 0.1" class="vm-load-grid" />
        <line v-for="gx in timeGridX" :key="'c'+gx" :x1="gx" :y1="0" :x2="gx" :y2="height" class="vm-load-vgrid" />
        <polygon v-if="cpuArea" :points="cpuArea" class="vm-load-area cpu" />
        <polyline v-if="cpuPoints" :points="cpuPoints" class="vm-load-line cpu" />
      </svg>
    </div>

    <!-- MEMORY LOAD -->
    <div class="vm-load-row">
      <div class="vm-load-header">
        <span class="vm-load-title">MEM LOAD</span>
        <span class="vm-load-metrics">
          <span class="num num-mem">{{ latest ? formatBytes(latest.memPeakUsed) + '/' + formatBytes(latest.memCapacity) : '--' }}</span>
          <span class="num num-pct" :class="memClass">{{ latest ? latest.memLoadPercent.toFixed(0) + '%' : '--' }}</span>
        </span>
      </div>
      <svg class="vm-load-svg" :viewBox="`0 0 ${width} ${height}`" preserveAspectRatio="none">
        <line :x1="0" :y1="height * 0.1" :x2="width" :y2="height * 0.1" class="vm-load-grid" />
        <line v-for="gx in timeGridX" :key="'m'+gx" :x1="gx" :y1="0" :x2="gx" :y2="height" class="vm-load-vgrid" />
        <polygon v-if="memArea" :points="memArea" class="vm-load-area mem" />
        <polyline v-if="memPoints" :points="memPoints" class="vm-load-line mem" />
      </svg>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { getNodeVmStats, type VmStatsSample } from '@/api/runtime'

const props = defineProps<{
  uuid: string
  running: boolean
}>()

const width = 160
const height = 34
const windowMs = 15000 // fixed 15s time window
const windowSec = windowMs / 1000
const maxSamples = 4000 // hard safety cap (memory bound; 15s @ <4ms still fits)

const samples = ref<VmStatsSample[]>([])
const latest = ref<VmStatsSample | null>(null)
let lastSeq: number | undefined = undefined
let timer: ReturnType<typeof setInterval> | null = null

function tms(s: VmStatsSample): number {
  return Date.parse(s.timestamp)
}

// Window is anchored to the newest sample (right edge = now), avoiding any
// browser/server clock skew. [tMin .. tMax] spans exactly windowMs.
const tMax = computed(() => {
  const n = samples.value.length
  return n > 0 ? tms(samples.value[n - 1]!) : 0
})
const tMin = computed(() => tMax.value - windowMs)

// Vertical gridlines every 5s (mapped from the right edge).
const timeGridX = computed(() => {
  const xs: number[] = []
  for (let k = 5000; k < windowMs; k += 5000) {
    xs.push((1 - k / windowMs) * width)
  }
  return xs
})

function classFor(v: number): string {
  if (v >= 90) return 'load-high'
  if (v >= 60) return 'load-mid'
  return 'load-low'
}
const cpuClass = computed(() => classFor(latest.value?.loadPercent ?? 0))
const memClass = computed(() => classFor(latest.value?.memLoadPercent ?? 0))

// CPU auto-scales (>=100% floor); memory is always 0..100% of the buffer.
const cpuScaleMax = computed(() => {
  const peak = samples.value.reduce((m, s) => Math.max(m, s.loadPercent), 0)
  return Math.max(100, Math.ceil(peak / 25) * 25)
})

function line(getVal: (s: VmStatsSample) => number, scaleMax: number): string {
  const n = samples.value.length
  if (n === 0) return ''
  const top = height * 0.1
  const usable = height - top - 2
  const lo = tMin.value
  return samples.value
    .map((s) => {
      const x = Math.max(0, Math.min(width, ((tms(s) - lo) / windowMs) * width))
      const y = top + usable * (1 - Math.min(getVal(s), scaleMax) / scaleMax)
      return `${x},${y}`
    })
    .join(' ')
}

const cpuPoints = computed(() => line((s) => s.loadPercent, cpuScaleMax.value))
const memPoints = computed(() => line((s) => s.memLoadPercent, 100))
const cpuArea = computed(() => (cpuPoints.value ? `0,${height} ${cpuPoints.value} ${width},${height}` : ''))
const memArea = computed(() => (memPoints.value ? `0,${height} ${memPoints.value} ${width},${height}` : ''))

function formatCycles(c: number): string {
  if (c >= 1_000_000) return (c / 1_000_000).toFixed(2) + 'M cyc'
  if (c >= 1_000) return (c / 1_000).toFixed(1) + 'k cyc'
  return c + ' cyc'
}

// Effective duration: prefer DWT cycles (sub-us precise); fall back to the
// 1ms-quantized wall clock when cpuHz/cycles are unavailable.
function effUs(s: VmStatsSample): number {
  if (s.cpuHz > 0 && s.cycles > 0) return (s.cycles / s.cpuHz) * 1_000_000
  return s.micros
}
function formatTime(s: VmStatsSample): string {
  const us = effUs(s)
  if (us >= 1000) {
    const ms = us / 1000
    return (ms >= 100 ? ms.toFixed(0) : ms.toFixed(1)) + ' ms'
  }
  return Math.round(us) + ' us'
}

function formatBytes(b: number): string {
  if (b >= 1024) return (b / 1024).toFixed(1) + 'K'
  return b + 'B'
}

async function poll() {
  try {
    const res = await getNodeVmStats(props.uuid, lastSeq, maxSamples)
    if (!res.ok) return
    if (res.latest) latest.value = res.latest
    if (res.samples && res.samples.length > 0) {
      lastSeq = res.latestSeq
      const merged = [...samples.value, ...res.samples]
      // Keep only samples within the 15s window (anchored to the newest), with
      // a hard cap as a memory safety net.
      const newest = tms(merged[merged.length - 1]!)
      const cutoff = newest - windowMs
      let trimmed = merged.filter((s) => tms(s) >= cutoff)
      if (trimmed.length > maxSamples) trimmed = trimmed.slice(-maxSamples)
      samples.value = trimmed
    } else if (lastSeq === undefined) {
      lastSeq = res.latestSeq
    }
  } catch {
    // ignore transient polling errors
  }
}

function start() {
  if (timer) return
  poll()
  timer = setInterval(poll, 1000)
}

function stop() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

watch(
  () => props.running,
  (run) => {
    if (run) start()
    else stop()
  }
)

onMounted(() => {
  if (props.running) start()
})

onBeforeUnmount(stop)
</script>

<style scoped>
.vm-load {
  padding: 4px 8px 6px;
  border-top: 1px solid var(--border-color, #2a2a2a);
}
.vm-load-row {
  margin-bottom: 2px;
}
.vm-load-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.vm-load-title {
  font-size: 9px;
  letter-spacing: 0.08em;
  color: var(--text-secondary, #888);
}
/* Fixed-width, right-aligned numeric columns so positions never shift as
   magnitudes change (1ms vs 100ms, 1% vs 100%, 1M vs 100M). */
.vm-load-metrics {
  display: inline-flex;
  gap: 8px;
  font-variant-numeric: tabular-nums;
  font-size: 9px;
  color: var(--text-secondary, #888);
}
.num {
  display: inline-block;
  text-align: right;
}
.num-cyc { min-width: 62px; }
.num-time { min-width: 50px; }
.num-mem { min-width: 76px; }
.num-pct {
  min-width: 38px;
  font-size: 11px;
  font-weight: 600;
}
.load-low { color: #4ade80; }
.load-mid { color: #facc15; }
.load-high { color: #f87171; }
.vm-load-svg {
  width: 100%;
  height: 34px;
  display: block;
}
.vm-load-grid {
  stroke: var(--border-color, #333);
  stroke-width: 0.5;
  stroke-dasharray: 2 2;
}
.vm-load-vgrid {
  stroke: var(--border-color, #333);
  stroke-width: 0.5;
  stroke-dasharray: 1 3;
  opacity: 0.5;
}
.vm-load-win {
  font-size: 8px;
  opacity: 0.6;
  letter-spacing: 0;
}
.vm-load-line {
  fill: none;
  stroke-width: 1.5;
  vector-effect: non-scaling-stroke;
}
.vm-load-line.cpu { stroke: #38bdf8; }
.vm-load-line.mem { stroke: #a78bfa; }
.vm-load-area { stroke: none; }
.vm-load-area.cpu { fill: rgba(56, 189, 248, 0.15); }
.vm-load-area.mem { fill: rgba(167, 139, 250, 0.15); }
</style>
