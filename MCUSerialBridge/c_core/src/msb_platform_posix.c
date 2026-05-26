#ifndef _WIN32
#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include "msb_platform.h"

#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/time.h>
#include <termios.h>
#include <time.h>
#include <unistd.h>

#define MSB_MAX_BAUD 12000000u
#define MSB_TRACE_PREVIEW_BYTES 32u

#ifndef NCCS2
#define NCCS2 19
#endif

#ifndef BOTHER
#define BOTHER 0010000
#endif

#ifndef CBAUD
#define CBAUD 0010017
#endif

struct termios2 {
    tcflag_t c_iflag;
    tcflag_t c_oflag;
    tcflag_t c_cflag;
    tcflag_t c_lflag;
    cc_t c_line;
    cc_t c_cc[NCCS2];
    speed_t c_ispeed;
    speed_t c_ospeed;
};

enum msb_platform_handle_kind {
    MSB_PLATFORM_HANDLE_SERIAL = 1,
    MSB_PLATFORM_HANDLE_EVENT = 2,
    MSB_PLATFORM_HANDLE_THREAD = 3
};

struct msb_platform_event {
    pthread_mutex_t mutex;
    pthread_cond_t cond;
    BOOL manual_reset;
    BOOL signaled;
};

struct msb_platform_thread {
    pthread_t thread;
    BOOL joined;
};

struct msb_platform_handle {
    enum msb_platform_handle_kind kind;
    union {
        int fd;
        struct msb_platform_event event;
        struct msb_platform_thread thread;
    } u;
};

static __thread DWORD g_last_error = 0;

static int msb_trace_io_enabled(void)
{
    const char* value = getenv("MSB_TRACE_IO");
    return value && value[0] != '\0' && strcmp(value, "0") != 0;
}

static void msb_trace_io_bytes(const char* op, const void* buffer, DWORD length)
{
    if (!msb_trace_io_enabled()) {
        return;
    }

    const uint8_t* bytes = (const uint8_t*)buffer;
    DWORD preview = length < MSB_TRACE_PREVIEW_BYTES ? length : MSB_TRACE_PREVIEW_BYTES;
    fprintf(stderr, "[MSB_TRACE_IO] %s len=%lu data=", op, (unsigned long)length);
    for (DWORD i = 0; i < preview; i++) {
        fprintf(stderr, "%02X", bytes[i]);
        if (i + 1 < preview) {
            fputc(' ', stderr);
        }
    }
    if (length > preview) {
        fprintf(stderr, " ...");
    }
    fputc('\n', stderr);
    fflush(stderr);
}

static DWORD msb_error_from_errno(int err)
{
    switch (err) {
        case ENOENT:
            return ERROR_FILE_NOT_FOUND;
        case EACCES:
        case EPERM:
            return ERROR_ACCESS_DENIED;
        case ENOMEM:
            return ERROR_NOT_ENOUGH_MEMORY;
        case EBADF:
            return ERROR_INVALID_HANDLE;
        case ETIMEDOUT:
            return ERROR_TIMEOUT;
        case ENODEV:
        case EIO:
            return ERROR_DEVICE_NOT_CONNECTED;
        default:
            return (DWORD)err;
    }
}

static int msb_is_valid_handle(HANDLE handle)
{
    return handle != NULL && handle != INVALID_HANDLE_VALUE;
}

static int msb_baud_to_speed(DWORD baud, speed_t* speed)
{
    switch (baud) {
        case 9600:
            *speed = B9600;
            return 1;
        case 19200:
            *speed = B19200;
            return 1;
        case 38400:
            *speed = B38400;
            return 1;
        case 57600:
            *speed = B57600;
            return 1;
        case 115200:
            *speed = B115200;
            return 1;
#ifdef B230400
        case 230400:
            *speed = B230400;
            return 1;
#endif
#ifdef B460800
        case 460800:
            *speed = B460800;
            return 1;
#endif
#ifdef B500000
        case 500000:
            *speed = B500000;
            return 1;
#endif
#ifdef B576000
        case 576000:
            *speed = B576000;
            return 1;
#endif
#ifdef B921600
        case 921600:
            *speed = B921600;
            return 1;
#endif
#ifdef B1000000
        case 1000000:
            *speed = B1000000;
            return 1;
#endif
#ifdef B1500000
        case 1500000:
            *speed = B1500000;
            return 1;
#endif
#ifdef B2000000
        case 2000000:
            *speed = B2000000;
            return 1;
#endif
#ifdef B2500000
        case 2500000:
            *speed = B2500000;
            return 1;
#endif
#ifdef B3000000
        case 3000000:
            *speed = B3000000;
            return 1;
#endif
#ifdef B3500000
        case 3500000:
            *speed = B3500000;
            return 1;
#endif
#ifdef B4000000
        case 4000000:
            *speed = B4000000;
            return 1;
#endif
        default:
            return 0;
    }
}

