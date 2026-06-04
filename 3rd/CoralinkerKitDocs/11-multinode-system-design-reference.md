# 多节点系统设计参考

本文给 Cursor 和其他 Agents 作为多节点任务的设计参考。它不是固定方案，而是帮助 Agent 在写代码前向用户确认系统边界、节点职责、变量命名和安全逻辑。

多节点任务不要一开始就写代码。先把系统拆成 Root、MCU 节点、变量流和安全回路，再选择 Logic 和节点 UUID。

## 设计原则

- Root 运行在 Host 侧，适合做运动解算、模式管理、上层限速、遥控输入处理和跨节点协调。
- MCU 节点运行在真实或模拟节点上，适合做本节点 IO、通讯协议、驱动器控制、传感器读取和本地安全动作。
- Root 通过 `[AsControlItem]` 接收 UI/Agent 输入，通过 `[AsUpperIO]` 下发目标值。
- MCU 节点读取 `[AsUpperIO]`，写 `[AsLowerIO]` 上报实际值、状态和故障。
- 一个 Logic class 只能编程到一个 MCU 节点；需要区分前桥/后桥/左轮/右轮时，要创建不同 Logic class，并在 LowerIO 变量名中带角色前缀。
- 不要尝试让多个 MCU 节点共享同一个 Logic。Variables Flow 能区分节点 UUID，但不能消除同一 Logic 多节点写同名 LowerIO 的冲突。

## 推荐设计步骤

1. 向用户确认车辆或设备结构：有几个节点，每个节点接了哪些驱动器、传感器、急停、触边、防撞、灯、按钮和通讯口。
1. 给每个节点命名角色，例如 `FrontBridgeNode`、`RearBridgeNode`、`LeftWheelNode`、`RightWheelNode`、`SafetyIoNode`。
1. 定义 Root 输入，例如 `joystickX`、`joystickY`、`enable`、`speedLimit`、`mode`、`resetFault`。
1. 定义 Root 输出到各节点的 UpperIO，例如 `frontLeftTargetSpeed`、`frontRightTargetSpeed`、`rearSteerTargetAngle`。
1. 定义节点上报的 LowerIO，例如 `frontBridgeFault`、`frontEmergencyStopActive`、`rearTouchEdgeActive`、`rearLeftActualSpeed`。
1. 先写最小闭环：Root 解算目标值，节点接收目标值并上报状态。
1. 再加安全逻辑：急停、触边、通讯停滞、驱动器故障、目标限幅、超时清零。
1. 最后才增加复杂功能：模式切换、自动导航、路径跟踪、故障恢复和日志细化。

## 常用分解策略

### 双差速桥 AGV

适合前后各一个 MCU 节点，每个节点控制一组左右轮。

Root 负责：

- 接收 `joystickX` 和 `joystickY`。
- 根据线速度和角速度计算前桥左右轮速度、后桥左右轮速度。
- 做全局限速和使能管理。
- 汇总前后桥故障、急停和触边状态，决定是否继续下发非零速度。

前桥节点负责：

- 控制前左/前右驱动器。
- 读取前桥急停、前触边、驱动器故障和实际速度。
- 通信停滞或本地安全触发时，清零本桥目标速度并上报故障。

后桥节点负责：

- 控制后左/后右驱动器。
- 读取后桥急停、后触边、驱动器故障和实际速度。
- 通信停滞或本地安全触发时，清零本桥目标速度并上报故障。

### 前后舵轮 AGV

适合前后各一个舵轮节点，或每个舵轮一个独立节点。

Root 负责：

- 把 `joystickX/Y`、目标航向或路径命令转换为每个舵轮的目标角度和目标速度。
- 做舵角限幅、速度限幅和模式切换。
- 根据任一舵轮故障、急停或触边，进入安全状态。

舵轮节点负责：

- 控制本舵轮转向角和驱动速度。
- 读取编码器、驱动器状态、急停和触边。
- 在本地故障或通信停滞时关闭使能或速度清零。

### 中央安全 IO 节点

适合安全输入集中在一个节点，而驱动控制分布在其他节点。

Root 负责：

- 读取安全节点上报的急停、触边、防撞、门禁或继电器状态。
- 统一决定所有驱动节点的 `enable` 和目标速度是否允许下发。
- 把安全状态透传到 UI/Variables，方便 Agent 和用户验证。

安全 IO 节点负责：

- 采集安全输入。
- 控制蜂鸣器、灯、继电器或安全输出。
- 即使 Root 逻辑异常，也应尽量保持保守输出。

### 相似节点的独立 Logic

适合多个硬件结构相似的节点，例如四个相同轮组。即使控制算法相似，也不能把同一个 Logic class program 到多个节点。

Agent 必须注意：

- 每个 MCU 节点必须有独立 `logicName`，例如 `FrontLeftWheelLogic`、`FrontRightWheelLogic`、`RearLeftWheelLogic`、`RearRightWheelLogic`。
- 每个 Logic class 应使用角色化 LowerIO 字段，例如 `frontLeftActualSpeed`、`rearRightActualSpeed`。
- 可以复制同一算法结构，但要改 class 名、CartDefinition 类型名和 LowerIO 字段名。
- Host 会拒绝把已绑定到其他节点的 `logicName` 再 program 到当前节点。

## AGV 差速示例变量

Root control 输入：

