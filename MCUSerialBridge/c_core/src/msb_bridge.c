#include "msb_bridge.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

#include "c_core_common.h"
#include "msb_bridge.h"
#include "msb_handle.h"
#include "msb_thread.h"


MCUSerialBridgeError msb_open(
        msb_handle** handle,
        const char* port,
        uint32_t baud)
{
    if (!handle || !port)
        return MSB_Error_Win_InvalidParam;

    MCUSerialBridgeError ret = msb_handle_init(handle);
    if (ret != MSB_Error_OK) {
        return ret;
    }
    if (!*handle) {
        return MSB_Error_Win_AllocFail;
    }

    // 保存端口名（规范化）
    if (strncmp(port, "\\\\.\\", 4) == 0) {
        // 已经是 \\.\COMx 形式
        strncpy((*handle)->port_name, port, sizeof((*handle)->port_name) - 1);
    } else {
        // 自动补 \\.\ 前缀（兼容 COM1 ~ COMxx）
        snprintf(
                (*handle)->port_name,
                sizeof((*handle)->port_name),
                "\\\\.\\%s",
                port);
    }
    (*handle)->port_name[sizeof((*handle)->port_name) - 1] = '\0';

    (*handle)->baud = baud;

    // 打开串口
    (*handle)->hComm = CreateFileA(
            (*handle)->port_name,
            GENERIC_READ | GENERIC_WRITE,
            0,  // 串口必须独占
            NULL,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            NULL);

    if ((*handle)->hComm == INVALID_HANDLE_VALUE) {
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotOpenPort;
    }

    // 配置串口
    DCB dcbSerialParams = {0};
    dcbSerialParams.DCBlength = sizeof(dcbSerialParams);
    if (!GetCommState((*handle)->hComm, &dcbSerialParams)) {
        CloseHandle((*handle)->hComm);
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotGetCommState;
    }

    dcbSerialParams.BaudRate = baud;
    dcbSerialParams.ByteSize = 8;
    dcbSerialParams.StopBits = ONESTOPBIT;
    dcbSerialParams.Parity = NOPARITY;

    if (!SetCommState((*handle)->hComm, &dcbSerialParams)) {
        CloseHandle((*handle)->hComm);
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotSetCommState;
    }

    // 配置超时
    COMMTIMEOUTS timeouts = {0};
    timeouts.ReadIntervalTimeout = MAXDWORD;
    timeouts.ReadTotalTimeoutConstant = 0;
    timeouts.ReadTotalTimeoutMultiplier = 0;
    timeouts.WriteTotalTimeoutConstant = 0;
    timeouts.WriteTotalTimeoutMultiplier = 0;
    SetCommTimeouts((*handle)->hComm, &timeouts);

    (*handle)->is_open = true;

    // 创建接收线程
    (*handle)->recv_thread =
            CreateThread(NULL, 0, recv_thread_func, *handle, 0, NULL);
    if (!(*handle)->recv_thread) {
        (*handle)->is_open = false;
        CloseHandle((*handle)->hComm);
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotCreateThread;
    }

    (*handle)->parse_thread =
            CreateThread(NULL, 0, parse_thread_func, *handle, 0, NULL);
    if (!(*handle)->parse_thread) {
        (*handle)->is_open = false;
        WaitForSingleObject((*handle)->recv_thread, INFINITE);
        CloseHandle((*handle)->recv_thread);
        CloseHandle((*handle)->hComm);
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotCreateThread;
    }

    // 创建发送线程
    (*handle)->send_thread =
            CreateThread(NULL, 0, send_thread_func, *handle, 0, NULL);
    if (!(*handle)->send_thread) {
        (*handle)->is_open = false;
        WaitForSingleObject((*handle)->recv_thread, INFINITE);
        WaitForSingleObject((*handle)->parse_thread, INFINITE);
        CloseHandle((*handle)->recv_thread);
        CloseHandle((*handle)->parse_thread);
        CloseHandle((*handle)->hComm);
        free(*handle);
        *handle = NULL;
        return MSB_Error_Win_CannotCreateThread;
    }

    DBG_PRINT("MSB Open OK");
    return MSB_Error_OK;
}