static int msb_set_custom_baud(int fd, DWORD baud)
{
    if (baud == 0 || baud > MSB_MAX_BAUD) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    struct termios2 tio2;
    if (ioctl(fd, TCGETS2, &tio2) != 0) {
        if (msb_trace_io_enabled()) {
            fprintf(stderr,
                    "[MSB_TRACE_IO] TCGETS2 failed baud=%lu errno=%d\n",
                    (unsigned long)baud,
                    errno);
            fflush(stderr);
        }
        SetLastError(msb_error_from_errno(errno));
        return 0;
    }

    tio2.c_cflag &= ~CBAUD;
    tio2.c_cflag |= BOTHER;
    tio2.c_ispeed = (speed_t)baud;
    tio2.c_ospeed = (speed_t)baud;

    if (ioctl(fd, TCSETS2, &tio2) != 0) {
        if (msb_trace_io_enabled()) {
            fprintf(stderr,
                    "[MSB_TRACE_IO] TCSETS2 failed baud=%lu errno=%d\n",
                    (unsigned long)baud,
                    errno);
            fflush(stderr);
        }
        SetLastError(msb_error_from_errno(errno));
        return 0;
    }

    if (msb_trace_io_enabled()) {
        fprintf(stderr, "[MSB_TRACE_IO] TCSETS2 ok baud=%lu\n", (unsigned long)baud);
        fflush(stderr);
    }

    return 1;
}

static int msb_make_deadline(struct timespec* ts, DWORD timeout_ms)
{
    if (clock_gettime(CLOCK_REALTIME, ts) != 0) {
        return -1;
    }
    ts->tv_sec += timeout_ms / 1000u;
    ts->tv_nsec += (long)(timeout_ms % 1000u) * 1000000L;
    if (ts->tv_nsec >= 1000000000L) {
        ts->tv_sec++;
        ts->tv_nsec -= 1000000000L;
    }
    return 0;
}

void InitializeCriticalSection(CRITICAL_SECTION* cs)
{
    pthread_mutex_init(cs, NULL);
}

void DeleteCriticalSection(CRITICAL_SECTION* cs)
{
    pthread_mutex_destroy(cs);
}

void EnterCriticalSection(CRITICAL_SECTION* cs)
{
    pthread_mutex_lock(cs);
}

void LeaveCriticalSection(CRITICAL_SECTION* cs)
{
    pthread_mutex_unlock(cs);
}

void InitializeConditionVariable(CONDITION_VARIABLE* cv)
{
    pthread_cond_init(cv, NULL);
}

BOOL SleepConditionVariableCS(CONDITION_VARIABLE* cv, CRITICAL_SECTION* cs, DWORD timeout_ms)
{
    int ret;
    if (timeout_ms == INFINITE) {
        ret = pthread_cond_wait(cv, cs);
    } else {
        struct timespec deadline;
        if (msb_make_deadline(&deadline, timeout_ms) != 0) {
            SetLastError(msb_error_from_errno(errno));
            return FALSE;
        }
        ret = pthread_cond_timedwait(cv, cs, &deadline);
    }

    if (ret == 0) {
        return TRUE;
    }
    SetLastError(ret == ETIMEDOUT ? ERROR_TIMEOUT : msb_error_from_errno(ret));
    return FALSE;
}

