/**
 * @file fatal_error.c
 * @brief 致命错误处理模块实现
 * 
 * 错误发送流程：
 * 1. 禁用所有中断 (防止堆栈进一步破坏)
 * 2. 构建 ErrorPayloadC 数据包
 * 3. 使用 hal_usart_send_sync 阻塞发送
 * 4. 重复发送 10 次，间隔 500ms (确保上位机收到)
 * 5. 调用 hal_nvic_reset 复位 MCU
 */

#include "appl/fatal_error.h"
#include "appl/threads.h"
#include "appl/version.h"
#include "hal/usart.h"
#include "hal/nvic.h"
#include "hal/delay.h"
#include "util/crc.h"
#include <string.h>

/** 错误包重复发送次数 */
#define ERROR_SEND_REPEAT_COUNT  10

/** 每次发送间隔 (ms) */
#define ERROR_SEND_INTERVAL_MS   500

/** 静态缓冲区 (避免使用堆，防止堆被破坏) */
static uint8_t s_error_packet[PACKET_OFFLOAD_SIZE + sizeof(PayloadHeader) + sizeof(ErrorPayloadC)];

/**
 * @brief 构建并阻塞发送错误包
 * 
 * 此函数在禁用中断的情况下运行，独占串口。
 * 函数不会返回，会在发送完成后复位 MCU。
 * 
 * @param payload 错误 Payload 指针
 */
static void fatal_error_send_impl(const ErrorPayloadC* payload)
{
    // ========== 1. 禁用所有中断 ==========
    // 使用内联汇编直接禁用中断 (永久禁用，不会重新启用)
    __asm volatile("cpsid i" : : : "memory");
    
    // ========== 2. 构建数据包框架 ==========
    uint8_t* pkt = s_error_packet;
    
    // Header
    pkt[0] = PACKET_HEADER_1;
    pkt[1] = PACKET_HEADER_2;
    
    // Length
    uint16_t payload_len = sizeof(PayloadHeader) + sizeof(ErrorPayloadC);
    pkt[2] = (uint8_t)(payload_len & 0xFF);
    pkt[3] = (uint8_t)((payload_len >> 8) & 0xFF);
    pkt[4] = (uint8_t)(~pkt[3]);
    pkt[5] = (uint8_t)(~pkt[2]);
    
    // PayloadHeader 位置
    uint8_t* header_ptr = pkt + 6;
    
    // ErrorPayloadC
    uint8_t* payload_ptr = header_ptr + sizeof(PayloadHeader);
    memcpy(payload_ptr, payload, sizeof(ErrorPayloadC));
    
    // CRC 和 Tail 位置
    uint8_t* crc_ptr = payload_ptr + sizeof(ErrorPayloadC);
    uint8_t* tail_ptr = crc_ptr + 2;
    
    // Tail
    tail_ptr[0] = PACKET_TAIL_1_2;
    tail_ptr[1] = PACKET_TAIL_1_2;
    
    uint32_t total_len = tail_ptr + 2 - pkt;
    
    // ========== 3. 重复发送 10 次 ==========
    for (int i = 0; i < ERROR_SEND_REPEAT_COUNT; i++) {
        // 每次更新 PayloadHeader (sequence = i)
        PayloadHeader header = {
            .command = CommandError,
            .sequence = (uint32_t)i,
            .timestamp_ms = 0,
            .error_code = 0xDEAD
        };
        memcpy(header_ptr, &header, sizeof(PayloadHeader));
        
        // 重新计算 CRC (因为 sequence 变了)
        uint16_t crc = crc16(pkt + 6, payload_len);
        crc_ptr[0] = (uint8_t)(crc & 0xFF);
        crc_ptr[1] = (uint8_t)((crc >> 8) & 0xFF);
        
        // 阻塞发送
        hal_usart_send_sync(uplink_usart, pkt, total_len);
        
        // 等待间隔
        delay_ms(ERROR_SEND_INTERVAL_MS);
    }
    
    // ========== 4. 复位 MCU ==========
    hal_nvic_reset();
    
    // 不会执行到这里
    while (1) {}
}

void fatal_error_send_string(int il_offset, const char* error_str, int line_no)
{
    ErrorPayloadC payload;
    memset(&payload, 0, sizeof(payload));
    
    payload.payload_version = ERROR_PAYLOAD_VERSION;
    payload.version = g_version_info;
    payload.debug_info.il_offset = il_offset;
    payload.debug_info.line_no = line_no;
    payload.core_dump_layout = CoreDumpLayout_String;
    
    // 复制错误字符串 (最多 127 字节 + null)
    if (error_str) {
        size_t len = strlen(error_str);
        if (len > sizeof(payload.core_dump.raw) - 1) {
            len = sizeof(payload.core_dump.raw) - 1;
        }
        memcpy(payload.core_dump.raw, error_str, len);
        payload.core_dump.raw[len] = '\0';
    }
    
    fatal_error_send_impl(&payload);
}

void fatal_error_send_coredump(int il_offset, const CoreDumpVariables* core_dump)
{
    ErrorPayloadC payload;
    memset(&payload, 0, sizeof(payload));
    
    payload.payload_version = ERROR_PAYLOAD_VERSION;
    payload.version = g_version_info;
    payload.debug_info.il_offset = il_offset;
    payload.debug_info.line_no = 0;
    payload.core_dump_layout = CoreDumpLayout_STM32F4;
    
    // 复制 CoreDump 数据
    if (core_dump) {
        memcpy(&payload.core_dump.f4, core_dump, sizeof(CoreDumpVariablesF4C));
    }
    
    fatal_error_send_impl(&payload);
}
