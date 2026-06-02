import type { VariableValue } from '@/types'

export interface FlowPoint {
  x: number
  y: number
}

export interface NodeRect {
  x: number
  y: number
  width: number
  height: number
}

export interface VariableFlowItem {
  id: string
  name: string
  type: string
  value: string
  direction: VariableValue['direction']
  x: number
  y: number
  width: number
  height: number
}

export interface FlowLine {
  id: string
  direction: VariableValue['direction']
  path: string
}

export interface VariableFlowLayoutInput {
  rootRect: NodeRect
  nodeRects: NodeRect[]
  flowVariables: VariableValue[]
  controlVariables: VariableValue[]
}

export interface VariableFlowLayoutResult {
  items: VariableFlowItem[]
  lines: FlowLine[]
}

interface FlowLayoutContext {
  rootRect: NodeRect
  nodeRects: NodeRect[]
  occupied: NodeRect[]
  allBounds: NodeRect
  desiredCenter: FlowPoint
  targetCenter: FlowPoint
}

interface FlowLayoutCandidate {
  origin: FlowPoint
  variables: VariableValue[]
  score: number
}

export const FLOW_ITEM_WIDTH = 210
export const FLOW_ITEM_HEIGHT = 38
export const FLOW_ITEM_GAP = 18
export const ROOT_SIZE = { width: 150, height: 128 }
export const NODE_SIZE = { width: 220, height: 150 }

const FLOW_NODE_PADDING = 36

export function computeVariableFlowLayout(input: VariableFlowLayoutInput): VariableFlowLayoutResult {
  const externalItems = layoutExternalVariables(input.flowVariables, input.rootRect, input.nodeRects)
  const controlItems = layoutControlVariables(input.controlVariables, input.rootRect)
  const lines = routeVariableLines(input.rootRect, input.nodeRects, externalItems)
  return {
    items: [...externalItems, ...controlItems],
    lines
  }
}

function layoutControlVariables(variables: VariableValue[], rootRect: NodeRect): VariableFlowItem[] {
  return variables.map((variable, index) => ({
    id: `flow-control-${variable.name}`,
    name: variable.name,
    type: formatType(variable.type),
    value: formatValue(variable.value),
    direction: variable.direction,
    x: rootRect.x + 12,
    y: rootRect.y + Math.max(54, rootRect.height - 46) + index * 34,
    width: FLOW_ITEM_WIDTH - 56,
    height: FLOW_ITEM_HEIGHT - 8
  }))
}

function layoutExternalVariables(
  variables: VariableValue[],
  rootRect: NodeRect,
  nodeRects: NodeRect[]
): VariableFlowItem[] {
  if (variables.length === 0) return []

  const targetBounds = nodeRects.length > 0
    ? rectBounds(nodeRects)
    : {
        x: rootRect.x + rootRect.width + 420,
        y: rootRect.y,
        width: NODE_SIZE.width,
        height: NODE_SIZE.height
      }
  const allBounds = rectBounds([rootRect, ...nodeRects])
  const rootCenter = rectCenter(rootRect)
  const targetCenter = rectCenter(targetBounds)
  const context: FlowLayoutContext = {
    rootRect,
    nodeRects,
    occupied: [rootRect, ...nodeRects].map(rect => expandRect(rect, FLOW_NODE_PADDING)),
    allBounds,
    desiredCenter: {
      x: (rootCenter.x + targetCenter.x) / 2,
      y: (rootCenter.y + targetCenter.y) / 2
    },
    targetCenter
  }
  const groupSize = {
    width: FLOW_ITEM_WIDTH,
    height: variables.length * FLOW_ITEM_HEIGHT + (variables.length - 1) * FLOW_ITEM_GAP
  }
  const layout = chooseVariableLayout(variables, groupSize, context)

  return layout.variables.map((variable, index) => ({
    id: `flow-${variable.direction}-${variable.name}`,
    name: variable.name,
    type: formatType(variable.type),
    value: formatValue(variable.value),
    direction: variable.direction,
    x: layout.origin.x,
    y: layout.origin.y + index * (FLOW_ITEM_HEIGHT + FLOW_ITEM_GAP),
    width: FLOW_ITEM_WIDTH,
    height: FLOW_ITEM_HEIGHT
  }))
}

