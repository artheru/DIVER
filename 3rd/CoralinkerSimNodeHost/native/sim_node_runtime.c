#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define inline
#include "mcu_runtime.h"
#undef inline

#ifdef _WIN32
#define SIM_EXPORT __declspec(dllexport)
#else
#define SIM_EXPORT __attribute__((visibility("default")))
#endif

typedef void(*SimBytesCb)(unsigned char* data, int length, unsigned int timestamp_ms);
typedef void(*SimTextCb)(unsigned char* message, unsigned int timestamp_ms);
typedef void(*SimPortBytesCb)(unsigned char port_index, unsigned char direction, unsigned char* data, int length, unsigned int timestamp_ms);
typedef void(*SimFatalCb)(int il_offset, unsigned char* message, int line_no, unsigned int timestamp_ms);

static SimBytesCb sim_lower_cb = 0;
static SimTextCb sim_console_cb = 0;
static SimBytesCb sim_snapshot_cb = 0;
static SimPortBytesCb sim_stream_cb = 0;
static SimPortBytesCb sim_event_cb = 0;
static SimFatalCb sim_fatal_cb = 0;
static uchar* sim_vm_memory = 0;
static int sim_vm_memory_size = 0;
static unsigned int sim_tick_ms = 0;
static uchar sim_snapshot_input[256] = { 0 };
static int sim_snapshot_input_size = 4;

void write_snapshot(uchar* buffer, int size)
{
    if (buffer != 0 && size > 0)
    {
        int copy_size = size > (int)sizeof(sim_snapshot_input) ? (int)sizeof(sim_snapshot_input) : size;
        memcpy(sim_snapshot_input, buffer, copy_size);
        sim_snapshot_input_size = copy_size;
    }

    if (sim_snapshot_cb != 0)
        sim_snapshot_cb(buffer, size, sim_tick_ms);
}

void write_stream(int streamID, uchar* buffer, int size)
{
    if (sim_stream_cb != 0)
        sim_stream_cb((unsigned char)streamID, 1, buffer, size, sim_tick_ms);
}

void write_event(int portID, int eventID, uchar* buffer, int size)
{
    if (sim_event_cb != 0)
        sim_event_cb((unsigned char)portID, 1, buffer, size, sim_tick_ms);
}

void flush_console(void) { fflush(stdout); }

void report_error(int il_offset, uchar* error_str, int line_no)
{
    flush_console();
    if (sim_fatal_cb != 0)
        sim_fatal_cb(il_offset, error_str, line_no, sim_tick_ms);
    if (sim_console_cb != 0)
        sim_console_cb(error_str, sim_tick_ms);
    exit(2);
}

void print_line(uchar* str, int length)
{
    if (sim_console_cb != 0)
        sim_console_cb(str, sim_tick_ms);
    else
        printf("%.*s\n", length, str);
}

void enter_critical() {}
void leave_critical() {}

int get_cyclic_millis() { return (int)sim_tick_ms; }
int get_cyclic_micros() { return (int)(sim_tick_ms * 1000); }
int get_cyclic_seconds() { return (int)(sim_tick_ms / 1000); }

SIM_EXPORT void sim_set_callbacks(
    SimBytesCb lower_cb,
    SimTextCb console_cb,
    SimBytesCb snapshot_cb,
    SimPortBytesCb stream_cb,
    SimPortBytesCb event_cb,
    SimFatalCb fatal_cb)
{
    sim_lower_cb = lower_cb;
    sim_console_cb = console_cb;
    sim_snapshot_cb = snapshot_cb;
    sim_stream_cb = stream_cb;
    sim_event_cb = event_cb;
    sim_fatal_cb = fatal_cb;
}

SIM_EXPORT int sim_load_program(uchar* bin, int len, int memory_size)
{
    if (bin == 0 || len <= 0)
        return -1;

    if (memory_size < len)
        memory_size = len;

    if (sim_vm_memory != 0)
    {
        free(sim_vm_memory);
        sim_vm_memory = 0;
        sim_vm_memory_size = 0;
    }

    sim_vm_memory = (uchar*)malloc(memory_size);
    if (sim_vm_memory == 0)
        return -2;

    memset(sim_vm_memory, 0, memory_size);
    memcpy(sim_vm_memory, bin, len);
    memset(sim_snapshot_input, 0, sizeof(sim_snapshot_input));
    sim_snapshot_input_size = 4;
    sim_vm_memory_size = memory_size;
    sim_tick_ms = 0;
    return vm_set_program(sim_vm_memory, sim_vm_memory_size);
}

SIM_EXPORT int sim_put_upper(uchar* buf, int len)
{
    if (buf == 0 || len < 0)
        return -1;
    vm_put_upper_memory(buf, len);
    return 0;
}

SIM_EXPORT int sim_put_port_input(unsigned char port_index, uchar* buf, int len)
{
    if (buf == 0 || len < 0)
        return -1;

    if (port_index == 3)
        vm_put_event_buffer(port_index, 0x80, buf, len);
    else
        vm_put_stream_buffer(port_index, buf, len);
    return 0;
}

SIM_EXPORT int sim_step(unsigned int timestamp_ms)
{
    sim_tick_ms = timestamp_ms;
    vm_put_snapshot_buffer(sim_snapshot_input, sim_snapshot_input_size);
    vm_run((int)sim_tick_ms);

    uchar* mem = vm_get_lower_memory();
    int len = vm_get_lower_memory_size();
    if (sim_lower_cb != 0)
        sim_lower_cb(mem, len, sim_tick_ms);
    return 0;
}

SIM_EXPORT void sim_destroy()
{
    if (sim_vm_memory != 0)
    {
        free(sim_vm_memory);
        sim_vm_memory = 0;
        sim_vm_memory_size = 0;
    }
}
