#include "appl/control.h"

#include "appl/packet.h"
#include "appl/upload.h"
#include "bsp/digital_io.h"
#include "bsp/ports.h"
#include "common.h"
#include "hal/nvic.h"
#include "util/console.h"

typedef struct {
    union {
        USARTHandle serial;
        DirectCANHandle can;
    };
    PortConfigC config;  // 保存完整的端口配置
    bool valid;
    uint32_t pending_sequence;  // for serial can use other
    bool pending;               // for serial, can use other
} Handles;
Handles g_handles[PACKET_MAX_PORTS_NUM];

volatile uint32_t g_inputs;
volatile uint32_t g_outputs;

volatile MCUStateC g_mcu_state = {.raw = 0};  // Bridge, Idle, not configured
volatile bool g_wire_tap_enabled = false;

// DIVER 程序缓冲区
#define PROGRAM_BUFFER_MAX_SIZE (16 * 1024)  // 16KB 最大程序大小
static uint8_t program_buffer_storage[PROGRAM_BUFFER_MAX_SIZE];
uint8_t* g_program_buffer = program_buffer_storage;
uint32_t g_program_length = 0;
static uint32_t g_program_receiving_offset = 0;

static void control_on_serial_complete(uint32_t port_index)
{
    if (port_index >= PACKET_MAX_PORTS_NUM) {
        return;
    }

    // console_printf_do(
    //         "Write: Pending write for serial %d is completed!\n",
    //         port_index);

    if (g_handles[port_index].pending) {
        PayloadHeader response_payload_header =
                {.command = 0x80 | CommandWritePort,
                 .error_code = 0,
                 .sequence = g_handles[port_index].pending_sequence};
        packet_send(&response_payload_header, NULL, 0);

        g_handles[port_index].pending = 0;
        g_handles[port_index].pending_sequence = 0;
    }
}

static void control_on_can_complete(
        bool success,
        uint32_t port_index,
        uint32_t sequence)
{
    if (port_index >= PACKET_MAX_PORTS_NUM) {
        return;
    }

    // if (success) {
    //     console_printf_do(
    //             "Write: Pending write for CAN %d is completed!\n",
    //             port_index);
    // } else {
    //     console_printf_do(
    //             "Write: Pending write for CAN %d is aborted!\n", port_index);
    // }

    PayloadHeader response_payload_header =
            {.command = 0x80 | CommandWritePort,
             .error_code = success ? 0 : MSB_Error_CAN_SendFail,
             .sequence = sequence};
    packet_send(&response_payload_header, NULL, 0);
}

