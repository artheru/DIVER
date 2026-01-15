#include "appl/vm.h"
//
#include "appl/control.h"
#include "appl/upload.h"
#include "bsp/digital_io.h"
#include "util/async.h"
#include "util/console.h"


static bool vm_is_program_loaded = false;
static int32_t vm_iteration_count = 0;
static uint64_t vm_interval_period_us = 100000;

static volatile uint64_t vm_last_iteration_time_us = 0;

static void vm_loop()
{
    if (g_mcu_state.mode != MCU_Mode_DIVER || g_mcu_state.is_programmed == 0 ||
        g_mcu_state.is_configured == 0 ||
        g_mcu_state.running_state != MCU_RunState_Running) {
        return;
    }

    if (!vm_is_program_loaded) {
        console_printf(
                LogLevelInfo, "VM: Program not loaded, try load program\n");
        // Pass full buffer size, not just program length - VM needs heap/stack
        // space!
        int interval =
                vm_set_program(g_program_buffer, PROGRAM_BUFFER_MAX_SIZE);
        console_printf(
                LogLevelInfo, "VM: Program loaded, interval=%d\n", interval);
        vm_iteration_count = 0;
        vm_interval_period_us = (uint64_t)interval * (uint64_t)1000;
        vm_is_program_loaded = true;
    }

    uint64_t current_time_us = g_hal_timestamp_us;
    if (current_time_us > vm_last_iteration_time_us + vm_interval_period_us) {
        // Run Once
        console_printf(LogLevelInfo, "VM: Start new loop\n");

        // Read IO Data and put into snapshot buffer
        uint32_t inputs_u32 = bsp_get_inputs();
        vm_put_snapshot_buffer((void*)&inputs_u32, sizeof(inputs_u32));

        // if (vm_uplink_data_received) {
        //     enter_critical();
        //     vm_put_upper_memory(
        //             vm_upper_memory_cmd, vm_upper_memory_cmd_length);
        //     vm_uplink_data_received = 0;
        //     leave_critical();

        //     vm_run(vm_iteration_count++);
        //     console_printf_do(
        //             "VMLoop: upperio updated, iteration = %d\n",
        //             vm_iteration_count);
        // } else {
        //     vm_run(vm_iteration_count);
        //     console_printf_do(
        //             "VMLoop: upperio not updated, iteration = %d\n",
        //             vm_iteration_count);
        // }

        // TODO: iteration should be handled by if uplink upperio received or
        // not.
        vm_run(vm_iteration_count++);

        // Call upload console writeline to actually upload the console
        // writeline data to the host.
        upload_console_writeline();
        vm_last_iteration_time_us += vm_interval_period_us;
    }
}

void vm_start_program()
{
    console_printf_do("VM: Starting program loop in background thread!\n");
    async_interval_low_priority(1, AsyncPriority_Medium, vm_loop, 0);
}