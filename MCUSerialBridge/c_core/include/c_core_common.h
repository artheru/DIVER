#ifndef C_CORE_COMMON_H
#define C_CORE_COMMON_H

#define DBG_PRINT(fmt, ...)                                    \
    do {                                                       \
        SYSTEMTIME st;                                         \
        GetLocalTime(&st);                                     \
        printf("[%02d:%02d:%02d.%03d] MCU Bridge | " fmt "\n", \
               st.wHour,                                       \
               st.wMinute,                                     \
               st.wSecond,                                     \
               st.wMilliseconds,                               \
               ##__VA_ARGS__);                                 \
    } while (0)

#endif