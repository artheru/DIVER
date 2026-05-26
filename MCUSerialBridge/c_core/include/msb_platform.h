#ifndef MSB_PLATFORM_H
#define MSB_PLATFORM_H

#ifdef _WIN32

#include <windows.h>

#else

#include <pthread.h>
#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef int BOOL;
typedef uint32_t DWORD;
typedef void* LPVOID;
typedef DWORD (*LPTHREAD_START_ROUTINE)(LPVOID);

#ifndef TRUE
#define TRUE 1
#endif
#ifndef FALSE
#define FALSE 0
#endif

#define WINAPI
#define INFINITE 0xFFFFFFFFu
#define WAIT_OBJECT_0 0
#define WAIT_TIMEOUT 258
#define WAIT_FAILED 0xFFFFFFFFu
#define MAXDWORD 0xFFFFFFFFu

#define GENERIC_READ 0x80000000u
#define GENERIC_WRITE 0x40000000u
#define OPEN_EXISTING 3
#define FILE_ATTRIBUTE_NORMAL 0x00000080u

#define PURGE_RXABORT 0x0002
#define PURGE_RXCLEAR 0x0008
#define PURGE_TXABORT 0x0001
#define PURGE_TXCLEAR 0x0004

#define ONESTOPBIT 0
#define NOPARITY 0

#define ERROR_FILE_NOT_FOUND 2
#define ERROR_PATH_NOT_FOUND 3
#define ERROR_INVALID_HANDLE 6
#define ERROR_NOT_ENOUGH_MEMORY 8
#define ERROR_ACCESS_DENIED 5
#define ERROR_INVALID_USER_BUFFER 1784
#define ERROR_INVALID_PARAMETER 87
#define ERROR_SHARING_VIOLATION 32
#define ERROR_GEN_FAILURE 31
#define ERROR_OPERATION_ABORTED 995
#define ERROR_DEVICE_NOT_CONNECTED 1167
#define ERROR_TIMEOUT 1460

typedef struct msb_platform_handle* HANDLE;
#define INVALID_HANDLE_VALUE ((HANDLE)(intptr_t)-1)

typedef pthread_mutex_t CRITICAL_SECTION;
typedef pthread_cond_t CONDITION_VARIABLE;

typedef struct {
    uint16_t wYear;
    uint16_t wMonth;
    uint16_t wDayOfWeek;
    uint16_t wDay;
    uint16_t wHour;
    uint16_t wMinute;
    uint16_t wSecond;
    uint16_t wMilliseconds;
} SYSTEMTIME;

typedef struct {
    DWORD DCBlength;
    DWORD BaudRate;
    uint8_t ByteSize;
    uint8_t StopBits;
    uint8_t Parity;
    BOOL fBinary;
    BOOL fParity;
    BOOL fOutxCtsFlow;
    BOOL fOutxDsrFlow;
    DWORD fDtrControl;
    DWORD fRtsControl;
    BOOL fOutX;
    BOOL fInX;
} DCB;

#define DTR_CONTROL_DISABLE 0
#define RTS_CONTROL_DISABLE 0

typedef struct {
    DWORD ReadIntervalTimeout;
    DWORD ReadTotalTimeoutMultiplier;
    DWORD ReadTotalTimeoutConstant;
    DWORD WriteTotalTimeoutMultiplier;
    DWORD WriteTotalTimeoutConstant;
} COMMTIMEOUTS;

typedef struct {
    DWORD cbInQue;
    DWORD cbOutQue;
} COMSTAT;

void InitializeCriticalSection(CRITICAL_SECTION* cs);
void DeleteCriticalSection(CRITICAL_SECTION* cs);
void EnterCriticalSection(CRITICAL_SECTION* cs);
void LeaveCriticalSection(CRITICAL_SECTION* cs);

void InitializeConditionVariable(CONDITION_VARIABLE* cv);
BOOL SleepConditionVariableCS(CONDITION_VARIABLE* cv, CRITICAL_SECTION* cs, DWORD timeout_ms);
void WakeConditionVariable(CONDITION_VARIABLE* cv);

HANDLE CreateEvent(void* security_attributes, BOOL manual_reset, BOOL initial_state, const char* name);
BOOL SetEvent(HANDLE handle);
DWORD WaitForSingleObject(HANDLE handle, DWORD timeout_ms);
BOOL CloseHandle(HANDLE handle);

HANDLE CreateThread(
        void* security_attributes,
        size_t stack_size,
        LPTHREAD_START_ROUTINE start_address,
        LPVOID parameter,
        DWORD creation_flags,
        DWORD* thread_id);

HANDLE CreateFileA(
        const char* filename,
        DWORD desired_access,
        DWORD share_mode,
        void* security_attributes,
        DWORD creation_disposition,
        DWORD flags_and_attributes,
        HANDLE template_file);
BOOL ReadFile(HANDLE handle, void* buffer, DWORD bytes_to_read, DWORD* bytes_read, void* overlapped);
BOOL WriteFile(HANDLE handle, const void* buffer, DWORD bytes_to_write, DWORD* bytes_written, void* overlapped);
BOOL GetCommState(HANDLE handle, DCB* dcb);
BOOL SetCommState(HANDLE handle, DCB* dcb);
BOOL SetCommTimeouts(HANDLE handle, COMMTIMEOUTS* timeouts);
BOOL ClearCommError(HANDLE handle, DWORD* errors, COMSTAT* stat);
BOOL PurgeComm(HANDLE handle, DWORD flags);
BOOL CancelIoEx(HANDLE handle, void* overlapped);
BOOL FlushFileBuffers(HANDLE handle);

DWORD GetLastError(void);
void SetLastError(DWORD error);
DWORD GetTickCount(void);
uint64_t GetTickCount64(void);
void Sleep(DWORD milliseconds);
void GetLocalTime(SYSTEMTIME* system_time);

#ifdef __cplusplus
}
#endif

#endif

#endif
