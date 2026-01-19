#include "appl/control.h"

#include "appl/packet.h"
#include "appl/upload.h"
#include "appl/vm.h"
#include "bsp/digital_io.h"
#include "bsp/ports.h"
#include "hal/nvic.h"
#include "msb_error_c.h"
#include "msb_protocol.h"
#include "util/console.h"
#include "util/mempool.h"

// ===============================
// VMTXBUF debug logs (temporary)
// IMPORTANT:
// - Do NOT call console_printf_do between critical_section_enter() and quit()
// - ISR is considered a "medium" critical section; printf is allowed there
// ===============================
#define VMTXBUF_DEBUG 1
#if VMTXBUF_DEBUG
#define VMTXBUF_LOG(fmt, ...) console_printf_do("VMTXBUF: " fmt, ##__VA_ARGS__)
#else
#define VMTXBUF_LOG(fmt, ...) ((void)0)
#endif

typedef struct {
    uint32_t start;  // 数据在 buffer 中的起始偏移
    uint32_t len;    // 数据长度
} DIVERSerialSendBufferSegment;

typedef struct {
    uint8_t buffer[DIVER_SERIAL_SEND_BUFFER_TOTAL_SIZE];
    DIVERSerialSendBufferSegment
            segments[DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT];
    volatile uint32_t head;  // 分段队列头（下一个要发送的分段索引，消费者）
    volatile uint32_t tail;  // 分段队列尾（下一个要写入的分段槽位，生产者）
    volatile uint32_t buf_write;  // buffer 中下一个写入位置
    volatile bool sending;  // 是否正在发送中（防止 flush 和回调竞态）
} DIVERSerialSendBuffer;

// 辅助宏：判断分段队列是否为空
#define DIVER_SENDBUF_IS_EMPTY(buf) ((buf)->head == (buf)->tail)

// 辅助宏：判断分段队列是否已满
#define DIVER_SENDBUF_IS_FULL(buf) \
    (((buf)->tail + 1) % DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT == (buf)->head)

typedef struct {
    union {
        USARTHandle serial;
        DirectCANHandle can;
    };
    PortConfigC config;  // 保存完整的端口配置
    bool valid;
    uint32_t pending_sequence;  // pending Bridge sequence for Host use
                                // WriteSerial, CAN use other
    bool pending;               // is pending_sequence valid
    void* diver_send_buffer;    // for DIVER mode, pointer to
} Handles;
Handles g_handles[PACKET_MAX_PORTS_NUM];

volatile uint32_t g_inputs;
volatile uint32_t g_outputs;

volatile MCUStateC g_mcu_state = {.raw = 0};  // Bridge, Idle, not configured
volatile bool g_wire_tap_enabled = false;

// DIVER 程序缓冲区 (PROGRAM_BUFFER_MAX_SIZE defined in control.h)
static uint8_t program_buffer_storage[PROGRAM_BUFFER_MAX_SIZE];
uint8_t* g_program_buffer = program_buffer_storage;
uint32_t g_program_length = 0;
static uint32_t g_program_receiving_offset = 0;

// UpperIO 双缓存结构（避免临界区）
typedef struct {
    uint8_t* buffer[2];                  // 双缓存 [0] 和 [1]
    volatile uint32_t hot_buffer_index;  // 当前"热"buffer
                                         // 索引（接收线程写入，可能被多次覆盖）
    volatile uint32_t write_length;  // 当前写入的数据长度
    volatile bool has_new_data;      // 是否有新数据标志
} UpperIODoubleBuffer;

static UpperIODoubleBuffer g_upperio_buffer = {0};

// Callback for serial data transmission started by write_port command complete
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

// Callback for CAN data transmission started by write_port command complete
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
        case PortType_Serial: {
            const SerialPortConfigC* serial_config =
                    (const SerialPortConfigC*)&ports[port_index];
            bsp_serial_configs[serial_count].baud_rate = serial_config->baud;
            g_handles[port_index].serial =
                    hal_usart_register(bsp_serial_configs[serial_count]);
            // 注意：receive 回调在 control_on_start 中注册，配置阶段不启动接收
            ++serial_count;
            break;
        }
        case PortType_CAN: {
            const CANPortConfigC* can_config =
                    (const CANPortConfigC*)&ports[port_index];
            bsp_can_configs[can_count].baud_rate = can_config->baud;
            g_handles[port_index].can =
                    hal_direct_can_register(bsp_can_configs[can_count]);
            // 注意：receive 回调在 control_on_start 中注册，配置阶段不启动接收
            ++can_count;
            break;
        }
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
 * VM 端口写入实现
 * =============================== */

