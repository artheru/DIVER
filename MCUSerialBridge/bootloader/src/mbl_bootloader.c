/**
 * @file mbl_bootloader.c
 * @brief MCU Bootloader 上位机通讯库核心实现
 */

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

#include "mbl_bootloader.h"
#include "mbl_error.h"
#include "mbl_protocol.h"

/*==============================================================================
 * 内部句柄结构
 *============================================================================*/

struct mbl_handle {
    /** 串口名称 */
    char port_name[64];

    /** 当前波特率 */
    uint32_t baud;

    /** Windows 串口句柄 */
    HANDLE hComm;

    /** 是否已打开 */
    bool is_open;

    /** 进度回调 */
    mbl_progress_callback_t progress_callback;

    /** 回调用户上下文 */
    void* callback_ctx;
};

/*==============================================================================
 * CRC32 计算（与 Python zlib.crc32 兼容）
 *============================================================================*/

static uint32_t crc32_table[256];
static bool crc32_table_initialized = false;

static void init_crc32_table(void) {
    if (crc32_table_initialized) return;

    for (uint32_t i = 0; i < 256; i++) {
        uint32_t crc = i;
        for (int j = 0; j < 8; j++) {
            if (crc & 1) {
                crc = (crc >> 1) ^ 0xEDB88320;
            } else {
                crc >>= 1;
            }
        }
        crc32_table[i] = crc;
    }
    crc32_table_initialized = true;
}

static uint32_t calc_crc32(const uint8_t* data, size_t len) {
    init_crc32_table();

    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        crc = crc32_table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
    }
    return crc ^ 0xFFFFFFFF;
}

/*==============================================================================
 * 错误码描述
 *============================================================================*/

const char* mbl_error_to_string(MCUBootloaderError error) {
    switch (error) {
        case MBL_Error_OK:
            return "OK";
        case MBL_Error_Win_OpenFailed:
            return "Failed to open serial port";
        case MBL_Error_Win_ConfigFailed:
            return "Failed to configure serial port";
        case MBL_Error_Win_WriteFailed:
            return "Failed to write to serial port";
        case MBL_Error_Win_ReadFailed:
            return "Failed to read from serial port";
        case MBL_Error_Win_InvalidParam:
            return "Invalid parameter";
        case MBL_Error_Win_HandleNotFound:
            return "Handle not found or not initialized";
        case MBL_Error_Win_OutOfMemory:
            return "Out of memory";
        case MBL_Error_Win_Timeout:
            return "Operation timeout";
        case MBL_Error_Win_ProbeFailed:
            return "Baudrate probe failed";
        case MBL_Error_Win_AlreadyOpen:
            return "Serial port already open";
        case MBL_Error_Win_NotOpen:
            return "Serial port not open";
        case MBL_Error_Proto_HeaderError:
            return "Frame header error";
        case MBL_Error_Proto_TailError:
            return "Frame tail error";
        case MBL_Error_Proto_CRCError:
            return "CRC32 checksum error";
        case MBL_Error_Proto_LengthError:
            return "Frame length error";
        case MBL_Error_Proto_UnknownResponse:
            return "Unknown response type";
        case MBL_Error_Proto_ResponseMismatch:
            return "Response type mismatch";
        case MBL_Error_MCU_ReadFailed:
            return "MCU Read command failed";
        case MBL_Error_MCU_EraseFailed:
            return "MCU Erase command failed";
        case MBL_Error_MCU_WriteFailed:
            return "MCU Write command failed";
        case MBL_Error_MCU_ExitFailed:
            return "MCU Exit command failed";
        default:
            return "Unknown error";
    }
}

/*==============================================================================
 * 串口操作
 *============================================================================*/

/**
 * @brief 配置串口参数
 */
