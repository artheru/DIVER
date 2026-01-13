#include "appl/control.h"

#include "appl/packet.h"
#include "appl/upload.h"
#include "bsp/digital_io.h"
#include "bsp/ports.h"
#include "common.h"
#include "hal/nvic.h"
#include "util/console.h"

typedef struct {
    union {
        USARTHandle serial;
        DirectCANHandle can;
    };
    PortTypeC type;
    bool valid;
    uint32_t pending_sequence;  // for serial can use other
    bool pending;               // for serial, can use other
} Handles;
Handles g_handles[PACKET_MAX_PORTS_NUM];

volatile uint32_t g_inputs;
volatile uint32_t g_outputs;

volatile MCUStateC g_mcu_state = MCUState_Bridge_Idle;

static void control_on_serial_complete(uint32_t port_index)
{
    if (port_index >= PACKET_MAX_PORTS_NUM) {
        return;
    }

    // console_printf_do(
    //         "Write: Pending write for serial %d is completed!\n",
    //         port_index);

    if (g_handles[port_index].pending) {
        PayloadHeader response_payload_header =
                {.command = 0x80 | CommandWritePort,
                 .error_code = 0,
                 .sequence = g_handles[port_index].pending_sequence};
        packet_send(&response_payload_header, NULL, 0);

        g_handles[port_index].pending = 0;
        g_handles[port_index].pending_sequence = 0;
    }
}

static void control_on_can_complete(
        bool success,
        uint32_t port_index,
        uint32_t sequence)
{
    if (port_index >= PACKET_MAX_PORTS_NUM) {
        return;
    }

    // if (success) {
    //     console_printf_do(
    //             "Write: Pending write for CAN %d is completed!\n",
    //             port_index);
    // } else {
    //     console_printf_do(
    //             "Write: Pending write for CAN %d is aborted!\n", port_index);
    // }

    PayloadHeader response_payload_header =
            {.command = 0x80 | CommandWritePort,
             .error_code = success ? 0 : MSB_Error_CAN_SendFail,
             .sequence = sequence};
    packet_send(&response_payload_header, NULL, 0);
}