void WakeConditionVariable(CONDITION_VARIABLE* cv)
{
    pthread_cond_signal(cv);
}

HANDLE CreateEvent(void* security_attributes, BOOL manual_reset, BOOL initial_state, const char* name)
{
    (void)security_attributes;
    (void)name;
    struct msb_platform_handle* handle =
            (struct msb_platform_handle*)calloc(1, sizeof(struct msb_platform_handle));
    if (!handle) {
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return NULL;
    }
    handle->kind = MSB_PLATFORM_HANDLE_EVENT;
    pthread_mutex_init(&handle->u.event.mutex, NULL);
    pthread_cond_init(&handle->u.event.cond, NULL);
    handle->u.event.manual_reset = manual_reset;
    handle->u.event.signaled = initial_state;
    return handle;
}

BOOL SetEvent(HANDLE handle)
{
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_EVENT) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    pthread_mutex_lock(&handle->u.event.mutex);
    handle->u.event.signaled = TRUE;
    if (handle->u.event.manual_reset) {
        pthread_cond_broadcast(&handle->u.event.cond);
    } else {
        pthread_cond_signal(&handle->u.event.cond);
    }
    pthread_mutex_unlock(&handle->u.event.mutex);
    return TRUE;
}

static DWORD msb_wait_event(HANDLE handle, DWORD timeout_ms)
{
    int ret = 0;
    pthread_mutex_lock(&handle->u.event.mutex);
    while (!handle->u.event.signaled && ret == 0) {
        if (timeout_ms == INFINITE) {
            ret = pthread_cond_wait(&handle->u.event.cond, &handle->u.event.mutex);
        } else {
            struct timespec deadline;
            if (msb_make_deadline(&deadline, timeout_ms) != 0) {
                ret = errno;
                break;
            }
            ret = pthread_cond_timedwait(&handle->u.event.cond, &handle->u.event.mutex, &deadline);
        }
    }
    if (ret == 0 && !handle->u.event.manual_reset) {
        handle->u.event.signaled = FALSE;
    }
    pthread_mutex_unlock(&handle->u.event.mutex);
    if (ret == 0) {
        return WAIT_OBJECT_0;
    }
    SetLastError(ret == ETIMEDOUT ? ERROR_TIMEOUT : msb_error_from_errno(ret));
    return ret == ETIMEDOUT ? WAIT_TIMEOUT : WAIT_FAILED;
}

static DWORD msb_wait_thread(HANDLE handle, DWORD timeout_ms)
{
    if (handle->u.thread.joined) {
        return WAIT_OBJECT_0;
    }

    int ret;
    if (timeout_ms == INFINITE) {
        ret = pthread_join(handle->u.thread.thread, NULL);
    } else {
        struct timespec deadline;
        if (msb_make_deadline(&deadline, timeout_ms) != 0) {
            SetLastError(msb_error_from_errno(errno));
            return WAIT_FAILED;
        }
        ret = pthread_timedjoin_np(handle->u.thread.thread, NULL, &deadline);
    }

    if (ret == 0) {
        handle->u.thread.joined = TRUE;
        return WAIT_OBJECT_0;
    }
    if (ret == ETIMEDOUT) {
        SetLastError(ERROR_TIMEOUT);
        return WAIT_TIMEOUT;
    }
    SetLastError(msb_error_from_errno(ret));
    return WAIT_FAILED;
}

DWORD WaitForSingleObject(HANDLE handle, DWORD timeout_ms)
{
    if (!msb_is_valid_handle(handle)) {
        SetLastError(ERROR_INVALID_HANDLE);
        return WAIT_FAILED;
    }
    if (handle->kind == MSB_PLATFORM_HANDLE_EVENT) {
        return msb_wait_event(handle, timeout_ms);
    }
    if (handle->kind == MSB_PLATFORM_HANDLE_THREAD) {
        return msb_wait_thread(handle, timeout_ms);
    }
    SetLastError(ERROR_INVALID_HANDLE);
    return WAIT_FAILED;
}