MCUSerialBridgeError control_vm_write_port(
        uint32_t port_index,
        const uint8_t* data,
        uint32_t data_length)
{
    // 检查端口索引有效性
    if (port_index >= PACKET_MAX_PORTS_NUM || !g_handles[port_index].valid) {
        return MSB_Error_Config_PortNumOver;
    }

    if (!data || data_length == 0) {
        return MSB_Error_Proto_InvalidPayload;
    }

    const PortConfigC* cfg = &g_handles[port_index].config;

    switch (cfg->port_type) {
    case PortType_Serial: {
        // Serial 类型：缓冲数据，等待 flush 时发送
        DIVERSerialSendBuffer* buf =
                (DIVERSerialSendBuffer*)g_handles[port_index].diver_send_buffer;

        if (!buf) {
            VMTXBUF_LOG(
                    "ENQ port=%u len=%u fail: no buffer\n",
                    port_index,
                    data_length);
            return MSB_Error_Serial_NotOpen;
        }

        // ========== 无临界区读取快照 ==========
        // 生产者只有 VM 一个，消费者（回调）只会推进 head，不会修改
        // tail/buf_write 消费者推进 head
        // 只会让可用空间变大，所以这里的计算是保守的
        uint32_t snapshot_head = buf->head;
        uint32_t snapshot_tail = buf->tail;

        // 检查分段队列是否已满
        if ((snapshot_tail + 1) % DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT ==
            snapshot_head) {
            VMTXBUF_LOG(
                    "ENQ port=%u len=%u fail: segq full head=%u tail=%u\n",
                    port_index,
                    data_length,
                    snapshot_head,
                    snapshot_tail);
            return MSB_Error_MCU_SerialDataFlushFailed;
        }

        // 计算写入位置和可用空间
        uint32_t write_pos;
        uint32_t next_buf_write;

        if (snapshot_head == snapshot_tail) {
            // 队列为空：
            // - segments[head] 内容可能是旧数据，不能用于 buf_read 计算
            // - 为了提升局部性，并且避免空队列时计算，这里直接从 0 写入
            write_pos = 0;
            if (data_length > DIVER_SERIAL_SEND_BUFFER_TOTAL_SIZE) {
                VMTXBUF_LOG(
                        "ENQ port=%u len=%u fail: too large\n",
                        port_index,
                        data_length);
                return MSB_Error_MCU_SerialDataFlushFailed;
            }
            next_buf_write = data_length;
        } else {
            // 队列非空，基于当前 buf_write 计算
            uint32_t current_buf_write = buf->buf_write;
            uint32_t buf_read = buf->segments[snapshot_head].start;

            uint32_t available_at_end;  // 尾部连续可用空间
            uint32_t available_at_start;  // 头部连续可用空间（回绕后）

            if (current_buf_write >= buf_read) {
                // 正常情况：已用区间是 [buf_read, buf_write)
                available_at_end =
                        DIVER_SERIAL_SEND_BUFFER_TOTAL_SIZE - current_buf_write;
                available_at_start = buf_read;
            } else {
                // 已回绕：已用区间是 [0, buf_write) 和 [buf_read, SIZE)
                available_at_end = buf_read - current_buf_write;
                available_at_start = 0;  // 不能再次回绕
            }

            // 优先在尾部写入（保证数据连续，便于 DMA）
            if (data_length <= available_at_end) {
                write_pos = current_buf_write;
                next_buf_write = current_buf_write + data_length;
                if (next_buf_write >= DIVER_SERIAL_SEND_BUFFER_TOTAL_SIZE) {
                    next_buf_write = 0;
                }
            } else if (data_length <= available_at_start) {
                // 尾部不够，回绕到头部（不拆分数据）
                write_pos = 0;
                next_buf_write = data_length;
            } else {
                VMTXBUF_LOG(
                        "ENQ port=%u len=%u fail: no contig space "
                        "head=%u tail=%u bw=%u read=%u end=%u start=%u\n",
                        port_index,
                        data_length,
                        snapshot_head,
                        snapshot_tail,
                        current_buf_write,
                        buf_read,
                        available_at_end,
                        available_at_start);
                return MSB_Error_MCU_SerialDataFlushFailed;
            }
        }

        // ========== 无临界区执行 memcpy ==========
        // 生产者独占写入区域，消费者不会访问 [write_pos, write_pos+len) 区间
        memcpy(buf->buffer + write_pos, data, data_length);

        // ========== 进入临界区更新索引 ==========
        // 只需要原子更新 segment 信息和 tail
        hal_nvic_critical_section_enter();
        buf->segments[snapshot_tail].start = write_pos;
        buf->segments[snapshot_tail].len = data_length;
        buf->buf_write = next_buf_write;
        buf->tail =
                (snapshot_tail + 1) % DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT;
        hal_nvic_critical_section_quit();

        VMTXBUF_LOG(
                "ENQ ok port=%u len=%u write_pos=%u next_bw=%u head=%u "
                "tail->%u sending=%u\n",
                port_index,
                data_length,
                write_pos,
                next_buf_write,
                snapshot_head,
                (snapshot_tail + 1) % DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT,
                (uint32_t)buf->sending);

        return MSB_Error_OK;
    }

    case PortType_CAN: {
        // CAN 类型：直接发送，不需要缓冲
        // 数据格式: CANIDInfo (2 bytes) + data (0~8 bytes)
        if (data_length < sizeof(CANIDInfo)) {
            console_printf_do("CONTROL: WRITE CAN PORT, data_length < "
                              "sizeof(CANIDInfo)\n");
            return MSB_Error_CAN_DataError;
        }

        CANIDInfo id_info;
        memcpy(&id_info, data, sizeof(CANIDInfo));

        // 验证 DLC 和数据长度
        if (id_info.dlc > 8 || data_length < sizeof(CANIDInfo) + id_info.dlc) {
            console_printf_do("CONTROL: WRITE CAN PORT, data_length < "
                              "sizeof(CANIDInfo) + id_info.dlc\n");
            return MSB_Error_CAN_DataError;
        }

        const uint8_t* can_data = data + sizeof(CANIDInfo);

        // 直接发送 CAN 数据（无需回调，VM 模式下不需要等待确认）
        bool enqueued = hal_direct_can_send(
                g_handles[port_index].can,
                id_info,
                can_data,
                NULL,  // 无回调
                0);

        if (!enqueued) {
            return MSB_Error_CAN_BufferFull;
        }

        return MSB_Error_OK;
    }

    default:
        return MSB_Error_Config_UnknownPortType;
    }
}

