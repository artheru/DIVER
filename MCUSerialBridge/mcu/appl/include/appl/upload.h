#pragma once

#include "common.h"
#include "hal/dcan.h"

/**
 * @brief 初始化上传模块
 * @note 必须在使用其他 upload 函数前调用
 */
void init_upload(void);

/* ===============================
 * HAL 接收回调（从 USART/CAN 中断调用）
 * =============================== */

/**
 * @brief 串口数据接收回调
 * 从 HAL 串口中断调用，处理接收到的数据
 * @param data 数据指针
 * @param length 数据长度
 * @param port_index 端口索引
 */
void on_hal_receive_serial(
        const void* data,
        uint32_t length,
        uint32_t port_index);

/**
 * @brief CAN 数据接收回调
 * 从 HAL CAN 中断调用，处理接收到的数据
 * @param id_info CAN ID 信息
 * @param data_0_3 数据字节 0-3
 * @param data_4_7 数据字节 4-7
 * @param port_index 端口索引
 */
void on_hal_receive_can(
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7,
        uint32_t port_index);

/* ===============================
 * WireTap TX 上报（从 VM 写端口调用）
 * =============================== */

/**
 * @brief 上报串口发送数据（TX WireTap）
 * 在 VM 写端口时调用，上报发送的数据到 PC
 * @param data 数据指针
 * @param length 数据长度
 * @param port_index 端口索引
 */
void report_wiretap_transmit_serial(
        const void* data,
        uint32_t length,
        uint32_t port_index);

/**
 * @brief 上报 CAN 发送数据（TX WireTap）
 * 在 VM 写端口时调用，上报发送的数据到 PC
 * @param id_info CAN ID 信息
 * @param data_0_3 数据字节 0-3
 * @param data_4_7 数据字节 4-7
 * @param port_index 端口索引
 */
void report_wiretap_transmit_can(
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