MCUSerialBridgeError msb_close(msb_handle* handle)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    handle->is_open = false;

    // 等待线程退出
    if (handle->recv_thread) {
        WaitForSingleObject(handle->recv_thread, INFINITE);
        CloseHandle(handle->recv_thread);
    }
    if (handle->parse_thread) {
        WaitForSingleObject(handle->send_thread, INFINITE);
        CloseHandle(handle->parse_thread);
    }
    if (handle->send_thread) {
        WaitForSingleObject(handle->send_thread, INFINITE);
        CloseHandle(handle->send_thread);
    }

    if (handle->hComm)
        CloseHandle(handle->hComm);

    return msb_handle_deinit(handle);
}

MCUSerialBridgeError msb_reset(msb_handle* handle, uint32_t timeout)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    // 调用 mcu_send_packet
    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandReset, NULL, 0, NULL, 0, timeout);

    DBG_PRINT("Reset finished with result[0x%08X]", ret);
    return ret;
}

MCUSerialBridgeError mcu_state(
        msb_handle* handle,
        MCUStateC* state,
        uint32_t timeout_ms)
{
    DBG_PRINT("State called");
    // -------------------------
    // 参数检查
    // -------------------------
    if (!handle) {
        return MSB_Error_Win_HandleNotFound;
    }
    if (!state) {
        return MSB_Error_Win_InvalidParam;
    }

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandState, 0, 0, state, sizeof(MCUStateC), timeout_ms);

    DBG_PRINT("State finished with result[0x%08X], state[0x%08X]", ret, state->raw);
    return ret;
}

MCUSerialBridgeError msb_version(
        msb_handle* handle,
        VersionInfoC* version,
        uint32_t timeout_ms)
{
    DBG_PRINT("Version called");

    // -------------------------
    // 参数检查
    // -------------------------
    if (!handle) {
        return MSB_Error_Win_HandleNotFound;
    }
    if (!version) {
        return MSB_Error_Win_InvalidParam;
    }

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle,
            CommandVersion,
            0,
            0,
            version,
            sizeof(VersionInfoC),
            timeout_ms);

    if (ret == MSB_Error_OK) {
        DBG_PRINT(
                "Version OK: PDN='%.*s', Tag='%.*s', Commit='%.*s', "
                "BuildTime='%.*s'",
                (int)sizeof(version->PDN),
                version->PDN,
                (int)sizeof(version->Tag),
                version->Tag,
                (int)sizeof(version->Commit),
                version->Commit,
                (int)sizeof(version->BuildTime),
                version->BuildTime);
    } else {
        DBG_PRINT("Version failed with error [0x%08X]", ret);
    }

    return ret;
}

MCUSerialBridgeError mcu_get_layout(
        msb_handle* handle,
        LayoutInfoC* layout,
        uint32_t timeout_ms)
{
    DBG_PRINT("GetLayout called");

    // -------------------------
    // 参数检查
    // -------------------------
    if (!handle) {
        return MSB_Error_Win_HandleNotFound;
    }
    if (!layout) {
        return MSB_Error_Win_InvalidParam;
    }

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle,
            CommandGetLayout,
            0,
            0,
            layout,
            sizeof(LayoutInfoC),
            timeout_ms);

    if (ret == MSB_Error_OK) {
        DBG_PRINT(
                "GetLayout OK: DI=%d, DO=%d, Ports=%d",
                layout->digital_input_count,
                layout->digital_output_count,
                layout->port_count);
    } else {
        DBG_PRINT("GetLayout failed with error [0x%08X]", ret);
    }

    return ret;
}

