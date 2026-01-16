#include "appl/vm.h"
//
#include "hal/core_dump.h"
#include "util/console.h"

extern int cur_il_offset;

static void core_dump_handler(CoreDumpVariables* core_dump)
{
#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
    console_printf_do("Core Dump: IL Offset = %d\n", cur_il_offset);
#endif
}

void register_vm_core_dump()
{
    hal_core_dump_register(core_dump_handler);
}