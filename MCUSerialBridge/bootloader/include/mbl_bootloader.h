/**
 * @file mbl_bootloader.h
 * @brief MCU Bootloader 上位机通讯库公开 API
 *
 * 本库用于与 MCU Bootloader 进行通讯，支持固件读取、擦除、写入、退出等操作。
 * 通讯采用一问一答模式（类似 Modbus），固定 92 字节帧长。
 *
 * 使用流程：
 * 1. mbl_open() 打开串口（可指定波特率或自动探测）
 * 2. mbl_command_read() 读取下位机信息
 * 3. mbl_command_erase() 擦除固件
 * 4. mbl_command_write() 分块写入固件
 * 5. mbl_command_exit() 退出 Bootloader
 * 6. mbl_close() 关闭串口
 */

#ifndef MBL_BOOTLOADER_H
#define MBL_BOOTLOADER_H

#include <stdint.h>

#include "mbl_error.h"
#include "mbl_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
#define MBL_EXPORT __declspec(dllexport)
#else
#define MBL_EXPORT
#endif

/*==============================================================================
 * 句柄类型
 *============================================================================*/

/**
 * @brief MCU Bootloader 句柄（不透明指针）
 */
typedef struct mbl_handle mbl_handle;

/*==============================================================================
 * 回调函数类型
 *============================================================================*/

/**
 * @brief 进度与错误回调函数类型
 *
 * 在固件写入过程中，每写入一个数据块后调用此回调。
 *
 * @param progress 当前进度百分比 (0-100)
 * @param error    当前错误码（MBL_Error_OK 表示正常）
 * @param user_ctx 用户上下文指针
 */
typedef void (*mbl_progress_callback_t)(
        int progress,
        MCUBootloaderError error,
        void* user_ctx);

/*==============================================================================
 * 核心 API
 *============================================================================*/

/**
 * @brief 打开串口并连接 MCU Bootloader
 *
 * @param[out] handle   返回创建的句柄指针
 * @param port          串口名称，如 "COM3"
 * @param baud          波特率。若为 0，则自动探测波特率
 *
 * @return MCUBootloaderError
 *         - MBL_Error_OK: 成功
 *         - MBL_Error_Win_OpenFailed: 打开串口失败
 *         - MBL_Error_Win_ProbeFailed: 波特率探测失败（仅当 baud=0）
 */
MBL_EXPORT MCUBootloaderError
mbl_open(mbl_handle** handle, const char* port, uint32_t baud);

/**
 * @brief 关闭串口并释放资源
 *
 * @param handle 句柄指针
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_close(mbl_handle* handle);

/**
 * @brief 获取当前连接的波特率
 *
 * @param handle 句柄
 *
 * @return 波特率值，失败返回 0
 */
MBL_EXPORT uint32_t mbl_get_baudrate(mbl_handle* handle);

/**
 * @brief 注册进度与错误回调
 *
 * @param handle   句柄
 * @param callback 回调函数指针（可为 NULL 取消注册）
 * @param user_ctx 用户上下文指针
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_register_progress_callback(
        mbl_handle* handle,
        mbl_progress_callback_t callback,
        void* user_ctx);

/*==============================================================================
 * Bootloader 命令 API
 *============================================================================*/

/**
 * @brief 读取下位机固件信息
 *
 * @param handle     句柄
 * @param[out] info  返回固件信息
 * @param timeout_ms 超时时间（毫秒），建议 1000
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_command_read(
        mbl_handle* handle,
        MBL_FirmwareInfo* info,
        uint32_t timeout_ms);

/**
 * @brief 擦除固件
 *
 * @param handle     句柄
 * @param params     擦除参数（包含固件元信息用于校验）
 * @param timeout_ms 超时时间（毫秒），建议 10000（擦除较慢）
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_command_erase(
        mbl_handle* handle,
        const MBL_EraseParams* params,
        uint32_t timeout_ms);

/**
 * @brief 写入固件数据块
 *
 * 单次写入最多 64 字节。固件写入需要循环调用此函数。
 *
 * @param handle       句柄
 * @param offset       当前块在固件中的偏移
 * @param total_length 固件总长度
 * @param chunk_data   当前块数据
 * @param chunk_length 当前块长度（最大 64）
 * @param timeout_ms   超时时间（毫秒），建议 1000
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_command_write(
        mbl_handle* handle,
        uint32_t offset,
        uint32_t total_length,
        const uint8_t* chunk_data,
        uint32_t chunk_length,
        uint32_t timeout_ms);

/**
 * @brief 退出 Bootloader，重启进入应用程序
 *
 * @param handle     句柄
 * @param timeout_ms 超时时间（毫秒），建议 1000
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError
mbl_command_exit(mbl_handle* handle, uint32_t timeout_ms);

/*==============================================================================
 * 便捷 API
 *============================================================================*/

/**
 * @brief 写入完整固件（自动分块）
 *
 * 将整个固件数据自动按 64 字节分块写入。
 * 写入过程中会触发进度回调（如果已注册）。
 *
 * @param handle       句柄
 * @param firmware     完整固件数据
 * @param firmware_len 固件长度
 * @param timeout_ms   单次写入超时（毫秒），建议 1000
 *
 * @return MCUBootloaderError
 */
MBL_EXPORT MCUBootloaderError mbl_write_firmware(
        mbl_handle* handle,
        const uint8_t* firmware,
        uint32_t firmware_len,
        uint32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif /* MBL_BOOTLOADER_H */
