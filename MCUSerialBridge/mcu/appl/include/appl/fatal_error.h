/**
 * @file fatal_error.h
 * @brief 致命错误处理模块
 * 
 * 提供可靠的错误上报机制：
 * - 禁用中断，独占串口
 * - 阻塞发送，重复 10 次确保上位机收到
 * - 发送完成后自动复位 MCU
 */

#pragma once

#include "msb_protocol.h"
#include "hal/core_dump.h"

/**
 * @brief 发送致命错误并复位 (字符串错误)
 * 
 * 用于 ASSERT_RT 等主动检测到的错误。
 * 此函数不会返回，会在发送完成后复位 MCU。
 * 
 * @param il_offset IL 指令偏移
 * @param error_str 错误字符串 (最多 127 字节)
 * @param line_no   C 代码行号 (__LINE__)
 */
void fatal_error_send_string(int il_offset, const char* error_str, int line_no);

/**
 * @brief 发送致命错误并复位 (HardFault Core Dump)
 * 
 * 用于 HardFault 等硬件异常。
 * 此函数不会返回，会在发送完成后复位 MCU。
 * 
 * @param il_offset  IL 指令偏移
 * @param core_dump  CoreDumpVariables 指针
 */
void fatal_error_send_coredump(int il_offset, const CoreDumpVariables* core_dump);
