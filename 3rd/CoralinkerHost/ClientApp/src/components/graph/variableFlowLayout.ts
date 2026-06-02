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

export interface FixedGraphNode {
  id: string
  rect?: NodeRect
  order?: number
  name?: string
  sourceId?: string
}

export interface VariableFlowItem {
  id: string
  name: string
  type: string
  value: string
  direction: VariableValue['direction']
  sourceIds: string[]
  readerIds: string[]
  writerIds: string[]
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

interface FlowTarget {
  rect: NodeRect
  sourceId?: string
}

export interface FixedGraphLayoutInput {
  nodes: FixedGraphNode[]
  flowVariables: VariableValue[]
  controlVariables: VariableValue[]
  nodeOrder: string[]
  variableOrder: string[]
  rootSourceIds: string[]
}

export interface FixedGraphLayoutResult {
  rootRect: NodeRect
  nodeRects: Record<string, NodeRect>
  items: VariableFlowItem[]
  lines: FlowLine[]
  variableOrder: string[]
}

export interface VariableFlowLayoutInput {
  rootRect: NodeRect
  nodeRects: NodeRect[]
  flowVariables: VariableValue[]
  controlVariables: VariableValue[]
  variableOrder?: string[]
  rootSourceIds?: string[]
}

export interface VariableFlowLayoutResult {
  items: VariableFlowItem[]
  lines: FlowLine[]
  variableOrder: string[]
}

export const FLOW_ITEM_WIDTH = 210
export const FLOW_ITEM_HEIGHT = 38
export const FLOW_ITEM_GAP = 18
export const ROOT_SIZE = { width: 320, height: 190 }
export const NODE_SIZE = { width: 420, height: 430 }

const ROOT_CENTER_X = 0
const ROOT_Y = 40
const ROOT_HORIZONTAL_PADDING = 80
const ROOT_BASE_HEIGHT = 190
const ROOT_CONTROL_PADDING_X = 28
const ROOT_CONTROL_PADDING_BOTTOM = 20
const ROOT_CONTROL_AREA_HEIGHT = 54
const CONTROL_ITEM_MIN_WIDTH = 124
const CONTROL_ITEM_MAX_WIDTH = 220
const FLOW_ITEM_MIN_WIDTH = 132
const FLOW_ITEM_MAX_WIDTH = 260
const CONTROL_ITEM_GAP = 12
const IO_LAYER_GAP = 86
const NODE_LAYER_GAP = 92
const NODE_GAP = 56
const LINE_SLOT_MARGIN = 28

export function computeFixedGraphLayout(input: FixedGraphLayoutInput): FixedGraphLayoutResult {
  const orderedNodes = orderNodes(input.nodes, input.nodeOrder)
  const visibleFlowVariables = filterVisibleVariables(input.flowVariables)
  const visibleControlVariables = filterVisibleVariables(input.controlVariables)
  const variableOrder = mergeVariableOrder(input.variableOrder, visibleFlowVariables)
  const orderedFlowVariables = orderVariables(visibleFlowVariables, variableOrder)

  const controlSizes = visibleControlVariables.map(variable => estimateVariableItemSize(variable, true))
  const controlRowWidth = rowWidth(controlSizes, CONTROL_ITEM_GAP)
  const rootWidth = Math.max(ROOT_SIZE.width, controlRowWidth + ROOT_HORIZONTAL_PADDING)
  const rootHeight = visibleControlVariables.length > 0
    ? ROOT_BASE_HEIGHT + ROOT_CONTROL_AREA_HEIGHT
    : ROOT_SIZE.height
  const nodeRects = layoutNodes(orderedNodes)
  const nodeBounds = Object.values(nodeRects)
  const nodeRowWidth = nodeBounds.length > 0 ? rectBounds(nodeBounds).width : NODE_SIZE.width
  const centerX = ROOT_CENTER_X

  const rootRect = {
    x: centerX - rootWidth / 2,
    y: ROOT_Y,
    width: rootWidth,
    height: rootHeight
  }

  const controlItems = layoutControlVariables(visibleControlVariables, rootRect)
  const controlBottom = controlItems.length > 0
    ? Math.max(...controlItems.map(item => item.y + item.height))
    : rootRect.y + rootRect.height
  const ioY = controlBottom + IO_LAYER_GAP
  const flowItems = layoutExternalVariables(orderedFlowVariables, centerX, ioY)
  const nodesY = (flowItems.length > 0 ? ioY + FLOW_ITEM_HEIGHT : controlBottom) + NODE_LAYER_GAP
  const shiftedNodeRects = shiftNodeRects(nodeRects, centerX - nodeRowWidth / 2, nodesY)
  const nodeTargets: FlowTarget[] = []
  for (const node of orderedNodes) {
    const rect = shiftedNodeRects[node.id]
    if (rect) nodeTargets.push({ rect, sourceId: node.sourceId })
  }
  const lines = routeVariableLines(rootRect, nodeTargets, flowItems, input.rootSourceIds)

  return {
    rootRect,
    nodeRects: shiftedNodeRects,
    items: [...flowItems, ...controlItems],
    lines,
    variableOrder
  }
}

export function computeVariableFlowLayout(input: VariableFlowLayoutInput): VariableFlowLayoutResult {
  const visibleFlowVariables = filterVisibleVariables(input.flowVariables)
  const visibleControlVariables = filterVisibleVariables(input.controlVariables)
  const variableOrder = mergeVariableOrder(input.variableOrder ?? [], visibleFlowVariables)
  const orderedFlowVariables = orderVariables(visibleFlowVariables, variableOrder)
  const externalItems = layoutExternalVariables(
    orderedFlowVariables,
    input.rootRect.x + input.rootRect.width / 2,
    input.rootRect.y + input.rootRect.height + IO_LAYER_GAP
  )
  const controlItems = layoutControlVariables(visibleControlVariables, input.rootRect)
  const nodeTargets = input.nodeRects.map(rect => ({ rect, sourceId: undefined }))
  const lines = routeVariableLines(input.rootRect, nodeTargets, externalItems, input.rootSourceIds ?? [])
  return {
    items: [...externalItems, ...controlItems],
    lines,
    variableOrder
  }
}

function orderNodes(nodes: FixedGraphNode[], savedOrder: string[]): FixedGraphNode[] {
  const orderIndex = new Map(savedOrder.map((id, index) => [id, index]))
  return [...nodes].sort((a, b) => {
    const aOrder = typeof a.order === 'number' ? a.order : orderIndex.get(a.id)
    const bOrder = typeof b.order === 'number' ? b.order : orderIndex.get(b.id)
    if (aOrder != null && bOrder != null && aOrder !== bOrder) return aOrder - bOrder
    if (aOrder != null && bOrder == null) return -1
    if (aOrder == null && bOrder != null) return 1
    return nodes.indexOf(a) - nodes.indexOf(b)
  })
}

function layoutNodes(nodes: FixedGraphNode[]): Record<string, NodeRect> {
  const result: Record<string, NodeRect> = {}
  let x = 0
  for (const node of nodes) {
    const size = node.rect ?? { x: 0, y: 0, width: NODE_SIZE.width, height: NODE_SIZE.height }
    const rect = { x, y: 0, width: size.width || NODE_SIZE.width, height: size.height || NODE_SIZE.height }
    result[node.id] = rect
    x += rect.width + NODE_GAP
  }
  return result
}

function shiftNodeRects(rects: Record<string, NodeRect>, x: number, y: number): Record<string, NodeRect> {
  const result: Record<string, NodeRect> = {}
  const values = Object.entries(rects)
  if (values.length === 0) return result

  let cursorX = x
  for (const [id, rect] of values) {
    result[id] = { ...rect, x: cursorX, y }
    cursorX += rect.width + NODE_GAP
  }
  return result
}

function filterVisibleVariables(variables: VariableValue[]): VariableValue[] {
  return variables.filter(variable => !isInternalVariableName(variable.name))
}

function isInternalVariableName(name: string): boolean {
  const rawTail = name.split('.').pop() ?? name
  return name.startsWith('__') || rawTail.startsWith('__') || rawTail.includes('__')
}

export function mergeVariableOrder(savedOrder: string[], variables: VariableValue[]): string[] {
  const visibleNames = variables.map(variable => variable.name)
  const visibleSet = new Set(visibleNames)
  const existing = savedOrder.filter(name => visibleSet.has(name))
  const existingSet = new Set(existing)
  const additions = visibleNames
    .filter(name => !existingSet.has(name))
    .sort((a, b) => defaultVariableKey(variables, a) - defaultVariableKey(variables, b) || a.localeCompare(b))

  if (additions.length === 0) return existing

  const result = [...existing]
  for (const name of additions) {
    const variable = variableByName(variables, name)
    const insertAfter = findInsertionIndex(result, variables, variable)
    result.splice(insertAfter + 1, 0, name)
  }
  return result
}

function findInsertionIndex(order: string[], variables: VariableValue[], variable?: VariableValue): number {
  if (!variable) return order.length - 1
  const key = variableGroup(variable)
  let insertAfter = -1
  for (let index = 0; index < order.length; index++) {
    const name = order[index]
    if (!name) continue
    const current = variableByName(variables, name)
    if (!current) continue
    if (variableGroup(current) <= key) insertAfter = index
  }
  return insertAfter
}

function variableByName(variables: VariableValue[], name: string): VariableValue | undefined {
  return variables.find(variable => variable.name === name)
}

function orderVariables(variables: VariableValue[], order: string[]): VariableValue[] {
  const byName = new Map(variables.map(variable => [variable.name, variable]))
  return order.map(name => byName.get(name)).filter((variable): variable is VariableValue => !!variable)
}

function defaultVariableKey(variables: VariableValue[], name: string): number {
  const variable = variableByName(variables, name)
  if (!variable) return Number.MAX_SAFE_INTEGER
  return variableGroup(variable) * 100000 + Math.max(0, variables.indexOf(variable))
}

function variableGroup(variable: VariableValue): number {
  return variable.direction === 'upper' ? 0 : variable.direction === 'mutual' ? 1 : 2
}

function layoutControlVariables(variables: VariableValue[], rootRect: NodeRect): VariableFlowItem[] {
  if (variables.length === 0) return []

  const sizes = variables.map(variable => estimateVariableItemSize(variable, true))
  const minX = rootRect.x + ROOT_CONTROL_PADDING_X
  const maxX = rootRect.x + rootRect.width - ROOT_CONTROL_PADDING_X
  const startX = clamp(rootRect.x + rootRect.width / 2 - rowWidth(sizes, CONTROL_ITEM_GAP) / 2, minX, maxX)
  let x = startX
  return variables.map((variable, index) => {
    const size = sizes[index] ?? { width: CONTROL_ITEM_MIN_WIDTH, height: FLOW_ITEM_HEIGHT }
    const item = createVariableItem(
      variable,
      `flow-control-${variable.name}`,
      x,
      rootRect.y + rootRect.height - ROOT_CONTROL_PADDING_BOTTOM - size.height,
      size
    )
    x += size.width + CONTROL_ITEM_GAP
    return item
  })
}

function layoutExternalVariables(variables: VariableValue[], centerX: number, y: number): VariableFlowItem[] {
  if (variables.length === 0) return []

  const sizes = variables.map(variable => estimateVariableItemSize(variable, false))
  const startX = centerX - rowWidth(sizes, FLOW_ITEM_GAP) / 2
  let x = startX
  return variables.map((variable, index) => {
    const size = sizes[index] ?? { width: FLOW_ITEM_WIDTH, height: FLOW_ITEM_HEIGHT }
    const item = createVariableItem(variable, `flow-${variable.direction}-${variable.name}`, x, y, size)
    x += size.width + FLOW_ITEM_GAP
    return item
  })
}

function createVariableItem(
  variable: VariableValue,
  id: string,
  x: number,
  y: number,
  size: { width: number; height: number }
): VariableFlowItem {
  return {
    id,
    name: variable.name,
    type: formatType(variable.type),
    value: formatValue(variable.value),
    direction: variable.direction,
    sourceIds: normalizeIds(variable.sourceIds),
    readerIds: normalizeIds(variable.readerIds),
    writerIds: normalizeIds(variable.writerIds),
    x,
    y,
    width: size.width,
    height: size.height
  }
}

function rowWidth(sizes: Array<{ width: number }>, gap: number): number {
  if (sizes.length === 0) return 0
  return sizes.reduce((sum, size) => sum + size.width, 0) + (sizes.length - 1) * gap
}

function estimateVariableItemSize(variable: VariableValue, compact: boolean): { width: number; height: number } {
  const type = formatType(variable.type)
  const value = formatValue(variable.value)
  const nameWidth = Math.min(150, Math.max(48, variable.name.length * 6.8))
  const typeWidth = Math.min(64, Math.max(34, type.length * 7.2 + 12))
  const valueWidth = Math.min(90, Math.max(24, value.length * 6.5))
  const minWidth = compact ? CONTROL_ITEM_MIN_WIDTH : FLOW_ITEM_MIN_WIDTH
  const maxWidth = compact ? CONTROL_ITEM_MAX_WIDTH : FLOW_ITEM_MAX_WIDTH
  const width = clamp(Math.ceil(typeWidth + Math.max(nameWidth, valueWidth) + 38), minWidth, maxWidth)
  return { width, height: FLOW_ITEM_HEIGHT }
}

function routeVariableLines(rootRect: NodeRect, nodeTargets: FlowTarget[], items: VariableFlowItem[], rootSourceIds: string[]): FlowLine[] {
  if (items.length === 0) return []

  const lines: FlowLine[] = []
  const rootIds = normalizeIds(rootSourceIds)

  items.forEach((item, itemIndex) => {
    const variableRect = itemToRect(item)
    const rootSlot = anchorRootBottom(rootRect, itemIndex, items.length)
    const readers = relationIds(item.readerIds)
    const writers = relationIds(item.writerIds)
    const rootReads = hasAnyId(rootIds, readers)
    const rootWrites = hasAnyId(rootIds, writers)
    const readerTargets = matchingTargets(nodeTargets, readers)
    const writerTargets = matchingTargets(nodeTargets, writers)
    const topCount = Number(rootWrites) + Number(rootReads)
    const bottomCount = writerTargets.length + readerTargets.length

    const pushLine = (id: string, from: FlowPoint, to: FlowPoint, lane = 0) => {
      lines.push({ id, direction: item.direction, path: bezierPath(from, to, lane) })
    }

    let topSlot = 0
    let bottomSlot = 0

    if (rootWrites) {
      pushLine(`${item.id}-root-writer`, rootSlot, anchorTop(variableRect, topSlot++, topCount), itemIndex)
    }

    writerTargets.forEach((target, targetIndex) => {
      pushLine(
        `${item.id}-node-writer-${target.sourceId ?? targetIndex}-${targetIndex}`,
        anchorTop(target.rect, itemIndex, items.length),
        anchorBottom(variableRect, bottomSlot++, bottomCount),
        targetIndex - (writerTargets.length - 1) / 2
      )
    })

    if (rootReads) {
      pushLine(`${item.id}-root-reader`, anchorTop(variableRect, topSlot++, topCount), rootSlot, itemIndex)
    }

    readerTargets.forEach((target, targetIndex) => {
      pushLine(
        `${item.id}-node-reader-${target.sourceId ?? targetIndex}-${targetIndex}`,
        anchorBottom(variableRect, bottomSlot++, bottomCount),
        anchorTop(target.rect, itemIndex, items.length),
        targetIndex - (readerTargets.length - 1) / 2
      )
    })
  })

  return lines
}

function bezierPath(from: FlowPoint, to: FlowPoint, lane = 0): string {
  const dy = Math.abs(to.y - from.y)
  const curve = Math.max(54, dy * 0.46)
  const laneOffset = lane * 8
  const c1 = { x: from.x + laneOffset, y: from.y + (to.y >= from.y ? curve : -curve) }
  const c2 = { x: to.x - laneOffset, y: to.y - (to.y >= from.y ? curve : -curve) }
  return `M ${from.x} ${from.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${to.x} ${to.y}`
}

function anchorTop(rect: NodeRect, slotIndex = 0, slotCount = 1): FlowPoint {
  return { x: rect.x + slotX(rect, slotIndex, slotCount), y: rect.y }
}

function anchorBottom(rect: NodeRect, slotIndex = 0, slotCount = 1): FlowPoint {
  return { x: rect.x + slotX(rect, slotIndex, slotCount), y: rect.y + rect.height }
}

function anchorRootBottom(rect: NodeRect, slotIndex = 0, slotCount = 1): FlowPoint {
  const inset = Math.max(ROOT_CONTROL_PADDING_X, rect.width * 0.12)
  return { x: rect.x + slotX(rect, slotIndex, slotCount, inset), y: rect.y + rect.height }
}

function slotX(rect: NodeRect, slotIndex: number, slotCount: number, margin = LINE_SLOT_MARGIN): number {
  if (slotCount <= 1) return rect.width / 2
  const usable = Math.max(0, rect.width - margin * 2)
  return margin + usable * ((slotIndex + 1) / (slotCount + 1))
}

function itemToRect(item: VariableFlowItem): NodeRect {
  return { x: item.x, y: item.y, width: item.width, height: item.height }
}

function normalizeIds(ids?: readonly string[]): string[] {
  return Array.from(new Set((ids ?? []).filter(id => !!id?.trim()).map(id => id.trim())))
}

function relationIds(ids: string[]): string[] {
  if (ids.length > 0) return ids
  return []
}

function hasAnyId(left: string[], right: string[]): boolean {
  if (left.length === 0 || right.length === 0) return false
  const rightSet = new Set(right.map(id => id.toLowerCase()))
  return left.some(id => rightSet.has(id.toLowerCase()))
}

function matchingTargets(targets: FlowTarget[], ids: string[]): FlowTarget[] {
  if (ids.length === 0) return []
  const idSet = new Set(ids.map(id => id.toLowerCase()))
  return targets.filter(target => target.sourceId && idSet.has(target.sourceId.toLowerCase()))
}

function rectBounds(rects: NodeRect[]): NodeRect {
  const minX = Math.min(...rects.map(rect => rect.x))
  const minY = Math.min(...rects.map(rect => rect.y))
  const maxX = Math.max(...rects.map(rect => rect.x + rect.width))
  const maxY = Math.max(...rects.map(rect => rect.y + rect.height))
  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY }
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value))
}

function formatType(type: string): string {
  const shortType = type.replace(/^System\./, '')
  const typeMap: Record<string, string> = {
    Int32: 'i32',
    UInt32: 'u32',
    Int16: 'i16',
    UInt16: 'u16',
    Int64: 'i64',
    UInt64: 'u64',
    Single: 'f32',
    Double: 'f64',
    Byte: 'u8',
    SByte: 'i8',
    Boolean: 'bool',
    String: 'str'
  }
  return typeMap[shortType] || shortType?.toLowerCase() || '?'
}

function formatValue(value: unknown): string {
  if (value == null) return 'null'
  if (typeof value === 'number') {
    return Number.isInteger(value) ? String(value) : value.toFixed(3)
  }
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}
