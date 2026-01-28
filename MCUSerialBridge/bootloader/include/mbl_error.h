/**
 * @file mbl_error.h
 * @brief MCU Bootloader 错误码定义
 */

#ifndef MBL_ERROR_H
#define MBL_ERROR_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * @brief MCU Bootloader 错误码枚举
 *
 * 错误码设计：
 * - 0x00000000: 成功
 * - 0x8xxxxxxx: Windows/上位机侧错误
 * - 0xExxxxxxx: 协议层错误
 * - 0x0Fxxxxxx: MCU 返回的错误
 */
typedef enum {
    /*==========================================================================
     * 成功
     *========================================================================*/

    /** 操作成功 */
    MBL_Error_OK = 0x00000000,

    /*==========================================================================
     * Windows/上位机侧错误 (0x8xxxxxxx)
     *========================================================================*/

    /** 打开串口失败 */
    MBL_Error_Win_OpenFailed = 0x80000001,

    /** 串口配置失败 */
    MBL_Error_Win_ConfigFailed = 0x80000002,

    /** 串口写入失败 */
    MBL_Error_Win_WriteFailed = 0x80000003,

    /** 串口读取失败 */
    MBL_Error_Win_ReadFailed = 0x80000004,

    /** 无效参数 */
    MBL_Error_Win_InvalidParam = 0x80000005,

    /** 句柄无效或未初始化 */
    MBL_Error_Win_HandleNotFound = 0x80000006,

    /** 内存分配失败 */
    MBL_Error_Win_OutOfMemory = 0x80000007,

    /** 操作超时 */
    MBL_Error_Win_Timeout = 0x80000008,

    /** 波特率探测失败 */
    MBL_Error_Win_ProbeFailed = 0x80000009,

    /** 串口已打开 */
    MBL_Error_Win_AlreadyOpen = 0x8000000A,

    /** 串口未打开 */
    MBL_Error_Win_NotOpen = 0x8000000B,

    /*==========================================================================
     * 协议层错误 (0xExxxxxxx)
     *========================================================================*/

    /** 帧头错误 */
    MBL_Error_Proto_HeaderError = 0xE0000001,

    /** 帧尾错误 */
    MBL_Error_Proto_TailError = 0xE0000002,

    /** CRC32 校验失败 */
    MBL_Error_Proto_CRCError = 0xE0000003,

    /** 帧长度错误 */
    MBL_Error_Proto_LengthError = 0xE0000004,

    /** 未知响应类型 */
    MBL_Error_Proto_UnknownResponse = 0xE0000005,

    /** 响应类型不匹配 */
    MBL_Error_Proto_ResponseMismatch = 0xE0000006,

    /*==========================================================================
     * MCU 返回的错误 (0x0Fxxxxxx)
     *========================================================================*/

    /** MCU Read 命令失败 */
    MBL_Error_MCU_ReadFailed = 0x0F000001,

    /** MCU Erase 命令失败 */
    MBL_Error_MCU_EraseFailed = 0x0F000002,

    /** MCU Write 命令失败 */
    MBL_Error_MCU_WriteFailed = 0x0F000003,

    /** MCU Exit 命令失败 */
    MBL_Error_MCU_ExitFailed = 0x0F000004,

} MCUBootloaderError;

/**
 * @brief 获取错误码的描述字符串
 *
 * @param error 错误码
 * @return 错误描述字符串（静态常量，不可释放）
 */
const char* mbl_error_to_string(MCUBootloaderError error);

#ifdef __cplusplus
}
#endif

#endif /* MBL_ERROR_H */