static MCUBootloaderError configure_serial(HANDLE hComm, uint32_t baud) {
    DCB dcb = {0};
    dcb.DCBlength = sizeof(DCB);

    if (!GetCommState(hComm, &dcb)) {
        return MBL_Error_Win_ConfigFailed;
    }

    dcb.BaudRate = baud;
    dcb.ByteSize = 8;
    dcb.Parity = NOPARITY;
    dcb.StopBits = ONESTOPBIT;
    dcb.fBinary = TRUE;
    dcb.fParity = FALSE;
    dcb.fOutxCtsFlow = FALSE;
    dcb.fOutxDsrFlow = FALSE;
    dcb.fDtrControl = DTR_CONTROL_DISABLE;
    dcb.fRtsControl = RTS_CONTROL_DISABLE;
    dcb.fOutX = FALSE;
    dcb.fInX = FALSE;

    if (!SetCommState(hComm, &dcb)) {
        return MBL_Error_Win_ConfigFailed;
    }

    // 设置超时参数
    COMMTIMEOUTS timeouts = {0};
    timeouts.ReadIntervalTimeout = 50;
    timeouts.ReadTotalTimeoutConstant = 50;
    timeouts.ReadTotalTimeoutMultiplier = 10;
    timeouts.WriteTotalTimeoutConstant = 50;
    timeouts.WriteTotalTimeoutMultiplier = 10;

    if (!SetCommTimeouts(hComm, &timeouts)) {
        return MBL_Error_Win_ConfigFailed;
    }

    return MBL_Error_OK;
}

/**
 * @brief 打开串口（内部函数）
 */
static MCUBootloaderError open_serial(mbl_handle* h, uint32_t baud) {
    char port_path[128];
    snprintf(port_path, sizeof(port_path), "\\\\.\\%s", h->port_name);

    h->hComm = CreateFileA(port_path,
                           GENERIC_READ | GENERIC_WRITE,
                           0,
                           NULL,
                           OPEN_EXISTING,
                           0,
                           NULL);

    if (h->hComm == INVALID_HANDLE_VALUE) {
        return MBL_Error_Win_OpenFailed;
    }

    MCUBootloaderError err = configure_serial(h->hComm, baud);
    if (err != MBL_Error_OK) {
        CloseHandle(h->hComm);
        h->hComm = INVALID_HANDLE_VALUE;
        return err;
    }

    h->baud = baud;
    h->is_open = true;
    return MBL_Error_OK;
}

/**
 * @brief 清空串口缓冲区
 */
static void flush_serial(HANDLE hComm) {
    PurgeComm(hComm, PURGE_RXCLEAR | PURGE_TXCLEAR);
}

/**
 * @brief 写串口数据
 */
static MCUBootloaderError
write_serial(HANDLE hComm, const uint8_t* data, size_t len) {
    DWORD written = 0;
    if (!WriteFile(hComm, data, (DWORD)len, &written, NULL) ||
        written != len) {
        return MBL_Error_Win_WriteFailed;
    }
    FlushFileBuffers(hComm);
    return MBL_Error_OK;
}

/**
 * @brief 读串口数据（带超时）
 */
static MCUBootloaderError read_serial_timeout(
        HANDLE hComm,
        uint8_t* buffer,
        size_t expected_len,
        uint32_t timeout_ms) {
    DWORD start_tick = GetTickCount();
    size_t total_read = 0;

    while (total_read < expected_len) {
        DWORD elapsed = GetTickCount() - start_tick;
        if (elapsed >= timeout_ms) {
            return MBL_Error_Win_Timeout;
        }

        DWORD bytes_read = 0;
        if (!ReadFile(hComm,
                      buffer + total_read,
                      (DWORD)(expected_len - total_read),
                      &bytes_read,
                      NULL)) {
            return MBL_Error_Win_ReadFailed;
        }
        total_read += bytes_read;

        if (total_read < expected_len) {
            Sleep(1);
        }
    }

    return MBL_Error_OK;
}

/*==============================================================================
 * 波特率探测
 *============================================================================*/