MCUSerialBridgeError msb_configure(
        msb_handle* handle,
        uint32_t num_ports,
        const PortConfigC* ports,
        uint32_t timeout_ms)
{
    DBG_PRINT("Configure called");
    // -------------------------
    // 参数检查
    // -------------------------
    if (!handle) {
        return MSB_Error_Win_HandleNotFound;
    }

    if (num_ports > 0 && ports == NULL) {
        return MSB_Error_Win_InvalidParam;
    }

    if (num_ports > PACKET_MAX_PORTS_NUM) {
        return MSB_Error_Config_PortNumOver;
    }

    // -------------------------
    // 构造配置包
    // -------------------------
    uint8_t other_data[PACKET_MAX_PAYLOAD_LEN];
    uint32_t other_data_len = 0;

    uint32_t offset = 0;
    memcpy(other_data + offset, &num_ports, sizeof(uint32_t));
    offset += sizeof(uint32_t);

    if (num_ports > 0) {
        uint32_t ports_size = num_ports * sizeof(PortConfigC);
        memcpy(other_data + offset, ports, ports_size);
        offset += ports_size;
    }

    static const uint32_t DefaultConfigureTimeout = 500;
    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle,
            CommandConfigure,
            other_data,
            offset,
            NULL,
            0,
            DefaultConfigureTimeout);

    DBG_PRINT("Configure finished with result[0x%08X]", ret);
    return ret;
}

// --------------------
// 启动 MCU
// --------------------
MCUSerialBridgeError msb_start(msb_handle* handle, uint32_t timeout_ms)
{
    DBG_PRINT("Start called");

    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandStart, NULL, 0, NULL, 0, timeout_ms);

    DBG_PRINT("Start finished with result[0x%08X]", ret);
    return ret;
}

// --------------------
// 启用 Wire Tap
// --------------------
MCUSerialBridgeError msb_enable_wire_tap(msb_handle* handle, uint32_t timeout_ms)
{
    DBG_PRINT("EnableWireTap called");

    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandEnableWireTap, NULL, 0, NULL, 0, timeout_ms);

    DBG_PRINT("EnableWireTap finished with result[0x%08X]", ret);
    return ret;
}

// --------------------
// 写IO输出
// --------------------
MCUSerialBridgeError msb_write_output(
        msb_handle* handle,
        const uint8_t* outputs,
        uint32_t timeout_ms)
{
    DBG_PRINT("Write Output called");

    if (!handle || !outputs)
        return MSB_Error_Win_InvalidParam;

    // 调用 mcu_send_packet
    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandWriteOutput, outputs, 4, NULL, 0, timeout_ms);

    DBG_PRINT("Write Output finished with result[0x%08X]", ret);
    return ret;
}

MCUSerialBridgeError msb_read_input(
        msb_handle* handle,
        uint8_t* inputs,
        uint32_t timeout_ms)
{
    DBG_PRINT("Read Input called");

    if (!handle || !inputs)
        return MSB_Error_Win_InvalidParam;

    // 调用 mcu_send_packet
    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle, CommandReadInput, NULL, 0, inputs, 4, timeout_ms);

    DBG_PRINT("Read Input finished with result[0x%08X]", ret);
    return ret;
}

// --------------------
// 写Ports
// --------------------
MCUSerialBridgeError msb_write_port(
        msb_handle* handle,
        uint8_t port_index,
        const uint8_t* src_data,
        uint32_t src_data_len,
        uint32_t timeout_ms)
{
    DBG_PRINT(
            "WritePort[%u], len=%u, timeout=%u",
            port_index,
            src_data_len,
            timeout_ms);
    if (!handle || !src_data || src_data_len == 0)
        return MSB_Error_Win_InvalidParam;

    if (src_data_len > PACKET_MAX_DATALEN) {
        return MSB_Error_Proto_FrameTooLong;
    }

    // 构建DataPacket
    uint8_t packet_buf[PACKET_MAX_PAYLOAD_LEN];
    DataPacket* pkt = (DataPacket*)packet_buf;
    pkt->port_index = port_index;
    pkt->data_len = (uint16_t)src_data_len;

    memcpy(pkt->data, src_data, src_data_len);

    // 调用 mcu_send_packet
    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle,
            CommandWritePort,
            packet_buf,
            sizeof(DataPacket) + src_data_len,
            NULL,
            0,
            timeout_ms);

    DBG_PRINT("WritePort finished with result[0x%08X]", ret);
    return ret;
}

