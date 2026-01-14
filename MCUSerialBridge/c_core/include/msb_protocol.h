/**
 * @file msb_protocol_c.h
 * @brief MCU ↔ PC 通信协议公共定义（C 接口）
 *
 * 本文件定义了 MCU 与上位机（PC）之间通信协议的
 * 基础类型、命令枚举、端口类型、数据包结构及相关宏。
 *
 * ⚠️ 注意：
 * - 本文件 **同时被 X86（PC 端）与 Cortex-M4（MCU 端）使用**
 * - 所有结构体均为 **packed 格式**，严禁随意修改字段顺序或大小
 * - 修改前请确认不会破坏通信兼容性
 *
 * @author
 * @date
 */

#pragma once
#include <stdint.h>

// THIS FILE WILL BE USED BY BOTH C_CORE IN X86 AND CORTEX-ARM-M4

#ifdef __cplusplus
extern "C" {
#endif

#include "msb_error_c.h"

/**
 * @brief 静态断言宏（跨编译器）
 */
#if defined(_MSC_VER)
#define STATIC_ASSERT(expr, msg) static_assert(expr, msg)
#else
#define STATIC_ASSERT(expr, msg) _Static_assert(expr, msg)
#endif

#pragma pack(push, 1)

/* ===============================
 * Basic Types
 * =============================== */

/**
 * @brief 基础整数类型定义（跨平台统一）
 */
typedef uint8_t u8;
typedef int8_t i8;
typedef uint16_t u16;
typedef uint32_t u32;
typedef int16_t i16;
typedef int32_t i32;

/* ===============================
 * CommandType
 * =============================== */

/**
 * @brief 通信命令类型枚举
 *
 * 定义 MCU 与 PC 之间的所有通信命令。
 *
 * 规则说明：
 * - PC → MCU：请求命令
 * - MCU → PC：响应 / 上报命令
 * - 响应命令通常为：0x80 | 请求命令
 * - 主动上报类命令 sequence 固定为 0
 */
typedef enum {
    /**
     * @brief 配置端口命令 (PC → MCU)
     *
     * 上位机发送端口配置（串口/CAN 等），配置完成后 MCU 需返回确认响应。
     * 响应命令：0x81（无额外数据）。
     */
    CommandConfigure = 0x01,

    /**
     * @brief 复位 MCU 命令 (PC → MCU)
     *
     * 上位机要求 MCU 复位。MCU 收到后立即返回确认响应，然后延迟约 200ms
     * 后执行复位。 响应命令：0x82（无额外数据）。
     */
    CommandReset = 0x02,

    /**
     * @brief 读取 MCU 状态 (PC → MCU)
     *
     * 上位机要求 MCU 发送状态。
     * MCU收到后立即返回状态信息，响应命令：0x83（同seq，额外数据为4字节State）。
     */
    CommandState = 0x03,

    /**
     * @brief 读取 MCU 版本信息 (PC → MCU)
     *
     * 上位机要求 MCU 发送版本信息。
     * MCU收到后立即返回版本信息，响应命令：0x84（同seq，额外数据为 为Info）。
     */
    CommandVersion = 0x04,

    /**
     * @brief 启用 Wire Tap 模式 (PC → MCU)
     *
     * 启用后，即使在 DIVER 模式下，端口收到的数据也会上报给 PC。
     * 响应命令：0x85（同 seq）。
     */
    CommandEnableWireTap = 0x05,

    /**
     * @brief 启动 MCU 运行 (PC → MCU)
     *
     * 上位机命令 MCU 开始执行（DIVER 模式运行程序，或透传模式开始转发）。
     * 响应命令：0x8F（同 seq）。
     */
    CommandStart = 0x0F,

    /**
     * @brief 向 MCU 端口写数据命令 (PC → MCU)
     *
     * 上位机向下发数据到指定端口（串口/CAN 等）。MCU 执行写操作后需返回同 seq
     * 的确认响应。 响应命令：0x90（同 seq）。
     */
    CommandWritePort = 0x10,

    /**
     * @brief MCU 端口数据上报命令 (MCU → PC)
     *
     * MCU 主动上报端口收到的数据（透传）。无需确认响应，sequence 固定为 0。
     */
    CommandUploadPort = 0x20,

    /**
     * @brief 写 MCU IO 输出命令 (PC → MCU)
     *
     * 上位机写入 4 字节 IO 输出状态。MCU 执行后需返回同 seq 的确认响应。
     * 响应命令：0xB0（同 seq）。
     */
    CommandWriteOutput = 0x30,

    /**
     * @brief 读取 MCU IO 输入命令 (PC → MCU)
     *
     * 上位机请求读取当前 IO 输入状态。MCU 需返回响应包（命令 0xC0，同
     * seq），并携带 4 字节输入数据。
     */
    CommandReadInput = 0x40,

    /**
     * @brief 下载程序到 MCU (PC → MCU)
     *
     * 上位机向 MCU 发送程序数据。如果数据长度为 0，MCU 进入透传模式；
     * 如果数据长度非 0，MCU 进入 DIVER 模式并加载程序。
     * 程序可能需要分片传输，使用 offset 和 total_len 字段标识。
     * 响应命令：0xD0（同 seq）。
     */
    CommandProgram = 0x50,

    /**
     * @brief PC → MCU 内存交换 (UpperIO) (PC → MCU)
     *
     * PC 向 MCU 发送 UpperIO 数据（DIVER 模式下的输入变量）。
     * 响应命令（同 seq）。
     */
    CommandMemoryUpperIO = 0x60,

    /**
     * @brief MCU → PC 内存交换上报 (LowerIO) (MCU → PC)
     *
     * MCU 主动上报 LowerIO 数据（DIVER 模式下的输出变量）。
     * 无需确认响应。
     */
    CommandMemoryLowerIO = 0x70,

    /**
     * @brief 致命错误上报命令 (MCU → PC 或 PC → MCU)
     *
     * 任意一方检测到致命错误时上报。sequence 固定为 0，通常携带错误码。
     */
    CommandError = 0xFF,

} CommandType;

/* ===============================
 * PortType
 * =============================== */

/**
 * @brief 端口类型定义
 *
 * 用于描述 CommandWritePort / CommandUploadPort
 * 所操作的端口种类。
 */
typedef enum {
    PortType_Serial = 0x01, /**< 串口 */
    PortType_CAN = 0x02,    /**< CAN 总线 */
    PortType_LED = 0x03,    /**< LED / IO 类端口 */
} PortTypeC;

/* ===============================
 * Packet Struct
 * =============================== */
#define PACKET_HEADER_SIZE 2u
#define PACKET_TAIL_SIZE 2u
#define PACKET_HEADER_1 (0xBB)
#define PACKET_HEADER_2 (0xAA)
#define PACKET_TAIL_1_2 (0xEE)


/**
 * @brief 数据包额外固定开销（字节）
 * header(2B) + payload length(2B) + payload length rev(2B) + payload([0]) +
 * CRC16(2B) + Tail(2B)
 */
#define PACKET_OFFLOAD_SIZE 10u

/**
 * @brief 最小合法数据包长度
 */
#define PACKET_MIN_VALID_LEN (PACKET_OFFLOAD_SIZE + sizeof(PayloadHeader))

/** 最大 Payload 长度 */
#define PACKET_MAX_PAYLOAD_LEN 1200

/** 最大数据区长度 */
#define PACKET_MAX_DATALEN 1024

/** 最大端口数量 */
#define PACKET_MAX_PORTS_NUM 16

/* ===============================
 * Payload Header
 * =============================== */

/**
 * @brief Payload 通用头部
 *
 * 所有命令 Payload 的起始结构。
 */
typedef struct {
    u8 command;       /**< CommandType */
    u32 sequence;     /**< 包序号 */
    u32 timestamp_ms; /**< 时间戳（毫秒） */
    u32 error_code;   /**< 错误码（如有） */
    /* followed by OtherData */
} PayloadHeader;

STATIC_ASSERT(sizeof(PayloadHeader) == 13, "PayloadHeader size error");

/* ===============================
 * Version Info
 * =============================== */

/**
 * @brief 固件版本信息结构
 */
typedef struct {
    char PDN[16];       /**< 产品型号 */
    char Tag[8];        /**< 版本 Tag */
    char Commit[8];     /**< Git Commit 简写 */
    char BuildTime[24]; /**< 编译时间字符串 */
} VersionInfoC;

/* ===============================
 * MCU State
 * =============================== */

/**
 * @brief MCU 当前运行状态定义
 *
 * MCU 运行状态采用 32-bit 编码：
 * - Bit31 = 0 : Bridge（透传/桥接）模式
 * - Bit31 = 1 : DIVER（应用/驱动）模式
 * - 低 8-bit 表示具体子状态
 *
 * 该状态用于：
 * - PC 端判断 MCU 当前工作模式
 * - UI 状态显示
 * - 状态机与异常处理
 */
typedef enum {

    /* ===============================
     * Bridge (Passthrough) States
     * =============================== */

    /** Bridge 模式：空闲（未建立透传或未启动） */
    MCUState_Bridge_Idle = 0x00000000,

    /** Bridge 模式：透传运行中 */
    MCUState_Bridge_Running = 0x0000000F,

    /** Bridge 模式：错误状态 */
    MCUState_Bridge_Error = 0x000000FF,


    /* ===============================
     * DIVER (Application) States
     * =============================== */

    /** DIVER 模式：已进入 DIVER，但尚未完成配置 */
    MCUState_DIVER_Idle = 0x80000000,

    /** DIVER 模式：配置完成（参数/端口已就绪） */
    MCUState_DIVER_Configured = 0x80000001,

    /** DIVER 模式：应用/驱动逻辑运行中 */
    MCUState_DIVER_Running = 0x8000000F,

    /** DIVER 模式：运行错误状态 */
    MCUState_DIVER_Error = 0x800000FF,


    /* ===============================
     * Boundary
     * =============================== */

    /** 枚举边界值（非法状态） */
    MCUState_MAX = 0xFFFFFFFF,

} MCUStateC;


/* ===============================
 * Port Config
 * =============================== */
/**
 * @brief 通用端口配置结构
 *
 * 根据 port_type 不同，port_specific
 * 内容解释不同。
 */
typedef struct {
    u8 port_type;
    u8 port_specific[15];
} PortConfigC;

STATIC_ASSERT(sizeof(PortConfigC) == 16, "PortConfig size error");

/**
 * @brief 串口端口配置
 */
typedef struct {
    u8 port_type;
    u32 baud;
    u32 receive_frame_ms;
    u8 reserved[7];
} SerialPortConfigC;

STATIC_ASSERT(sizeof(SerialPortConfigC) == 16, "SerialPortConfig size error");

/**
 * @brief CAN 端口配置
 */
typedef struct {
    u8 port_type;
    u32 baud;
    u32 retry_time_ms;
    u8 reserved[7];
} CANPortConfigC;

STATIC_ASSERT(sizeof(CANPortConfigC) == 16, "CANPortConfig size error");

/**
 * @brief 端口数据包结构
 *
 * 用于 CommandWritePort / CommandUploadPort。
 */
typedef struct {
    i8 port_index; /**< 端口索引，IO 时为 -1 */
    u16 data_len;  /**< 数据长度 */
    u8 data[0];    /**< 可变长度数据 */
} DataPacket;

STATIC_ASSERT(sizeof(DataPacket) == 3, "DataPacket size must be 3 (packed)");

/**
 * @brief 打包 CAN ID / RTR / DLC 信息
 */
#define CANID_INFO_PACK(id_val, rtr_val, dlc_val)                            \
    ((uint16_t)(                                                             \
            (((uint16_t)(id_val)&0x7FF)) | (((uint16_t)(rtr_val)&1) << 11) | \
            (((uint16_t)(dlc_val)&0xF) << 12)))

#define CANID_INFO_GET_ID(info) ((info)&0x7FF)
#define CANID_INFO_GET_RTR(info) (((info) >> 11) & 1)
#define CANID_INFO_GET_DLC(info) (((info) >> 12) & 0xF)

/**
 * @brief CAN 数据结构
 */
typedef struct {
    u16 info;   /**< CANID / RTR / DLC */
    u8 data[8]; /**< CAN 数据 */
} CANData;

/* ===============================
 * Program Packet
 * =============================== */

/**
 * @brief 程序下载数据包结构
 *
 * 用于 CommandProgram，支持分片传输大程序。
 */
typedef struct {
    u32 total_len; /**< 程序总长度（字节） */
    u32 offset;    /**< 当前分片偏移量 */
    u16 chunk_len; /**< 当前分片长度 */
    u8 data[0];    /**< 可变长度程序数据 */
} ProgramPacket;

STATIC_ASSERT(
        sizeof(ProgramPacket) == 10,
        "ProgramPacket size must be 10 (packed)");

/**
 * @brief 内存交换数据包结构
 *
 * 用于 CommandMemoryExchange / CommandMemoryExchangeUpload。
 */
typedef struct {
    u16 data_len; /**< 数据长度 */
    u8 data[0];   /**< 可变长度交换数据 */
} MemoryExchangePacket;

STATIC_ASSERT(
        sizeof(MemoryExchangePacket) == 2,
        "MemoryExchangePacket size must be 2 (packed)");

#pragma pack(pop)

#ifdef __cplusplus
}
#endif
