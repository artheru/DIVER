# test_programs

存放给物理节点跑的测试 / 基准逻辑（用户上传到网页 → Build → 选为 Logic → Program 到节点）。

| 文件 | 用途 |
| --- | --- |
| `BenchLogic.cs` | DIVER VM CPU 基准逻辑。每个 cycle 做固定量的「数组读写 + 方法调用 + 静态字段访问」，正好命中 CCM 优化的热点结构（heap_obj 表 / 栈帧 / static）。用于通过 telemetry 对比「CCM 优化前(v2.1) vs 优化后」的时钟周期数。 |

## LowerIO 字段含义

| 字段 | 含义 |
| --- | --- |
| `checksum` | **稳定值**：只取决于 `rounds`，每个 cycle 都一样。两套固件（CCM/非 CCM）同 `rounds` 下必须完全相等 → 验证 CCM 没改坏行为。|
| `iteration` | 循环序号回显，**本来就每 cycle 变**（它不是计算结果，只是 `it`）。|
| `workUnits` | 本 cycle 内层操作数 = `effRounds * 256`。|
| `effRounds` | 实际用的 rounds（`rounds<=0` → 默认 8），让默认值不再是黑盒。|

## 如何使用 BenchLogic 测性能

1. 网页里上传 `BenchLogic.cs` 为 asset，Save（提交输入版本），点 Build。
2. 给物理节点（已刷 v2.1 固件）Program `BenchLogic`，Start。
3. 看节点的 CPU Load 图 / telemetry：记录 `cycles`（每轮 vm_run 的 DWT 周期数）。
   - `rounds`(UpperIO) 默认 0 → 实际用 8（看 `effRounds`）。调大让每轮更重、更易观察差异。
   - `checksum` 应是个**固定不变**的数；两套固件同 `rounds` 下相等即正确。
4. 之后刷 CCM 优化版固件（v2.2），同样跑 `BenchLogic`，对比**同一 `rounds`** 下的 `cycles`。

## CPU 负载图是怎么算出来的（telemetry 链路）

1. **固件**：每个 vm_loop 用 DWT 周期计数器 + `g_hal_timestamp_us` 包住 `vm_run()`，量出本轮 `cycles` 和 `micros`；连同 LowerIO 一起打进 `0x72`(CommandUploadLowerIoAndVmStats) 包上报，还带 `interval_us`(=scanInterval)、`cpuHz`、heap 用量。
2. **c_core** 收 `0x72` → 拆成 LowerIO 和 VmStats 两路回调。
3. **SDK** `OnVmStats` → `DIVERSession` 每节点存一个滚动历史（环形缓冲）。
4. **Host** `GET /api/node/{uuid}/vmstats` 返回历史 + 最新样本。
5. **前端** `CpuLoadChart.vue` 每 1s 拉一次，画 sparkline。
   - **负载% = 100 × micros / interval_us**（本轮执行耗时 ÷ 扫描周期）。例：vm_run 花 5ms、scanInterval 50ms → 10%。
   - 卡片下方三个数：`cycles`（DWT 周期）/ `micros`（耗时）/ `heapUsed / heapObjs`（堆占用/活对象数）。

### 为什么用它而不是模拟节点
模拟节点跑在 PC 上、没有 CCM 概念，`lastCycles` 是 0（PC 只报 wall-clock 微秒），无法度量 MCU 上 CCM 的真实加速。性能对比必须在物理节点上做。
