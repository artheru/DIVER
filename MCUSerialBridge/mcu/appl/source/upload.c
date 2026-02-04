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

/* ===============================
 * 内部辅助函数
 * =============================== */

/**
 * @brief 检查是否应该上报 RX 数据到 PC
 * @param port_index 端口索引
 * @return true 需要上报, false 不上报
 */
static inline bool should_upload_rx(uint32_t port_index)
{
    // Bridge 模式：总是上报
    if (g_mcu_state.mode == MCU_Mode_Bridge) {
        return true;
    }
    // DIVER 模式：检查该端口的 RX WireTap 标志
    if (port_index < PACKET_MAX_PORTS_NUM) {
        return WIRETAP_RX_ENABLED(port_index);
    }
    return false;
}

/**
 * @brief 检查是否应该上报 TX 数据到 PC
 * @param port_index 端口索引
 * @return true 需要上报, false 不上报
 */
static inline bool should_upload_tx(uint32_t port_index)
{
    // Bridge 模式：不上报 TX（只有 DIVER 模式有 TX WireTap）
    if (g_mcu_state.mode == MCU_Mode_Bridge) {
        return false;
    }
    // DIVER 模式：检查该端口的 TX WireTap 标志
    if (port_index < PACKET_MAX_PORTS_NUM) {
        return WIRETAP_TX_ENABLED(port_index);
    }
    return false;
}

/**
 * @brief 上报端口数据到 PC（内部函数）
 * @param port_index_with_dir 端口索引（bit7 = 方向：0=RX, 1=TX）
 * @param data 数据指针
 * @param length 数据长度
 */
static void upload_port_data(
        uint8_t port_index_with_dir,
        const void* data,
        uint32_t length)
{
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
    pkt->port_index = port_index_with_dir;
    pkt->data_len = length;
    memcpy(pkt->data, data, length);

    packet_send(&header, other_data, sizeof(DataPacket) + length);
}

/**
 * @brief 上报 CAN 端口数据到 PC（内部函数）
 * @param port_index_with_dir 端口索引（bit7 = 方向：0=RX, 1=TX）
 * @param id_info CAN ID 信息
 * @param data_0_3 数据字节 0-3
 * @param data_4_7 数据字节 4-7
 */
static void upload_can_port_data(
        uint8_t port_index_with_dir,
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7)
{
    PayloadHeader header = {
            .command = CommandUploadPort,
            .sequence = 0,
            .error_code = 0,
            .timestamp_ms = 0,
    };

    if (id_info.dlc > 8) {
        id_info.dlc = 8;
    }

    uint8_t other_data[sizeof(CANData) + sizeof(DataPacket)];
    DataPacket* pkt = (void*)other_data;

    pkt->port_index = port_index_with_dir;
    pkt->data_len = sizeof(CANData) - 8 + id_info.dlc;
    CANData* can_data = (CANData*)pkt->data;
    memcpy(&can_data->info, &id_info, 2);
    memcpy(can_data->data, &data_0_3, 4);
    memcpy(can_data->data + 4, &data_4_7, 4);

    packet_send(&header, other_data, sizeof(other_data) - 8 + id_info.dlc);
}

/* ===============================
 * Console 上传缓冲区
 * =============================== */

CCM_RAM uint8_t upload_console_writeline_buffer[PACKET_MAX_DATALEN];
CCM_RAM uint32_t upload_console_writeline_buffer_length;

void init_upload(void)
{
    // This value is in CCMRAM area
    // Which is not initialized by default
    console_printf_do("[init_upload] BEFORE: addr=0x%08X, val=%u\n",
                      (uint32_t)&upload_console_writeline_buffer_length,
                      upload_console_writeline_buffer_length);
    
    // 直接写入
    upload_console_writeline_buffer_length = 0;
    
    memset(upload_console_writeline_buffer, 0, sizeof(upload_console_writeline_buffer));
}

/* ===============================
 * HAL 接收回调
 * =============================== */

void on_hal_receive_serial(
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

    // 检查是否应该上报 RX 到 PC
    if (!should_upload_rx(port_index)) {
        return;
    }

    // 上报 RX 数据（port_index 不带 0x80 标志）
    upload_port_data((uint8_t)port_index, data, length);
}

void on_hal_receive_can(
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
        memcpy(&can_msg.info, &id_info, sizeof(can_msg.info));
        memcpy(can_msg.data, &data_0_3, 4);
        memcpy(can_msg.data + 4, &data_4_7, 4);
        
        uint8_t dlc_clamped = id_info.dlc > 8 ? 8 : id_info.dlc;
        vm_put_event_buffer(
                (int)port_index,
                (int)id_info.id,  // eventID = CAN Standard ID
                (uchar*)&can_msg,
                (int)(sizeof(can_msg.info) + dlc_clamped));  // size = info(2) + payload(dlc)
    }
#endif

    // 检查是否应该上报 RX 到 PC
    if (!should_upload_rx(port_index)) {
        return;
    }

    // 上报 RX 数据（port_index 不带 0x80 标志）
    upload_can_port_data((uint8_t)port_index, id_info, data_0_3, data_4_7);
}

/* ===============================
 * WireTap TX 上报
 * =============================== */

void report_wiretap_transmit_serial(
        const void* data,
        uint32_t length,
        uint32_t port_index)
{
    // 检查是否应该上报 TX 到 PC
    if (!should_upload_tx(port_index)) {
        return;
    }

    console_printf_do("WIRETAP TX SERIAL %u, len %u\n", port_index, length);

    // 上报 TX 数据（port_index | 0x80 表示 TX 方向）
    upload_port_data((uint8_t)(port_index | PORT_DIRECTION_TX_MASK), data, length);
}

void report_wiretap_transmit_can(
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7,
        uint32_t port_index)
{
    // 检查是否应该上报 TX 到 PC
    if (!should_upload_tx(port_index)) {
        return;
    }

    console_printf_do("WIRETAP TX CAN %d\n", port_index);

    // 上报 TX 数据（port_index | 0x80 表示 TX 方向）
    upload_can_port_data((uint8_t)(port_index | PORT_DIRECTION_TX_MASK), 
                         id_info, data_0_3, data_4_7);
}

/* ===============================
 * Console 日志上传
 * =============================== */

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
    console_printf_do("[upload_append] enter: len=%u, buf_len=%u, max=%u\n",
                      length,
                      upload_console_writeline_buffer_length,
                      PACKET_MAX_DATALEN);

    if (length == 0 || data == NULL) {
        console_printf_do("[upload_append] exit: null/zero input\n");
        return 0;
    }

    // 防御性检查：如果 buffer_length 已经超过 max，说明数据损坏
    if (upload_console_writeline_buffer_length >= PACKET_MAX_DATALEN) {
        console_printf_do("[upload_append] ERROR: buf_len(%u) >= max(%u)!\n",
                          upload_console_writeline_buffer_length,
                          PACKET_MAX_DATALEN);
        upload_console_writeline_buffer_length = 0;  // 重置
        return 0;
    }

    uint32_t max_append =
            PACKET_MAX_DATALEN - upload_console_writeline_buffer_length;
    console_printf_do("[upload_append] max_append=%u\n", max_append);

    if (max_append <= 1) {
        console_printf_do("[upload_append] exit: buffer full\n");
        return 0;
    }
    if (length >= max_append) {
        length = max_append - 1;
    }

    console_printf_do("[upload_append] memcpy dst=0x%08X, src=0x%08X, len=%u\n",
                      (uint32_t)(upload_console_writeline_buffer +
                                 upload_console_writeline_buffer_length),
                      (uint32_t)data,
                      length);

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
