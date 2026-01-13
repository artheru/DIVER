#pragma once

#include "common.h"
#include "msb_protocol.h"

void packet_parse(const void* data, uint32_t length, ...);

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
        uint32_t other_data_len);
