#pragma once

#include "common.h"
#include "hal/dcan.h"

/**
 * @brief 初始化上传模块
 * @note 必须在使用其他 upload 函数前调用
 */
void init_upload(void);

void upload_serial_packet(
        const void* data,
        uint32_t length,
        uint32_t port_index);

void upload_can_packet(
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7,
        uint32_t port_index);

/*
 * @brief 上传日志
 * 上传日志到 PC
 * @note 调用该函数后，会清空上传缓冲区
 */
void upload_console_writeline();

/*
 * @brief 追加日志到上传缓冲区
 * @param data 数据指针
 * @param length 数据长度
 * @return 实际追加的数据长度
 */
uint32_t upload_console_writeline_append(const void* data, uint32_t length);