MCUSerialBridgeError control_on_configure(
        const uint8_t* data,
        uint32_t data_length)
{
    if (!data)
        return MSB_Error_Proto_InvalidPayload;

    // --------------------------------
    // 已经配置过，拒绝重复配置
    // --------------------------------
    if (g_mcu_state != MCUState_Bridge_Idle) {
        return MSB_Error_State_Running;
    }

    // --------------------------------
    // 至少要有 port_num
    // --------------------------------
    if (data_length < sizeof(uint32_t)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // --------------------------------
    // unpack port_num
    // --------------------------------
    uint32_t port_num = 0;
    memcpy(&port_num, data, sizeof(uint32_t));

    // 合理性检查（根据你 MCU 实际能力）
    if (port_num > ports_total_num) {
        return MSB_Error_Config_PortNumOver;
    }

    // --------------------------------
    // 检查后续数据长度是否足够
    // --------------------------------
    uint32_t expect_len = sizeof(uint32_t) + port_num * sizeof(PortConfigC);
    if (data_length < expect_len) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // --------------------------------
    // 逐端口解析配置
    // --------------------------------
    uint32_t serial_count = 0;
    uint32_t can_count = 0;
    const PortConfigC* ports = (const PortConfigC*)(data + sizeof(uint32_t));

    // --------------------------------
    // Check first
    // --------------------------------
    for (uint32_t i = 0; i < port_num; i++) {
        switch (ports[i].port_type) {
        case PortType_Serial:
            if (++serial_count > ports_serial_num) {
                return MSB_Error_Config_SerialNumOver;
            }
            break;
        case PortType_CAN:
            if (++can_count > ports_can_num) {
                return MSB_Error_Config_CANNumOver;
            }
            break;
        default:
            return MSB_Error_Config_UnknownPortType;
            break;
        }
    }

    serial_count = 0;
    can_count = 0;
    memset(g_handles, 0, sizeof(g_handles));

    for (uint32_t port_index = 0; port_index < port_num; port_index++) {
        g_handles[port_index].type = ports[port_index].port_type;
        switch (ports[port_index].port_type) {
        case PortType_Serial:
            const SerialPortConfigC* serial_config =
                    (const SerialPortConfigC*)&ports[port_index];
            bsp_serial_configs[serial_count].baud_rate = serial_config->baud;
            g_handles[port_index].serial =
                    hal_usart_register(bsp_serial_configs[serial_count]);
            hal_usart_register_receive(
                    g_handles[port_index].serial,
                    (DataReceiveCallback)upload_serial_packet,
                    serial_config->receive_frame_ms,
                    1,
                    port_index);
            ++serial_count;
            break;
        case PortType_CAN:
            const CANPortConfigC* can_config =
                    (const CANPortConfigC*)&ports[port_index];
            bsp_can_configs[can_count].baud_rate = can_config->baud;
            g_handles[port_index].can =
                    hal_direct_can_register(bsp_can_configs[can_count]);
            hal_direct_can_register_receive(
                    g_handles[port_index].can,
                    (DirectCANMessageReceiveCallback)upload_can_packet,
                    1,
                    port_index);
            ++can_count;
            break;
        }
        g_handles[port_index].valid = true;
    }

    // --------------------------------
    // 标记配置完成
    // --------------------------------
    g_mcu_state = MCUState_Bridge_Running;

    return MSB_Error_OK;
}

MCUSerialBridgeError control_on_reset(const uint8_t* data, uint32_t data_length)
{
    console_printf_do("CONTROL: RESETTING MCU!\n");
    async_timeout(200, hal_nvic_reset, 0);
    return 0;
}

MCUSerialBridgeError control_on_write_port(
        const uint8_t* data,
        uint32_t data_length,
        uint32_t sequence,
        uint8_t* async)
{
    console_printf_do("CONTROL: WRITE PORT, seq = %d\n", sequence);
    *async = false;

    if (g_mcu_state != MCUState_Bridge_Running) {
        return MSB_Error_State_NotRunning;
    }

    if (data_length < sizeof(DataPacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    const DataPacket* pkt = (const DataPacket*)data;
    if (pkt->data_len != data_length - sizeof(DataPacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    int8_t port_index = pkt->port_index;

    if (port_index < 0 || port_index > PACKET_MAX_PORTS_NUM ||
        !g_handles[port_index].valid) {
        return MSB_Error_Config_PortNumOver;
    }

    if (g_handles[port_index].pending) {
        return MSB_Error_Port_WriteBusy;
    }

    switch (g_handles[port_index].type) {
    case PortType_Serial:
        // console_printf_do("SEND VIA SERIAL %d\n", port_index);
        // console_print_buffer_do(pkt->data, pkt->data_len);
        if (!g_handles[port_index].pending) {
            g_handles[port_index].pending_sequence = sequence;
            g_handles[port_index].pending = true;
            hal_usart_send(
                    g_handles[port_index].serial,
                    pkt->data,
                    pkt->data_len,
                    (AsyncCallback)control_on_serial_complete,
                    1,
                    port_index);
        } else {
            return MSB_Error_Serial_WriteFail;
        }

        *async = 1;
        return MSB_Error_OK;
        break;

    case PortType_CAN:
        // console_printf_do("SEND VIA CAN %d\n", port_index);
        // console_print_buffer_do(pkt->data, pkt->data_len);
        if (pkt->data_len < sizeof(CANData) - 8) {
            return MSB_Error_CAN_DataError;
        }

        const CANData* can_data = (const CANData*)pkt->data;
        CANIDInfo info;
        memcpy(&info, &can_data->info, sizeof(info));

        // console_printf_do(
        //         "CAN DATA = dlc %u, rtr %u, id %u, pkt_datalen %u\n",
        //         info.dlc,
        //         info.rtr,
        //         info.id,
        //         pkt->data_len);

        if ((info.dlc > 8) ||
            (pkt->data_len < sizeof(CANData) - 8 + info.dlc)) {
            return MSB_Error_CAN_DataError;
        }

        bool send_enqueue_ok = hal_direct_can_send(
                g_handles[port_index].can,
                info,
                can_data->data,
                (DirectCANMessageSendCompleteCallback)control_on_can_complete,
                2,
                port_index,
                sequence);

        if (send_enqueue_ok) {
            *async = 1;
            return MSB_Error_OK;
        } else {
            return MSB_Error_CAN_BufferFull;
        }

        break;

    default:
        break;
    }

    return MSB_Error_MCU_Unknown;
}

MCUSerialBridgeError control_on_write_output(
        const uint8_t* data,
        uint32_t data_length)
{
    if (data_length < 4 || !data) {
        return MSB_Error_MCU_IOSizeError;
    }

    uint32_t output = *(uint32_t*)data;
    console_printf_do("CONTROL: WRITE OUTPUT, value = [0x%08X]\n", output);
    bsp_set_outputs(output);

    return MSB_Error_OK;
}
