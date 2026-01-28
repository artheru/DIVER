/**
 * @file mbl_protocol.h
 * @brief MCU Bootloader 协议定义
 *
 * 定义 Bootloader 通讯协议的帧结构、命令类型、响应类型等常量。
 * 协议特点：固定 92 字节帧长，一问一答模式（类似 Modbus）。
 */

#ifndef MBL_PROTOCOL_H
#define MBL_PROTOCOL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*==============================================================================
 * 帧结构常量
 *============================================================================*/

/** 帧头 */
#define MBL_FRAME_HEADER_0      0xAA
#define MBL_FRAME_HEADER_1      0xBB
#define MBL_FRAME_HEADER        ((uint16_t)0xBBAA)  // 小端序

/** 帧尾 */
#define MBL_FRAME_TAIL_0        0xEE
#define MBL_FRAME_TAIL_1        0xEE
#define MBL_FRAME_TAIL          ((uint16_t)0xEEEE)

/** 帧长度 */
#define MBL_FRAME_LEN           92      // 总帧长
#define MBL_PAYLOAD_LEN         80      // Payload 固定长度
#define MBL_HEADER_LEN          2       // 帧头长度
#define MBL_CMD_LEN             4       // CommandType 长度
#define MBL_CRC_LEN             4       // CRC32 长度
#define MBL_TAIL_LEN            2       // 帧尾长度

/** 波特率探测同步帧 */
#define MBL_SYNC_TX_0           0xAA
#define MBL_SYNC_TX_1           0x55
#define MBL_SYNC_TX_2           0xA5
#define MBL_SYNC_TX_3           0xA5

#define MBL_SYNC_RX_0           0x55
#define MBL_SYNC_RX_1           0xAA
#define MBL_SYNC_RX_2           0x5A
#define MBL_SYNC_RX_3           0x5A

#define MBL_SYNC_LEN            4       // 同步帧长度

/*==============================================================================
 * 命令类型枚举（PC → MCU）
 *============================================================================*/

typedef enum {
    /** 读取下位机固件信息 */
    MBL_CMD_READ  = 0x00000001,

    /** 擦除固件（带参数校验） */
    MBL_CMD_ERASE = 0x00000002,

    /** 写入固件数据（分块） */
    MBL_CMD_WRITE = 0x00000003,

    /** 退出 Bootloader / 重启进入 App */
    MBL_CMD_EXIT  = 0x00000004,
} MBL_CommandType;

/*==============================================================================
 * 响应类型枚举（MCU → PC）
 *============================================================================*/

typedef enum {
    /** Read 成功响应 */
    MBL_RSP_READ_OK   = 0x11,

    /** Erase 成功响应 */
    MBL_RSP_ERASE_OK  = 0x12,

    /** Write 成功响应 */
    MBL_RSP_WRITE_OK  = 0x13,

    /** Exit 成功响应 */
    MBL_RSP_EXIT_OK   = 0x14,

    /** Read 失败响应 */
    MBL_RSP_READ_ERR  = 0x81,

    /** Erase 失败响应 */
    MBL_RSP_ERASE_ERR = 0x82,

    /** Write 失败响应 */
    MBL_RSP_WRITE_ERR = 0x83,

    /** Exit 失败响应 */
    MBL_RSP_EXIT_ERR  = 0x84,
} MBL_ResponseType;

/*==============================================================================
 * 候选波特率列表
 *============================================================================*/

/** 自动探测波特率列表 */
static const uint32_t MBL_CANDIDATE_BAUDS[] = {
    460800,
    115200,
    1000000,
    230400,
};
#define MBL_CANDIDATE_BAUDS_COUNT \
    (sizeof(MBL_CANDIDATE_BAUDS) / sizeof(MBL_CANDIDATE_BAUDS[0]))

/*==============================================================================
 * 数据结构
 *============================================================================*/

/**
 * @brief 下位机固件信息（CommandRead 响应）
 */
typedef struct {
    /** 固件是否有效：1=有效，0=无效，其他=未知 */
    int32_t is_valid;

    /** 产品型号（最多 16 字符） */
    char pdn[16];

    /** 标签版本（最多 8 字符） */
    char tag[8];

    /** Git Commit（最多 8 字符） */
    char commit[8];

    /** 编译时间（最多 24 字符） */
    char build_time[24];

    /** 固件长度（字节） */
    uint32_t app_length;

    /** 固件 CRC32 */
    uint32_t app_crc32;

    /** 固件信息区 CRC32 */
    uint32_t app_info_crc32;
} MBL_FirmwareInfo;

/**
 * @brief Erase 命令参数
 */
typedef struct {
    /** 标签版本（8 字节） */
    uint8_t tag[8];

    /** Git Commit（8 字节） */
    uint8_t commit[8];

    /** 编译时间（24 字节） */
    uint8_t build_time[24];

    /** 固件总长度 */
    uint32_t app_length;

    /** 固件 CRC32 */
    uint32_t app_crc32;

    /** 加密固件的前 32 字节 */
    uint8_t first_32_bytes[32];
} MBL_EraseParams;

/**
 * @brief Write 命令参数
 */
typedef struct {
    /** 当前块在固件中的偏移 */
    uint32_t offset;

    /** 固件总长度 */
    uint32_t total_length;

    /** 当前块数据长度（最大 64 字节） */
    uint32_t chunk_length;

    /** 当前块数据 */
    uint8_t chunk_data[64];
} MBL_WriteParams;

/**
 * @brief MCU 返回的错误信息
 */
typedef struct {
    /** 错误码 */
    uint32_t error_code;

    /** 错误消息（最多 76 字符） */
    char error_message[76];
} MBL_McuError;

#ifdef __cplusplus
}
#endif

#endif /* MBL_PROTOCOL_H */