BOOL CloseHandle(HANDLE handle)
{
    if (!msb_is_valid_handle(handle)) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    if (handle->kind == MSB_PLATFORM_HANDLE_SERIAL) {
        close(handle->u.fd);
    } else if (handle->kind == MSB_PLATFORM_HANDLE_EVENT) {
        pthread_cond_destroy(&handle->u.event.cond);
        pthread_mutex_destroy(&handle->u.event.mutex);
    } else if (handle->kind == MSB_PLATFORM_HANDLE_THREAD) {
        if (!handle->u.thread.joined) {
            pthread_detach(handle->u.thread.thread);
        }
    }
    free(handle);
    return TRUE;
}

struct msb_thread_start {
    LPTHREAD_START_ROUTINE start;
    LPVOID parameter;
};

static void* msb_thread_trampoline(void* arg)
{
    struct msb_thread_start* start = (struct msb_thread_start*)arg;
    LPTHREAD_START_ROUTINE fn = start->start;
    LPVOID parameter = start->parameter;
    free(start);
    return (void*)(uintptr_t)fn(parameter);
}

HANDLE CreateThread(
        void* security_attributes,
        size_t stack_size,
        LPTHREAD_START_ROUTINE start_address,
        LPVOID parameter,
        DWORD creation_flags,
        DWORD* thread_id)
{
    (void)security_attributes;
    (void)stack_size;
    (void)creation_flags;
    struct msb_platform_handle* handle =
            (struct msb_platform_handle*)calloc(1, sizeof(struct msb_platform_handle));
    struct msb_thread_start* start =
            (struct msb_thread_start*)calloc(1, sizeof(struct msb_thread_start));
    if (!handle || !start) {
        free(handle);
        free(start);
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return NULL;
    }
    handle->kind = MSB_PLATFORM_HANDLE_THREAD;
    start->start = start_address;
    start->parameter = parameter;
    int ret = pthread_create(&handle->u.thread.thread, NULL, msb_thread_trampoline, start);
    if (ret != 0) {
        free(handle);
        free(start);
        SetLastError(msb_error_from_errno(ret));
        return NULL;
    }
    if (thread_id) {
        *thread_id = 0;
    }
    return handle;
}

HANDLE CreateFileA(
        const char* filename,
        DWORD desired_access,
        DWORD share_mode,
        void* security_attributes,
        DWORD creation_disposition,
        DWORD flags_and_attributes,
        HANDLE template_file)
{
    (void)desired_access;
    (void)share_mode;
    (void)security_attributes;
    (void)creation_disposition;
    (void)flags_and_attributes;
    (void)template_file;

    int fd = open(filename, O_RDWR | O_NOCTTY);
    if (fd < 0) {
        SetLastError(msb_error_from_errno(errno));
        return INVALID_HANDLE_VALUE;
    }
    struct msb_platform_handle* handle =
            (struct msb_platform_handle*)calloc(1, sizeof(struct msb_platform_handle));
    if (!handle) {
        close(fd);
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return INVALID_HANDLE_VALUE;
    }
    handle->kind = MSB_PLATFORM_HANDLE_SERIAL;
    handle->u.fd = fd;
    return handle;
}

BOOL ReadFile(HANDLE handle, void* buffer, DWORD bytes_to_read, DWORD* bytes_read, void* overlapped)
{
    (void)overlapped;
    if (bytes_read) {
        *bytes_read = 0;
    }
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    if (bytes_to_read == 0) {
        return TRUE;
    }

    struct pollfd pfd;
    pfd.fd = handle->u.fd;
    pfd.events = POLLIN;
    pfd.revents = 0;
    int poll_ret = poll(&pfd, 1, 10);
    if (poll_ret == 0) {
        return TRUE;
    }
    if (poll_ret < 0) {
        if (errno == EINTR) {
            return TRUE;
        }
        SetLastError(msb_error_from_errno(errno));
        return FALSE;
    }
    if ((pfd.revents & (POLLERR | POLLHUP | POLLNVAL)) != 0) {
        SetLastError(ERROR_DEVICE_NOT_CONNECTED);
        return FALSE;
    }
    if ((pfd.revents & POLLIN) == 0) {
        return TRUE;
    }

    ssize_t ret = read(handle->u.fd, buffer, bytes_to_read);
    if (ret >= 0) {
        if (bytes_read) {
            *bytes_read = (DWORD)ret;
        }
        if (ret > 0) {
            msb_trace_io_bytes("read", buffer, (DWORD)ret);
        }
        return TRUE;
    }
    if (errno == EAGAIN || errno == EWOULDBLOCK) {
        return TRUE;
    }
    SetLastError(msb_error_from_errno(errno));
    return FALSE;
}

