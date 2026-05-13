# CORAL-NODE-V2.1 硬件适配参考

> 本文档由 BSP 适配过程沉淀，记录 V2.1 PCB 上 MCU(STM32F405RG) 的引脚原始分配
> 与外设 / DMA 资源规划。代码实现以本文档为唯一真源。

---

## 1. 主芯片
- **MCU**：STM32F405RG（LQFP64，ARM Cortex-M4F，168 MHz）
- **HSE**：12 MHz（沿用 V2.0，`FORCE_PLL_M=12`）

---

## 2. 主协议（Uplink）

| 信号 | 引脚 | 外设 | AF | 备注 |
| ---- | ---- | ---- | -- | ---- |
| Uplink TX | PA2 | USART2_TX | AF7 | 与 V2.0 相同 |
| Uplink RX | PA3 | USART2_RX | AF7 | 与 V2.0 相同 |

> 默认波特率 1 Mbps，无 DE，使用 DMA 收发，详见 §10。

---

## 3. 对外通讯接口

### 3.1 RS485-1（USART6）
| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| 485-1 TX | PC6 | USART6_TX | AF8 |
| 485-1 RX | PC7 | USART6_RX | AF8 |
| 485-1 DE | PC8 | GPIO 输出 | — |

### 3.2 RS485-2（USART1）
| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| 485-2 TX | PA9 | USART1_TX | AF7 |
| 485-2 RX | PA10 | USART1_RX | AF7 |
| 485-2 DE | PA11 | GPIO 输出 | — |

### 3.3 RS485-3（UART4）
| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| 485-3 TX | PC10 | UART4_TX | AF8 |
| 485-3 RX | PC11 | UART4_RX | AF8 |
| 485-3 DE | PC12 | GPIO 输出 | — |

### 3.4 CAN-1（CAN1）
| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| CAN1 TX | PB9 | CAN1_TX | AF9 |
| CAN1 RX | PB8 | CAN1_RX | AF9 |

### 3.5 CAN-2（CAN2）
| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| CAN2 TX | PB6 | CAN2_TX | AF9 |
| CAN2 RX | PB5 | CAN2_RX | AF9 |

### 3.6 灯带（TIM1 CH1）
| 信号 | 引脚 | 外设 | AF | 备注 |
| ---- | ---- | ---- | -- | ---- |
| LED Strip | PA8 | TIM1_CH1 | AF1 | WS2812 类，需 DMA |

> STM32F405RG LQFP64 上 TIM1_CH1 唯一可用引脚为 PA8。

---

## 4. 用户交互

| 信号 | 引脚 | 类型 | 备注 |
| ---- | ---- | ---- | ---- |
| KEY1（保留） | PC13 | GPIO 输入 | 上拉，按下为低 |
| LED1 绿 | PB4 | **GPIO 输出** | **低电平点亮**（不使用 TIM3） |
| LED2 黄 | PB3 | **GPIO 输出** | **低电平点亮**（不使用 TIM2 CH2） |
| 蜂鸣器 | PA15 | TIM2_CH1 | AF1，正极性 PWM |

> LED 用普通 GPIO 即可（不需要呼吸效果）。
> 蜂鸣器独占 TIM2，可自由调整音调频率。

---

## 5. 固定输入

| 信号 | 引脚 | 类型 | 上下拉 | 备注 |
| ---- | ---- | ---- | ------ | ---- |
| 24V 检测 | PB0 | GPIO 输入 | 下拉 | HIGH=24V 存在，占 input bitmap bit 24 |
| F2 触边 | PB1 | GPIO 输入 | 下拉 | HIGH=触边被按下、禁止运行，占 input bitmap bit 26 |
| F1 急停 | PB2 | GPIO 输入 | 下拉 | HIGH=急停未按下、允许运行，占 input bitmap bit 25 |

> 若 24V 检测不到电压，则输出状态应由上层软件判断为无效输出。

---

## 6. 输入输出移位寄存器

PB10–PB15 共 6 根线驱动 74HC595 / 74HC165 链：

| 信号 | 引脚 | 角色 | SPI 复用 |
| ---- | ---- | ---- | -------- |
| NOE（输出禁止） | PB10 | GPIO 输出，低有效 | — |
| IN_LOAD（输入锁存） | PB11 | GPIO 输出 | — |
| OUT_LOAD（输出锁存） | PB12 | GPIO 输出 | — |
| IO_CLK | PB13 | GPIO/SPI2 SCK (AF5) | SPI2_SCK |
| IN_DATA（并→串入） | PB14 | GPIO/SPI2 MISO (AF5) | SPI2_MISO |
| OUT_DATA（串→并出） | PB15 | GPIO/SPI2 MOSI (AF5) | SPI2_MOSI |

