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
  placement?: 'center' | 'root-side' | 'node-side'
  gapIndex?: number
  sideStackHeight?: number
  sideSlotIndex?: number
  sideSlotCount?: number
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
export const NODE_SIZE = { width: 380, height: 430 }

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
const FLOW_ITEM_MAX_WIDTH = 320
const CONTROL_ITEM_GAP = 12
const IO_LAYER_GAP = 86
const NODE_LAYER_GAP = 92
const NODE_GAP = 56
const SIDE_VARIABLE_GAP = 40
const SIDE_VARIABLE_STACK_GAP = 12
const SIDE_VARIABLE_TOP_OFFSET = 92
const LINE_SLOT_MARGIN = 28
const ROOT_RIGHT_GAP = -100000

export function computeFixedGraphLayout(input: FixedGraphLayoutInput): FixedGraphLayoutResult {
  const orderedNodes = orderNodes(input.nodes, input.nodeOrder)
  const visibleFlowVariables = filterVisibleVariables(input.flowVariables)
  const visibleControlVariables = filterVisibleVariables(input.controlVariables)
  const variableOrder = mergeVariableOrder(input.variableOrder, visibleFlowVariables)
  const orderedFlowVariables = orderVariables(visibleFlowVariables, variableOrder)
  const rootIds = normalizeIds(input.rootSourceIds)
  const gapVariableGroups = groupGapVariables(orderedFlowVariables, orderedNodes, rootIds)
  const gapVariableNames = new Set(
    Array.from(gapVariableGroups.values()).flat().map(variable => variable.name)
  )
  const centerFlowVariables = orderedFlowVariables.filter(variable => !gapVariableNames.has(variable.name))

  const controlSizes = visibleControlVariables.map(variable => estimateVariableItemSize(variable, true))
  const controlRowWidth = rowWidth(controlSizes, CONTROL_ITEM_GAP)
  const rootWidth = Math.max(ROOT_SIZE.width, controlRowWidth + ROOT_HORIZONTAL_PADDING)
  const rootHeight = visibleControlVariables.length > 0
    ? ROOT_BASE_HEIGHT + ROOT_CONTROL_AREA_HEIGHT
    : ROOT_SIZE.height
  const nodeRects = layoutNodes(orderedNodes, gapVariableGroups)
  const nodeBounds = Object.values(nodeRects)
  const nodeRowWidth = nodeBounds.length > 0
    ? nodeLayoutWidth(orderedNodes, nodeRects)
    : NODE_SIZE.width
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
  const flowItems = layoutExternalVariables(centerFlowVariables, centerX, ioY)
  const nodesY = (flowItems.length > 0 ? ioY + FLOW_ITEM_HEIGHT : controlBottom) + NODE_LAYER_GAP
  const shiftedNodeRects = shiftNodeRects(nodeRects, centerX - nodeRowWidth / 2, nodesY)
  const gapItems = layoutGapVariables(gapVariableGroups, orderedNodes, shiftedNodeRects, rootRect)
  const allFlowItems = [...flowItems, ...gapItems]
  const nodeTargets: FlowTarget[] = []
  for (const node of orderedNodes) {
    const rect = shiftedNodeRects[node.id]
    if (rect) nodeTargets.push({ rect, sourceId: node.sourceId })
  }
  const lines = routeVariableLines(rootRect, nodeTargets, allFlowItems, input.rootSourceIds)

  return {
    rootRect,
    nodeRects: shiftedNodeRects,
    items: [...allFlowItems, ...controlItems],
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

function layoutNodes(
  nodes: FixedGraphNode[],
  gapVariableGroups: Map<number, VariableValue[]> = new Map()
): Record<string, NodeRect> {
  const result: Record<string, NodeRect> = {}
  let x = 0
  for (let index = 0; index < nodes.length; index++) {
    const node = nodes[index]
    if (!node) continue
    const size = node.rect ?? { x: 0, y: 0, width: NODE_SIZE.width, height: NODE_SIZE.height }
    const rect = { x, y: 0, width: size.width || NODE_SIZE.width, height: size.height || NODE_SIZE.height }
    result[node.id] = rect
    x += rect.width + gapWidth(index, nodes.length, gapVariableGroups)
  }
  return result
}

function shiftNodeRects(rects: Record<string, NodeRect>, x: number, y: number): Record<string, NodeRect> {
  const result: Record<string, NodeRect> = {}
  const values = Object.entries(rects)
  if (values.length === 0) return result

  const bounds = rectBounds(values.map(([, rect]) => rect))
  const deltaX = x - bounds.x
  for (const [id, rect] of values) {
    result[id] = { ...rect, x: rect.x + deltaX, y }
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

function layoutGapVariables(
  gapVariableGroups: Map<number, VariableValue[]>,
  nodes: FixedGraphNode[],
  nodeRects: Record<string, NodeRect>,
  rootRect: NodeRect
): VariableFlowItem[] {
  const items: VariableFlowItem[] = []
  for (const [gapIndex, variables] of gapVariableGroups) {
    if (variables.length === 0) continue

    const sizes = variables.map(variable => estimateVariableItemSize(variable, false))
    const maxWidth = Math.max(...sizes.map(size => size.width))
    const stackHeight = rowHeight(sizes, SIDE_VARIABLE_STACK_GAP)
    const x = gapVariableX(gapIndex, maxWidth, nodes, nodeRects, rootRect)
    const yBase = gapVariableY(gapIndex, nodes, nodeRects, rootRect, stackHeight)
    let y = yBase
    variables.forEach((variable, index) => {
      const size = sizes[index] ?? { width: FLOW_ITEM_WIDTH, height: FLOW_ITEM_HEIGHT }
      const itemX = x + (maxWidth - size.width) / 2
      items.push(createVariableItem(
        variable,
        `flow-gap-${gapIndex}-${variable.name}`,
        itemX,
        y,
        size,
        {
          placement: gapIndex === ROOT_RIGHT_GAP ? 'root-side' : 'node-side',
          gapIndex,
          sideStackHeight: stackHeight,
          sideSlotIndex: index,
          sideSlotCount: variables.length
        }
      ))
      y += size.height + SIDE_VARIABLE_STACK_GAP
    })
  }
  return items
}

function groupGapVariables(
  variables: VariableValue[],
  nodes: FixedGraphNode[],
  rootIds: string[]
): Map<number, VariableValue[]> {
  const groups = new Map<number, VariableValue[]>()
  const nodeIndexBySource = new Map<string, number>()
  nodes.forEach((node, index) => {
    if (node.sourceId) nodeIndexBySource.set(node.sourceId.toLowerCase(), index)
  })

  for (const variable of variables) {
    const readers = relationIds(normalizeIds(variable.readerIds))
    const writers = relationIds(normalizeIds(variable.writerIds))
    const rootInvolved = hasAnyId(rootIds, readers) || hasAnyId(rootIds, writers)
    if (rootInvolved) {
      if (readers.length === 0 && relationIds(writers.filter(id => hasAnyId(rootIds, [id]))).length === 1 && writers.length === 1) {
        const group = groups.get(ROOT_RIGHT_GAP) ?? []
        group.push(variable)
        groups.set(ROOT_RIGHT_GAP, group)
      }
      continue
    }

    const readerIndexes = relationNodeIndexes(readers, nodeIndexBySource)
    const writerIndexes = relationNodeIndexes(writers, nodeIndexBySource)
    const gapIndex = classifyGapVariable(readerIndexes, writerIndexes, nodes.length)
    if (gapIndex == null) continue

    const group = groups.get(gapIndex) ?? []
    group.push(variable)
    groups.set(gapIndex, group)
  }

  return groups
}

function classifyGapVariable(readerIndexes: number[], writerIndexes: number[], nodeCount: number): number | null {
  if (nodeCount === 0) return null

  if (readerIndexes.length === 0 && writerIndexes.length === 1) {
    return writerIndexes[0] ?? null
  }

  if (writerIndexes.length === 0 && readerIndexes.length === 1) {
    return (readerIndexes[0] ?? 0) - 1
  }

  if (readerIndexes.length === 1 && writerIndexes.length === 1) {
    const readerIndex = readerIndexes[0] ?? 0
    const writerIndex = writerIndexes[0] ?? 0
    if (Math.abs(readerIndex - writerIndex) === 1) {
      return Math.min(readerIndex, writerIndex)
    }
  }

  return null
}

function relationNodeIndexes(ids: string[], nodeIndexBySource: Map<string, number>): number[] {
  const indexes: number[] = []
  for (const id of ids) {
    const index = nodeIndexBySource.get(id.toLowerCase())
    if (index != null && !indexes.includes(index)) indexes.push(index)
  }
  return indexes
}

function gapWidth(gapIndex: number, nodeCount: number, gapVariableGroups: Map<number, VariableValue[]>): number {
  const variables = gapVariableGroups.get(gapIndex)
  if (gapIndex === ROOT_RIGHT_GAP) return NODE_GAP
  if (!variables?.length) {
    return gapIndex >= nodeCount - 1 ? 0 : NODE_GAP
  }
  const variableWidth = maxVariableWidth(variables)
  const required = variableWidth + SIDE_VARIABLE_GAP * 2
  return gapIndex >= nodeCount - 1 ? required : Math.max(NODE_GAP, required)
}

function nodeLayoutWidth(
  nodes: FixedGraphNode[],
  nodeRects: Record<string, NodeRect>
): number {
  if (nodes.length === 0) return 0
  const firstNode = nodes[0]
  const lastNode = nodes[nodes.length - 1]
  const first = firstNode ? nodeRects[firstNode.id] : undefined
  const last = lastNode ? nodeRects[lastNode.id] : undefined
  if (!first || !last) return 0
  return last.x + last.width - first.x
}

function maxVariableWidth(variables: VariableValue[]): number {
  if (variables.length === 0) return 0
  return Math.max(...variables.map(variable => estimateVariableItemSize(variable, false).width))
}

function gapVariableX(
  gapIndex: number,
  maxWidth: number,
  nodes: FixedGraphNode[],
  nodeRects: Record<string, NodeRect>,
  rootRect: NodeRect
): number {
  if (gapIndex === ROOT_RIGHT_GAP) {
    return rootRect.x + rootRect.width + SIDE_VARIABLE_GAP
  }

  if (gapIndex < 0) {
    const firstNode = nodes[0]
    const first = firstNode ? nodeRects[firstNode.id] : undefined
    return first ? first.x - SIDE_VARIABLE_GAP - maxWidth : -maxWidth / 2
  }

  if (gapIndex >= nodes.length - 1) {
    const lastNode = nodes[nodes.length - 1]
    const last = lastNode ? nodeRects[lastNode.id] : undefined
    return last ? last.x + last.width + SIDE_VARIABLE_GAP : maxWidth / 2
  }

  const leftNode = nodes[gapIndex]
  const rightNode = nodes[gapIndex + 1]
  const left = leftNode ? nodeRects[leftNode.id] : undefined
  const right = rightNode ? nodeRects[rightNode.id] : undefined
  if (!left || !right) return 0
  const gapLeft = left.x + left.width
  const gapRight = right.x
  return gapLeft + (gapRight - gapLeft - maxWidth) / 2
}

function gapVariableY(
  gapIndex: number,
  nodes: FixedGraphNode[],
  nodeRects: Record<string, NodeRect>,
  rootRect: NodeRect,
  stackHeight = 0
): number {
  if (gapIndex === ROOT_RIGHT_GAP) {
    return rootRect.y + rootRect.height - stackHeight
  }

  const rects: NodeRect[] = []
  const leftNode = gapIndex >= 0 ? nodes[gapIndex] : undefined
  if (leftNode) {
    const rect = nodeRects[leftNode.id]
    if (rect) rects.push(rect)
  }
  const rightNode = gapIndex + 1 < nodes.length ? nodes[gapIndex + 1] : undefined
  if (rightNode) {
    const rect = nodeRects[rightNode.id]
    if (rect) rects.push(rect)
  }
  const y = rects.length > 0 ? Math.min(...rects.map(rect => rect.y)) : 0
  return y + SIDE_VARIABLE_TOP_OFFSET
}

function createVariableItem(
  variable: VariableValue,
  id: string,
  x: number,
  y: number,
  size: { width: number; height: number },
  placement?: Pick<VariableFlowItem, 'placement' | 'gapIndex' | 'sideStackHeight' | 'sideSlotIndex' | 'sideSlotCount'>
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
    height: size.height,
    ...placement
  }
}

function rowWidth(sizes: Array<{ width: number }>, gap: number): number {
  if (sizes.length === 0) return 0
  return sizes.reduce((sum, size) => sum + size.width, 0) + (sizes.length - 1) * gap
}

function rowHeight(sizes: Array<{ height: number }>, gap: number): number {
  if (sizes.length === 0) return 0
  return sizes.reduce((sum, size) => sum + size.height, 0) + (sizes.length - 1) * gap
}

function estimateVariableItemSize(variable: VariableValue, compact: boolean): { width: number; height: number } {
  const type = formatType(variable.type)
  const value = formatValue(variable.value)
  const nameWidth = Math.min(240, Math.max(48, variable.name.length * 6.2))
  const typeWidth = Math.min(64, Math.max(34, type.length * 7.2 + 12))
  const valueWidth = Math.min(90, Math.max(24, value.length * 6.5))
  const minWidth = compact ? CONTROL_ITEM_MIN_WIDTH : FLOW_ITEM_MIN_WIDTH
  const maxWidth = compact ? CONTROL_ITEM_MAX_WIDTH : FLOW_ITEM_MAX_WIDTH
  const width = clamp(Math.ceil(typeWidth + Math.max(nameWidth, valueWidth) + 24), minWidth, maxWidth)
  return { width, height: FLOW_ITEM_HEIGHT }
}

function routeVariableLines(rootRect: NodeRect, nodeTargets: FlowTarget[], items: VariableFlowItem[], rootSourceIds: string[]): FlowLine[] {
  if (items.length === 0) return []

  const lines: FlowLine[] = []
  const rootIds = normalizeIds(rootSourceIds)
  const rootBottomSlots: string[] = []
  const nodeTopSlots = new Map<string, string[]>()
  const nodeTargetKey = (target: FlowTarget, index: number) => target.sourceId ?? `node-${index}`
  const addUniqueSlot = (slots: string[], key: string) => {
    if (!slots.includes(key)) slots.push(key)
  }

  items.forEach(item => {
    if (item.placement === 'root-side' || item.placement === 'node-side') return

    const readers = relationIds(item.readerIds)
    const writers = relationIds(item.writerIds)
    const rootReads = hasAnyId(rootIds, readers)
    const rootWrites = hasAnyId(rootIds, writers)
    const readerTargets = matchingTargets(nodeTargets, readers)
    const writerTargets = matchingTargets(nodeTargets, writers)

    if (rootWrites) addUniqueSlot(rootBottomSlots, rootSlotKey(item, 'writer'))
    if (rootReads) addUniqueSlot(rootBottomSlots, rootSlotKey(item, 'reader'))

    writerTargets.forEach((target, targetIndex) => {
      const targetKey = nodeTargetKey(target, targetIndex)
      const slots = nodeTopSlots.get(targetKey) ?? []
      addUniqueSlot(slots, nodeSlotKey(item, 'writer', target, targetIndex))
      nodeTopSlots.set(targetKey, slots)
    })

    readerTargets.forEach((target, targetIndex) => {
      const targetKey = nodeTargetKey(target, targetIndex)
      const slots = nodeTopSlots.get(targetKey) ?? []
      addUniqueSlot(slots, nodeSlotKey(item, 'reader', target, targetIndex))
      nodeTopSlots.set(targetKey, slots)
    })
  })

  items.forEach((item, itemIndex) => {
    const variableRect = itemToRect(item)
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

    if (item.placement === 'root-side') {
      if (rootWrites) {
        pushSideLine(
          `${item.id}-root-side-writer`,
          anchorSide(rootRect, 'right', item),
          anchorSide(variableRect, 'left'),
          item,
          rootRect,
          itemIndex
        )
      }
      if (rootReads) {
        pushSideLine(
          `${item.id}-root-side-reader`,
          anchorSide(variableRect, 'left'),
          anchorSide(rootRect, 'right', item),
          item,
          rootRect,
          itemIndex
        )
      }
      return
    }

    if (item.placement === 'node-side') {
      writerTargets.forEach((target, targetIndex) => {
        const variableSide = variableRect.x < target.rect.x + target.rect.width / 2 ? 'right' : 'left'
        const nodeSide = variableSide === 'right' ? 'left' : 'right'
        pushSideLine(
          `${item.id}-node-side-writer-${target.sourceId ?? targetIndex}-${targetIndex}`,
          anchorSide(target.rect, nodeSide, item),
          anchorSide(variableRect, variableSide),
          item,
          target.rect,
          targetIndex - (writerTargets.length - 1) / 2
        )
      })

      readerTargets.forEach((target, targetIndex) => {
        const variableSide = variableRect.x < target.rect.x + target.rect.width / 2 ? 'right' : 'left'
        const nodeSide = variableSide === 'right' ? 'left' : 'right'
        pushSideLine(
          `${item.id}-node-side-reader-${target.sourceId ?? targetIndex}-${targetIndex}`,
          anchorSide(variableRect, variableSide),
          anchorSide(target.rect, nodeSide, item),
          item,
          target.rect,
          targetIndex - (readerTargets.length - 1) / 2
        )
      })
      return
    }

    if (rootWrites) {
      const slotKey = rootSlotKey(item, 'writer')
      pushLine(
        `${item.id}-root-writer`,
        anchorRootBottom(rootRect, slotIndex(rootBottomSlots, slotKey), rootBottomSlots.length),
        anchorTop(variableRect, topSlot++, topCount),
        itemIndex
      )
    }

    writerTargets.forEach((target, targetIndex) => {
      const targetKey = nodeTargetKey(target, targetIndex)
      const slots = nodeTopSlots.get(targetKey) ?? []
      const slotKey = nodeSlotKey(item, 'writer', target, targetIndex)
      pushLine(
        `${item.id}-node-writer-${target.sourceId ?? targetIndex}-${targetIndex}`,
        anchorTop(target.rect, slotIndex(slots, slotKey), slots.length),
        anchorBottom(variableRect, bottomSlot++, bottomCount),
        targetIndex - (writerTargets.length - 1) / 2
      )
    })

    if (rootReads) {
      const slotKey = rootSlotKey(item, 'reader')
      pushLine(
        `${item.id}-root-reader`,
        anchorTop(variableRect, topSlot++, topCount),
        anchorRootBottom(rootRect, slotIndex(rootBottomSlots, slotKey), rootBottomSlots.length),
        itemIndex
      )
    }

    readerTargets.forEach((target, targetIndex) => {
      const targetKey = nodeTargetKey(target, targetIndex)
      const slots = nodeTopSlots.get(targetKey) ?? []
      const slotKey = nodeSlotKey(item, 'reader', target, targetIndex)
      pushLine(
        `${item.id}-node-reader-${target.sourceId ?? targetIndex}-${targetIndex}`,
        anchorBottom(variableRect, bottomSlot++, bottomCount),
        anchorTop(target.rect, slotIndex(slots, slotKey), slots.length),
        targetIndex - (readerTargets.length - 1) / 2
      )
    })

    function pushSideLine(id: string, from: FlowPoint, to: FlowPoint, sideItem: VariableFlowItem, targetRect: NodeRect, lane = 0) {
      const stackHeight = sideItem.sideStackHeight ?? sideItem.height
      const path = stackHeight <= targetRect.height
        ? straightPath(from, to)
        : sideBezierPath(from, to, lane)
      lines.push({ id, direction: sideItem.direction, path })
    }
  })

  return lines
}

function rootSlotKey(item: VariableFlowItem, role: 'reader' | 'writer'): string {
  return `${item.id}:root:${role}`
}

function nodeSlotKey(item: VariableFlowItem, role: 'reader' | 'writer', target: FlowTarget, targetIndex: number): string {
  return `${item.id}:node:${role}:${target.sourceId ?? targetIndex}`
}

function slotIndex(slots: string[], key: string): number {
  const index = slots.indexOf(key)
  return index >= 0 ? index : 0
}

function straightPath(from: FlowPoint, to: FlowPoint): string {
  return `M ${from.x} ${from.y} L ${to.x} ${to.y}`
}

function bezierPath(from: FlowPoint, to: FlowPoint, lane = 0): string {
  const dy = Math.abs(to.y - from.y)
  const curve = Math.max(54, dy * 0.46)
  const laneOffset = lane * 8
  const c1 = { x: from.x + laneOffset, y: from.y + (to.y >= from.y ? curve : -curve) }
  const c2 = { x: to.x - laneOffset, y: to.y - (to.y >= from.y ? curve : -curve) }
  return `M ${from.x} ${from.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${to.x} ${to.y}`
}

function sideBezierPath(from: FlowPoint, to: FlowPoint, lane = 0): string {
  const dx = Math.abs(to.x - from.x)
  const curve = clamp(dx * 0.45, 16, Math.max(16, dx * 0.5))
  const laneOffset = clamp(lane * 4, -Math.max(0, dx * 0.12), Math.max(0, dx * 0.12))
  const sign = to.x >= from.x ? 1 : -1
  const c1 = { x: from.x + sign * (curve + laneOffset), y: from.y }
  const c2 = { x: to.x - sign * (curve + laneOffset), y: to.y }
  return `M ${from.x} ${from.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${to.x} ${to.y}`
}

function anchorTop(rect: NodeRect, slotIndex = 0, slotCount = 1): FlowPoint {
  return { x: rect.x + slotX(rect, slotIndex, slotCount), y: rect.y }
}

function anchorBottom(rect: NodeRect, slotIndex = 0, slotCount = 1): FlowPoint {
  return { x: rect.x + slotX(rect, slotIndex, slotCount), y: rect.y + rect.height }
}

function anchorSide(rect: NodeRect, side: 'left' | 'right', item?: VariableFlowItem): FlowPoint {
  const useSlots = item && (item.sideStackHeight ?? 0) > rect.height
  const y = useSlots
    ? rect.y + slotY(rect, item?.sideSlotIndex ?? 0, item?.sideSlotCount ?? 1)
    : item
      ? item.y + item.height / 2
      : rect.y + rect.height / 2
  return { x: side === 'left' ? rect.x : rect.x + rect.width, y }
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

function slotY(rect: NodeRect, slotIndex: number, slotCount: number, margin = LINE_SLOT_MARGIN): number {
  if (slotCount <= 1) return rect.height / 2
  const usable = Math.max(0, rect.height - margin * 2)
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
