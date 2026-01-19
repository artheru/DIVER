#include "appl/packet.h"

#include "appl/control.h"
#include "appl/threads.h"
#include "appl/version.h"
#include "bsp/digital_io.h"
#include "bsp/ports.h"
#include "hal/usart.h"
#include "msb_protocol.h"
#include "util/console.h"
#include "util/crc.h"


typedef struct {
    bool in_use;   // 是否占用
    uint16_t len;  // 实际长度
    uint8_t packet[PACKET_OFFLOAD_SIZE + PACKET_MAX_PAYLOAD_LEN];  // 包缓冲
} SendQueueItem;

#define SEND_QUEUE_SIZE 16
static SendQueueItem send_queue[SEND_QUEUE_SIZE];
static volatile uint32_t send_head = 0;  // 正在发送的索引
static volatile uint32_t send_tail = 0;  // 下一个写入索引
static volatile bool sending = false;    // 当前是否正在发送

void send_complete_callback()
{
    send_queue[send_head].in_use = false;
    send_head = (send_head + 1) % SEND_QUEUE_SIZE;
    sending = false;

    // 如果队列还有数据，继续发送
    if (send_queue[send_head].in_use) {
        sending = true;
        hal_usart_send(
                uplink_usart,
                send_queue[send_head].packet,
                send_queue[send_head].len,
                send_complete_callback,
                0);
    }
}

void packet_parse(const void* data_void, uint32_t length, ...)
{
    if (length < PACKET_MIN_VALID_LEN) {
        return;
    }

    uint32_t offset = 0;
    while (offset + PACKET_MIN_VALID_LEN <= length) {
        const uint8_t* data = (const uint8_t*)data_void + offset;

        // ------------------
        // 检查头部
        // ------------------
        if (data[0] != PACKET_HEADER_1 || data[1] != PACKET_HEADER_2) {
            offset++;
            continue;
        }

        // ------------------
        // 检查长度字段
        // ------------------
        uint8_t len_lo = data[2];
        uint8_t len_hi = data[3];
        uint8_t rev_lo = data[4];
        uint8_t rev_hi = data[5];

        if (len_lo != (uint8_t)(~rev_hi) || len_hi != (uint8_t)(~rev_lo)) {
            // 长度字段反码校验失败
            offset++;
            continue;
        }

        uint32_t payload_len = (uint32_t)len_lo + ((uint32_t)len_hi << 8);
        if (payload_len > PACKET_MAX_PAYLOAD_LEN) {
            // 超过最大负载
            offset++;
            continue;
        }

        // 整包长度检查
        if (offset + PACKET_OFFLOAD_SIZE + payload_len > length) {
            // 数据还没接全, 直接丢弃了, MCU没有那么多资源
            break;
        }

        // ------------------
        // CRC 校验
        // ------------------
        uint16_t checked_crc = crc16(data + 6, payload_len);
        uint32_t crc_offset = 6 + payload_len;
        uint16_t reported_crc = (uint16_t)data[crc_offset] |
                                ((uint16_t)data[crc_offset + 1] << 8);

        if (checked_crc != reported_crc) {
            // CRC 校验失败
            offset++;
            continue;
        }

        // ------------------
        // 尾部校验
        // ------------------
        uint32_t tail_offset = PACKET_OFFLOAD_SIZE + payload_len - 2;
        if (data[tail_offset] != PACKET_TAIL_1_2 ||
            data[tail_offset + 1] != PACKET_TAIL_1_2) {
            offset++;
            continue;
        }

        PayloadHeader* payload_header = (PayloadHeader*)(data + 6);
        uint8_t* other_data =
                (uint8_t*)(payload_header) + sizeof(PayloadHeader);
        uint32_t other_data_len = payload_len - sizeof(PayloadHeader);

        MCUSerialBridgeError ret = MSB_Error_Proto_UnknownCommand;
        uint8_t is_async_command = false;
        uint32_t read_result;
        uint8_t* return_buffer = 0;
        uint32_t return_buffer_size = 0;

        switch (payload_header->command) {
        case CommandConfigure:
            ret = control_on_configure(other_data, other_data_len);
            break;
        case CommandReset:
            ret = control_on_reset(other_data, other_data_len);
            break;
        case CommandState:
            ret = MSB_Error_OK;
            read_result = g_mcu_state.raw;
            return_buffer = (void*)&read_result;
            return_buffer_size = sizeof(read_result);
            console_printf_do(
                    "CONTROL: READ STATE, value = [0x%08X]\n", read_result);
            break;
        case CommandVersion:
            ret = MSB_Error_OK;
            return_buffer = (void*)&g_version_info;
            return_buffer_size = sizeof(g_version_info);
            break;
        case CommandEnableWireTap:
            ret = control_on_enable_wire_tap(other_data, other_data_len);
            break;
        case CommandGetLayout: {
            static LayoutInfoC layout_info;
            bsp_get_layout(&layout_info);
            ret = MSB_Error_OK;
            return_buffer = (void*)&layout_info;
            return_buffer_size = sizeof(layout_info);
            console_printf_do(
                    "CONTROL: GET LAYOUT, DI=%d, DO=%d, Ports=%d\n",
                    layout_info.digital_input_count,
                    layout_info.digital_output_count,
                    layout_info.port_count);
            break;
        }
        case CommandStart:
            ret = control_on_start(other_data, other_data_len);
            break;
        case CommandWritePort:
            ret = control_on_write_port(
                    other_data,
                    other_data_len,
                    payload_header->sequence,
                    &is_async_command);
            break;
        case CommandWriteOutput:
            ret = control_on_write_output(other_data, other_data_len);
            break;
        case CommandReadInput:
            ret = MSB_Error_OK;
            read_result = bsp_get_inputs();
            return_buffer = (void*)&read_result;
            return_buffer_size = sizeof(read_result);
            break;
        case CommandProgram:
            ret = control_on_program(other_data, other_data_len);
            break;
        case CommandMemoryUpperIO:
            ret = control_on_memory_upper_io(other_data, other_data_len);
            break;
        default:
            break;
        }

        if (!is_async_command) {
            PayloadHeader response_payload_header = *payload_header;
            response_payload_header.command |= 0x80;
            response_payload_header.error_code = ret;
            packet_send(
                    &response_payload_header,
                    return_buffer,
                    return_buffer_size);
        } else {
            // 不回复
        }

        // Move to next packet
        offset += PACKET_OFFLOAD_SIZE + payload_len;
    }
}