BOOL WriteFile(HANDLE handle, const void* buffer, DWORD bytes_to_write, DWORD* bytes_written, void* overlapped)
{
    (void)overlapped;
    if (bytes_written) {
        *bytes_written = 0;
    }
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    const uint8_t* ptr = (const uint8_t*)buffer;
    DWORD total = 0;
    if (msb_trace_io_enabled()) {
        fprintf(stderr, "[MSB_TRACE_IO] write-begin len=%lu\n", (unsigned long)bytes_to_write);
        fflush(stderr);
    }
    while (total < bytes_to_write) {
        ssize_t ret = write(handle->u.fd, ptr + total, bytes_to_write - total);
        if (ret > 0) {
            total += (DWORD)ret;
            if (msb_trace_io_enabled()) {
                fprintf(stderr,
                        "[MSB_TRACE_IO] write-chunk bytes=%ld total=%lu/%lu\n",
                        (long)ret,
                        (unsigned long)total,
                        (unsigned long)bytes_to_write);
                fflush(stderr);
            }
            continue;
        }
        if (ret < 0 && (errno == EAGAIN || errno == EWOULDBLOCK)) {
            usleep(1000);
            continue;
        }
        SetLastError(msb_error_from_errno(errno));
        if (bytes_written) {
            *bytes_written = total;
        }
        return FALSE;
    }
    if (bytes_written) {
        *bytes_written = total;
    }
    msb_trace_io_bytes("write", buffer, total);
    if (tcdrain(handle->u.fd) != 0) {
        SetLastError(msb_error_from_errno(errno));
        return FALSE;
    }
    if (msb_trace_io_enabled()) {
        fprintf(stderr, "[MSB_TRACE_IO] write-drained len=%lu\n", (unsigned long)total);
        struct pollfd pfd;
        pfd.fd = handle->u.fd;
        pfd.events = POLLIN;
        pfd.revents = 0;
        int poll_ret = poll(&pfd, 1, 100);
        int queued = 0;
        ioctl(handle->u.fd, FIONREAD, &queued);
        fprintf(stderr,
                "[MSB_TRACE_IO] after-write poll=%d revents=0x%x queued=%d\n",
                poll_ret,
                pfd.revents,
                queued);
        fflush(stderr);
    }
    return TRUE;
}

BOOL GetCommState(HANDLE handle, DCB* dcb)
{
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL || !dcb) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    memset(dcb, 0, sizeof(*dcb));
    dcb->DCBlength = sizeof(*dcb);
    dcb->BaudRate = 115200;
    dcb->ByteSize = 8;
    dcb->StopBits = ONESTOPBIT;
    dcb->Parity = NOPARITY;
    return TRUE;
}

BOOL SetCommState(HANDLE handle, DCB* dcb)
{
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL || !dcb) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }

    struct termios tty;
    if (tcgetattr(handle->u.fd, &tty) != 0) {
        SetLastError(msb_error_from_errno(errno));
        return FALSE;
    }
    cfmakeraw(&tty);
    tty.c_iflag &= ~(IXON | IXOFF | IXANY);
    if (dcb->BaudRate == 0 || dcb->BaudRate > MSB_MAX_BAUD) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    speed_t speed;
    int has_standard_baud = msb_baud_to_speed(dcb->BaudRate, &speed);
    if (has_standard_baud) {
        cfsetispeed(&tty, speed);
        cfsetospeed(&tty, speed);
    } else {
        // Use a valid placeholder for tcsetattr; TCSETS2/BOTHER applies the exact baud below.
        cfsetispeed(&tty, B38400);
        cfsetospeed(&tty, B38400);
    }
    tty.c_cflag |= (CLOCAL | CREAD);
    tty.c_cflag &= ~CSIZE;
    tty.c_cflag |= CS8;
    tty.c_cflag &= ~PARENB;
    tty.c_cflag &= ~CSTOPB;
