#include "bsp/ports.h"
#include "bsp/digital_io.h"
#include <string.h>

/* CORAL-NODE-V2.1 端口表
 *  CAN  : CAN1 (PB9/PB8)  / CAN2 (PB6/PB5)
 *  RS485: 485-1 USART6 / 485-2 USART1 / 485-3 UART4
 * DMA 分配见 sch.md §10
 */

const uint32_t ports_can_num = 2;
const uint32_t ports_serial_num = 3;

DirectCANConfig bsp_can_configs[2] = {
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
        {
                .can_no = 2,
                .baud_rate = 500000,
                .loopback = 0,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 6,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 5,
                        },
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .tx_buffer_size = 32,
                .usage = "CAN Bus 2",
        },
};

USARTConfig bsp_serial_configs[3] = {
        /* RS485-1 : USART6, PC6/PC7, DE=PC8
         * DMA2 S6 C5 (TX) / S2 C5 (RX)
         */
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
                                .pin = 8,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 6,
                                .channel = 5,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 2,
                                .channel = 5,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-1",
        },

        /* RS485-2 : USART1, PA9/PA10, DE=PA11
         * DMA2 S7 C4 (TX) / S5 C4 (RX)
         */
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
                                .port = 'A',
                                .pin = 11,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 7,
                                .channel = 4,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 5,
                                .channel = 4,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-2",
        },

        /* RS485-3 : UART4, PC10/PC11, DE=PC12
         * DMA1 S4 C4 (TX) / S2 C4 (RX)
         */
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
                                .port = 'C',
                                .pin = 12,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 1,
                                .stream = 4,
                                .channel = 4,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 1,
                                .stream = 2,
                                .channel = 4,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 0,
                        },
                .rx_buffer_size = 256,
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .usage = "RS485-3",
        },
};

void bsp_get_layout(LayoutInfoC* layout)
{
    if (!layout) return;

    memset(layout, 0, sizeof(LayoutInfoC));

    layout->digital_input_count = (i8)bsp_get_digital_input_count();
    layout->digital_output_count = (i8)bsp_get_digital_output_count();

    uint32_t total_ports = ports_serial_num + ports_can_num;
    if (total_ports > PACKET_MAX_PORTS_NUM) {
        total_ports = PACKET_MAX_PORTS_NUM;
    }
    layout->port_count = (i8)total_ports;

    for (uint32_t i = 0; i < ports_serial_num && i < PACKET_MAX_PORTS_NUM; i++) {
        layout->ports[i].port_type = PortType_Serial;
        strncpy(layout->ports[i].name, bsp_serial_configs[i].usage,
                sizeof(layout->ports[i].name) - 1);
        layout->ports[i].name[sizeof(layout->ports[i].name) - 1] = '\0';
    }

    for (uint32_t i = 0; i < ports_can_num && (ports_serial_num + i) < PACKET_MAX_PORTS_NUM; i++) {
        uint32_t idx = ports_serial_num + i;
        layout->ports[idx].port_type = PortType_CAN;
        strncpy(layout->ports[idx].name, bsp_can_configs[i].usage,
                sizeof(layout->ports[idx].name) - 1);
        layout->ports[idx].name[sizeof(layout->ports[idx].name) - 1] = '\0';
    }
}