/**
 * @brief 发送完成回调（ISR 上下文）
 *
 * 当一个分段发送完成后被调用。消费当前分段，如果队列中还有分段则继续发送。
 * 注意：此回调已在 ISR 中运行，中断已禁用，无需额外临界区。
 */
static void control_vm_flush_serial_complete(uint32_t port_index)
{
    DIVERSerialSendBuffer* buf =
            (DIVERSerialSendBuffer*)g_handles[port_index].diver_send_buffer;

    if (!buf) {
        return;
    }

    // ISR 上下文，无需临界区

    // 消费当前分段：推进 head
    if (!DIVER_SENDBUF_IS_EMPTY(buf)) {
        uint32_t prev_head = buf->head;
        buf->head = (buf->head + 1) % DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT;
        VMTXBUF_LOG(
                "TX done port=%u head %u->%u tail=%u\n",
                port_index,
                prev_head,
                buf->head,
                buf->tail);
    }

    // 检查队列中是否还有分段要发送
    if (!DIVER_SENDBUF_IS_EMPTY(buf)) {
        // 还有分段，继续发送下一个
        DIVERSerialSendBufferSegment* seg = &buf->segments[buf->head];

        VMTXBUF_LOG(
                "TX next port=%u start=%u len=%u head=%u tail=%u\n",
                port_index,
                seg->start,
                seg->len,
                buf->head,
                buf->tail);
        hal_usart_send(
                g_handles[port_index].serial,
                buf->buffer + seg->start,
                seg->len,
                (AsyncCallback)control_vm_flush_serial_complete,
                1,
                port_index);
    } else {
        // 队列已空，标记发送完成
        // 注意：不在这里重置 buf_write，由生产者在队列为空时自动重置到 0
        buf->sending = false;
        VMTXBUF_LOG("TX idle port=%u (queue empty) sending=0\n", port_index);
    }
}