function chooseVariableLayout(
  variables: VariableValue[],
  groupSize: { width: number; height: number },
  context: FlowLayoutContext
): FlowLayoutCandidate {
  const candidates = createLayoutCandidates(groupSize, context)
  const orders = createVariableOrderCandidates(variables, context)
  const scored = candidates.flatMap(origin => orders.map(order => ({
    origin,
    variables: order,
    score: scoreLayout(origin, order, groupSize, context)
  })))
  return scored.sort((a, b) => a.score - b.score)[0] ?? {
    origin: {
      x: context.desiredCenter.x - groupSize.width / 2,
      y: context.desiredCenter.y - groupSize.height / 2
    },
    variables,
    score: 0
  }
}

function createLayoutCandidates(groupSize: { width: number; height: number }, context: FlowLayoutContext): FlowPoint[] {
  const gap = FLOW_NODE_PADDING + 18
  const { allBounds, desiredCenter } = context
  const centerX = desiredCenter.x - groupSize.width / 2
  const centerY = desiredCenter.y - groupSize.height / 2
  const leftX = allBounds.x - gap - groupSize.width
  const rightX = allBounds.x + allBounds.width + gap
  const topY = allBounds.y - gap - groupSize.height
  const bottomY = allBounds.y + allBounds.height + gap
  const middleX = allBounds.x + allBounds.width / 2 - groupSize.width / 2
  const middleY = allBounds.y + allBounds.height / 2 - groupSize.height / 2

  return uniquePoints([
    { x: centerX, y: centerY },
    { x: middleX, y: topY },
    { x: middleX, y: bottomY },
    { x: leftX, y: middleY },
    { x: rightX, y: middleY },
    { x: leftX, y: topY },
    { x: rightX, y: topY },
    { x: leftX, y: bottomY },
    { x: rightX, y: bottomY },
    { x: centerX, y: topY },
    { x: centerX, y: bottomY },
    { x: leftX, y: centerY },
    { x: rightX, y: centerY }
  ])
}

function createVariableOrderCandidates(variables: VariableValue[], context: FlowLayoutContext): VariableValue[][] {
  if (variables.length <= 1) return [variables]

  const orders: VariableValue[][] = [
    variables,
    [...variables].reverse(),
    [...variables].sort((a, b) => variableOrderKey(a, context) - variableOrderKey(b, context)),
    [...variables].sort((a, b) => variableOrderKey(b, context) - variableOrderKey(a, context)),
    [...variables].sort((a, b) => a.name.localeCompare(b.name))
  ]

  if (variables.length <= 6) {
    orders.push(...permuteVariables(variables))
  }

  return uniqueVariableOrders(orders)
}

function scoreLayout(
  origin: FlowPoint,
  variables: VariableValue[],
  groupSize: { width: number; height: number },
  context: FlowLayoutContext
): number {
  const groupRect = { x: origin.x, y: origin.y, width: groupSize.width, height: groupSize.height }
  const groupCenter = rectCenter(groupRect)
  const rootCenter = rectCenter(context.rootRect)
  let score = distance(groupCenter, context.desiredCenter) * 0.75

  for (const occupied of context.occupied) {
    if (rectsOverlap(groupRect, occupied)) {
      score += 100000 + overlapArea(groupRect, occupied) * 50
    }
  }

  const verticalTopology = Math.abs(context.targetCenter.y - rootCenter.y) >
    Math.abs(context.targetCenter.x - rootCenter.x) * 0.85
  if (verticalTopology && groupCenter.y > context.allBounds.y && groupCenter.y < context.allBounds.y + context.allBounds.height) {
    score += 18000
  }

  variables.forEach((variable, index) => {
    const rect = {
      x: origin.x,
      y: origin.y + index * (FLOW_ITEM_HEIGHT + FLOW_ITEM_GAP),
      width: FLOW_ITEM_WIDTH,
      height: FLOW_ITEM_HEIGHT
    }
    score += scoreConnectionSketch(variable.direction, rect, context, index, variables.length)
  })

  return score
}

