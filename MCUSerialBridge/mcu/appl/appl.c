#include "midware.h"
#include "util/async.h"
#include "util/console.h"
//
#include "appl/threads.h"
#include "appl/version.h"
#include "bsp/bsp.h"


int main(void)
{
    init_midware();
    g_min_log_level = LogLevelDebug;

    init_bsp();

    init_version_info();

    init_threads();

    midware_loop();
}