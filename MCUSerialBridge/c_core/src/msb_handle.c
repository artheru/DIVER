#include "msb_handle.h"

#include <stdlib.h>
#include <string.h>

#include "c_core_common.h"


MCUSerialBridgeError msb_handle_init(msb_handle** handle)
{
    if (!handle)
        return MSB_Error_Win_InvalidParam;

    *handle = (msb_handle*)malloc(sizeof(msb_handle));
    if (!*handle)
        return MSB_Error_Win_AllocFail;

    memset(*handle, 0, sizeof(msb_handle));
    (*handle)->is_open = false;
    (*handle)->sequence = 1;

    // 初始化序号锁
    InitializeCriticalSection(&(*handle)->seq_lock);

    // 初始化 SeqWaiter 的锁和条件变量
    for (int i = 0; i < MAX_PENDING_SEQ; i++) {
        InitializeCriticalSection(&(*handle)->pending[i].mtx);
        InitializeConditionVariable(&(*handle)->pending[i].cnd);
        (*handle)->pending[i].in_use = false;
        (*handle)->pending[i].done_flag = false;
        (*handle)->pending[i].seq = 0;
        (*handle)->pending[i].result = MSB_Error_OK;
    }

    for (int i = 0; i < PACKET_MAX_PORTS_NUM; i++) {
        PortQueue* q = &(*handle)->ports[i];

        q->head = 0;
        q->tail = 0;

        q->data_event = CreateEvent(
                NULL,
                FALSE,  // auto-reset，非常关键
                FALSE,  // 初始为 non-signaled
                NULL);

        if (q->data_event == NULL) {
            // 清理已经创建的 event
            for (int j = 0; j < i; j++) {
                CloseHandle((*handle)->ports[j].data_event);
                (*handle)->ports[j].data_event = NULL;
            }
            DeleteCriticalSection(&(*handle)->seq_lock);
            free(*handle);
            *handle = NULL;
            return MSB_Error_Win_AllocFail;
        }
    }

    return MSB_Error_OK;
}

MCUSerialBridgeError msb_handle_deinit(msb_handle* handle)
{
    if (!handle)
        return MSB_Error_Win_HandleNotFound;

    // 删除 SeqWaiter 的锁
    for (int i = 0; i < MAX_PENDING_SEQ; i++) {
        DeleteCriticalSection(&handle->pending[i].mtx);
        // ConditionVariable 不需要销毁
    }

    for (int i = 0; i < PACKET_MAX_PORTS_NUM; i++) {
        if (handle->ports[i].data_event) {
            CloseHandle(handle->ports[i].data_event);
            handle->ports[i].data_event = NULL;
        }
    }

    DeleteCriticalSection(&handle->seq_lock);

    free(handle);
    return MSB_Error_OK;
}