/**
 * @brief MCU端发送包
 *
 * @param header PayloadHeader指针
 * @param other_data 其他数据指针
 * @param other_data_len 其他数据长度
 */
void packet_send(
        const PayloadHeader* header,
        const uint8_t* other_data,
        uint32_t other_data_len)
{
    if (!header)
        return;

    if (!other_data) {
        other_data_len = 0;
    }

    uint32_t payload_len = sizeof(PayloadHeader) + other_data_len;
    if (payload_len > PACKET_MAX_PAYLOAD_LEN) {
        console_printf_do("[packet_send] payload too large %u\n", payload_len);
        return;
    }
    uint32_t total_len = payload_len + PACKET_OFFLOAD_SIZE;

    // 找空位
    SendQueueItem* item = &send_queue[send_tail];
    if (item->in_use) {
        console_printf_do("[packet_send] send queue full\n");
        return;
    }

    uint8_t* itr = item->packet;

    // ------------------
    // 包头
    // ------------------
    *itr++ = PACKET_HEADER_1;
    *itr++ = PACKET_HEADER_2;

    // ------------------
    // 长度字段
    // ------------------
    uint8_t len_lo = (uint8_t)(payload_len & 0xFF);
    uint8_t len_hi = (uint8_t)((payload_len >> 8) & 0xFF);
    *itr++ = len_lo;
    *itr++ = len_hi;
    *itr++ = (uint8_t)(~len_hi);
    *itr++ = (uint8_t)(~len_lo);

    // ------------------
    // PayloadHeader
    // ------------------
    memcpy(itr, header, sizeof(PayloadHeader));
    itr += sizeof(PayloadHeader);

    // ------------------
    // OtherData
    // ------------------
    if (other_data && other_data_len > 0) {
        memcpy(itr, other_data, other_data_len);
        itr += other_data_len;
    }

    // ------------------
    // CRC16 校验
    // ------------------
    uint16_t crc = crc16(item->packet + 6, payload_len);
    *itr++ = (uint8_t)(crc & 0xFF);
    *itr++ = (uint8_t)((crc >> 8) & 0xFF);

    // ------------------
    // 尾部
    // ------------------
    *itr++ = PACKET_TAIL_1_2;
    *itr++ = PACKET_TAIL_1_2;

    item->len = total_len;
    item->in_use = true;

    // ------------------ 发送 ------------------
    if (!sending) {
        sending = true;
        hal_usart_send(
                uplink_usart,
                item->packet,
                item->len,
                send_complete_callback,
                0);
    }

    send_tail = (send_tail + 1) % SEND_QUEUE_SIZE;

    // console_printf_do(
    //         "[packet_send] queued cmd=0x%02X seq=%u len=%u\n",
    //         header->command,
    //         header->sequence,
    //         total_len);
}