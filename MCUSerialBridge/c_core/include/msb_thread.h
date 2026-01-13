#ifndef MCU_THREAD_H
#define MCU_THREAD_H

#include "msb_handle.h"
#include "msb_error_c.h"

#ifdef __cplusplus
extern "C" {
#endif

DWORD WINAPI recv_thread_func(LPVOID param);

DWORD WINAPI parse_thread_func(LPVOID param);

DWORD WINAPI send_thread_func(LPVOID param);

bool send_payload(msb_handle* handle, const uint8_t* data, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif // MCU_THREAD_H
