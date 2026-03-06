# CoralinkerTestCar 测试文件说明

本文档说明当前目录下迁移过来的测试车相关逻辑文件：来源、用途、作者信息、改动点、可运行性。

## 电机与通信配置

### CAN 总线参数

| 项目 | 配置 |
|---|---|
| CAN 端口 | `CAN-1`（端口索引 `4`） |
| CAN 波特率 | **500 Kbps** |
| 协议 | CANOpen |

### 电机 ID 分配

| 节点 | 转向电机 ID | 行走电机 ID | 备注 |
|---|---:|---:|---|
| Node1（前轮） | 1 | 2 | `Node1Motor12NewApiDemo` |
| Node2（后轮） | 3 | 4 | `Node2Motor34NewApiDemo` |

### PDO 配置（当前逻辑）

| 电机类型 | COB-ID | 方向 | 长度 | 数据定义 | 对应代码函数/位置 |
|---|---|---|---:|---|---|
| 行走电机 RPDO1 | `0x200 + NodeId` | Host -> Motor | 7 | `mode(1) + targetSpeed(4) + controlWord(2)` | `Node2MotorProtocol.GenerateRPDO1RunMotor()` |
| 行走电机 TPDO1 | `0x180 + NodeId` | Motor -> Host | 8 | `statusWord(2) + actualSpeed(4) + controlWord(2)` | `Node2MotorProtocol.ParseRunActualSpeed()` / `ParseRunStatusWord()` |
| 转向电机 RPDO1 | `0x200 + NodeId` | Host -> Motor | 7 | `mode(1) + targetPosition(4) + controlWord(2)` | `Node2MotorProtocol.GenerateRPDO1TurnMotor()` |
| 转向电机 RPDO2 | `0x300 + NodeId` | Host -> Motor | 4 | `turnSpeed(4)` | `Node2MotorProtocol.GenerateRPDO2TurnMotor()` |
| 转向电机 TPDO1 | `0x180 + NodeId` | Motor -> Host | 7 | `statusWord(2) + actualPosition(4) + mode/control(1)` | `Node2MotorProtocol.ParseTurnActualPosition()` / `ParseTurnStatusWord()` |

### 关键逻辑入口（便于 AI 对照）

| 目标 | 文件 | 类/方法 |
|---|---|---|
| Node2 后轮控制（M3/M4） | `Node2Motor34NewApiDemo.cs` | `Node2Motor34NewApiDemo.Operation()` |
| Node1 前轮控制（M1/M2） | `Node2Motor34NewApiDemo.cs` | `Node1Motor12NewApiDemo.Operation()` |
| CAN 收发统一封装 | `Node2Motor34NewApiDemo.cs` | `Node2CanIo.WriteCANPayload()` / `ReadCANPayload()` |
| NMT 启动状态机 | `Node2Motor34NewApiDemo.cs` | `MotorBootupHelper(int i)`（Node1/Node2 各一份） |
| 速度单位换算（rpm <-> raw） | `Node2Motor34NewApiDemo.cs` | `Node2Constants.RunRawPerRpm` |

## 文件清单

### 1) `TestRoutine.cs`
- **用途**: 较早版本的测试逻辑样例，主要演示两电机（Motor3/4）CANOpen 启动与速度控制基础流程。
- **来源**: 从仓库根目录 `DIVER/TestRoutine.cs` 迁移到本目录。
- **谁写的**: 历史文件（仓库原有），无法从文件内可靠判断具体个人作者。
- **实现/改动**:
  - 包含老 API 风格（`CoralinkerDIVERVehicle` / `RunOnMCU.WriteEvent` 等）。
  - 结构相对简化，主要聚焦电机 bootup + RPDO/TPDO 基本交互。
- **能不能跑**:
  - 在老 API/老工程上下文更容易直接运行。
  - 在当前新 API 体系下通常需要适配后再用。

### 2) `TestRoutineOldVersionAPIFINAL RUN.cs`
- **用途**: 老 API 最终版本参考逻辑，包含 Node1/Node2 结构，除电机外还包含 IO、触边、急停、障碍物等流程。
- **来源**: 从仓库根目录 `DIVER/TestRoutineOldVersionAPIFINAL RUN.cs` 迁移到本目录。
- **谁写的**: 历史文件（仓库原有），无法从文件内可靠判断具体个人作者。
- **实现/改动**:
  - 完整度高，包含较多工程化逻辑（输入/输出快照、安全逻辑、回原点等）。
  - 作为“老 API 终版”参考价值高。
- **能不能跑**:
  - 依赖老 API 和对应硬件接线/协议配置，不能保证在当前新 API 工程中直接编译运行。

### 3) `Node2Motor34NewApiDemo.cs`
- **用途**: 基于老终版思路改写的新 API 版本示例，当前包含 Node2(M3/M4) 及 Node1(M1/M2) 控制逻辑框架。
- **来源**: 从 `docs/LogicsForIntroductionVideo/Node2Motor34NewApiDemo.cs` 迁移到本目录。
- **谁写的**: 本次对话中由 AI 助手根据用户要求迭代生成/修改。
- **实现/改动**:
  - 改为新 API CAN 调用方式（`RunOnMCU.WriteCANMessage` / `RunOnMCU.ReadCANMessage`）。
  - 行走电机目标/反馈改为 `float`，并加入比例换算：
    - `raw = rpm * 400`
    - `rpm = raw / 400`
  - 提供关键阶段日志和 LowerIO 状态可视化字段。
  - 明确按用户要求移除/忽略急停、复位、触边、障碍物等外部 IO 依赖（在对应测试版本里不启用）。
- **能不能跑**:
  - 逻辑可用于当前新 API 方向验证。
  - 仍依赖实际硬件、PDO 映射、母线电压、节点 ID、运行时配置；需在真实环境中联调确认。

## 迁移说明
- 本次为“文件位置迁移 + 说明补充”，未承诺这些文件在同一工程内全部可同时编译通过。
- 若后续希望做“可编译整合版”，建议单独新增一个可编译目标文件，并逐步裁剪/合并重复类型定义。
