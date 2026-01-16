#pragma once

#include "common.h"
#include "hal/systick.h"

#if defined(HAS_DIVER_RUNTIME) && HAS_DIVER_RUNTIME == 1

__attribute__((always_inline)) static inline void enter_critical()
{
    __asm volatile("cpsid i" : : : "memory");
}
__attribute__((always_inline)) static inline void leave_critical()
{
    __asm volatile("cpsie i" : : : "memory");
}
__attribute__((always_inline)) static inline int get_cyclic_millis()
{
    return (int)(g_hal_timestamp_us / 1000);
}
__attribute__((always_inline)) static inline int get_cyclic_micros()
{
    return (int)(g_hal_timestamp_us);
}
__attribute__((always_inline)) static inline int get_cyclic_seconds()
{
    return (int)(g_hal_timestamp_us / 1000000);
}
#endif