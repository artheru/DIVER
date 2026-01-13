#include "bsp/ports.h"

const uint32_t ports_can_num = 1;
const uint32_t ports_serial_num = 4;

DirectCANConfig bsp_can_configs[1] = {
        {
                .can_no = 1,
                .baud_rate = 500000,
                .loopback = 0,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 9,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 8,
                        },
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .tx_buffer_size = 32,
                .usage = "CAN Bus 1",
        },
};

USARTConfig bsp_serial_configs[4] = {
        // RS485-1 (UART1)
        {
                .usart_no = 1,
                .baud_rate = 115200,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'A',
                                .pin = 9,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'A',
                                .pin = 10,
                        },
                .de_gpio =
                        {
                                .mode = GPIOMode_Output,
                                .port = 'C',
                                .pin = 8,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 7,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 2,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-1",
        },

        // RS485-2 (UART5)
        {
                .usart_no = 5,
                .baud_rate = 115200,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'C',
                                .pin = 12,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'D',
                                .pin = 2,
                        },
                .de_gpio =
                        {
                                .mode = GPIOMode_Output,
                                .port = 'A',
                                .pin = 12,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 1,
                                .stream = 7,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 1,
                                .stream = 0,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-2",
        },

        // RS485-3 (UART6)
        {
                .usart_no = 6,
                .baud_rate = 115200,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'C',
                                .pin = 6,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'C',
                                .pin = 7,
                        },
                .de_gpio =
                        {
                                .mode = GPIOMode_Output,
                                .port = 'C',
                                .pin = 9,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 6,
                                .channel = 5,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 1,
                                .channel = 5,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-3",
        },

        // RS485-4 (UART4)
        {
                .usart_no = 4,
                .baud_rate = 115200,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'C',
                                .pin = 10,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'C',
                                .pin = 11,
                        },
                .de_gpio =
                        {
                                .mode = GPIOMode_Output,
                                .port = 'A',
                                .pin = 11,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 1,
                                .stream = 4,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 1,
                                .stream = 2,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-4",
        },
};
