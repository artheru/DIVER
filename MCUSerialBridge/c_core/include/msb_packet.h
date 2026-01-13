#pragma once
#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#include "msb_handle.h"
#include "msb_protocol.h"

/**
 * @brief 发送 MCU 协议包并等待响应
 *
 * 该函数将 command 和附加数据构建成协议包，发送给 MCU，
 * 并在指定超时时间内等待响应。
 *
 * @param handle MCU 句柄
 * @param command MCU 命令码
 * @param other_data 附加数据缓冲（可为 NULL）
 * @param other_data_len 附加数据长度（单位字节）
 * @param timeout_ms 等待响应的超时时间（毫秒）
 *                   - timeout_ms > 0：等待响应或超时, 不可以超过1000
 *                   - timeout_ms == 0：发送后立即返回，不等待响应
 *
 * @return MCUSerialBridgeError 错误码
 * - MSB_Error_OK：发送成功（如果 timeout_ms > 0，则表示已收到响应）
 * - 其他值：发送失败或等待响应超时
 *
 * @note 阻塞式调用，直到收到响应或超时，除非 timeout_ms==0。
 */
MCUSerialBridgeError mcu_send_packet_and_wait(
        msb_handle* handle,
        uint8_t command,
        const uint8_t* other_data,
        uint32_t other_data_len,
        uint8_t* return_data,
        uint32_t return_data_len,
        uint32_t timeout_ms);

void msb_parse_upload_data(msb_handle* handle, const DataPacket* data_packet);


#ifdef __cplusplus
}
#endif
