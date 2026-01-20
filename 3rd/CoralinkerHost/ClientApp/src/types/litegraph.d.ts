/**
 * @file types/litegraph.d.ts
 * @description LiteGraph.js 类型声明
 * 
 * LiteGraph 没有官方 TypeScript 类型定义，
 * 这里提供基础类型声明以支持 TypeScript 编译。
 */

declare module 'litegraph.js' {
  /**
   * 节点图类 - 管理所有节点和连接
   */
  export class LGraph {
    _nodes: LGraphNode[]
    
    constructor()
    
    /** 启动图执行循环 */
    start(): void
    
    /** 停止图执行循环 */
    stop(): void
    
    /** 添加节点到图中 */
    add(node: LGraphNode): void
    
    /** 从图中移除节点 */
    remove(node: LGraphNode): void
    
    /** 清空所有节点 */
    clear(): void
    
    /** 序列化图状态 */
    serialize(): object
    
    /** 从序列化数据恢复图 */
    configure(data: object): void
    
    /** 根据位置获取节点 */
    getNodeOnPos(x: number, y: number, nodes?: LGraphNode[]): LGraphNode | null
    
    /** 根据 ID 获取节点 */
    getNodeById(id: number): LGraphNode | null
    
    /** 根据类型查找节点 */
    findNodesByType(type: string): LGraphNode[]
  }
  
  /**
   * 节点基类 - 所有自定义节点继承此类
   */
  export class LGraphNode {
    id: number
    type: string
    title: string
    pos: [number, number]
    size: [number, number]
    properties: Record<string, unknown>
    flags: Record<string, boolean>
    widgets: LGraphWidget[]
    inputs: LGraphSlot[]
    outputs: LGraphSlot[]
    graph: LGraph | null
    
    constructor(title?: string)
    
    /** 添加输入槽 */
    addInput(name: string, type: string): void
    
    /** 添加输出槽 */
    addOutput(name: string, type: string): void
    
    /** 添加控件 */
    addWidget(
      type: string,
      name: string,
      value: unknown,
      callback?: (value: unknown) => void,
      options?: object
    ): LGraphWidget
    
    /** 标记画布需要重绘 */
    setDirtyCanvas(fg: boolean, bg: boolean): void
    
    /** 执行节点逻辑 (每帧调用) */
    onExecute?(): void
    
    /** 自定义前景绘制 */
    onDrawForeground?(ctx: CanvasRenderingContext2D): void
    
    /** 自定义背景绘制 */
    onDrawBackground?(ctx: CanvasRenderingContext2D): void
    
    /** 属性变化回调 */
    onPropertyChanged?(name: string, value: unknown): void
    
    /** 鼠标按下事件 */
    onMouseDown?(event: MouseEvent, pos: [number, number], canvas: LGraphCanvas): boolean | void
    
    /** 双击事件 */
    onDblClick?(event: MouseEvent, pos: [number, number], canvas: LGraphCanvas): void
  }
  
  /**
   * 画布类 - 渲染和交互
   */
  export class LGraphCanvas {
    canvas: HTMLCanvasElement
    graph: LGraph
    ds: DragAndScale
    visible_nodes: LGraphNode[]
    prompt_box: HTMLElement | null
    
    constructor(canvas: HTMLCanvasElement | string, graph?: LGraph, options?: object)
    
    /** 设置画布大小 */
    resize(width: number, height: number): void
    
    /** 标记需要重绘 */
    setDirty(fg: boolean, bg: boolean): void
    
    /** 调整鼠标事件坐标 */
    adjustMouseEvent(event: MouseEvent): void
    
    /** 居中显示所有节点 */
    centerOnGraph(): void
    
    /** 处理右键菜单 (可覆盖以禁用) */
    processContextMenu(node: LGraphNode | null, event: MouseEvent): void
    
    /** 弹出编辑框 */
    prompt(
      title: string,
      value: string,
      callback: (value: string) => void,
      event?: MouseEvent,
      multiline?: boolean
    ): HTMLElement
  }
  
  /**
   * 拖拽和缩放状态
   */
  export interface DragAndScale {
    offset: [number, number]
    scale: number
    toCanvasContext(ctx: CanvasRenderingContext2D): void
  }
  
  /**
   * 控件
   */
  export interface LGraphWidget {
    name: string
    type: string
    value: unknown
    options?: object
    callback?: (value: unknown) => void
  }
  
  /**
   * 连接槽
   */
  export interface LGraphSlot {
    name: string
    type: string
    link: number | null
  }
  
  /**
   * LiteGraph 全局对象
   */
  const LiteGraph: {
    NODE_TITLE_HEIGHT: number
    NODE_WIDGET_HEIGHT: number
    
    /** 注册节点类型 */
    registerNodeType(type: string, nodeClass: typeof LGraphNode): void
    
    /** 关闭所有右键菜单 */
    closeAllContextMenus(): void
    
    /** 创建节点实例 */
    createNode(type: string): LGraphNode | null
  }
  
  export default LiteGraph
  export { LiteGraph }
}
