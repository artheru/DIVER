#include "msb_packet.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>

#include "c_core_common.h"
#include "msb_handle.h"
#include "msb_protocol.h"
#include "msb_thread.h"

// --------------------
// 构建并发送协议包
// --------------------
MCUSerialBridgeError mcu_send_packet_and_wait(
        msb_handle* handle,
        uint8_t command,
        const uint8_t* other_data,
        uint32_t other_data_len,
        uint8_t* return_data,
        uint32_t return_data_len,
        uint32_t timeout_ms)
{
    if (!handle || !handle->is_open)
        return MSB_Error_Win_HandleNotFound;

    // --------------------
    // 构建 Payload
    // --------------------
    uint32_t total_len = sizeof(PayloadHeader) + other_data_len;
    if (total_len > PACKET_MAX_PAYLOAD_LEN) {
        DBG_PRINT("Packet, payload too large, length = %u\n", total_len);
        return MSB_Error_Proto_FrameTooLong;
    }

    // 线程安全生成序号
    EnterCriticalSection(&handle->seq_lock);
    uint32_t seq = handle->sequence++;
    LeaveCriticalSection(&handle->seq_lock);

    // 构建PayloadHeader
    uint8_t buf[PACKET_MAX_PAYLOAD_LEN];
    PayloadHeader* header = (PayloadHeader*)buf;
    header->command = command;
    header->sequence = seq;
    header->timestamp_ms = (uint32_t)(clock() * 1000 / CLOCKS_PER_SEC);
    header->error_code = 0;

    // 拷贝其他数据
    if (other_data_len > 0) {
        memcpy(buf + sizeof(PayloadHeader), other_data, other_data_len);
    }

    DBG_PRINT(
            "Send Packet started, command[0x%02X], sequence[%u], timeout[%u]",
            command,
            seq,
            timeout_ms);

    if (timeout_ms == 0) {
        if (send_payload(handle, buf, total_len)) {
            DBG_PRINT(
                    "Send Packet without wait, command[0x%02X], sequence[%u], "
                    "timeout[%u]",
                    command,
                    seq,
                    timeout_ms);
            return MSB_Error_OK;
        } else {
            DBG_PRINT(
                    "Send Packet Failed, command[0x%02X], sequence[%u], "
                    "timeout[%u]",
                    command,
                    seq,
                    timeout_ms);
            return MSB_Error_Win_BufferFull;
        }
    } else {
        // 找空位并占领
        SeqWaiter* waiter = NULL;
        for (int i = 0; i < MAX_PENDING_SEQ; i++) {
            EnterCriticalSection(&handle->pending[i].mtx);
            if (!handle->pending[i].in_use) {
                waiter = &handle->pending[i];
                waiter->seq = seq;
                waiter->in_use = true;
                waiter->done_flag = false;
                LeaveCriticalSection(&handle->pending[i].mtx);
                break;
            }
            LeaveCriticalSection(&handle->pending[i].mtx);
        }

        if (!waiter) {
            // 没空位
            DBG_PRINT(
                    "Send Packet Failed, command[0x%02X], sequence[%u], "
                    "timeout[%u]",
                    command,
                    seq,
                    timeout_ms);
            return MSB_Error_Win_BufferFull;
        }

        // 发送包
        if (!send_payload(handle, buf, total_len)) {
            // 发送失败，释放槽位
            DBG_PRINT(
                    "Send Packet Failed, command[0x%02X], sequence[%u], "
                    "timeout[%u]",
                    command,
                    seq,
                    timeout_ms);
            waiter->in_use = false;
            return MSB_Error_Win_BufferFull;
        }

        // 等待接收线程唤醒
        EnterCriticalSection(&waiter->mtx);
        BOOL signaled = SleepConditionVariableCS(
                &waiter->cnd, &waiter->mtx, timeout_ms);
        if (!signaled) {
            // 超时
            waiter->in_use = false;
            LeaveCriticalSection(&waiter->mtx);

            DBG_PRINT(
                    "Send Packet Timed-out, command[0x%02X], sequence[%u], "
                    "timeout[%u]",
                    command,
                    seq,
                    timeout_ms);
            return MSB_Error_Proto_Timeout;
        }

        while (!waiter->done_flag) {
            SleepConditionVariableCS(&waiter->cnd, &waiter->mtx, INFINITE);
        }

        MCUSerialBridgeError ret = waiter->result;
        if (return_data && return_data_len > 0) {
            if (return_data_len > RETURN_DATA_MAX_SIZE) {
                return_data_len = RETURN_DATA_MAX_SIZE;
            }
            memcpy(return_data, &waiter->return_data, return_data_len);
        }
        waiter->in_use = false;
        LeaveCriticalSection(&waiter->mtx);

        DBG_PRINT(
                "Send Packet is done, command[0x%02X], sequence[%u], "
                "timeout[%u], "
                "result[0x%08X]",
                command,
                seq,
                timeout_ms,
                waiter->result);
        return ret;
    }
}

void msb_parse_upload_data(msb_handle* handle, const DataPacket* data_packet)
{
    if (!handle || !handle->is_open || !data_packet)
        return;

    int8_t port_index = data_packet->port_index;
    uint16_t data_len = data_packet->data_len;

    /* ---------- Port index 校验 ---------- */
    if (port_index < 0 || port_index >= PACKET_MAX_PORTS_NUM) {
        DBG_PRINT("UploadData: Invalid port index Port[%u]", port_index);
        return;
    }

    /* ---------- Payload 长度校验 ---------- */
    if (data_len == 0 || data_len > PACKET_MAX_DATALEN) {
        DBG_PRINT(
                "UploadData: Received Port[%u], invalid data length=%u",
                port_index,
                data_len);
        return;
    }

    if (handle->port_data_callback[port_index]) {
        handle->port_data_callback[port_index](
                data_packet->data,
                data_packet->data_len,
                handle->port_data_callback_ctx[port_index]);
        return;
    }

    PortQueue* q = &handle->ports[port_index];

    /* ---------- 单生产者无锁入队 ---------- */
    uint8_t next = q->head + 1;
    if (next == q->tail) {
        /* 队列满，丢帧 */
        DBG_PRINT(
                "UploadData: Received Port[%u], receive queue full, "
                "dropping data len=%u",
                port_index,
                data_len);
        return;
    }

    /* 拷贝 payload（一帧一次） */
    DBG_PRINT("UploadData: Received Port[%u], len=%u", port_index, data_len);
    memcpy(q->queue[q->head].data, data_packet->data, data_len);
    q->queue[q->head].len = data_len;
    q->head = next;

    /* 唤醒 read_ports */
    SetEvent(q->data_event);
}