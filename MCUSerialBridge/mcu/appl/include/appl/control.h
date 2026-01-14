#pragma once

#include "common.h"
#include "msb_protocol.h"

extern volatile uint32_t g_inputs;
extern volatile uint32_t g_outputs;

extern volatile MCUStateC g_mcu_state;

/** @brief Wire Tap 模式标志，启用后即使在 DIVER 模式下也会上报端口数据 */
extern volatile bool g_wire_tap_enabled;

/** @brief 程序缓冲区指针（DIVER 模式） */
extern uint8_t* g_program_buffer;
extern uint32_t g_program_length;

/* ===============================
 * 基础控制命令
 * =============================== */

MCUSerialBridgeError control_on_configure(
        const uint8_t* data,
        uint32_t data_length);

MCUSerialBridgeError control_on_reset(
        const uint8_t* data,
        uint32_t data_length);

MCUSerialBridgeError control_on_write_port(
        const uint8_t* data,
        uint32_t data_length,
        uint32_t sequence,
        uint8_t* async);

MCUSerialBridgeError control_on_write_output(
        const uint8_t* data,
        uint32_t data_length);

/* ===============================
 * DIVER / 扩展控制命令
 * =============================== */

/**
 * @brief 处理启动命令
 * 启动 MCU 运行（DIVER 模式运行程序，或透传模式开始转发）
 */
MCUSerialBridgeError control_on_start(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理启用 Wire Tap 命令
 * 启用后即使在 DIVER 模式下，端口收到的数据也会上报给 PC
 */
MCUSerialBridgeError control_on_enable_wire_tap(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理程序下载命令
 * 接收程序数据，支持分片传输。如果程序长度为 0，切换到透传模式
 */
MCUSerialBridgeError control_on_program(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理 UpperIO 内存交换命令
 * PC 向 MCU 发送输入变量数据（DIVER 模式下使用）
 */
MCUSerialBridgeError control_on_memory_upper_io(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 发送 LowerIO 内存交换数据
 * MCU 向 PC 上报输出变量数据（DIVER 模式下使用）
 */
void control_upload_memory_lower_io(const uint8_t* data, uint32_t data_length);
