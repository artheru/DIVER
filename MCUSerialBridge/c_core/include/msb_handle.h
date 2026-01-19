#ifndef MCU_HANDLE_H
#define MCU_HANDLE_H

#include <stdbool.h>
#include <stdint.h>
#include <windows.h>

#include "msb_bridge.h"
#include "msb_error_c.h"
#include "msb_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

#define MAX_PENDING_SEQ 32

// Port receive queue
typedef struct {
    uint8_t data[PACKET_MAX_DATALEN];
    uint16_t len;
} PortDataFrame;
typedef struct {
    PortDataFrame queue[0x100];
    uint8_t head;  // 写（parse 线程）
    uint8_t tail;  // 读（read_port）
    HANDLE data_event;
} PortQueue;

// Command Request and Reply Waiter
typedef struct {
    CRITICAL_SECTION mtx;         // 该槽位的互斥锁
    CONDITION_VARIABLE cnd;       // 条件变量，用于等待或唤醒
    uint32_t seq;                 // 对应的包序号
    bool in_use;                  // 是否已被占用
    bool done_flag;               // 接收到响应后置为 true
    MCUSerialBridgeError result;  // MCU返回结果
#define RETURN_DATA_MAX_SIZE 512
    uint8_t return_data[RETURN_DATA_MAX_SIZE];  // MCU 返回的额外数据
    uint32_t return_data_len;  // MCU 返回的额外数据的长度
} SeqWaiter;

// RawPacket entry for sending and receiving
typedef struct {
    uint16_t len;       // Payload长度
    uint8_t header[6];  // BB AA len_lo len_hi rev_lo rev_hi
    uint8_t payload[PACKET_MAX_PAYLOAD_LEN + 4];  // Payload数据 + crc16_lo
                                                  // crc16_hi + EE EE
} PayloadEntry;
typedef struct {
    PayloadEntry entries[0x100];  // 队列数组，环形队列长度=256, 不要改
    volatile uint8_t head;        // 写指针
    volatile uint8_t tail;        // 读指针
} RingQueue;


typedef struct msb_handle {
    char port_name[64];
    uint32_t baud;

    bool is_open;
    HANDLE hComm;

    // 线程相关
    void* recv_thread;
    void* parse_thread;
    void* send_thread;

    CRITICAL_SECTION seq_lock;  // 保护全局 sequence
    uint32_t sequence;
    SeqWaiter pending[MAX_PENDING_SEQ];

    // 命令接收队列
    RingQueue receive_queue;
    // 命令发送队列
    RingQueue send_queue;

    // Port 数据接收队列
    PortQueue ports[PACKET_MAX_PORTS_NUM];

    msb_on_port_data_callback_function_t
            port_data_callback[PACKET_MAX_PORTS_NUM];
    void* port_data_callback_ctx[PACKET_MAX_PORTS_NUM];

    // Memory Lower IO 回调（DIVER 模式 LowerIO 上报）
    msb_on_memory_lower_io_callback_function_t memory_lower_io_callback;
    void* memory_lower_io_callback_ctx;

    // Console WriteLine 回调（DIVER 模式日志上报）
    msb_on_console_writeline_callback_function_t console_writeline_callback;
    void* console_writeline_callback_ctx;

    // 用户自定义回调
    void (*error_cb)(MCUSerialBridgeError err, const char* msg, int len);
} msb_handle;

MCUSerialBridgeError msb_handle_init(msb_handle** handle);
MCUSerialBridgeError msb_handle_deinit(msb_handle* handle);

#ifdef __cplusplus
}
#endif

#endif
