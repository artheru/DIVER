#include "appl/upload.h"

#include "appl/control.h"
#include "appl/packet.h"
#include "hal/dcan.h"
#include "hal/usart.h"
#include "msb_protocol.h"
#include "util/console.h"
#include "util/crc.h"

/**
 * @brief 检查是否应该上报端口数据到 PC
 *
 * - Bridge 模式：总是上报
 * - DIVER 模式：仅当 wire_tap 启用时上报
 *
 * @return true 需要上报, false 不上报
 */
static inline bool should_upload_port_data(void)
{
    // Bridge 模式：总是上报
    if (g_mcu_state.mode == MCU_Mode_Bridge) {
        return true;
    }
    // DIVER 模式：仅当 wire_tap 启用时上报
    return g_wire_tap_enabled;
}

void upload_serial_packet(
        const void* data,
        uint32_t length,
        uint32_t port_index)
{
    console_printf_do(
            "RECEIVED PACKET FROM SERIAL %u, len %u\n", port_index, length);

    // TODO: DIVER 模式下，将数据传递给 DIVER 运行时处理
    // if (g_mcu_state.mode == MCU_Mode_DIVER) {
    //     diver_on_serial_data(port_index, data, length);
    // }

    // 检查是否应该上报到 PC
    if (!should_upload_port_data()) {
        return;
    }

    PayloadHeader header = {
            .command = CommandUploadPort,
            .sequence = 0,
            .error_code = 0,
            .timestamp_ms = 0,
    };

    if (length > PACKET_MAX_DATALEN) {
        length = PACKET_MAX_DATALEN;
    }

    uint8_t other_data[PACKET_MAX_PAYLOAD_LEN];
    DataPacket* pkt = (void*)other_data;
    pkt->port_index = port_index;
    pkt->data_len = length;
    memcpy(pkt->data, data, length);

    packet_send(&header, other_data, sizeof(DataPacket) + length);
}

void upload_can_packet(
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7,
        uint32_t port_index)
{
    console_printf_do("RECEIVED PACKET FROM CAN %d\n", port_index);

    // TODO: DIVER 模式下，将数据传递给 DIVER 运行时处理
    // if (g_mcu_state.mode == MCU_Mode_DIVER) {
    //     diver_on_can_data(port_index, id_info, data_0_3, data_4_7);
    // }

    // 检查是否应该上报到 PC
    if (!should_upload_port_data()) {
        return;
    }

    PayloadHeader header = {
            .command = CommandUploadPort,
            .sequence = 0,
            .error_code = 0,
            .timestamp_ms = 0,
    };

    // console_printf_do(
    //         "ID %u DLC %u RTR %u\n", id_info.id, id_info.dlc, id_info.rtr);

    if (id_info.dlc > 8) {
        id_info.dlc = 8;
    }

    uint8_t other_data[sizeof(CANData) + sizeof(DataPacket)];
    DataPacket* pkt = (void*)other_data;

    pkt->port_index = port_index;
    pkt->data_len = sizeof(CANData) - 8 + id_info.dlc;
    CANData* can_data = (CANData*)pkt->data;
    memcpy(&can_data->info, &id_info, 2);
    memcpy(can_data->data, &data_0_3, 4);
    memcpy(can_data->data + 4, &data_4_7, 4);

    packet_send(&header, other_data, sizeof(other_data) - 8 + id_info.dlc);
}