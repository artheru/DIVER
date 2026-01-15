// msb_thread.c
#include "msb_thread.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

#include "c_core_common.h"
#include "msb_handle.h"
#include "msb_packet.h"

#define LINEAR_BUFFER_SIZE 65536  // 接收线性缓冲区大小

#define READ_SLEEP_MS 1
#define WRITE_SLEEP_MS 2

static uint16_t calculate_crc16(const uint8_t* data, uint32_t len);

// --------------------
// 接收无锁入队
// --------------------
static bool receive_ring_enqueue(
        msb_handle* handle,
        const uint8_t* data,
        uint32_t len)
{
    uint8_t next = (handle->receive_queue.head + 1);
    if (next == handle->receive_queue.tail) {
        // 队列满，丢包
        DBG_PRINT(
                "Receive: Receive Queue full, drop packet of "
                "len=%u",
                len);
        return 0;
    }
    if (len > PACKET_MAX_PAYLOAD_LEN) {
        DBG_PRINT("Receive: Packet Payload too large, len=%u", len);
        return 0;
    }

    memcpy(handle->receive_queue.entries[handle->receive_queue.head].payload,
           data,
           len);
    handle->receive_queue.entries[handle->receive_queue.head].len = len;
    handle->receive_queue.head = next;

    return 1;
}

// --------------------
// 发送无锁入队
// --------------------
bool send_payload(msb_handle* handle, const uint8_t* data, uint32_t len)
{
    if (len > PACKET_MAX_PAYLOAD_LEN) {
        DBG_PRINT("Send: Packet Payload too large, len=%u", len);
        return 0;
    }

    uint8_t next = (handle->send_queue.head + 1);
    if (next == handle->send_queue.tail) {
        // 队列满，丢包
        DBG_PRINT(
                "Send: Send Queue full, drop packet of len=%u, head=%u "
                "tail=%u",
                len,
                handle->send_queue.head,
                handle->send_queue.tail);
        return 0;
    }

    memcpy(handle->send_queue.entries[handle->send_queue.head].payload,
           data,
           len);
    handle->send_queue.entries[handle->send_queue.head].len = len;
    handle->send_queue.head = next;

    return 1;
}

// --------------------
// 接收无锁出队（只返回长度，payload需拷贝到线程私有缓存）
// --------------------
static bool receive_ring_dequeue(
        msb_handle* handle,
        uint8_t* out_buf,
        uint32_t* out_len)
{
    if (handle->receive_queue.head == handle->receive_queue.tail)
        return 0;  // 队列空

    uint32_t len =
            handle->receive_queue.entries[handle->receive_queue.tail].len;
    memcpy(out_buf,
           handle->receive_queue.entries[handle->receive_queue.tail].payload,
           len);
    *out_len = len;
    handle->receive_queue.tail++;

    return 1;
}