#ifdef CRTSCTS
    tty.c_cflag &= ~CRTSCTS;
#endif
    tty.c_cc[VMIN] = 0;
    tty.c_cc[VTIME] = 0;

    if (tcsetattr(handle->u.fd, TCSANOW, &tty) != 0) {
        SetLastError(msb_error_from_errno(errno));
        return FALSE;
    }
    if (!msb_set_custom_baud(handle->u.fd, dcb->BaudRate)) {
        return FALSE;
    }
    tcflush(handle->u.fd, TCIOFLUSH);
    if (msb_trace_io_enabled()) {
        struct termios verify_tty;
        if (tcgetattr(handle->u.fd, &verify_tty) == 0) {
            fprintf(stderr,
                    "[MSB_TRACE_IO] termios baud=%lu standard=%d ispeed=%lu ospeed=%lu iflag=0x%lx cflag=0x%lx lflag=0x%lx\n",
                    (unsigned long)dcb->BaudRate,
                    has_standard_baud,
                    (unsigned long)cfgetispeed(&verify_tty),
                    (unsigned long)cfgetospeed(&verify_tty),
                    (unsigned long)verify_tty.c_iflag,
                    (unsigned long)verify_tty.c_cflag,
                    (unsigned long)verify_tty.c_lflag);
            fflush(stderr);
        }
    }
    return TRUE;
}

BOOL SetCommTimeouts(HANDLE handle, COMMTIMEOUTS* timeouts)
{
    (void)timeouts;
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    return TRUE;
}

BOOL ClearCommError(HANDLE handle, DWORD* errors, COMSTAT* stat)
{
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    if (errors) {
        *errors = 0;
    }
    if (stat) {
        memset(stat, 0, sizeof(*stat));
        int queued = 0;
        if (ioctl(handle->u.fd, FIONREAD, &queued) == 0) {
            stat->cbInQue = (DWORD)queued;
        }
    }
    return TRUE;
}

BOOL PurgeComm(HANDLE handle, DWORD flags)
{
    (void)flags;
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    tcflush(handle->u.fd, TCIOFLUSH);
    return TRUE;
}

BOOL CancelIoEx(HANDLE handle, void* overlapped)
{
    (void)overlapped;
    if (!msb_is_valid_handle(handle)) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    return TRUE;
}

BOOL FlushFileBuffers(HANDLE handle)
{
    if (!msb_is_valid_handle(handle) || handle->kind != MSB_PLATFORM_HANDLE_SERIAL) {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    return tcdrain(handle->u.fd) == 0 ? TRUE : FALSE;
}

DWORD GetLastError(void)
{
    return g_last_error;
}

void SetLastError(DWORD error)
{
    g_last_error = error;
}

DWORD GetTickCount(void)
{
    return (DWORD)(GetTickCount64() & 0xFFFFFFFFu);
}

uint64_t GetTickCount64(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000ull + (uint64_t)ts.tv_nsec / 1000000ull;
}

void Sleep(DWORD milliseconds)
{
    usleep((useconds_t)milliseconds * 1000u);
}

void GetLocalTime(SYSTEMTIME* system_time)
{
    if (!system_time) {
        return;
    }
    struct timeval tv;
    gettimeofday(&tv, NULL);
    struct tm tm_value;
    localtime_r(&tv.tv_sec, &tm_value);
    system_time->wYear = (uint16_t)(tm_value.tm_year + 1900);
    system_time->wMonth = (uint16_t)(tm_value.tm_mon + 1);
    system_time->wDay = (uint16_t)tm_value.tm_mday;
    system_time->wDayOfWeek = (uint16_t)tm_value.tm_wday;
    system_time->wHour = (uint16_t)tm_value.tm_hour;
    system_time->wMinute = (uint16_t)tm_value.tm_min;
    system_time->wSecond = (uint16_t)tm_value.tm_sec;
    system_time->wMilliseconds = (uint16_t)(tv.tv_usec / 1000);
}

#endif
