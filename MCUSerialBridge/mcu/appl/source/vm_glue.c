#include "appl/vm.h"
//
#include "appl/control.h"
#include "appl/fatal_error.h"
#include "appl/upload.h"
#include "bsp/digital_io.h"
#include "util/console.h"


// In this file, we will implement the glue code between the MCU and the DIVER
// runtime.

void write_snapshot(
        uchar* buffer,
        int size  // size is equal to "vm_put_snapshot_buffer", called per
                  // iteration
)
{
    if (size < 4) {
        console_printf_do("VMGlue: write_snapshot, size < 4");
        return;
    }

    bsp_set_outputs(*(uint32_t*)buffer);
    console_printf_do("VMGlue: called write_snapshot!\n");
    console_print_buffer_do(buffer, 4);
}

void write_stream(
        int streamID,
        uchar* buffer,
        int size)  // called to write bytes into serial. called anytime needed.
{
    console_printf_do(
            "VMGlue: write_stream, stm_id = %d, len = %d\n", streamID, size);

    control_vm_write_port(streamID, buffer, size);
    
    // if (!ports_add_buffer(streamID, 0, buffer, size)) {
    //     bsp_beep_play_once(BeepMusicError);
    //     g_state = State_ExecutionError;
    //     g_error_code = ErrorCode_ExecutionFIFOBufferFull;
    //     console_printf_do("ERROR: VMGlue, CAN Not add FIFO Buffer, stop!\n");
    // };
}
void write_event(
        int portID,
        int eventID,
        uchar* buffer,
        int size)  // called to write bytes into CAN/modbus similar ports.
// called anytime needed.
{
    console_printf_do(
            "VMGlue: write_event, port_id = %d, ev = %d!, len = %d\n",
            portID,
            eventID,
            size);

    control_vm_write_port(portID, buffer, size);

    // console_print_buffer_do(buffer, size);
    // if (!ports_add_buffer(portID, eventID, buffer, size)) {
    //     bsp_beep_play_once(BeepMusicError);
    //     g_state = State_ExecutionError;
    //     g_error_code = ErrorCode_ExecutionFIFOBufferFull;
    //     console_printf_do("ERROR: VMGlue, CAN Not add FIFO Buffer, stop!\n");
    // };
}

void old_report_error(
        const char* filename,
        uint32_t line_no,
        uchar* error_str,
        uint32_t length)  // should report error and terminate
// execution, enter safe mode.
{
    console_printf_do(
            "ERROR: VM Doomed at %s:%d, reason:\n", filename, line_no);
    console_print_string_do(error_str, length);

    // char line_info[64];
    // int line_info_len = snprintf(
    //         line_info,
    //         sizeof(line_info),
    //         "VM Doomed at %s:%lu\n",
    //         filename,
    //         line_no);
    // uplink_add_log(line_info, line_info_len);
    // uplink_add_log(error_str, length);
    // bsp_beep_play_once(BeepMusicError);
    // g_state = State_ExecutionError;

    // io_failsafe();
    // delay_ms(5);
    // uplink_upload_downside_memory(0, 0);
    // while (1) {
    //     ;
    // }
}

void report_error(int il_offset, uchar* error_str, int line_no)
{
    console_printf_do(
            "ERROR: VMGlue, report_error, il_offset = %d, error_str = %s, "
            "line_no = %d\n",
            il_offset,
            error_str,
            line_no);

    // Fail-fast: stop VM loop
    g_mcu_state.running_state = MCU_RunState_Error;
    g_mcu_state.is_programmed = 0;
    console_printf_do("VMGlue: VM aborted, sending error to PC...\n");

    // 发送错误到上位机并复位 MCU (此函数不会返回)
    fatal_error_send_string(il_offset, (const char*)error_str, line_no);

    // 不会执行到这里
    while (1) {}
}

void print_line(uchar* str, int length)  // should upload text info.
{
    console_printf_do("VMGlue: Console.WriteLine!\n");
    console_print_string_do(str, length);
    upload_console_writeline_append(str, length);
}