// --------------------
// 解析线程
// --------------------
DWORD WINAPI parse_thread_func(LPVOID param)
{
    DBG_PRINT("Thread: Parse thread started");

    msb_handle* handle = (msb_handle*)param;
    uint8_t local_buf[PACKET_MAX_PAYLOAD_LEN];  // 线程私有缓存

    while (handle && handle->is_open) {
        uint32_t len = 0;
        if (receive_ring_dequeue(handle, local_buf, &len)) {
            // Payload 已拷贝到线程私有缓存
            if (len < sizeof(PayloadHeader)) {
                continue;  // 数据太短
            }

            PayloadHeader* payload_header = (PayloadHeader*)local_buf;
            uint32_t seq = payload_header->sequence;
            u8 command = payload_header->command;

            DBG_PRINT(
                    "Parsing with packet, command[0x%02X], sequence[%u], "
                    "result[0x%08X]",
                    command,
                    seq,
                    payload_header->error_code);
            if (command == CommandUploadPort) {
                // Upload Port Data (MCU -> PC)
                if (len < sizeof(PayloadHeader) + sizeof(DataPacket)) {
                    continue;  // 数据太短
                }
                DataPacket* data_packet =
                        (DataPacket*)((uint8_t*)local_buf + sizeof(PayloadHeader));
                if (data_packet->data_len !=
                    len - sizeof(PayloadHeader) - sizeof(DataPacket)) {
                    continue;  // Length mismatch
                }

                msb_parse_upload_data(handle, data_packet);
            } else if (command == CommandMemoryLowerIO) {
                // Memory LowerIO Data (MCU -> PC, DIVER mode output)
                if (len <
                    sizeof(PayloadHeader) + sizeof(MemoryExchangePacket)) {
                    continue;  // 数据太短
                }
                MemoryExchangePacket* mem_packet =
                        (MemoryExchangePacket*)((uint8_t*)local_buf + sizeof(PayloadHeader));
                if (mem_packet->data_len !=
                    len - sizeof(PayloadHeader) -
                            sizeof(MemoryExchangePacket)) {
                    continue;  // Length mismatch
                }

                // 调用用户回调
                if (handle->memory_lower_io_callback) {
                    handle->memory_lower_io_callback(
                            mem_packet->data,
                            mem_packet->data_len,
                            handle->memory_lower_io_callback_ctx);
                }
            } else if (command == CommandUploadConsoleWriteLine) {
                // Console WriteLine (MCU -> PC, DIVER mode log output)
                // Payload 结构: PayloadHeader + string data (不含长度字段)
                uint32_t msg_len = len - sizeof(PayloadHeader);
                if (msg_len == 0) {
                    continue;  // 空消息
                }

                char* msg_ptr = (char*)(local_buf + sizeof(PayloadHeader));
                // 临时存储，确保末尾有 '\0'
                char msg_buf[PACKET_MAX_PAYLOAD_LEN + 1];
                if (msg_len > PACKET_MAX_PAYLOAD_LEN) {
                    msg_len = PACKET_MAX_PAYLOAD_LEN;
                }
                // 确保字符串以 '\0' 结尾
                memcpy(msg_buf, msg_ptr, msg_len);
                msg_buf[msg_len] = '\0';
                DBG_PRINT("MCU: Called Console.WriteLine, msg = >>>\n%s<<<", msg_buf);

                // 调用用户回调
                if (handle->console_writeline_callback) {
                    handle->console_writeline_callback(
                            msg_buf,
                            msg_len,
                            handle->console_writeline_callback_ctx);
                }
            } else {
                // -------------------------------
                // 找到对应的 SeqWaiter
                // -------------------------------
                SeqWaiter* waiter = NULL;
                for (int i = 0; i < MAX_PENDING_SEQ; i++) {
                    EnterCriticalSection(&handle->pending[i].mtx);
                    if (handle->pending[i].in_use &&
                        handle->pending[i].seq == seq) {
                        waiter = &handle->pending[i];
                        LeaveCriticalSection(&handle->pending[i].mtx);
                        break;
                    }
                    LeaveCriticalSection(&handle->pending[i].mtx);
                }

                if (waiter) {
                    // 设置结果并唤醒
                    EnterCriticalSection(&waiter->mtx);
                    waiter->result =
                            (MCUSerialBridgeError)payload_header->error_code;
                    waiter->done_flag = true;
                    memset(&waiter->return_data, 0, RETURN_DATA_MAX_SIZE);
                    if (len > sizeof(PayloadHeader)) {
                        uint32_t return_data_size = len - sizeof(PayloadHeader);
                        if (return_data_size > RETURN_DATA_MAX_SIZE) {
                            return_data_size = RETURN_DATA_MAX_SIZE;
                        }
                        memcpy(&waiter->return_data,
                               local_buf + sizeof(PayloadHeader),
                               return_data_size);
                    }
                    LeaveCriticalSection(&waiter->mtx);

                    WakeConditionVariable(&waiter->cnd);
                } else {
                    DBG_PRINT(
                            "Parse: ERROR, Sequence[%u] not pending, "
                            "result=%08X",
                            seq,
                            payload_header->error_code);
                }
            }
        } else {
            Sleep(READ_SLEEP_MS);  // 队列空，休眠
        }
    }
    DBG_PRINT("Thread: Parse thread exited");
    return 0;
}