static MCUBootloaderError probe_baudrate(mbl_handle* h) {
    const uint8_t sync_tx[] = {
            MBL_SYNC_TX_0, MBL_SYNC_TX_1, MBL_SYNC_TX_2, MBL_SYNC_TX_3};
    const uint8_t sync_rx_expected[] = {
            MBL_SYNC_RX_0, MBL_SYNC_RX_1, MBL_SYNC_RX_2, MBL_SYNC_RX_3};

    for (size_t i = 0; i < MBL_CANDIDATE_BAUDS_COUNT; i++) {
        uint32_t baud = MBL_CANDIDATE_BAUDS[i];

        // 打开串口
        MCUBootloaderError err = open_serial(h, baud);
        if (err != MBL_Error_OK) {
            continue;
        }

        // 尝试 2 次
        bool success = false;
        for (int attempt = 0; attempt < 2; attempt++) {
            flush_serial(h->hComm);

            // 发送同步帧
            err = write_serial(h->hComm, sync_tx, MBL_SYNC_LEN);
            if (err != MBL_Error_OK) {
                break;
            }

            // 读取响应（100ms 超时）
            uint8_t sync_rx[MBL_SYNC_LEN];
            err = read_serial_timeout(h->hComm, sync_rx, MBL_SYNC_LEN, 100);
            if (err == MBL_Error_OK &&
                memcmp(sync_rx, sync_rx_expected, MBL_SYNC_LEN) == 0) {
                success = true;
                break;
            }

            Sleep(50);
        }

        if (success) {
            return MBL_Error_OK;
        }

        // 关闭串口，尝试下一个波特率
        CloseHandle(h->hComm);
        h->hComm = INVALID_HANDLE_VALUE;
        h->is_open = false;
    }

    return MBL_Error_Win_ProbeFailed;
}

/*==============================================================================
 * 帧发送与接收
 *============================================================================*/

/**
 * @brief 构建并发送帧
 */
static MCUBootloaderError send_frame(
        mbl_handle* h,
        uint32_t command_type,
        const uint8_t* payload,
        size_t payload_len) {
    if (!h || !h->is_open) {
        return MBL_Error_Win_NotOpen;
    }

    // 构建帧
    uint8_t frame[MBL_FRAME_LEN];
    memset(frame, 0, MBL_FRAME_LEN);

    // 帧头
    frame[0] = MBL_FRAME_HEADER_0;
    frame[1] = MBL_FRAME_HEADER_1;

    // CommandType（小端序）
    frame[2] = (uint8_t)(command_type & 0xFF);
    frame[3] = (uint8_t)((command_type >> 8) & 0xFF);
    frame[4] = (uint8_t)((command_type >> 16) & 0xFF);
    frame[5] = (uint8_t)((command_type >> 24) & 0xFF);

    // Payload（最多 80 字节）
    size_t copy_len = (payload_len > MBL_PAYLOAD_LEN) ? MBL_PAYLOAD_LEN
                                                      : payload_len;
    if (payload && copy_len > 0) {
        memcpy(frame + 6, payload, copy_len);
    }

    // CRC32（覆盖 CommandType + Payload，即 frame[2] 到 frame[85]）
    uint32_t crc = calc_crc32(frame + 2, MBL_CMD_LEN + MBL_PAYLOAD_LEN);
    frame[86] = (uint8_t)(crc & 0xFF);
    frame[87] = (uint8_t)((crc >> 8) & 0xFF);
    frame[88] = (uint8_t)((crc >> 16) & 0xFF);
    frame[89] = (uint8_t)((crc >> 24) & 0xFF);

    // 帧尾
    frame[90] = MBL_FRAME_TAIL_0;
    frame[91] = MBL_FRAME_TAIL_1;

    // 清空接收缓冲后发送
    flush_serial(h->hComm);
    return write_serial(h->hComm, frame, MBL_FRAME_LEN);
}

/**
 * @brief 接收并解析帧
 *
 * @param h           句柄
 * @param timeout_ms  超时时间
 * @param[out] out_cmd_type  返回响应 CommandType
 * @param[out] out_payload   返回 Payload 数据（80 字节）
 */
