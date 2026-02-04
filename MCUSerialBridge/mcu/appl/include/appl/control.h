#pragma once

#include "common.h"
#include "msb_protocol.h"

extern volatile MCUStateC g_mcu_state;

/* ===============================
 * 端口统计数据
 * =============================== */

/** @brief 各端口的统计数据（TX/RX 帧数和字节数） */
extern volatile PortStatsC g_port_stats[PACKET_MAX_PORTS_NUM];

/** @brief 每端口 WireTap 标志数组，启用后即使在 DIVER 模式下也会上报端口数据 */
extern volatile uint8_t g_wire_tap_flags[PACKET_MAX_PORTS_NUM];

/** @brief 检查指定端口的 RX WireTap 是否启用 */
#define WIRETAP_RX_ENABLED(port) (g_wire_tap_flags[port] & WireTapFlag_RX)

/** @brief 检查指定端口的 TX WireTap 是否启用 */
#define WIRETAP_TX_ENABLED(port) (g_wire_tap_flags[port] & WireTapFlag_TX)

/** @brief 程序缓冲区指针（DIVER 模式） */
extern uint8_t* g_program_buffer;
extern uint32_t g_program_length;

/** @brief 程序缓冲区总大小（用于 VM 内存分配） */
#define PROGRAM_BUFFER_MAX_SIZE (20 * 1024)

/*
下列所有命令返回的错误码都会直接被交互层（packet）直接同步地返回给PC
*/

/* ===============================
 * 端口读写命令
 * =============================== */

/**
 * @brief 处理写端口命令
 * 向指定端口写数据
 @param data 数据指针
 @param data_length 数据长度
 @param sequence 序列号
 @param async 异步标志
 @return 错误码
 @note
 实现应该主动修改async标志，如果成功开始写入则是异步，写入完成回调已经被成功注册，当回调完成以后会向PC返回确认响应，这时候需要设置
 async 为 true，表示交互层（packet）不能立刻返回确认响应，而是需要等待回调完成。
 */
MCUSerialBridgeError control_on_write_port(
        const uint8_t* data,
        uint32_t data_length,
        uint32_t sequence,
        uint8_t* async);

/*
@brief 处理 VM 写端口命令
VM 层通过调用这个函数来写端口数据
- 对于 Serial
类型数据，因为接口不支持并发，所以本函数的实现会先将数据写入到缓冲区。
  然后在每个 VM Loop 结束时，vm_loop() 需要主动调用 control_vm_flush_ports()
来发送数据。
- 对于 CAN 类型端口，接口原生支持并发，本函数实现会直接调用 dcan 的接口直接发送
@param port_index 端口索引
@param data 数据指针
@param data_length 数据长度
@return 错误码
@note
*/
MCUSerialBridgeError control_vm_write_port(
        uint32_t port_index,
        const uint8_t* data,
        uint32_t data_length);

/*
@brief 刷新 VM 写端口数据
在每个 VM Loop 结束时，vm_loop() 需要主动调用这个函数来发送数据。
会遍历所有端口，并根据端口类型调用不同的发送函数。
对于 Serial 类型端口，内部会将每次写入的片段间隔分开，逐个发送，间隔与 FrameGap
设定保持一致，避免不同的包之间的粘连 CAN 端口则跳过，因为实际发送已经在
control_vm_write_port() 中完成。
@note
*/
void control_vm_flush_ports();

MCUSerialBridgeError control_on_write_output(
        const uint8_t* data,
        uint32_t data_length);

/* ===============================
 * DIVER / 控制命令
 * =============================== */

MCUSerialBridgeError control_on_reset(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理升级命令
 * MCU 收到后写入 bootflag，延迟约 200ms 后重启进入 Bootloader 模式
 */
MCUSerialBridgeError control_on_upgrade(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理程序下载命令
 * 接收程序数据，支持分片传输。如果程序长度为 0，切换到透传模式
 */
MCUSerialBridgeError control_on_program(
        const uint8_t* data,
        uint32_t data_length);

MCUSerialBridgeError control_on_configure(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理启动命令
 * 启动 MCU 运行（DIVER 模式运行程序，或透传模式开始转发）
 */
MCUSerialBridgeError control_on_start(
        const uint8_t* data,
        uint32_t data_length);

/**
 * @brief 处理设置 Wire Tap 命令
 * 配置指定端口的 WireTap 监视功能（RX/TX）。
 * 启用后即使在 DIVER 模式下，端口的收发数据也会上报给 PC
 * @param data WireTapConfigC 结构数据
 * @param data_length 数据长度
 */
MCUSerialBridgeError control_on_set_wire_tap(
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

/**
 * @brief 检查并获取 UpperIO 新数据（双缓存交换）
 * 
 * 在 VM loop 中调用，如果收到新的 UpperIO 数据，会交换双缓存并返回数据指针和长度。
 * 如果两个 Operation 之间收到多个 UpperIO，只保留最后一个。
 * 
 * @param[out] data_ptr 返回数据指针（指向读取 buffer）
 * @param[out] data_len 返回数据长度
 * @return true 如果有新数据，false 如果没有新数据
 */
bool control_vm_get_upper_io(const uint8_t** data_ptr, uint32_t* data_len);

/* ===============================
 * 统计数据
 * =============================== */

/**
 * @brief 累加端口发送统计
 * @param port_index 端口索引
 * @param bytes 发送的字节数
 */
void control_stats_add_tx(uint32_t port_index, uint32_t bytes);

/**
 * @brief 累加端口接收统计
 * @param port_index 端口索引
 * @param bytes 接收的字节数
 */
void control_stats_add_rx(uint32_t port_index, uint32_t bytes);

/**
 * @brief 获取运行时统计数据
 * @param[out] stats 输出统计数据结构指针
 */
void control_get_runtime_stats(RuntimeStatsC* stats);