**与 V2.0 的差异**：V2.0 上 NOE/IN_LOAD/OUT_LOAD 在 PC0/PC1/PC2，V2.1 全部
集中到 PB10/PB11/PB12。SPI2 数据线脚位不变。

**输入/输出数量与位图约定**：
- 输入移位寄存器：3 级级联，共 24 路，按 bit 0..23 顺序进入 input bitmap。
- 输出移位寄存器：共 20 路，按 bit 0..19 顺序输出。
- 24V 检测：input bitmap bit 24。
- F1 急停：input bitmap bit 25。
- F2 触边：input bitmap bit 26。
- 74HC165 输入为 NPN 漏极开路输入，软件读取后需要整体取反。

---

## 7. 板载 FLASH（备用）

| 信号 | 引脚 | 外设 | AF |
| ---- | ---- | ---- | -- |
| FLASH CS | PC4 | GPIO 输出 | — |
| FLASH SCK | PA5 | SPI1_SCK | AF5 |
| FLASH MISO | PA6 | SPI1_MISO | AF5 |
| FLASH MOSI | PA7 | SPI1_MOSI | AF5 |

外部芯片：W25Q16JV，2 MB SPI NOR FLASH。

---

## 8. 调试 / 系统

| 信号 | 引脚 | 备注 |
| ---- | ---- | ---- |
| SWDIO | PA13 | 保留给调试器 |
| SWCLK | PA14 | 保留给调试器 |

---

## 9. STM32F405 引脚使用全表

| 端口 | 引脚 | 用途 | 模式 |
| ---- | ---- | ---- | ---- |
| PA0 | — | 空闲 | — |
| PA1 | — | 空闲 | — |
| PA2 | Uplink TX | USART2 TX |
| PA3 | Uplink RX | USART2 RX |
| PA4 | — | 空闲 | — |
| PA5 | FLASH SCK | SPI1 SCK |
| PA6 | FLASH MISO | SPI1 MISO |
| PA7 | FLASH MOSI | SPI1 MOSI |
| PA8 | 灯带 | TIM1 CH1 |
| PA9 | 485-2 TX | USART1 TX |
| PA10 | 485-2 RX | USART1 RX |
| PA11 | 485-2 DE | GPIO |
| PA12 | — | 空闲 | — |
| PA13 | SWDIO | 调试 |
| PA14 | SWCLK | 调试 |
| PA15 | 蜂鸣器 | TIM2 CH1 |
| PB0 | 24V 检测 | GPIO 输入 |
| PB1 | F2 触边 | GPIO 输入 |
| PB2 | F1 急停 | GPIO 输入 |
| PB3 | LED2 黄 | GPIO 输出 |
| PB4 | LED1 绿 | GPIO 输出 |
| PB5 | CAN2 RX | CAN2 RX |
| PB6 | CAN2 TX | CAN2 TX |
| PB7 | — | 空闲 | — |
| PB8 | CAN1 RX | CAN1 RX |
| PB9 | CAN1 TX | CAN1 TX |
| PB10 | NOE | GPIO 输出 |
| PB11 | IN_LOAD | GPIO 输出 |
| PB12 | OUT_LOAD | GPIO 输出 |
| PB13 | IO_CLK | SPI2 SCK |
| PB14 | IN_DATA | SPI2 MISO |
| PB15 | OUT_DATA | SPI2 MOSI |
| PC4 | FLASH CS | GPIO 输出 |
| PC6 | 485-1 TX | USART6 TX |
| PC7 | 485-1 RX | USART6 RX |
| PC8 | 485-1 DE | GPIO 输出 |
| PC10 | 485-3 TX | UART4 TX |
| PC11 | 485-3 RX | UART4 RX |
| PC12 | 485-3 DE | GPIO 输出 |
| PC13 | KEY1（保留） | GPIO 输入 |

---

## 10. DMA 资源分配方案（用户已确认）

> STM32F4 每个 stream 同一时刻只能选一个 channel，跨 stream 不冲突。
> 下表与代码（ports.c / uplink.c / bsp.c）保持一致，是改板/排错的唯一真源。
> Channel 编号引用 RM0090 表 42/43。