function scoreConnectionSketch(
  direction: VariableValue['direction'],
  variableRect: NodeRect,
  context: FlowLayoutContext,
  variableIndex: number,
  variableCount: number
): number {
  const variableCenter = rectCenter(variableRect)
  const rootCenter = rectCenter(context.rootRect)
  const targets = context.nodeRects
  const slotCount = Math.max(1, variableCount)
  let score = 0

  const addScore = (from: FlowPoint, to: FlowPoint) => {
    const route = makeLinearRoute(from, to)
    score += polylineLength(route) * 0.42
    score += countBends(route) * 24
    for (const obstacle of context.occupied) {
      if (polylineIntersectsRect(route, obstacle)) score += 12000
    }
  }

  if (direction === 'upper' || direction === 'mutual') {
    addScore(anchorToward(context.rootRect, variableCenter, variableIndex, slotCount), anchorToward(variableRect, rootCenter, 0, targets.length + 1))
    targets.forEach((target, index) => {
      addScore(anchorToward(variableRect, rectCenter(target), index + 1, targets.length + 1), anchorToward(target, variableCenter, variableIndex, slotCount))
    })
  }

  if (direction === 'lower' || direction === 'mutual') {
    targets.forEach((target, index) => {
      addScore(anchorToward(target, variableCenter, variableIndex, slotCount), anchorToward(variableRect, rectCenter(target), index + 1, targets.length + 1))
    })
    addScore(anchorToward(variableRect, rootCenter, 0, targets.length + 1), anchorToward(context.rootRect, variableCenter, variableIndex, slotCount))
  }

  return score
}

function routeVariableLines(rootRect: NodeRect, nodeRects: NodeRect[], items: VariableFlowItem[]): FlowLine[] {
  const targets = nodeRects.length > 0
    ? nodeRects
    : [{ x: rootRect.x + rootRect.width + 460, y: rootRect.y + rootRect.height / 2, width: NODE_SIZE.width, height: NODE_SIZE.height }]
  const lines: FlowLine[] = []
  const itemCount = Math.max(1, items.length)

  items.forEach((item, itemIndex) => {
    const variableRect = itemToRect(item)
    const variableCenter = rectCenter(variableRect)
    const rootCenter = rectCenter(rootRect)
    const variableSlotCount = Math.max(1, targets.length + 1)

    const pushLine = (
      id: string,
      direction: VariableValue['direction'],
      from: FlowPoint,
      to: FlowPoint
    ) => {
      lines.push({ id, direction, path: pointsToPath(makeLinearRoute(from, to)) })
    }

    if (item.direction === 'lower') {
      targets.forEach((target, index) => {
        pushLine(
          `${item.id}-node-source-${index}`,
          item.direction,
          anchorToward(target, variableCenter, itemIndex, itemCount),
          anchorToward(variableRect, rectCenter(target), index + 1, variableSlotCount)
        )
      })
      pushLine(
        `${item.id}-root-dest`,
        item.direction,
        anchorToward(variableRect, rootCenter, 0, variableSlotCount),
        anchorToward(rootRect, variableCenter, itemIndex, itemCount)
      )
    } else if (item.direction === 'upper') {
      pushLine(
        `${item.id}-root-source`,
        item.direction,
        anchorToward(rootRect, variableCenter, itemIndex, itemCount),
        anchorToward(variableRect, rootCenter, 0, variableSlotCount)
      )
      targets.forEach((target, index) => {
        pushLine(
          `${item.id}-node-dest-${index}`,
          item.direction,
          anchorToward(variableRect, rectCenter(target), index + 1, variableSlotCount),
          anchorToward(target, variableCenter, itemIndex, itemCount)
        )
      })
    } else {
      pushLine(`${item.id}-root-mutual-out`, item.direction, anchorToward(rootRect, variableCenter, itemIndex, itemCount), anchorToward(variableRect, rootCenter, 0, variableSlotCount))
      pushLine(`${item.id}-root-mutual-in`, item.direction, anchorToward(variableRect, rootCenter, 0, variableSlotCount), anchorToward(rootRect, variableCenter, itemIndex, itemCount))
      targets.forEach((target, index) => {
        pushLine(`${item.id}-node-mutual-out-${index}`, item.direction, anchorToward(target, variableCenter, itemIndex, itemCount), anchorToward(variableRect, rectCenter(target), index + 1, variableSlotCount))
        pushLine(`${item.id}-node-mutual-in-${index}`, item.direction, anchorToward(variableRect, rectCenter(target), index + 1, variableSlotCount), anchorToward(target, variableCenter, itemIndex, itemCount))
      })
    }
  })

  return lines
}