MCUSerialBridgeError control_on_configure(
        const uint8_t* data,
        uint32_t data_length)
{
    if (!data)
        return MSB_Error_Proto_InvalidPayload;

    // --------------------------------
    // 已经配置过，拒绝重复配置
    // --------------------------------
    if (g_mcu_state.is_configured) {
        return MSB_Error_State_AlreadyConfigured;
    }

    // --------------------------------
    // 至少要有 port_num
    // --------------------------------
    if (data_length < sizeof(uint32_t)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // --------------------------------
    // unpack port_num
    // --------------------------------
    uint32_t port_num = 0;
    memcpy(&port_num, data, sizeof(uint32_t));

    // 合理性检查（根据你 MCU 实际能力）
    if (port_num > ports_total_num) {
        return MSB_Error_Config_PortNumOver;
    }

    // --------------------------------
    // 检查后续数据长度是否足够
    // --------------------------------
    uint32_t expect_len = sizeof(uint32_t) + port_num * sizeof(PortConfigC);
    if (data_length < expect_len) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // --------------------------------
    // 逐端口解析配置
    // --------------------------------
    uint32_t serial_count = 0;
    uint32_t can_count = 0;
    const PortConfigC* ports = (const PortConfigC*)(data + sizeof(uint32_t));

    // --------------------------------
    // Check first
    // --------------------------------
    for (uint32_t i = 0; i < port_num; i++) {
        switch (ports[i].port_type) {
        case PortType_Serial:
            if (++serial_count > ports_serial_num) {
                return MSB_Error_Config_SerialNumOver;
            }
            break;
        case PortType_CAN:
            if (++can_count > ports_can_num) {
                return MSB_Error_Config_CANNumOver;
            }
            break;
        default:
            return MSB_Error_Config_UnknownPortType;
            break;
        }
    }

    serial_count = 0;
    can_count = 0;
    memset(g_handles, 0, sizeof(g_handles));

    for (uint32_t port_index = 0; port_index < port_num; port_index++) {
        // 保存完整的端口配置
        g_handles[port_index].config = ports[port_index];

        switch (ports[port_index].port_type) {
        case PortType_Serial:
            const SerialPortConfigC* serial_config =
                    (const SerialPortConfigC*)&ports[port_index];
            bsp_serial_configs[serial_count].baud_rate = serial_config->baud;
            g_handles[port_index].serial =
                    hal_usart_register(bsp_serial_configs[serial_count]);
            // 注意：receive 回调在 control_on_start 中注册，配置阶段不启动接收
            ++serial_count;
            break;
        case PortType_CAN:
            const CANPortConfigC* can_config =
                    (const CANPortConfigC*)&ports[port_index];
            bsp_can_configs[can_count].baud_rate = can_config->baud;
            g_handles[port_index].can =
                    hal_direct_can_register(bsp_can_configs[can_count]);
            // 注意：receive 回调在 control_on_start 中注册，配置阶段不启动接收
            ++can_count;
            break;
        }
        g_handles[port_index].valid = true;
    }

    // --------------------------------
    // 标记配置完成
    // --------------------------------
    g_mcu_state.is_configured = 1;
    console_printf_do("CONTROL: Configured %u ports\n", port_num);

    return MSB_Error_OK;
}

MCUSerialBridgeError control_on_reset(const uint8_t* data, uint32_t data_length)
{
    console_printf_do("CONTROL: RESETTING MCU!\n");
    async_timeout(200, hal_nvic_reset, 0);
    return 0;
}

MCUSerialBridgeError control_on_write_port(
        const uint8_t* data,
        uint32_t data_length,
        uint32_t sequence,
        uint8_t* async)
{
    console_printf_do("CONTROL: WRITE PORT, seq = %d\n", sequence);
    *async = false;

    // 必须已配置且在运行状态
    if (!g_mcu_state.is_configured ||
        g_mcu_state.running_state != MCU_RunState_Running) {
        return MSB_Error_State_NotRunning;
    }

    if (data_length < sizeof(DataPacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    const DataPacket* pkt = (const DataPacket*)data;
    if (pkt->data_len != data_length - sizeof(DataPacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    int8_t port_index = pkt->port_index;

    if (port_index < 0 || port_index > PACKET_MAX_PORTS_NUM ||
        !g_handles[port_index].valid) {
        return MSB_Error_Config_PortNumOver;
    }

    if (g_handles[port_index].pending) {
        return MSB_Error_Port_WriteBusy;
    }

    switch (g_handles[port_index].config.port_type) {
    case PortType_Serial:
        // console_printf_do("SEND VIA SERIAL %d\n", port_index);
        // console_print_buffer_do(pkt->data, pkt->data_len);
        if (!g_handles[port_index].pending) {
            g_handles[port_index].pending_sequence = sequence;
            g_handles[port_index].pending = true;
            hal_usart_send(
                    g_handles[port_index].serial,
                    pkt->data,
                    pkt->data_len,
                    (AsyncCallback)control_on_serial_complete,
                    1,
                    port_index);
        } else {
            return MSB_Error_Serial_WriteFail;
        }

        *async = 1;
        return MSB_Error_OK;
        break;

    case PortType_CAN:
        // console_printf_do("SEND VIA CAN %d\n", port_index);
        // console_print_buffer_do(pkt->data, pkt->data_len);
        if (pkt->data_len < sizeof(CANData) - 8) {
            return MSB_Error_CAN_DataError;
        }

        const CANData* can_data = (const CANData*)pkt->data;
        CANIDInfo info;
        memcpy(&info, &can_data->info, sizeof(info));

        // console_printf_do(
        //         "CAN DATA = dlc %u, rtr %u, id %u, pkt_datalen %u\n",
        //         info.dlc,
        //         info.rtr,
        //         info.id,
        //         pkt->data_len);

        if ((info.dlc > 8) ||
            (pkt->data_len < sizeof(CANData) - 8 + info.dlc)) {
            return MSB_Error_CAN_DataError;
        }

        bool send_enqueue_ok = hal_direct_can_send(
                g_handles[port_index].can,
                info,
                can_data->data,
                (DirectCANMessageSendCompleteCallback)control_on_can_complete,
                2,
                port_index,
                sequence);

        if (send_enqueue_ok) {
            *async = 1;
            return MSB_Error_OK;
        } else {
            return MSB_Error_CAN_BufferFull;
        }

        break;

    default:
        break;
    }

    return MSB_Error_MCU_Unknown;
}

MCUSerialBridgeError control_on_write_output(
        const uint8_t* data,
        uint32_t data_length)
{
    if (data_length < 4 || !data) {
        return MSB_Error_MCU_IOSizeError;
    }

    uint32_t output = *(uint32_t*)data;
    console_printf_do("CONTROL: WRITE OUTPUT, value = [0x%08X]\n", output);
    bsp_set_outputs(output);

    return MSB_Error_OK;
}

/* ===============================
 * DIVER / 扩展控制命令实现
 * =============================== */

MCUSerialBridgeError control_on_start(const uint8_t* data, uint32_t data_length)
{
    console_printf_do("CONTROL: START command received\n");

    // 必须已配置
    if (!g_mcu_state.is_configured) {
        console_printf_do("CONTROL: START failed - not configured\n");
        return MSB_Error_State_NotConfigured;
    }

    // 如果是 DIVER 模式，必须已加载程序
    if (g_mcu_state.mode == MCU_Mode_DIVER && !g_mcu_state.is_programmed) {
        console_printf_do(
                "CONTROL: START failed - DIVER mode but no program\n");
        return MSB_Error_State_NotProgrammed;
    }

    // 已经在运行状态
    if (g_mcu_state.running_state == MCU_RunState_Running) {
        console_printf_do("CONTROL: Already running\n");
        return MSB_Error_OK;
    }

    // --------------------------------
    // 注册端口接收回调（启动数据接收）
    // --------------------------------
    for (uint32_t port_index = 0; port_index < PACKET_MAX_PORTS_NUM;
         port_index++) {
        if (!g_handles[port_index].valid) {
            continue;
        }

        const PortConfigC* cfg = &g_handles[port_index].config;

        switch (cfg->port_type) {
        case PortType_Serial:
            const SerialPortConfigC* serial_cfg = (const SerialPortConfigC*)cfg;
            hal_usart_register_receive(
                    g_handles[port_index].serial,
                    (DataReceiveCallback)upload_serial_packet,
                    serial_cfg->receive_frame_ms,
                    1,
                    port_index);
            console_printf_do(
                    "CONTROL: Registered serial[%u] receive callback\n",
                    port_index);
            break;
        case PortType_CAN:
            hal_direct_can_register_receive(
                    g_handles[port_index].can,
                    (DirectCANMessageReceiveCallback)upload_can_packet,
                    1,
                    port_index);
            console_printf_do(
                    "CONTROL: Registered CAN[%u] receive callback\n",
                    port_index);
            break;
        default:
            break;
        }
    }

    // 切换到运行状态
    g_mcu_state.running_state = MCU_RunState_Running;
    console_printf_do(
            "CONTROL: Started, mode=%s, state=0x%08X\n",
            g_mcu_state.mode == MCU_Mode_DIVER ? "DIVER" : "Bridge",
            g_mcu_state.raw);

    // TODO: 启动 DIVER 程序

    return MSB_Error_OK;
}

MCUSerialBridgeError control_on_enable_wire_tap(
        const uint8_t* data,
        uint32_t data_length)
{
    console_printf_do("CONTROL: ENABLE WIRE TAP\n");
    g_wire_tap_enabled = true;
    return MSB_Error_OK;
}

MCUSerialBridgeError control_on_program(
        const uint8_t* data,
        uint32_t data_length)
{
    if (!data || data_length < sizeof(ProgramPacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // 必须在空闲状态
    if (g_mcu_state.running_state != MCU_RunState_Idle) {
        return MSB_Error_State_Running;
    }

    const ProgramPacket* pkt = (const ProgramPacket*)data;

    console_printf_do(
            "CONTROL: PROGRAM chunk, total=%u, offset=%u, chunk=%u\n",
            pkt->total_len,
            pkt->offset,
            pkt->chunk_len);

    // 检查数据长度是否匹配
    if (data_length < sizeof(ProgramPacket) + pkt->chunk_len) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // 空程序表示切换到透传模式
    if (pkt->total_len == 0) {
        console_printf_do("CONTROL: Switching to Bridge (passthrough) mode\n");
        g_mcu_state.mode = MCU_Mode_Bridge;
        g_mcu_state.is_programmed = 0;
        g_program_length = 0;
        return MSB_Error_OK;
    }

    // 如果传入非零数据，检查是否有 DIVER runtime 支持
#if !defined(HAS_DIVER_RUNTIME) || HAS_DIVER_RUNTIME == 0
    console_printf_do(
            "CONTROL: Program download rejected - DIVER runtime not available\n");
    return MSB_Error_MCU_RuntimeNotAvailable;
#endif

    // 检查程序大小
    if (pkt->total_len > PROGRAM_BUFFER_MAX_SIZE) {
        console_printf_do(
                "CONTROL: Program too large (%u > %u)\n",
                pkt->total_len,
                PROGRAM_BUFFER_MAX_SIZE);
        return MSB_Error_Proto_ProgramTooLarge;
    }

    // 检查偏移量
    if (pkt->offset + pkt->chunk_len > pkt->total_len) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // 首个分片时，切换到 DIVER 模式并清空程序缓冲区
    if (pkt->offset == 0) {
        g_mcu_state.mode = MCU_Mode_DIVER;
        g_program_length = pkt->total_len;
        g_mcu_state.is_programmed = 0;
        g_program_receiving_offset = 0;
        memset(g_program_buffer, 0, PROGRAM_BUFFER_MAX_SIZE);
    }

    if (pkt->offset != g_program_receiving_offset ||
        pkt->total_len != g_program_length) {
        return MSB_Error_Proto_ProgramInvalidOffset;
    }

    // 复制程序数据
    memcpy(g_program_buffer + pkt->offset, pkt->data, pkt->chunk_len);
    g_program_receiving_offset += pkt->chunk_len;

    // 检查是否接收完整
    if (pkt->offset + pkt->chunk_len >= g_program_length) {
        g_mcu_state.is_programmed = 1;
        console_printf_do(
                "CONTROL: Program loaded, total %u bytes\n", g_program_length);
    }

    return MSB_Error_OK;
}

MCUSerialBridgeError control_on_memory_upper_io(
        const uint8_t* data,
        uint32_t data_length)
{
    if (!data || data_length < sizeof(MemoryExchangePacket)) {
        return MSB_Error_Proto_InvalidPayload;
    }

    const MemoryExchangePacket* pkt = (const MemoryExchangePacket*)data;

    if (data_length < sizeof(MemoryExchangePacket) + pkt->data_len) {
        return MSB_Error_Proto_InvalidPayload;
    }

    // 必须在 DIVER 模式且运行中
    if (g_mcu_state.mode != MCU_Mode_DIVER) {
        return MSB_Error_State_NotDIVERMode;
    }

    if (g_mcu_state.running_state != MCU_RunState_Running) {
        return MSB_Error_State_NotRunning;
    }

    console_printf_do("CONTROL: UpperIO received, len=%u\n", pkt->data_len);

    // TODO: 将 UpperIO 数据传递给 DIVER 运行时
    // diver_set_upper_io(pkt->data, pkt->data_len);

    return MSB_Error_OK;
}

void control_upload_memory_lower_io(const uint8_t* data, uint32_t data_length)
{
    if (!data || data_length == 0) {
        return;
    }

    PayloadHeader header = {
            .command = CommandMemoryLowerIO,
            .sequence = 0,
            .error_code = 0,
            .timestamp_ms = 0,
    };

    // 构造 MemoryExchangePacket
    uint8_t other_data[sizeof(MemoryExchangePacket) + PACKET_MAX_DATALEN];
    MemoryExchangePacket* pkt = (MemoryExchangePacket*)other_data;

    if (data_length > PACKET_MAX_DATALEN) {
        data_length = PACKET_MAX_DATALEN;
    }

    pkt->data_len = (uint16_t)data_length;
    memcpy(pkt->data, data, data_length);

    packet_send(
            &header, other_data, sizeof(MemoryExchangePacket) + data_length);
}
