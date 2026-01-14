#include "appl/vm.h"
//
#include "common.h"
#include "util/console.h"


// In this file, we will implement the glue code between the MCU and the DIVER
// runtime.


void write_snapshot(
        uchar* buffer,
        int size)  // size is equal to "vm_put_snapshot_buffer", called per
// iteration
{
    // IOSnapshotType* buffer_snap = AS_PTR(IOSnapshotType, buffer);
    // g_io_snapshot.outputs = buffer_snap->outputs;

    // console_print_buffer_do(buffer_snap, 8);

    console_printf_do("VMGlue: called write_snapshot!\n");
}

void write_stream(
        int streamID,
        uchar* buffer,
        int size)  // called to write bytes into serial. called anytime needed.
{
    console_printf_do(
            "VMGlue: write_stream, stm_id = %d, len = %d\n", streamID, size);

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

void report_error(int il_offset, uchar* error_str) {
    console_printf_do("ERROR: VMGlue, report_error, il_offset = %d, error_str = %s\n", il_offset, error_str);
}

void print_line(uchar* error_str)  // should upload text info.
{
    // console_printf_do("VMGlue: Console.WriteLine!\n");
    // uint32_t len = strnlen((const char*)error_str, 1024);
    // console_print_string_do(error_str, len);
    // uplink_add_log(error_str, len);
}
