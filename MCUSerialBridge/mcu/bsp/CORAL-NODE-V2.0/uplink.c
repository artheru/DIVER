#include "bsp/uplink.h"


USARTConfig bsp_uplink_config = {
        .usage = "UplinkHarness",

        .usart_no = 2,
        .baud_rate = 1000000,

        .tx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'A',
                        .pin = 2,
                },
        .rx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'A',
                        .pin = 3,
                },
        .de_gpio =
                {
                        .mode = GPIOMode_Disabled,
                },

        .tx_dma =
                {
                        .mode = DMAMode_Mem2Periph,
                        .controller = 1,
                        .stream = 6,
                        .channel = 4,
                        .priority = 1,
                        .data_width = DMADataWidth_8,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 0,
                },

        .rx_dma =
                {
                        .mode = DMAMode_Periph2Mem,
                        .controller = 1,
                        .stream = 5,
                        .channel = 4,
                        .priority = 1,
                        .data_width = DMADataWidth_8,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 0,
                },

        .tx_buffer_size = 0,
        .rx_buffer_size = 1024,
        .irq_priority = 2,
        .irq_sub_priority = 2,
};


USARTConfig bsp_uplink_local_config = {
        .usage = "UplinkLocal",

        .usart_no = 3,
        .baud_rate = 1000000,

        .tx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'B',
                        .pin = 10,
                },
        .rx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'B',
                        .pin = 11,
                },
        .de_gpio =
                {
                        .mode = GPIOMode_Disabled,
                },

        .tx_dma =
                {
                        .mode = DMAMode_Mem2Periph,
                        .controller = 1,
                        .stream = 3,
                        .channel = 4,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 0,
                },

        .rx_dma =
                {
                        .mode = DMAMode_Periph2Mem,
                        .controller = 1,
                        .stream = 1,
                        .channel = 4,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 0,
                },

        .tx_buffer_size = 0,
        .rx_buffer_size = 1024,
        .irq_priority = 2,
        .irq_sub_priority = 2,
};