// --------------------
// 接收线程
// --------------------
DWORD WINAPI recv_thread_func(LPVOID param)
{
    DBG_PRINT("Thread: Receive thread started");

    msb_handle* handle = (msb_handle*)param;
    uint8_t linear_buffer[LINEAR_BUFFER_SIZE];  // 线性缓冲区
    uint32_t head = 0, tail = 0;                // 读写指针

    while (handle && handle->is_open) {
        DWORD bytesRead = 0;
        uint32_t max_read = LINEAR_BUFFER_SIZE - head;

        // 调用Windows API读取串口
        if (!ReadFile(
                    handle->hComm,
                    linear_buffer + head,
                    (DWORD)max_read,
                    &bytesRead,
                    NULL)) {
            Sleep(READ_SLEEP_MS);
            continue;
        }

        if (bytesRead == 0) {
            Sleep(READ_SLEEP_MS);
            continue;
        }

        head += bytesRead;

        // --------------------
        // 粘包解析
        // --------------------
        while (head - tail >= PACKET_MIN_VALID_LEN) {
            uint32_t offset = tail;

            // 检查头部
            if (linear_buffer[offset] != PACKET_HEADER_1 ||
                linear_buffer[offset + 1] != PACKET_HEADER_2) {
                tail++;
                continue;
            }

            // 检查长度
            uint8_t len_lo = linear_buffer[offset + 2];
            uint8_t len_hi = linear_buffer[offset + 3];
            uint8_t rev_lo = linear_buffer[offset + 4];
            uint8_t rev_hi = linear_buffer[offset + 5];
            if (len_lo != (uint8_t)(~rev_hi) || len_hi != (uint8_t)(~rev_lo)) {
                DBG_PRINT("Receive: Invalid payload length rev check, "
                          "skipped!");
                tail++;  // 长度字段校验失败非法，只跳1字节
                continue;
            }
            uint32_t payload_len = (uint32_t)len_lo + (uint32_t)(len_hi << 8);

            if (payload_len > PACKET_MAX_PAYLOAD_LEN) {
                DBG_PRINT(
                        "Receive: Invalid payload length[%u] check, "
                        "skipped!",
                        payload_len);
                tail++;  // 长度非法，只跳1字节
                continue;
            }

            if (head - tail < payload_len + PACKET_OFFLOAD_SIZE)
                break;  // 整包没到

            // CRC检查
            uint16_t checked_crc =
                    calculate_crc16(linear_buffer + offset + 6, payload_len);
            uint32_t crc_offset = offset + 6 + payload_len;
            uint16_t reported_crc =
                    (uint16_t)linear_buffer[crc_offset] +
                    ((uint16_t)linear_buffer[crc_offset + 1] << 8);
            if (checked_crc != reported_crc) {
                DBG_PRINT("Receive: Packet CRC Mismatched, "
                          "skipped!");
                tail++;  // CRC错，跳1字节
                continue;
            }

            // 尾检查
            if (linear_buffer[offset + PACKET_OFFLOAD_SIZE + payload_len - 2] !=
                        PACKET_TAIL_1_2 ||
                linear_buffer[offset + PACKET_OFFLOAD_SIZE + payload_len - 1] !=
                        PACKET_TAIL_1_2) {
                DBG_PRINT("Receive: Packet Tail Mismatched, "
                          "skipped!");
                tail++;
                continue;
            }

            // 整包合法，入队RingQueue
            if (!receive_ring_enqueue(
                        handle, linear_buffer + offset + 6, payload_len)) {
                DBG_PRINT("Receive: RingQueue is full, can not enqueue!");
            }

            // 移动到下一包
            tail += payload_len + PACKET_OFFLOAD_SIZE;
        }

        // --------------------
        // 回收线性缓冲区
        // --------------------
        if (tail > LINEAR_BUFFER_SIZE / 2) {
            memmove(linear_buffer, linear_buffer + tail, head - tail);
            head -= tail;
            tail = 0;
            DBG_PRINT(
                    "Receive: Linear receive raw buffer compacted, "
                    "head=%u tail=%u",
                    head,
                    tail);
        }
    }

    DBG_PRINT("Thread: Receive thread exited");
    return 0;
}


DWORD WINAPI send_thread_func(LPVOID param)
{
    DBG_PRINT("Thread: Send thread started");

    msb_handle* handle = (msb_handle*)param;

    while (handle && handle->is_open) {
        if (handle->send_queue.head != handle->send_queue.tail) {
            PayloadEntry* entry =
                    &handle->send_queue.entries[handle->send_queue.tail];

            // --------- 直接在内存上组帧 ----------
            entry->header[0] = PACKET_HEADER_1;
            entry->header[1] = PACKET_HEADER_2;
            entry->header[2] = (uint8_t)(entry->len & 0xFF);
            entry->header[3] = (uint8_t)((entry->len >> 8) & 0xFF);
            entry->header[4] = (uint8_t) ~(entry->header[3]);
            entry->header[5] = (uint8_t) ~(entry->header[2]);

            // CRC直接写在payload后面
            uint16_t crc = calculate_crc16(entry->payload, entry->len);
            entry->payload[entry->len] = crc & 0xFF;
            entry->payload[entry->len + 1] = (crc >> 8) & 0xFF;

            // 尾巴
            entry->payload[entry->len + 2] = PACKET_TAIL_1_2;
            entry->payload[entry->len + 3] = PACKET_TAIL_1_2;

            // 写总长度
            uint32_t total_len = entry->len + PACKET_OFFLOAD_SIZE;

            // 发送
            DWORD bytesWritten = 0;
            if (!WriteFile(
                        handle->hComm,
                        entry->header,
                        total_len,
                        &bytesWritten,
                        NULL)) {
                DBG_PRINT("Send: ERROR, can not write file!");
                // TODO Call OnError
            }
            Sleep(WRITE_SLEEP_MS);

            // 出队
            handle->send_queue.tail++;
        } else {
            Sleep(1);  // 队列空
        }
    }

    DBG_PRINT("Thread: Send thread exited");
    return 0;
}


/**
 * @brief 计算给定数据的 CRC16（Modbus CRC16 算法）
 *
 * @param data 数据指针
 * @param len  数据长度
 * @return CRC16 校验值
 */