static MCUBootloaderError recv_frame(
        mbl_handle* h,
        uint32_t timeout_ms,
        uint32_t* out_cmd_type,
        uint8_t* out_payload) {
    if (!h || !h->is_open) {
        return MBL_Error_Win_NotOpen;
    }

    // 接收完整帧
    uint8_t frame[MBL_FRAME_LEN];
    MCUBootloaderError err =
            read_serial_timeout(h->hComm, frame, MBL_FRAME_LEN, timeout_ms);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 检查帧头
    if (frame[0] != MBL_FRAME_HEADER_0 || frame[1] != MBL_FRAME_HEADER_1) {
        return MBL_Error_Proto_HeaderError;
    }

    // 检查帧尾
    if (frame[90] != MBL_FRAME_TAIL_0 || frame[91] != MBL_FRAME_TAIL_1) {
        return MBL_Error_Proto_TailError;
    }

    // CRC32 校验
    uint32_t crc_recv = (uint32_t)frame[86] | ((uint32_t)frame[87] << 8) |
                        ((uint32_t)frame[88] << 16) |
                        ((uint32_t)frame[89] << 24);
    uint32_t crc_calc = calc_crc32(frame + 2, MBL_CMD_LEN + MBL_PAYLOAD_LEN);
    if (crc_recv != crc_calc) {
        return MBL_Error_Proto_CRCError;
    }

    // 解析 CommandType
    *out_cmd_type = (uint32_t)frame[2] | ((uint32_t)frame[3] << 8) |
                    ((uint32_t)frame[4] << 16) | ((uint32_t)frame[5] << 24);

    // 复制 Payload
    if (out_payload) {
        memcpy(out_payload, frame + 6, MBL_PAYLOAD_LEN);
    }

    return MBL_Error_OK;
}

/*==============================================================================
 * 公开 API 实现
 *============================================================================*/

MBL_EXPORT MCUBootloaderError
mbl_open(mbl_handle** handle, const char* port, uint32_t baud) {
    if (!handle || !port) {
        return MBL_Error_Win_InvalidParam;
    }

    // 分配句柄
    mbl_handle* h = (mbl_handle*)calloc(1, sizeof(mbl_handle));
    if (!h) {
        return MBL_Error_Win_OutOfMemory;
    }

    strncpy(h->port_name, port, sizeof(h->port_name) - 1);
    h->hComm = INVALID_HANDLE_VALUE;
    h->is_open = false;
    h->progress_callback = NULL;
    h->callback_ctx = NULL;

    MCUBootloaderError err;

    if (baud == 0) {
        // 自动探测波特率
        err = probe_baudrate(h);
    } else {
        // 使用指定波特率
        err = open_serial(h, baud);
    }

    if (err != MBL_Error_OK) {
        free(h);
        return err;
    }

    *handle = h;
    return MBL_Error_OK;
}

MBL_EXPORT MCUBootloaderError mbl_close(mbl_handle* handle) {
    if (!handle) {
        return MBL_Error_Win_InvalidParam;
    }

    if (handle->hComm != INVALID_HANDLE_VALUE) {
        CloseHandle(handle->hComm);
    }

    free(handle);
    return MBL_Error_OK;
}

MBL_EXPORT uint32_t mbl_get_baudrate(mbl_handle* handle) {
    if (!handle) return 0;
    return handle->baud;
}

MBL_EXPORT MCUBootloaderError mbl_register_progress_callback(
        mbl_handle* handle,
        mbl_progress_callback_t callback,
        void* user_ctx) {
    if (!handle) {
        return MBL_Error_Win_InvalidParam;
    }

    handle->progress_callback = callback;
    handle->callback_ctx = user_ctx;
    return MBL_Error_OK;
}

/*==============================================================================
 * 命令实现
 *============================================================================*/