### 10.1 DMA1（AHB1，主要服务 USART2 / UART4）

| Stream | Channel | 外设 | 方向 | 用途 |
| :----: | :-----: | ---- | ---- | ---- |
| 2 | 4 | UART4_RX | P→M | 485-3 RX |
| 4 | 4 | UART4_TX | M→P | 485-3 TX |
| 5 | 4 | USART2_RX | P→M | Uplink RX |
| 6 | 4 | USART2_TX | M→P | Uplink TX |

> Stream 0/1/3/7 留空，便于将来扩展。

### 10.2 DMA2（AHB1，主要服务 USART1 / USART6 / SPI1 / TIM1）

| Stream | Channel | 外设 | 方向 | 用途 |
| :----: | :-----: | ---- | ---- | ---- |
| 0 | 3 | SPI1_RX | P→M | FLASH 读 |
| 1 | 6 | TIM1_CH1 | M→P | 灯带 PWM 缓冲 |
| 2 | 5 | USART6_RX | P→M | 485-1 RX |
| 3 | 3 | SPI1_TX | M→P | FLASH 写 |
| 5 | 4 | USART1_RX | P→M | 485-2 RX |
| 6 | 5 | USART6_TX | M→P | 485-1 TX |
| 7 | 4 | USART1_TX | M→P | 485-2 TX |

> Stream 4 留空。

### 10.3 不分配 DMA 的外设

| 外设 | 原因 |
| ---- | ---- |
| **SPI2**（IO 移位寄存器） | DMA1 上 SPI2_TX(S4 C0) 与 UART4_TX(S4 C4) 同 stream 冲突。沿用 V2.0 同步模式 `hal_spi_master_transmit_sync`，每帧 3 字节 @ 1 MHz ≈ 24 µs，可接受。 |
| **CAN1 / CAN2** | HAL 不使用 DMA，使用中断 + 软件队列 |
| **TIM2 CH1（蜂鸣器）** | 单纯 PWM 占空比控制，无需 DMA |
| **LED1 / LED2** | 纯 GPIO，无需 DMA |
| **GPIO 固定输入 / 移位寄存器控制脚** | 软件轮询 |

### 10.4 校验：所有 DMA stream 占用一览

| DMA | S0 | S1 | S2 | S3 | S4 | S5 | S6 | S7 |
| --- | -- | -- | -- | -- | -- | -- | -- | -- |
| 1   | -  | -  | UART4_RX C4 | -  | UART4_TX C4 | USART2_RX C4 | USART2_TX C4 | -  |
| 2   | SPI1_RX C3 | TIM1_CH1 C6 | USART6_RX C5 | SPI1_TX C3 | -  | USART1_RX C4 | USART6_TX C5 | USART1_TX C4 |

✅ 无 stream 重复使用。

---

## 11. 中断优先级建议

| 模块 | preempt | sub | 备注 |
| ---- | ------- | --- | ---- |
| Uplink USART2 | 2 | 2 | 主协议优先 |
| RS485-x | 2 | 0 | 与 V2.0 同 |
| CAN1/CAN2 | 2 | 0 | |
| DMA TC | 2 | 0/2 | 跟随挂载外设 |

---

## 12. 与 V2.0 的差异速查

| 项 | V2.0 | V2.1 |
| --- | ---- | ---- |
| 移位寄存器控制脚 | PC0/PC1/PC2 | PB10/PB11/PB12 |
| F1 / F2 | PC13 / PC14 | PB2 / PB1 |
| 24V 检测 | 无 | PB0 |
| 主协议 USART | USART2(PA2/PA3) | 同 |
| 485 数量 | 4 (USART1/4/5/6) | 3 (USART1/4/6) |
| CAN 数量 | 1 | 2（新增 CAN2 PB6/PB5） |
| 输入数量 | 32 | 24 个移位输入 + 3 个固定输入 = 27 |
| 输出数量 | 32 | 20 |
| 灯带 | 无 | TIM1_CH1=PA8 + DMA2 S1 C6 |
| LED | 无 | PB4 绿 + PB3 黄（GPIO 低有效） |
| 蜂鸣器 | 无 | TIM2_CH1=PA15 |
| 板载 FLASH | 无 | W25Q16JV，SPI1（PA5/6/7 + PC4 CS） |
| KEY1 | 无 | PC13 |
