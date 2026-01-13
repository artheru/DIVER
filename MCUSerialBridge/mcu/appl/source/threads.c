#include "appl/threads.h"

#include "appl/control.h"
#include "appl/packet.h"
#include "appl/upload.h"
#include "bsp/bsp.h"
#include "util/console.h"

#define USART_RX_BUFFER 1536

USARTHandle uplink_usart;

void init_threads()
{
    bsp_uplink_config.rx_buffer_size = USART_RX_BUFFER;
    uplink_usart = hal_usart_register(bsp_uplink_config);

    hal_usart_register_receive(uplink_usart, packet_parse, 0, 0);
}