MBL_EXPORT MCUBootloaderError mbl_command_read(
        mbl_handle* handle,
        MBL_FirmwareInfo* info,
        uint32_t timeout_ms) {
    if (!handle || !info) {
        return MBL_Error_Win_InvalidParam;
    }

    // 发送 Read 命令（无 Payload）
    MCUBootloaderError err = send_frame(handle, MBL_CMD_READ, NULL, 0);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 接收响应
    uint32_t cmd_type;
    uint8_t payload[MBL_PAYLOAD_LEN];
    err = recv_frame(handle, timeout_ms, &cmd_type, payload);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 检查响应类型
    if (cmd_type == MBL_RSP_READ_OK) {
        // 解析响应 Payload
        // [0-15] PDN, [16-23] Tag, [24-31] Commit, [32-55] BuildTime
        // [56-59] AppLength, [60-63] AppCRC32, [64-67] AppInfoCRC32
        // [68-71] IsValid
        memset(info, 0, sizeof(MBL_FirmwareInfo));
        memcpy(info->pdn, payload, 16);
        memcpy(info->tag, payload + 16, 8);
        memcpy(info->commit, payload + 24, 8);
        memcpy(info->build_time, payload + 32, 24);

        info->app_length = (uint32_t)payload[56] |
                           ((uint32_t)payload[57] << 8) |
                           ((uint32_t)payload[58] << 16) |
                           ((uint32_t)payload[59] << 24);

        info->app_crc32 = (uint32_t)payload[60] | ((uint32_t)payload[61] << 8) |
                          ((uint32_t)payload[62] << 16) |
                          ((uint32_t)payload[63] << 24);

        info->app_info_crc32 = (uint32_t)payload[64] |
                               ((uint32_t)payload[65] << 8) |
                               ((uint32_t)payload[66] << 16) |
                               ((uint32_t)payload[67] << 24);

        info->is_valid = (int32_t)((uint32_t)payload[68] |
                                   ((uint32_t)payload[69] << 8) |
                                   ((uint32_t)payload[70] << 16) |
                                   ((uint32_t)payload[71] << 24));

        return MBL_Error_OK;
    } else if (cmd_type == MBL_RSP_READ_ERR) {
        return MBL_Error_MCU_ReadFailed;
    } else {
        return MBL_Error_Proto_UnknownResponse;
    }
}

MBL_EXPORT MCUBootloaderError mbl_command_erase(
        mbl_handle* handle,
        const MBL_EraseParams* params,
        uint32_t timeout_ms) {
    if (!handle || !params) {
        return MBL_Error_Win_InvalidParam;
    }

    // 构建 Payload
    // [0-7] Tag, [8-15] Commit, [16-39] BuildTime
    // [40-43] AppLength, [44-47] AppCRC32, [48-79] First32Bytes
    uint8_t payload[MBL_PAYLOAD_LEN];
    memset(payload, 0, MBL_PAYLOAD_LEN);

    memcpy(payload, params->tag, 8);
    memcpy(payload + 8, params->commit, 8);
    memcpy(payload + 16, params->build_time, 24);

    payload[40] = (uint8_t)(params->app_length & 0xFF);
    payload[41] = (uint8_t)((params->app_length >> 8) & 0xFF);
    payload[42] = (uint8_t)((params->app_length >> 16) & 0xFF);
    payload[43] = (uint8_t)((params->app_length >> 24) & 0xFF);

    payload[44] = (uint8_t)(params->app_crc32 & 0xFF);
    payload[45] = (uint8_t)((params->app_crc32 >> 8) & 0xFF);
    payload[46] = (uint8_t)((params->app_crc32 >> 16) & 0xFF);
    payload[47] = (uint8_t)((params->app_crc32 >> 24) & 0xFF);

    memcpy(payload + 48, params->first_32_bytes, 32);

    // 发送 Erase 命令
    MCUBootloaderError err =
            send_frame(handle, MBL_CMD_ERASE, payload, MBL_PAYLOAD_LEN);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 接收响应
    uint32_t cmd_type;
    uint8_t rsp_payload[MBL_PAYLOAD_LEN];
    err = recv_frame(handle, timeout_ms, &cmd_type, rsp_payload);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 检查响应类型
    if (cmd_type == MBL_RSP_ERASE_OK) {
        return MBL_Error_OK;
    } else if (cmd_type == MBL_RSP_ERASE_ERR) {
        return MBL_Error_MCU_EraseFailed;
    } else {
        return MBL_Error_Proto_UnknownResponse;
    }
}