function makeLinearRoute(from: FlowPoint, to: FlowPoint): FlowPoint[] {
  const dx = Math.abs(to.x - from.x)
  const dy = Math.abs(to.y - from.y)
  if (dx > dy) {
    const midX = (from.x + to.x) / 2
    return simplifyOrthogonalPoints([from, { x: midX, y: from.y }, { x: midX, y: to.y }, to])
  }

  const midY = (from.y + to.y) / 2
  return simplifyOrthogonalPoints([from, { x: from.x, y: midY }, { x: to.x, y: midY }, to])
}

function anchorToward(rect: NodeRect, point: FlowPoint, slotIndex = 0, slotCount = 1): FlowPoint {
  const slot = edgeSlot(slotIndex, slotCount)
  const margin = 10
  const projectedX = Math.max(rect.x + margin, Math.min(rect.x + rect.width - margin, point.x))
  const projectedY = Math.max(rect.y + margin, Math.min(rect.y + rect.height - margin, point.y))
  const slotX = rect.x + rect.width * slot
  const slotY = rect.y + rect.height * slot
  const side = [
    { side: 'left', distance: Math.abs(point.x - rect.x) },
    { side: 'right', distance: Math.abs(point.x - (rect.x + rect.width)) },
    { side: 'top', distance: Math.abs(point.y - rect.y) },
    { side: 'bottom', distance: Math.abs(point.y - (rect.y + rect.height)) }
  ].sort((a, b) => a.distance - b.distance)[0]?.side ?? 'right'

  if (side === 'top' || side === 'bottom') {
    return { x: projectedX * 0.72 + slotX * 0.28, y: side === 'top' ? rect.y : rect.y + rect.height }
  }

  return { x: side === 'left' ? rect.x : rect.x + rect.width, y: projectedY * 0.72 + slotY * 0.28 }
}

function edgeSlot(slotIndex: number, slotCount: number): number {
  if (slotCount <= 1) return 0.5
  return (slotIndex + 1) / (slotCount + 1)
}

function itemToRect(item: VariableFlowItem): NodeRect {
  return { x: item.x, y: item.y, width: item.width, height: item.height }
}

function rectCenter(rect: NodeRect): FlowPoint {
  return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 }
}

function rectBounds(rects: NodeRect[]): NodeRect {
  const minX = Math.min(...rects.map(rect => rect.x))
  const minY = Math.min(...rects.map(rect => rect.y))
  const maxX = Math.max(...rects.map(rect => rect.x + rect.width))
  const maxY = Math.max(...rects.map(rect => rect.y + rect.height))
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY }
}

function expandRect(rect: NodeRect, padding: number): NodeRect {
  return { x: rect.x - padding, y: rect.y - padding, width: rect.width + padding * 2, height: rect.height + padding * 2 }
}

function rectsOverlap(a: NodeRect, b: NodeRect): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y
}

function overlapArea(a: NodeRect, b: NodeRect): number {
  const width = Math.max(0, Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x))
  const height = Math.max(0, Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y))
  return width * height
}

