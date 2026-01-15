#pragma once

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1
#include "mcu_runtime.h"

#endif

// call vm_start_program() to start the program.
// before this, make sure the program is loaded.
// program is runned in a low priority thread.
void vm_start_program();

void register_vm_core_dump();

#define DIVER_SERIAL_SEND_BUFFER_TOTAL_SIZE (2048)
#define DIVER_SERIAL_SEND_BUFFER_SEGMENT_COUNT (16)
#define UPPERIO_BUFFER_SIZE (1024)