/**
 * @brief 刷新所有 VM 端口的发送缓冲区
 *
 * 在 VM loop 末尾调用。对于每个 Serial
 * 端口，如果有待发送的分段且当前没有正在发送， 则启动发送。
 *
 * 注意：
 * - 此函数与 ISR 回调存在竞态，需要用临界区保护 sending 标志的检查和设置
 * - 如果上一次发送还没完成（sending=true），本次 flush 会跳过该端口
 */
void control_vm_flush_ports()
{
    for (uint32_t port_index = 0; port_index < PACKET_MAX_PORTS_NUM;
         port_index++) {
        if (!g_handles[port_index].valid) {
            continue;
        }

        const PortConfigC* cfg = &g_handles[port_index].config;

        // 只处理 Serial 类型端口（CAN 在 write_port 时已直接发送）
        if (cfg->port_type != PortType_Serial) {
            continue;
        }

        DIVERSerialSendBuffer* buf =
                (DIVERSerialSendBuffer*)g_handles[port_index].diver_send_buffer;

        if (!buf) {
            continue;
        }

        // ========== 进入临界区 ==========
        // 需要原子检查和设置 sending 标志，防止与回调竞态
        hal_nvic_critical_section_enter();

        // 检查：队列是否为空？是否已经在发送中？
        bool is_empty = DIVER_SENDBUF_IS_EMPTY(buf);
        bool is_sending = buf->sending;
        if (is_empty || is_sending) {
            hal_nvic_critical_section_quit();
            if (is_empty) {
                // optional, only when debugging
                // VMTXBUF_LOG("FLUSH skip port=%u: empty\n", port_index);
            } else {
                VMTXBUF_LOG("FLUSH skip port=%u: sending=1\n", port_index);
            }
            continue;
        }

        // 标记开始发送，防止重入
        buf->sending = true;

        // 获取队首分段信息（在临界区内读取，保证一致性）
        uint32_t seg_start = buf->segments[buf->head].start;
        uint32_t seg_len = buf->segments[buf->head].len;
        uint32_t snapshot_head = buf->head;
        uint32_t snapshot_tail = buf->tail;

        hal_nvic_critical_section_quit();
        // ========== 退出临界区 ==========

        VMTXBUF_LOG(
                "FLUSH start port=%u start=%u len=%u head=%u tail=%u\n",
                port_index,
                seg_start,
                seg_len,
                snapshot_head,
                snapshot_tail);

        // 启动发送（带回调）
        hal_usart_send(
                g_handles[port_index].serial,
                buf->buffer + seg_start,
                seg_len,
                (AsyncCallback)control_vm_flush_serial_complete,
                1,
                port_index);
    }
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
        case PortType_Serial: {
            const SerialPortConfigC* serial_cfg = (const SerialPortConfigC*)cfg;
            hal_usart_register_receive(
                    g_handles[port_index].serial,
                    (DataReceiveCallback)upload_serial_packet,
                    serial_cfg->receive_frame_ms,
                    1,
                    port_index);
            if (g_mcu_state.mode == MCU_Mode_DIVER) {
                g_handles[port_index].diver_send_buffer =
                        mempool_malloc(sizeof(DIVERSerialSendBuffer));
                if (g_handles[port_index].diver_send_buffer) {
                    memset(g_handles[port_index].diver_send_buffer,
                           0,
                           sizeof(DIVERSerialSendBuffer));
                } else {
                    g_mcu_state.running_state = MCU_RunState_Error;
                    console_printf_do(
                            "CONTROL: Failed to allocate diver send buffer for "
                            "serial %u\n",
                            port_index);
                    return MSB_Error_MCU_MemoryAllocFailed;
                }
            }
            console_printf_do(
                    "CONTROL: Registered serial[%u] receive callback\n",
                    port_index);
            break;
        }
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

    // --------------------------------
    // 为 DIVER 模式分配 UpperIO 双缓存
    // --------------------------------
    if (g_mcu_state.mode == MCU_Mode_DIVER) {
        // 分配双缓存
        g_upperio_buffer.buffer[0] = mempool_malloc(UPPERIO_BUFFER_SIZE);
        g_upperio_buffer.buffer[1] = mempool_malloc(UPPERIO_BUFFER_SIZE);

        if (!g_upperio_buffer.buffer[0] || !g_upperio_buffer.buffer[1]) {
            g_mcu_state.running_state = MCU_RunState_Error;
            console_printf_do(
                    "CONTROL: Failed to allocate UpperIO double buffer\n");
            return MSB_Error_MCU_MemoryAllocFailed;
        }

        // 初始化：hot_buffer_index=0 表示 buffer[0]
        // 是"热"buffer（接收线程写入，可能被覆盖） VM 线程读取 buffer[1]（冷
        // buffer，安全不会变化）
        g_upperio_buffer.hot_buffer_index = 0;
        g_upperio_buffer.write_length = 0;
        g_upperio_buffer.has_new_data = false;

        console_printf_do(
                "CONTROL: UpperIO double buffer allocated (%u bytes each)\n",
                UPPERIO_BUFFER_SIZE);
    }

    // 切换到运行状态
    g_mcu_state.running_state = MCU_RunState_Running;
    console_printf_do(
            "CONTROL: Started, mode=%s, state=0x%08X\n",
            g_mcu_state.mode == MCU_Mode_DIVER ? "DIVER" : "Bridge",
            g_mcu_state.raw);

    console_printf_do("CONTROL: Calling VM Start Program\n");
    vm_start_program();

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
    console_printf_do("CONTROL: Program download rejected - DIVER runtime not "
                      "available\n");
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

    // 检查双缓存是否已分配
    if (!g_upperio_buffer.buffer[0] || !g_upperio_buffer.buffer[1]) {
        console_printf_do("CONTROL: UpperIO buffer not allocated\n");
        return MSB_Error_State_NotRunning;
    }

    // 检查数据长度
    if (pkt->data_len > UPPERIO_BUFFER_SIZE) {
        console_printf_do(
                "CONTROL: UpperIO data too large: %u > %u\n",
                pkt->data_len,
                UPPERIO_BUFFER_SIZE);
        return MSB_Error_Proto_FrameTooLong;
    }

    // ========== 无临界区写入双缓存 ==========
    // 读取当前 hot_buffer_index，写入到热 buffer（可能被多次覆盖）
    // 如果两个 Op 之间收到多个 UpperIO，后面的会覆盖前面的（只保留最后一个）
    uint32_t hot_idx = g_upperio_buffer.hot_buffer_index;

    memcpy(g_upperio_buffer.buffer[hot_idx], pkt->data, pkt->data_len);

    // 原子更新长度和标志（volatile 写入，VM 线程会看到）
    g_upperio_buffer.write_length = pkt->data_len;
    g_upperio_buffer.has_new_data = true;

    console_printf_do("CONTROL: UpperIO received, len=%u\n", pkt->data_len);

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

bool control_vm_get_upper_io(const uint8_t** data_ptr, uint32_t* data_len)
{
    if (!g_upperio_buffer.buffer[0] || !g_upperio_buffer.buffer[1]) {
        return false;
    }

    // ========== 进入临界区 ==========
    // 需要原子读取 has_new_data 和 hot_buffer_index，防止在交换过程中
    // 进入 control_on_memory_upper_io 的 ISR
    hal_nvic_critical_section_enter();

    // 检查是否有新数据
    if (!g_upperio_buffer.has_new_data) {
        hal_nvic_critical_section_quit();
        return false;
    }

    // 读取当前 hot_buffer_index 和数据长度
    uint32_t hot_idx = g_upperio_buffer.hot_buffer_index;
    uint32_t len = g_upperio_buffer.write_length;

    // 计算冷 buffer 索引（VM 读取的安全 buffer，不会变化）
    uint32_t cold_idx = 1 - hot_idx;

    // 交换 hot_buffer_index（0 <-> 1）
    // 热 buffer 和冷 buffer 互换：刚才的热 buffer 变成冷 buffer（VM
    // 可以安全读取） 刚才的冷 buffer 变成新的热
    // buffer（接收线程可以写入，可能被覆盖）
    g_upperio_buffer.hot_buffer_index = cold_idx;

    // 清除标志（必须在临界区内清除，避免竞态）
    g_upperio_buffer.has_new_data = false;

    hal_nvic_critical_section_quit();
    // ========== 退出临界区 ==========

    // 返回数据（cold_idx 指向的是刚才热 buffer 的数据，现在变成冷
    // buffer，安全不会变化）
    *data_ptr = g_upperio_buffer.buffer[cold_idx];
    *data_len = len;

    return true;
}