MCUSerialBridgeError msb_read_port(
        msb_handle* handle,
        uint8_t port_index,
        uint8_t* dst_data,
        uint32_t dst_capacity,
        uint32_t* out_length,
        uint32_t timeout_ms)
{
    DBG_PRINT("ReadPort[%u], timeout[%u]", port_index, timeout_ms);
    if (!handle || !handle->is_open)
        return MSB_Error_Win_HandleNotFound;

    if (!dst_data || !out_length)
        return MSB_Error_Win_InvalidParam;

    if (port_index >= PACKET_MAX_PORTS_NUM)
        return MSB_Error_Config_PortNumOver;

    PortQueue* q = &handle->ports[port_index];

retry:
    /* ---------- 队列非空：直接取一帧 ---------- */
    if (q->head != q->tail) {
        PortDataFrame* frame = &q->queue[q->tail];
        uint32_t frame_len = frame->len;

        /* 用户缓冲区不够 */
        if (dst_capacity < frame_len) {
            *out_length = 0;
            return MSB_Error_Win_UserBufferTooSmall;
        }

        memcpy(dst_data, frame->data, frame_len);
        *out_length = frame_len;

        q->tail++;  // uint8_t 自动 wrap

        DBG_PRINT("ReadPort result, OK");
        return MSB_Error_OK;
    }

    /* ---------- 队列为空 ---------- */
    if (timeout_ms == 0) {
        *out_length = 0;
        DBG_PRINT("ReadPort result, No Data");
        return MSB_Error_NoData;  // 非阻塞模式
    }

    /* ---------- 等待新数据 ---------- */
    DWORD wait_ret = WaitForSingleObject(q->data_event, timeout_ms);
    if (wait_ret == WAIT_OBJECT_0) {
        /* 有新数据，回到 retry 再取 */
        goto retry;
    }

    if (wait_ret == WAIT_TIMEOUT) {
        *out_length = 0;
        DBG_PRINT("ReadPort result, No Data");
        return MSB_Error_NoData;
    }

    return MSB_Error_Win_Unknown;
}

DLL_EXPORT MCUSerialBridgeError msb_register_port_data_callback(
        msb_handle* handle,
        uint8_t port_index,
        msb_on_port_data_callback_function_t callback,
        void* user_ctx)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    if (port_index >= PACKET_MAX_PORTS_NUM)
        return MSB_Error_Config_PortNumOver;

    handle->port_data_callback[port_index] = 0;
    handle->port_data_callback_ctx[port_index] = user_ctx;
    handle->port_data_callback[port_index] = callback;
    DBG_PRINT(
            "Registered port[%u] callback to[0x%08X]",
            port_index,
            (uint32_t)(size_t)(void*)callback);

    return MSB_Error_OK;
}

// --------------------
// 下载程序到 MCU
// --------------------
#define PROGRAM_CHUNK_SIZE 512  // 每次传输的最大分片大小

MCUSerialBridgeError msb_program(
        msb_handle* handle,
        const uint8_t* program_bytes,
        uint32_t program_len,
        uint32_t timeout_ms)
{
    DBG_PRINT("Program called, len=%u", program_len);

    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    // 如果 program_bytes 为 NULL 或 program_len 为 0，发送空程序包（透传模式）
    if (program_bytes == NULL || program_len == 0) {
        DBG_PRINT("Program: switching to passthrough mode (empty program)");

        uint8_t packet_buf[sizeof(ProgramPacket)];
        ProgramPacket* pkt = (ProgramPacket*)packet_buf;
        pkt->total_len = 0;
        pkt->offset = 0;
        pkt->chunk_len = 0;

        MCUSerialBridgeError ret = mcu_send_packet_and_wait(
                handle,
                CommandProgram,
                packet_buf,
                sizeof(ProgramPacket),
                NULL,
                0,
                timeout_ms);

        DBG_PRINT("Program (empty) finished with result[0x%08X]", ret);
        return ret;
    }

    // 分片传输程序
    uint32_t offset = 0;
    while (offset < program_len) {
        uint32_t remaining = program_len - offset;
        uint16_t chunk_len =
                (remaining > PROGRAM_CHUNK_SIZE) ? PROGRAM_CHUNK_SIZE : (uint16_t)remaining;

        // 构建 ProgramPacket
        uint8_t packet_buf[sizeof(ProgramPacket) + PROGRAM_CHUNK_SIZE];
        ProgramPacket* pkt = (ProgramPacket*)packet_buf;
        pkt->total_len = program_len;
        pkt->offset = offset;
        pkt->chunk_len = chunk_len;
        memcpy(pkt->data, program_bytes + offset, chunk_len);

        DBG_PRINT(
                "Program chunk: offset=%u, chunk_len=%u, total=%u",
                offset,
                chunk_len,
                program_len);

        MCUSerialBridgeError ret = mcu_send_packet_and_wait(
                handle,
                CommandProgram,
                packet_buf,
                sizeof(ProgramPacket) + chunk_len,
                NULL,
                0,
                timeout_ms);

        if (ret != MSB_Error_OK) {
            DBG_PRINT("Program chunk failed at offset=%u, error=0x%08X", offset, ret);
            return ret;
        }

        offset += chunk_len;
    }

    DBG_PRINT("Program finished, total %u bytes transferred", program_len);
    return MSB_Error_OK;
}