uint16_t calculate_crc16(const uint8_t* data, uint32_t len)
{
    static const uint16_t CRC16Table[256] =
            {0x0000, 0xC0C1, 0xC181, 0x0140, 0xC301, 0x03C0, 0x0280, 0xC241,
             0xC601, 0x06C0, 0x0780, 0xC741, 0x0500, 0xC5C1, 0xC481, 0x0440,
             0xCC01, 0x0CC0, 0x0D80, 0xCD41, 0x0F00, 0xCFC1, 0xCE81, 0x0E40,
             0x0A00, 0xCAC1, 0xCB81, 0x0B40, 0xC901, 0x09C0, 0x0880, 0xC841,
             0xD801, 0x18C0, 0x1980, 0xD941, 0x1B00, 0xDBC1, 0xDA81, 0x1A40,
             0x1E00, 0xDEC1, 0xDF81, 0x1F40, 0xDD01, 0x1DC0, 0x1C80, 0xDC41,
             0x1400, 0xD4C1, 0xD581, 0x1540, 0xD701, 0x17C0, 0x1680, 0xD641,
             0xD201, 0x12C0, 0x1380, 0xD341, 0x1100, 0xD1C1, 0xD081, 0x1040,
             0xF001, 0x30C0, 0x3180, 0xF141, 0x3300, 0xF3C1, 0xF281, 0x3240,
             0x3600, 0xF6C1, 0xF781, 0x3740, 0xF501, 0x35C0, 0x3480, 0xF441,
             0x3C00, 0xFCC1, 0xFD81, 0x3D40, 0xFF01, 0x3FC0, 0x3E80, 0xFE41,
             0xFA01, 0x3AC0, 0x3B80, 0xFB41, 0x3900, 0xF9C1, 0xF881, 0x3840,
             0x2800, 0xE8C1, 0xE981, 0x2940, 0xEB01, 0x2BC0, 0x2A80, 0xEA41,
             0xEE01, 0x2EC0, 0x2F80, 0xEF41, 0x2D00, 0xEDC1, 0xEC81, 0x2C40,
             0xE401, 0x24C0, 0x2580, 0xE541, 0x2700, 0xE7C1, 0xE681, 0x2640,
             0x2200, 0xE2C1, 0xE381, 0x2340, 0xE101, 0x21C0, 0x2080, 0xE041,
             0xA001, 0x60C0, 0x6180, 0xA141, 0x6300, 0xA3C1, 0xA281, 0x6240,
             0x6600, 0xA6C1, 0xA781, 0x6740, 0xA501, 0x65C0, 0x6480, 0xA441,
             0x6C00, 0xACC1, 0xAD81, 0x6D40, 0xAF01, 0x6FC0, 0x6E80, 0xAE41,
             0xAA01, 0x6AC0, 0x6B80, 0xAB41, 0x6900, 0xA9C1, 0xA881, 0x6840,
             0x7800, 0xB8C1, 0xB981, 0x7940, 0xBB01, 0x7BC0, 0x7A80, 0xBA41,
             0xBE01, 0x7EC0, 0x7F80, 0xBF41, 0x7D00, 0xBDC1, 0xBC81, 0x7C40,
             0xB401, 0x74C0, 0x7580, 0xB541, 0x7700, 0xB7C1, 0xB681, 0x7640,
             0x7200, 0xB2C1, 0xB381, 0x7340, 0xB101, 0x71C0, 0x7080, 0xB041,
             0x5000, 0x90C1, 0x9181, 0x5140, 0x9301, 0x53C0, 0x5280, 0x9241,
             0x9601, 0x56C0, 0x5780, 0x9741, 0x5500, 0x95C1, 0x9481, 0x5440,
             0x9C01, 0x5CC0, 0x5D80, 0x9D41, 0x5F00, 0x9FC1, 0x9E81, 0x5E40,
             0x5A00, 0x9AC1, 0x9B81, 0x5B40, 0x9901, 0x59C0, 0x5880, 0x9841,
             0x8801, 0x48C0, 0x4980, 0x8941, 0x4B00, 0x8BC1, 0x8A81, 0x4A40,
             0x4E00, 0x8EC1, 0x8F81, 0x4F40, 0x8D01, 0x4DC0, 0x4C80, 0x8C41,
             0x4400, 0x84C1, 0x8581, 0x4540, 0x8701, 0x47C0, 0x4680, 0x8641,
             0x8201, 0x42C0, 0x4380, 0x8341, 0x4100, 0x81C1, 0x8081, 0x4040};

    uint16_t crc = 0xffff;  // 初始化
    uint8_t temp;

    while (len-- > 0) {
        temp = crc & 0xFF;
        crc = (crc >> 8) ^ CRC16Table[(temp ^ *data++) & 0xFF];
    }

    return crc;
}