function distance(a: FlowPoint, b: FlowPoint): number {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

function polylineLength(points: FlowPoint[]): number {
  return points.reduce((sum, point, index) => {
    const prev = points[index - 1]
    return prev ? sum + distance(prev, point) : sum
  }, 0)
}

function countBends(points: FlowPoint[]): number {
  let bends = 0
  for (let i = 2; i < points.length; i++) {
    const a = points[i - 2]
    const b = points[i - 1]
    const c = points[i]
    if (!a || !b || !c) continue
    if ((a.x === b.x) !== (b.x === c.x)) bends++
  }
  return bends
}

function polylineIntersectsRect(points: FlowPoint[], rect: NodeRect): boolean {
  for (let i = 1; i < points.length; i++) {
    const from = points[i - 1]
    const to = points[i]
    if (from && to && segmentIntersectsRect(from, to, rect)) return true
  }
  return false
}

function segmentIntersectsRect(from: FlowPoint, to: FlowPoint, rect: NodeRect): boolean {
  if (pointInRect(from, rect) || pointInRect(to, rect)) return true
  const topLeft = { x: rect.x, y: rect.y }
  const topRight = { x: rect.x + rect.width, y: rect.y }
  const bottomRight = { x: rect.x + rect.width, y: rect.y + rect.height }
  const bottomLeft = { x: rect.x, y: rect.y + rect.height }
  return segmentsIntersect(from, to, topLeft, topRight) ||
    segmentsIntersect(from, to, topRight, bottomRight) ||
    segmentsIntersect(from, to, bottomRight, bottomLeft) ||
    segmentsIntersect(from, to, bottomLeft, topLeft)
}

function pointInRect(point: FlowPoint, rect: NodeRect): boolean {
  return point.x >= rect.x && point.x <= rect.x + rect.width && point.y >= rect.y && point.y <= rect.y + rect.height
}

function segmentsIntersect(a: FlowPoint, b: FlowPoint, c: FlowPoint, d: FlowPoint): boolean {
  const ccw = (p1: FlowPoint, p2: FlowPoint, p3: FlowPoint) =>
    (p3.y - p1.y) * (p2.x - p1.x) > (p2.y - p1.y) * (p3.x - p1.x)
  return ccw(a, c, d) !== ccw(b, c, d) && ccw(a, b, c) !== ccw(a, b, d)
}

function simplifyOrthogonalPoints(points: FlowPoint[]): FlowPoint[] {
  const result: FlowPoint[] = []
  for (const point of points) {
    const prev = result[result.length - 1]
    const prevPrev = result[result.length - 2]
    if (prev && prev.x === point.x && prev.y === point.y) continue
    if (prev && prevPrev && ((prevPrev.x === prev.x && prev.x === point.x) || (prevPrev.y === prev.y && prev.y === point.y))) {
      result[result.length - 1] = point
    } else {
      result.push(point)
    }
  }
  return result
}

function pointsToPath(points: FlowPoint[]): string {
  const [first, ...rest] = points
  if (!first) return ''
  return `M ${first.x} ${first.y} ${rest.map(point => `L ${point.x} ${point.y}`).join(' ')}`
}

function permuteVariables(variables: VariableValue[]): VariableValue[][] {
  const result: VariableValue[][] = []
  const used = new Array(variables.length).fill(false)
  const current: VariableValue[] = []

  const walk = () => {
    if (current.length === variables.length) {
      result.push([...current])
      return
    }

    for (let i = 0; i < variables.length; i++) {
      if (used[i]) continue
      const variable = variables[i]
      if (!variable) continue
      used[i] = true
      current.push(variable)
      walk()
      current.pop()
      used[i] = false
    }
  }

  walk()
  return result
}

function variableOrderKey(variable: VariableValue, context: FlowLayoutContext): number {
  const rootCenter = rectCenter(context.rootRect)
  const verticalBias = context.targetCenter.y >= rootCenter.y ? 1 : -1
  const directionWeight = variable.direction === 'upper' ? 0 : variable.direction === 'mutual' ? 1 : 2
  return directionWeight * verticalBias
}

function uniqueVariableOrders(orders: VariableValue[][]): VariableValue[][] {
  const seen = new Set<string>()
  const result: VariableValue[][] = []
  for (const order of orders) {
    const key = order.map(variable => `${variable.direction}:${variable.name}`).join('|')
    if (seen.has(key)) continue
    seen.add(key)
    result.push(order)
  }
  return result
}

function uniquePoints(points: FlowPoint[]): FlowPoint[] {
  const seen = new Set<string>()
  const result: FlowPoint[] = []
  for (const point of points) {
    const key = `${Math.round(point.x)}:${Math.round(point.y)}`
    if (seen.has(key)) continue
    seen.add(key)
    result.push(point)
  }
  return result
}

function formatType(type: string): string {
  const typeMap: Record<string, string> = {
    Int32: 'i32',
    Int16: 'i16',
    Int64: 'i64',
    UInt32: 'u32',
    UInt16: 'u16',
    UInt64: 'u64',
    Single: 'f32',
    Double: 'f64',
    Boolean: 'bool',
    Byte: 'u8',
    SByte: 'i8',
    String: 'str'
  }
  return typeMap[type] || type
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '-'
  if (typeof value === 'number') return Number.isInteger(value) ? String(value) : value.toFixed(3)
  if (Array.isArray(value)) {
    const preview = value.slice(0, 6).map(item => Number(item).toString(16).padStart(2, '0')).join(' ')
    return value.length > 6 ? `[${preview}...]` : `[${preview}]`
  }
  return String(value)
}
