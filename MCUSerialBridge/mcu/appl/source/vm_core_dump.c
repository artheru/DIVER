#include "appl/vm.h"
//
#include "appl/fatal_error.h"
#include "util/console.h"

extern int cur_il_offset;

static void core_dump_handler(CoreDumpVariables* core_dump)
{
    console_printf_do(
            "Core Dump: IL Offset = %d, sending to PC...\n", cur_il_offset);

    // 发送错误到上位机并复位 MCU (此函数不会返回)
    fatal_error_send_coredump(cur_il_offset, core_dump);

    while (1) {
    }
}

void register_vm_core_dump()
{
    hal_core_dump_register(core_dump_handler);
}