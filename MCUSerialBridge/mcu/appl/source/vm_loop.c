#include "appl/vm.h"
//
#include "appl/control.h"
#include "appl/upload.h"
#include "util/console.h"

static bool vm_is_program_loaded = false;
static int32_t vm_iteration_count = 0;
static uint64_t vm_interval_period_us = 100000;

static volatile uint64_t vm_last_iteration_time_us = 0;

static void vm_loop()
{
    if (g_mcu_state.mode == MCU_Mode_DIVER || g_mcu_state.is_programmed == 0 ||
        g_mcu_state.is_configured == 0 ||
        g_mcu_state.running_state != MCU_RunState_Running) {
        return;
    }

    if (!vm_is_program_loaded) {
        console_printf(
                LogLevelInfo, "VM: Program not loaded, try load program\n");
        int interval = vm_set_program(g_program_buffer, g_program_length);
        console_printf(
                LogLevelInfo, "VM: Program loaded, interval=%d\n", interval);
        vm_iteration_count = 0;
        vm_interval_period_us = (uint64_t)interval * (uint64_t)1000;
        vm_is_program_loaded = true;
    }

    uint64_t current_time_us = get_cyclic_micros();
    if (current_time_us > vm_last_iteration_time_us + vm_interval_period_us) {
        // Run Once
        console_printf(LogLevelInfo, "VM: Start new loop\n");

        // TODO: Update IO Data
        
    }
}