| 字段 | 含义 |
| --- | --- |
| `joystickX` | 转向，左负右正 |
| `joystickY` | 前后速度，后退负前进正 |
| `enable` | 总使能 |
| `speedLimit` | 速度限幅 |

Root 下发 UpperIO：

| 字段 | 目标 |
| --- | --- |
| `frontLeftTargetSpeed` | 前桥左轮目标速度 |
| `frontRightTargetSpeed` | 前桥右轮目标速度 |
| `rearLeftTargetSpeed` | 后桥左轮目标速度 |
| `rearRightTargetSpeed` | 后桥右轮目标速度 |
| `frontBridgeEnable` | 前桥使能 |
| `rearBridgeEnable` | 后桥使能 |

节点上报 LowerIO：

| 字段 | 来源 |
| --- | --- |
| `frontEmergencyStopActive` | 前桥急停输入 |
| `frontTouchEdgeActive` | 前桥触边输入 |
| `frontBridgeFault` | 前桥驱动器或节点故障 |
| `frontLeftActualSpeed` | 前左实际速度 |
| `rearEmergencyStopActive` | 后桥急停输入 |
| `rearTouchEdgeActive` | 后桥触边输入 |
| `rearBridgeFault` | 后桥驱动器或节点故障 |
| `rearLeftActualSpeed` | 后左实际速度 |

## 安全逻辑要求

每个 MCU 节点都应该有本地安全逻辑，不要只依赖 Root：

- 急停触发：立即清零速度、关闭使能或进入故障状态。
- 触边触发：根据用户确认的方向策略停止、限速或只禁止继续向触发方向运动。
- 驱动器故障：停止本节点输出并上报故障码。
- `iteration` 停滞：说明 Host 下发 IO 更新表并收到响应的通信周期没有继续完成，长时间停滞应触发安全动作。
- 命令超限：对 Root 下发的速度、角度、电流、PWM 做本地限幅。
- 复位故障：必须确认用户意图，不要自动清除真实硬件故障。

Root 也应该有全局安全逻辑：

- 任一节点上报急停时，所有节点目标速度清零。
- 任一节点上报触边时，根据车辆结构决定全车停车还是方向性限速。
- 任一关键节点通信异常时，停止全车运动。
- 用户没有确认安全输入接线时，只能写模拟或保守逻辑，不要假设急停和触边接在哪里。

## 必须向用户确认的问题

真实硬件任务开始前，Agent 至少要问清楚：

- 有几个 MCU 节点？每个节点的名称、UUID 或物理位置是什么？
- 每个节点控制哪些执行器？例如前桥左右轮、后桥左右轮、舵轮转向、升降、电磁阀。
- 每个设备的型号、安装位置、负载重量、减速比、轮径、最大速度、最大加速度、最大舵角和机械限位是什么？
- 急停接在哪个节点、哪个端口、哪个电平或协议字段？
- 触边、防撞、限位、门禁、安全继电器分别接在哪里？
- 驱动器协议是什么？CAN、RS232、RS485、GPIO 还是模拟量？波特率、CAN ID、寄存器/对象字典、payload 格式、字节序和缩放系数是什么？
- 速度、角度、方向的单位和正负方向是什么？
- 车辆构型是什么？差速、前后桥舵轮、四舵轮、麦轮、履带、叉车式还是其他结构？
- 实际应用场景和节拍是什么？例如搬运、巡检、对接、装卸、运行周期、人机协作区域。
- 触边触发后是全车停车，还是只禁止继续向触发方向运动？
- 通信断开多久需要进入安全状态？
- 是否允许自动复位故障，还是必须人工确认？
- 启动授权模式是什么？由人类在网页上手动点击 Start，还是每次确认后允许 Agent 调用 Start，还是本次会话授权 Agent 直接 Start？
- 验证方式是什么？看 Variables、节点日志、WireTap、实际轮子动作，还是只做模拟节点测试？

启动授权模式必须明确：

- 人类手动 Start：Agent 只部署到 build/program/root configure，等待用户在网页点击 Start。
- 每次确认后 Agent Start：Agent 每次 Start 或写入可能导致运动的控制量前都要再次确认。
- 本次会话授权 Agent 直接 Start：Agent 可直接操作，但仍需在操作前说明计划、操作后报告结果。

如果用户没有提供这些信息，Agent 应先追问。不能为了完成代码而假设真实硬件的急停、触边、方向、安全阈值和启动授权。没有明确授权时，默认人类手动 Start。

确认后的硬件事实必须整理到 `ai-deck/fact.md`。该文件是后续 Agent 的现场事实来源，应只写已确认事实、来源和待确认问题，不写猜测。如果 `ai-deck/fact.md` 不存在，按 bundle 中的 [runtime/fact-template.md](runtime/fact-template.md) 创建。

## Agent 执行建议

- 先画出 Root -> UpperIO -> MCU 节点 -> LowerIO -> Root/UI 的变量流。
- 多节点先用模拟节点跑通，再切换真实节点。
- 用 `variables flow` 验证变量来源和读者是否符合设计。
- 用 `node states` 验证节点状态、IO 和端口统计。
- 用 `wiretap` 验证串口/CAN 协议收发。
- 用 `iteration`、LowerIO 状态和日志共同判断通信是否健康。
- 任务结束时说明每个节点承担的职责、每个安全输入的处理策略，以及哪些硬件信息仍待用户确认。
