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

    // 发送已缓冲的 Console.WriteLine 输出
    upload_console_writeline_fatal();

    // 发送错误到上位机并复位 MCU (此函数不会返回)
    fatal_error_send_string(il_offset, (const char*)error_str, line_no);

    // 不会执行到这里
    while (1) {
    }
}

void print_line(uchar* str, int length)  // should upload text info.
{
    console_printf_do("VMGlue: Console.WriteLine!\n");
    console_print_string_do(str, length);
    upload_console_writeline_append(str, length);
}
