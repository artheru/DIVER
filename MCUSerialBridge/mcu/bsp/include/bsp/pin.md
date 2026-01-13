### **RS-485 Functional Blocks**

#### **RS-485 <1>**

| MCU Pin | UART  | Direction / Function   | RS-485 Signal           |
| ------- | ----- | ---------------------- | ----------------------- |
| PA9     | U1_TX | UART1 Transmit         | 485 Data Input <1>      |
| PA10    | U1_RX | UART1 Receive          | 485 Receiver Output <1> |
| PC8     | –     | RS-485 Receiver Enable | 485 Receiver Enable <1> |

#### **RS-485 <2>**

| MCU Pin | UART  | Direction / Function   | RS-485 Signal           |
| ------- | ----- | ---------------------- | ----------------------- |
| PC12    | U5_TX | UART5 Transmit         | 485 Data Input <2>      |
| PD2     | U5_RX | UART5 Receive          | 485 Receiver Output <2> |
| PA12    | –     | RS-485 Receiver Enable | 485 Receiver Enable <2> |

#### **RS-485 <3>**

| MCU Pin | UART  | Direction / Function   | RS-485 Signal           |
| ------- | ----- | ---------------------- | ----------------------- |
| PC6     | U6_TX | UART6 Transmit         | 485 Data Input <3>      |
| PC7     | U6_RX | UART6 Receive          | 485 Receiver Output <3> |
| PC9     | –     | RS-485 Receiver Enable | 485 Receiver Enable <3> |

#### **RS-485 <4>**

| MCU Pin | UART  | Direction / Function   | RS-485 Signal           |
| ------- | ----- | ---------------------- | ----------------------- |
| PC10    | U4_TX | UART4 Transmit         | 485 Data Input <4>      |
| PC11    | U4_RX | UART4 Receive          | 485 Receiver Output <4> |
| PA11    | –     | RS-485 Receiver Enable | 485 Receiver Enable <4> |

### **CAN Functional Blocks**

#### **CAN <1>**

| MCU Pin | CAN Bus | Direction / Function |
| ------- | ------- | -------------------- |
| PB8     | CAN1    | CAN1 Receive (RX)    |
| PB9     | CAN1    | CAN1 Transmit (TX)   |

#### **CAN <2>**

| MCU Pin | CAN Bus | Direction / Function |
| ------- | ------- | -------------------- |
| PB12    | CAN2    | CAN2 Receive (RX)    |
| PB6     | CAN2    | CAN2 Transmit (TX)   |

### **DMA Usage**

| DMA  | Stream  | Channel   | Usage     |
| ---- | ------- | --------- | --------- |
| DMA1 | Stream0 | Channel 4 | UART5_RX  |
| DMA1 | Stream1 | Channel 4 | USART3_RX |
| DMA1 | Stream2 | Channel 4 | UART4_RX  |
| DMA1 | Stream3 | Channel 4 | USART3_TX |
| DMA1 | Stream4 | Channel 4 | UART4_TX  |
| DMA1 | Stream5 | Channel 4 | USART2_RX |
| DMA1 | Stream6 | Channel 4 | USART2_TX |
| DMA1 | Stream7 | Channel 4 | UART5_TX  |
| DMA2 | Stream0 | Channel 3 | SPI1_RX   |
| DMA2 | Stream1 | Channel 5 | USART6_RX |
| DMA2 | Stream2 | Channel 4 | USART1_RX |
| DMA2 | Stream3 | Channel 6 | TIM1_CH1  |
| DMA2 | Stream4 | —         | Not used  |
| DMA2 | Stream5 | Channel 3 | SPI1_TX   |
| DMA2 | Stream6 | Channel 5 | USART6_TX |
| DMA2 | Stream7 | Channel 4 | USART1_TX |
