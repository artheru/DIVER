#include "bsp/uplink.h"


USARTConfig bsp_uplink_config = {
        .usage = "Uplink",

        .usart_no = 4,
        .baud_rate = 1000000,

        .tx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'A',
                        .pin = 0,
                },
        .rx_gpio =
                {
                        .mode = GPIOMode_AlternativeFunction,
                        .port = 'A',
                        .pin = 1,
                },
        .de_gpio =
                {
                        .mode = GPIOMode_Disabled,
                },

        .tx_dma =
                {
                        .mode = DMAMode_Mem2Periph,
                        .controller = 1,
                        .stream = 4,
                        .channel = 4,
                        .priority = 1,
                        .data_width = DMADataWidth_8,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 1,
                },

        .rx_dma =
                {
                        .mode = DMAMode_Periph2Mem,
                        .controller = 1,
                        .stream = 2,
                        .channel = 4,
                        .priority = 1,
                        .data_width = DMADataWidth_8,
                        .tc_irq_priority = 2,
                        .tc_irq_sub_priority = 1,
                },

        .tx_buffer_size = 0,
        .rx_buffer_size = 2048,
        .irq_priority = 2,
        .irq_sub_priority = 1,
};
