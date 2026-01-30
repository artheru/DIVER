#include "midware.h"
#include "util/async.h"
#include "util/console.h"
//
#include "appl/threads.h"
#include "appl/upload.h"
#include "appl/version.h"
#include "appl/vm.h"
#include "bsp/bsp.h"
#include "hal/delay.h"


int main(void)
{
    init_midware();
    g_min_log_level = LogLevelDebug;

    init_bsp();

    init_upload();

    console_printf_do("Application started!\n");

    init_version_info();

    register_vm_core_dump();

    init_threads();

    midware_loop();
}