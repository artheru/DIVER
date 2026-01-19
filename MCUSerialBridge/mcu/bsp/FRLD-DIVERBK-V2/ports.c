#include "bsp/ports.h"
#include "bsp/digital_io.h"
#include <string.h>

const uint32_t ports_can_num = 2;
const uint32_t ports_serial_num = 4;

DirectCANConfig bsp_can_configs[2] = {
        {
                .can_no = 1,
                .baud_rate = 500000,
                .loopback = 0,
                .retry_time_ms = 20,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'D',
                                .pin = 1,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'D',
                                .pin = 0,
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
                .retry_time_ms = 20,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 13,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'B',
                                .pin = 12,
                        },
                .irq_priority = 2,
                .irq_sub_priority = 0,
                .tx_buffer_size = 32,
                .usage = "CAN Bus 2",
        },
};

USARTConfig bsp_serial_configs[4] = {
        // RS485-1 (UART3)
        {
                .usart_no = 3,
                .baud_rate = 1200,
                .tx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'D',
                                .pin = 8,
                        },
                .rx_gpio =
                        {
                                .mode = GPIOMode_AlternativeFunction,
                                .port = 'D',
                                .pin = 9,
                        },
                .de_gpio =
                        {
                                .mode = GPIOMode_Output,
                                .port = 'A',
                                .pin = 7,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 1,
                                .stream = 3,
                                .channel = 4,
                                .priority = 1,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 1,
                                .stream = 1,
                                .channel = 4,
                                .priority = 1,
                                .data_width = DMADataWidth_8,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_buffer_size = 1024,
                .irq_priority = 2,
                .irq_sub_priority = 2,
                .usage = "RS485-1",
        },

        // RS485-2 (UART6)
        {
                .usart_no = 6,
                .baud_rate = 1200,
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
                                .port = 'A',
                                .pin = 6,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 6,
                                .channel = 5,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 1,
                                .channel = 5,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_buffer_size = 1024,
                .irq_priority = 2,
                .irq_sub_priority = 2,
                .usage = "RS485-2",
        },

        // RS485-3 (UART1)
        {
                .usart_no = 1,
                .baud_rate = 1200,
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
                                .pin = 8,
                        },
                .tx_dma =
                        {
                                .mode = DMAMode_Mem2Periph,
                                .controller = 2,
                                .stream = 7,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 2,
                                .stream = 2,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_buffer_size = 1024,
                .irq_priority = 2,
                .irq_sub_priority = 2,
                .usage = "RS485-3",
        },

        // RS232-1 (UART2)
        {
                .usart_no = 2,
                .baud_rate = 1200,
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
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_dma =
                        {
                                .mode = DMAMode_Periph2Mem,
                                .controller = 1,
                                .stream = 5,
                                .channel = 4,
                                .tc_irq_priority = 2,
                                .tc_irq_sub_priority = 2,
                        },
                .rx_buffer_size = 1024,
                .irq_priority = 2,
                .irq_sub_priority = 2,
                .usage = "RS232-1",
        },
};

void bsp_get_layout(LayoutInfoC* layout)
{
    if (!layout) return;

    // 清空结构体
    memset(layout, 0, sizeof(LayoutInfoC));

    // 数字 IO 数量
    layout->digital_input_count = (i8)bsp_get_digital_input_count();
    layout->digital_output_count = (i8)bsp_get_digital_output_count();

    // 端口数量（串口在前，CAN 在后）
    uint32_t total_ports = ports_serial_num + ports_can_num;
    if (total_ports > PACKET_MAX_PORTS_NUM) {
        total_ports = PACKET_MAX_PORTS_NUM;
    }
    layout->port_count = (i8)total_ports;

    // 填充串口端口信息
    for (uint32_t i = 0; i < ports_serial_num && i < PACKET_MAX_PORTS_NUM; i++) {
        layout->ports[i].port_type = PortType_Serial;
        strncpy(layout->ports[i].name, bsp_serial_configs[i].usage, 
                sizeof(layout->ports[i].name) - 1);
        layout->ports[i].name[sizeof(layout->ports[i].name) - 1] = '\0';
    }

    // 填充 CAN 端口信息
    for (uint32_t i = 0; i < ports_can_num && (ports_serial_num + i) < PACKET_MAX_PORTS_NUM; i++) {
        uint32_t idx = ports_serial_num + i;
        layout->ports[idx].port_type = PortType_CAN;
        strncpy(layout->ports[idx].name, bsp_can_configs[i].usage,
                sizeof(layout->ports[idx].name) - 1);
        layout->ports[idx].name[sizeof(layout->ports[idx].name) - 1] = '\0';
    }
}
