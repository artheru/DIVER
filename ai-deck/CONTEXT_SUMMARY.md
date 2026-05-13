# CONTEXT_SUMMARY — CORAL-NODE-V2.1 BSP 适配任务

> 该文件每次任务结束前会被刷新，记录当前工作上下文，便于下次继续。

## 最近一次支持

- 时间：2026-05-14 00:46 UTC+8
- 事项：验证 `CoralinkerHost` 脱离 VS 运行。
- 结果：用户确认当前 Host 已开着，并且网页可以访问。
- 备注：启动过程未重新构建前端；当前使用已有前端构建产物/现有 Host 输出。

## 任务背景
用户在 CORAL-NODE-V2.0 板基础上做了 PCB 改版，得到 CORAL-NODE-V2.1。需要在
`MCUSerialBridge/mcu/bsp/` 下新增对应的 BSP 适配目录，并把固件代码同步适配。

## 关键事实

- 工程：`d:/Documents/Coral/DIVER/MCUSerialBridge`
- 主芯片：STM32F405RG（沿用 V2.0）
- BSP 选择机制：`scons PDN=<目录名>`，每个 BSP 目录至少包含
  - `bsp_config.py`（CHIP_NAME + CPP_DEFINES）
  - `bsp.c`（板级初始化入口）
  - `digital_io.c`（数字 IO）
  - `ports.c`（外部 USART/CAN）
  - `uplink.c`（主协议串口）

- HAL 抽象层（`mcu/unilib/midware/include/hal/*.h`）已经提供
  USART / SPI / CAN / TIM / DMA / GPIO 的统一接口，BSP 仅填配置结构体即可。
  `hal_tim_oc_register` 内部完成 OC + DMA 链接。

- 现有 BSP 可作模板：
  - `CORAL-NODE-V2.0`：使用 74HC595 / 74HC165 通过 SPI2 扫描数字 IO，结构与 V2.1 接近
  - `FRLD-DIVERBK-V2`：使用 STM32F446VE，多端口对照参考

## V2.1 vs V2.0 关键变化（已与用户对齐）

| 项 | V2.0 | V2.1 |
| --- | --- | --- |
| 数字 IO 移位寄存器 SPI | SPI2（PB13/14/15）+ 控制脚 PC0/PC1/PC2 | SPI2（PB13/14/15）+ 控制脚 PB10(NOE)/PB11(IN_LOAD)/PB12(OUT_LOAD) |
| F1 急停 / F2 触边 | F1=PC13, F2=PC14 | F1=PB2, F2=PB1 |
| 24V 检测 | 无 | PB0 |
| 上行主协议 USART | USART2 (PA2/PA3) | 同 |
| RS485 数量 | 4 (USART1/4/5/6) | 3 (USART1/4/6) |
| CAN 数量 | 1 (CAN1) | 2 (CAN1+CAN2) |
| 灯带 | 无 | TIM1_CH1=PA8（带 DMA） |
| 板载 LED | 无 | LED1(绿)=PB4, LED2(黄)=PB3，**低电平点亮**，纯 GPIO |
| 蜂鸣器 | 无 | TIM2_CH1=PA15，正极性 PWM |
| 板载 FLASH | 无 | W25Q16JV，SPI1，CS=PC4 |
| KEY1 | 无 | PC13 |

## 用户决策（重要）

1. **F1 急停=PB2**，下拉输入；HIGH=急停未按下、允许运行。
2. **F2 触边=PB1**，下拉输入；HIGH=触边被按下、禁止运行。
3. **24V 检测=PB0**，下拉输入；HIGH=24V 存在。若 24V 不存在，上层软件应判断输出无效。
4. 输入移位寄存器为 **3 级级联，共 24 路**；输出当前为 **20 路**，后续用户会重整。
5. 输入 bitmap 约定：移位输入占 bit 0..23，24V=bit24，F1=bit25，F2=bit26。
6. 74HC165 输入为 NPN 漏极开路输入，软件读取后需要整体取反。
7. **TIM1_CH1=PA8**（灯带），DMA 由用户按图确认。
8. **LED 不使用 PWM/TIM**：直接 GPIO 控制（低电平点亮）。
9. **蜂鸣器单独占用 TIM2 CH1**。
10. **不要助记词 / 不要额外宏**。`bsp_config.py` 必须保持与 V2.0 同样的简洁风格。

## DMA 分配方案（已按用户图确认）

详见 `mcu/bsp/CORAL-NODE-V2.1/sch.md` 的 DMA Usage 表。要点：
- DMA1: UART4_RX S2C4, UART4_TX S4C4, USART2_RX S5C4, USART2_TX S6C4
- DMA2: SPI1_RX S0C3, TIM1_CH1 S1C6, USART6_RX S2C5, SPI1_TX S3C3, USART1_RX S5C4, USART6_TX S6C5, USART1_TX S7C4
- SPI2(IO 扫描) **不分配 DMA**（沿用 V2.0 同步模式，避开与 UART4 的 stream 冲突）

## 当前进度

- [x] 完成代码勘察（HAL/BSP/构建脚本）
- [x] 与用户对齐 V2.1 引脚分配
- [x] 落盘 `mcu/bsp/CORAL-NODE-V2.1/sch.md`（管脚 + DMA）
- [x] 用户确认 DMA 方案
- [x] 实现 5 个 BSP 源文件
- [x] 编译验证
- [x] 生成 UPG：`MCUSerialBridge/build/MCUSerialBridge_CORAL-NODE-V2.1_f1d8f16__20260513_171759.upg`

## 下一步行动

1. 后续按用户重整方案调整 20 路输出位序
2. 若需要启用灯带/蜂鸣器/板载 Flash 的上层驱动，再新增对应 BSP API 或配置出口
3. 烧录前使用 `scons BUILD_MCU=1 PDN=CORAL-NODE-V2.1 ENABLE_DIVER_RUNTIME=1 -j 12 debug=1`