// --------------------
// PC → MCU 内存交换（UpperIO）
// --------------------
MCUSerialBridgeError msb_memory_upper_io(
        msb_handle* handle,
        const uint8_t* data,
        uint32_t data_len,
        uint32_t timeout_ms)
{
    DBG_PRINT("MemoryUpperIO called, len=%u", data_len);

    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    if (!data || data_len == 0)
        return MSB_Error_Win_InvalidParam;

    if (data_len > PACKET_MAX_DATALEN)
        return MSB_Error_Proto_FrameTooLong;

    // 构建 MemoryExchangePacket
    uint8_t packet_buf[sizeof(MemoryExchangePacket) + PACKET_MAX_DATALEN];
    MemoryExchangePacket* pkt = (MemoryExchangePacket*)packet_buf;
    pkt->data_len = (uint16_t)data_len;
    memcpy(pkt->data, data, data_len);

    MCUSerialBridgeError ret = mcu_send_packet_and_wait(
            handle,
            CommandMemoryUpperIO,
            packet_buf,
            sizeof(MemoryExchangePacket) + data_len,
            NULL,
            0,
            timeout_ms);

    DBG_PRINT("MemoryUpperIO finished with result[0x%08X]", ret);
    return ret;
}

// --------------------
// 注册 LowerIO 回调
// --------------------
MCUSerialBridgeError msb_register_memory_lower_io_callback(
        msb_handle* handle,
        msb_on_memory_lower_io_callback_function_t callback,
        void* user_ctx)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    handle->memory_lower_io_callback = NULL;
    handle->memory_lower_io_callback_ctx = user_ctx;
    handle->memory_lower_io_callback = callback;

    DBG_PRINT(
            "Registered memory_lower_io callback to[0x%08X]",
            (uint32_t)(size_t)(void*)callback);

    return MSB_Error_OK;
}

// --------------------
// 注册 Console WriteLine 回调
// --------------------
MCUSerialBridgeError msb_register_console_writeline_callback(
        msb_handle* handle,
        msb_on_console_writeline_callback_function_t callback,
        void* user_ctx)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    handle->console_writeline_callback = NULL;
    handle->console_writeline_callback_ctx = user_ctx;
    handle->console_writeline_callback = callback;

    DBG_PRINT(
            "Registered console_writeline callback to[0x%08X]",
            (uint32_t)(size_t)(void*)callback);

    return MSB_Error_OK;
}

// --------------------
// 获取 API 函数指针
// --------------------
void mcu_serial_bridge_get_api(MCUSerialBridgeAPI* api)
{
    if (!api)
        return;
    api->msb_open = msb_open;
    api->msb_close = msb_close;
    api->mcu_state = mcu_state;
    api->msb_version = msb_version;
    api->mcu_get_layout = mcu_get_layout;
    api->msb_configure = msb_configure;
    api->msb_read_input = msb_read_input;
    api->msb_write_output = msb_write_output;
    api->msb_read_port = msb_read_port;
    api->msb_register_port_data_callback = msb_register_port_data_callback;
    api->msb_write_port = msb_write_port;
    api->msb_reset = msb_reset;
    api->msb_start = msb_start;
    api->msb_program = msb_program;
    api->msb_enable_wire_tap = msb_enable_wire_tap;
    api->msb_memory_upper_io = msb_memory_upper_io;
    api->msb_register_memory_lower_io_callback = msb_register_memory_lower_io_callback;
    api->msb_register_console_writeline_callback = msb_register_console_writeline_callback;
}