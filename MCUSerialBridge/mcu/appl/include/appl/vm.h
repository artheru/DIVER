#pragma once

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
#include "mcu_runtime.h"

#endif

// call vm_start_program() to start the program.
// before this, make sure the program is loaded.
// program is runned in a low priority thread.
void vm_start_program();