MBL_EXPORT MCUBootloaderError mbl_command_write(
        mbl_handle* handle,
        uint32_t offset,
        uint32_t total_length,
        const uint8_t* chunk_data,
        uint32_t chunk_length,
        uint32_t timeout_ms) {
    if (!handle || !chunk_data) {
        return MBL_Error_Win_InvalidParam;
    }

    if (chunk_length > 64) {
        return MBL_Error_Win_InvalidParam;
    }

    // 构建 Payload
    // [0-3] Offset, [4-7] TotalLength, [8-11] ChunkLength, [12-75] ChunkData
    uint8_t payload[MBL_PAYLOAD_LEN];
    memset(payload, 0, MBL_PAYLOAD_LEN);

    payload[0] = (uint8_t)(offset & 0xFF);
    payload[1] = (uint8_t)((offset >> 8) & 0xFF);
    payload[2] = (uint8_t)((offset >> 16) & 0xFF);
    payload[3] = (uint8_t)((offset >> 24) & 0xFF);

    payload[4] = (uint8_t)(total_length & 0xFF);
    payload[5] = (uint8_t)((total_length >> 8) & 0xFF);
    payload[6] = (uint8_t)((total_length >> 16) & 0xFF);
    payload[7] = (uint8_t)((total_length >> 24) & 0xFF);

    payload[8] = (uint8_t)(chunk_length & 0xFF);
    payload[9] = (uint8_t)((chunk_length >> 8) & 0xFF);
    payload[10] = (uint8_t)((chunk_length >> 16) & 0xFF);
    payload[11] = (uint8_t)((chunk_length >> 24) & 0xFF);

    memcpy(payload + 12, chunk_data, chunk_length);

    // 发送 Write 命令
    MCUBootloaderError err =
            send_frame(handle, MBL_CMD_WRITE, payload, MBL_PAYLOAD_LEN);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 接收响应
    uint32_t cmd_type;
    uint8_t rsp_payload[MBL_PAYLOAD_LEN];
    err = recv_frame(handle, timeout_ms, &cmd_type, rsp_payload);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 检查响应类型
    if (cmd_type == MBL_RSP_WRITE_OK) {
        return MBL_Error_OK;
    } else if (cmd_type == MBL_RSP_WRITE_ERR) {
        return MBL_Error_MCU_WriteFailed;
    } else {
        return MBL_Error_Proto_UnknownResponse;
    }
}

MBL_EXPORT MCUBootloaderError
mbl_command_exit(mbl_handle* handle, uint32_t timeout_ms) {
    if (!handle) {
        return MBL_Error_Win_InvalidParam;
    }

    // 发送 Exit 命令（无 Payload）
    MCUBootloaderError err = send_frame(handle, MBL_CMD_EXIT, NULL, 0);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 接收响应
    uint32_t cmd_type;
    uint8_t payload[MBL_PAYLOAD_LEN];
    err = recv_frame(handle, timeout_ms, &cmd_type, payload);
    if (err != MBL_Error_OK) {
        return err;
    }

    // 检查响应类型
    if (cmd_type == MBL_RSP_EXIT_OK) {
        return MBL_Error_OK;
    } else if (cmd_type == MBL_RSP_EXIT_ERR) {
        return MBL_Error_MCU_ExitFailed;
    } else {
        return MBL_Error_Proto_UnknownResponse;
    }
}

/*==============================================================================
 * 便捷 API
 *============================================================================*/

MBL_EXPORT MCUBootloaderError mbl_write_firmware(
        mbl_handle* handle,
        const uint8_t* firmware,
        uint32_t firmware_len,
        uint32_t timeout_ms) {
    if (!handle || !firmware || firmware_len == 0) {
        return MBL_Error_Win_InvalidParam;
    }

    const uint32_t chunk_size = 64;
    uint32_t offset = 0;

    while (offset < firmware_len) {
        uint32_t remaining = firmware_len - offset;
        uint32_t chunk_len = (remaining > chunk_size) ? chunk_size : remaining;

        MCUBootloaderError err = mbl_command_write(
                handle, offset, firmware_len, firmware + offset, chunk_len,
                timeout_ms);

        if (err != MBL_Error_OK) {
            // 出错时回调
            if (handle->progress_callback) {
                int progress = (int)((offset * 100) / firmware_len);
                handle->progress_callback(progress, err, handle->callback_ctx);
            }
            return err;
        }

        offset += chunk_len;

        // 进度回调
        if (handle->progress_callback) {
            int progress = (int)((offset * 100) / firmware_len);
            handle->progress_callback(
                    progress, MBL_Error_OK, handle->callback_ctx);
        }
    }

    return MBL_Error_OK;
}
