#include "appl/upload.h"

#include "appl/control.h"
#include "appl/packet.h"
#include "hal/dcan.h"
#include "msb_protocol.h"
#include "util/console.h"
#include "util/mempool.h"

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
#include "mcu_runtime.h"
#endif

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

CCM_RAM uint8_t upload_console_writeline_buffer[PACKET_MAX_DATALEN];
CCM_RAM uint32_t  upload_console_writeline_buffer_length = 0;

void upload_serial_packet(
        const void* data,
        uint32_t length,
        uint32_t port_index)
{
    console_printf_do(
            "RECEIVED PACKET FROM SERIAL %u, len %u\n", port_index, length);
    
    // 累加 RX 统计
    control_stats_add_rx(port_index, length);

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
    // DIVER 模式下，将串口数据传递给 DIVER 运行时处理
    if (g_mcu_state.mode == MCU_Mode_DIVER &&
        g_mcu_state.running_state == MCU_RunState_Running) {
        vm_put_stream_buffer((int)port_index, (uchar*)data, (int)length);
    }
#endif

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
    
    // 累加 RX 统计（CAN 帧大小 = header(2) + payload(dlc)）
    uint8_t dlc = id_info.dlc > 8 ? 8 : id_info.dlc;
    control_stats_add_rx(port_index, 2 + dlc);

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
    // DIVER 模式下，将 CAN 数据传递给 DIVER 运行时处理
    if (g_mcu_state.mode == MCU_Mode_DIVER &&
        g_mcu_state.running_state == MCU_RunState_Running) {
        CANData can_msg;
        can_msg.info = *(uint16_t*)&id_info;
        memcpy(can_msg.data, &data_0_3, 4);
        memcpy(can_msg.data + 4, &data_4_7, 4);
        
        uint8_t dlc = id_info.dlc > 8 ? 8 : id_info.dlc;
        vm_put_event_buffer(
                (int)port_index,
                (int)id_info.id,  // eventID = CAN Standard ID
                (uchar*)&can_msg,
                (int)(sizeof(can_msg.info) + dlc));  // size = info(2) + payload(dlc)
    }
#endif

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

void upload_console_writeline()
{
    if (upload_console_writeline_buffer_length == 0) {
        return;
    }

    PayloadHeader header = {
            .command = CommandUploadConsoleWriteLine,
            .sequence = 0,
            .error_code = 0,
            .timestamp_ms = 0,
    };

    if (upload_console_writeline_buffer_length > PACKET_MAX_DATALEN) {
        upload_console_writeline_buffer_length = PACKET_MAX_DATALEN;
    }

    // Directly send upload console writeline buffer to PC
    packet_send(
            &header,
            upload_console_writeline_buffer,
            upload_console_writeline_buffer_length);

    // After sending, clear the buffer by setting the length to 0
    upload_console_writeline_buffer_length = 0;
}

uint32_t upload_console_writeline_append(const void* data, uint32_t length)
{
    if (length == 0 || data == NULL) {
        return 0;
    }

    uint32_t max_append =
            PACKET_MAX_DATALEN - upload_console_writeline_buffer_length;
    if (max_append <= 1) {
        return 0;
    }
    if (length >= max_append) {
        length = max_append - 1;
    }
    memcpy(upload_console_writeline_buffer +
                   upload_console_writeline_buffer_length,
           data,
           length);
    upload_console_writeline_buffer_length += length;
    upload_console_writeline_buffer[upload_console_writeline_buffer_length] =
            '\n';
    upload_console_writeline_buffer_length++;
    return length;
}
