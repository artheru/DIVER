#ifndef MCU_BRIDGE_H
#define MCU_BRIDGE_H

#include <stdint.h>

#include "msb_error_c.h"   // 新的 MCUSerialBridgeError 枚举
#include "msb_protocol.h"  // PortConfig, CANMessage 等协议类型

#ifdef __cplusplus
extern "C" {
#endif

// 导出每个 API
#ifdef _WIN32
#define DLL_EXPORT __declspec(dllexport)
#else
#define DLL_EXPORT
#endif

/**
 * @brief MCU句柄结构体
 * @note 由msb_open分配，由msb_close释放
 */
typedef struct msb_handle msb_handle;

/**
 * @brief 打开 MCU 节点并建立通信
 *
 * 根据指定的串口名称和波特率打开与 MCU 的通信通道，
 * 并创建一个 MCU 句柄用于后续操作。
 *
 * @param[out] handle
 *      返回创建的 MCU 句柄指针。成功时由库内部分配内存，
 *      调用者在不再使用时需调用 @ref msb_close 释放。
 * @param port
 *      MCU 串口设备名称，例如 "COM3"（Windows）
 * @param baud
 *      串口通信波特率，例如 115200。
 *
 * @return MCUSerialBridgeError
 *      错误码，MSB_Error_OK 表示成功，其它值表示失败原因。
 * @note
 *      成功返回时会分配 msb_handle 内存资源，
 *      必须通过 @ref msb_close 显式释放。
 */
DLL_EXPORT MCUSerialBridgeError
msb_open(msb_handle** handle, const char* port, uint32_t baud);

/**
 * @brief 关闭 MCU 节点并释放资源
 *
 * 关闭与 MCU 的通信通道，并释放由 @ref msb_open
 * 创建的 MCU 句柄及其相关资源。
 *
 * @param handle
 *      MCU 句柄指针。调用成功后该句柄将不再有效，
 *      调用者应避免再次使用。
 *
 * @return MCUSerialBridgeError
 *      错误码，MSB_Error_OK 表示成功。
 */
DLL_EXPORT MCUSerialBridgeError msb_close(msb_handle* handle);

/**
 * @brief MCU复位
 *
 * @param handle MCU句柄
 * @param timeout_ms 超时时间（毫秒）
 * @return MCUSerialBridgeError 错误码
 */
DLL_EXPORT MCUSerialBridgeError
msb_reset(msb_handle* handle, uint32_t timeout_ms);

/**
 * @brief 获取 MCU 当前运行状态
 *
 * 查询 MCU 当前的运行状态，包括 Bridge（透传）
 * 或 DIVER（应用/驱动）模式及其子状态。
 *
 * @param handle
 *      MCU 句柄指针。
 * @param state
 *      返回 MCU 当前运行状态，取值参考 @ref MCUStateC。
 * @param timeout_ms 超时时间（毫秒）
 *
 * @return MCUSerialBridgeError
 *      错误码，MSB_Error_OK 表示成功。
 */
DLL_EXPORT MCUSerialBridgeError
mcu_state(msb_handle* handle, MCUStateC* state, uint32_t timeout_ms);

/**
 * @brief 获取 MCU 固件版本信息
 *
 * 查询 MCU 当前运行固件的版本信息，包括
 * 产品型号、版本 Tag、Git Commit 以及编译时间。
 *
 * @param handle
 *      MCU 句柄指针。
 * @param version
 *      返回 MCU 固件版本信息，结构体定义参考
 *      @ref VersionInfoC。
 * @param timeout_ms 超时时间（毫秒）
 *
 * @return MCUSerialBridgeError
 *      错误码，MSB_Error_OK 表示成功。
 */
DLL_EXPORT MCUSerialBridgeError
msb_version(msb_handle* handle, VersionInfoC* version, uint32_t timeout_ms);

/**
 * @brief 配置MCU端口
 *
 * @param handle MCU句柄
 * @param num_ports 端口数量
 * @param ports 端口配置数组
 * @param timeout_ms 超时时间（毫秒）
 * @return MCUSerialBridgeError 错误码
 */
DLL_EXPORT MCUSerialBridgeError msb_configure(
        msb_handle* handle,
        uint32_t num_ports,
        const PortConfigC* ports,
        uint32_t timeout_ms);

/**
 * @brief 读取MCU IO输入
 *
 * @param handle MCU句柄
 * @param inputs 输入数据缓冲, 4bytes，目的字节
 * @param timeout_ms 超时时间（毫秒）
 * @return MCUSerialBridgeError 错误码
 */
DLL_EXPORT MCUSerialBridgeError
msb_read_input(msb_handle* handle, uint8_t* inputs, uint32_t timeout_ms);

/**
 * @brief 写MCU IO输出
 *
 * @param handle MCU句柄
 * @param outputs 输出数据缓冲, 4bytes，源字节
 * @param timeout_ms 超时时间（毫秒）
 * @return MCUSerialBridgeError 错误码
 */
DLL_EXPORT MCUSerialBridgeError msb_write_output(
        msb_handle* handle,
        const uint8_t* outputs,
        uint32_t timeout_ms);

/**
 * @brief 读取 MCU 指定 Port 的一帧数据（按帧读取）
 *
 * MCU 上报的数据按帧入队；本接口每次调用最多读取一帧。
 *
 * @note
 * - 如果已注册了 Port 数据回调（msb_register_port_data_callback），
 *   本接口将始终无法读取到数据，请使用回调方式获取数据。
 *
 * @param handle MCU 句柄
 * @param port_index Port 索引
 * @param dst_data 接收缓冲区
 * @param dst_capacity 接收缓冲区容量（字节）
 * @param out_length 实际读取的数据长度（字节）
 * @param timeout_ms 超时时间（毫秒）
 *        - 0：不等待，有数据立即返回，没有数据立即返回 MSB_Error_NoData
 *        - >0：若当前无数据，最多等待 timeout_ms，期间有新帧到达则立即返回
 *
 * @return MCUSerialBridgeError
 *         - MSB_Error_OK            成功读取一帧
 *         - MSB_Error_NoData        当前无可读数据（仅在 timeout_ms == 0
 * 或等待超时）
 *         - MSB_Error_BufferTooSmall 缓冲区容量不足（out_length 返回所需长度）
 *         - MSB_Error_Win_InvalidParam 参数错误
 */
DLL_EXPORT MCUSerialBridgeError msb_read_port(
        msb_handle* handle,
        uint8_t port_index,
        uint8_t* dst_data,
        uint32_t dst_capacity,
        uint32_t* out_length,
        uint32_t timeout_ms);

/**
 * @brief Port 数据回调函数类型定义
 *
 * 如果调用了 msb_register_port_data_callback，当 MCU
 * 上报数据时，会调用该回调函数，将数据直接传给用户。
 *
 * @param dst_data 指向接收到的数据缓冲
 *                  - 仅在回调函数内使用
 *                  - 不可跨回调保存指针
 * @param dst_data_size 数据缓冲长度（字节）
 * @param user_ctx 用户注册的其他参数
 */
typedef void (*msb_on_port_data_callback_function_t)(
        const uint8_t* dst_data,
        uint32_t dst_data_size,
        void* user_ctx);

/**
 * @brief 注册 Port 数据回调函数
 *
 * MCU 上报数据时，会调用用户提供的回调函数。
 * 一旦注册，msb_read_port 将无法获取数据，必须使用回调方式处理。
 *
 * @param handle MCU 句柄
 * @param port_index 端口索引
 * @param callback 用户提供的回调函数指针
 * @param user_ctx 用户注册的其他参数
 *
 * @return MCUSerialBridgeError
 *         - MSB_Error_OK            注册成功
 *         - MSB_Error_Win_InvalidParam 参数错误
 */
DLL_EXPORT MCUSerialBridgeError msb_register_port_data_callback(
        msb_handle* handle,
        uint8_t port_index,
        msb_on_port_data_callback_function_t callback,
        void* user_ctx);

/**
 * @brief 向MCU Ports发送数据
 *
 * @param handle MCU句柄
 * @param port_index 索引
 * @param[in] src_data 数据缓冲
 * @param src_data_len 数据长度
 * @param timeout_ms 超时时间（毫秒）
 * @return MCUSerialBridgeError 错误码
 */
DLL_EXPORT MCUSerialBridgeError msb_write_port(
        msb_handle* handle,
        uint8_t port_index,
        const uint8_t* src_data,
        uint32_t src_data_len,
        uint32_t timeout_ms);

/*
 * @brief 生成函数指针结构体
 * 导出所有 API
 */
typedef struct MCUSerialBridgeAPI {
    MCUSerialBridgeError (*msb_open)(msb_handle**, const char*, uint32_t);
    MCUSerialBridgeError (*msb_close)(msb_handle*);
    MCUSerialBridgeError (*mcu_state)(
            msb_handle* handle,
            MCUStateC* state,
            uint32_t timeout_ms);
    MCUSerialBridgeError (*msb_version)(
            msb_handle* handle,
            VersionInfoC* version,
            uint32_t timeout_ms);
    MCUSerialBridgeError (*msb_configure)(
            msb_handle*,
            uint32_t,
            const PortConfigC*,
            uint32_t timeout_ms);
    MCUSerialBridgeError (*msb_read_input)(msb_handle*, uint8_t*, uint32_t);
    MCUSerialBridgeError (
            *msb_write_output)(msb_handle*, const uint8_t*, uint32_t);
    MCUSerialBridgeError (*msb_read_port)(
            msb_handle*,
            uint8_t,
            uint8_t*,
            uint32_t,
            uint32_t*,
            uint32_t);
    MCUSerialBridgeError (*msb_register_port_data_callback)(
            msb_handle*,
            uint8_t,
            msb_on_port_data_callback_function_t,
            void*);
    MCUSerialBridgeError (*msb_write_port)(
            msb_handle*,
            uint8_t,
            const uint8_t*,
            uint32_t,
            uint32_t);
    MCUSerialBridgeError (*msb_reset)(msb_handle*, uint32_t);
} MCUSerialBridgeAPI;

DLL_EXPORT void mcu_serial_bridge_get_api(MCUSerialBridgeAPI* api);

#ifdef __cplusplus
}
#endif

#endif  // MCU_BRIDGE_H
