#include <stdio.h>
#include <time.h>
#include <windows.h>

#include "c_core_common.h"
#include "msb_bridge.h"


static volatile BOOL g_running = TRUE;

/* Ctrl+C 回调 */
BOOL WINAPI console_ctrl_handler(DWORD ctrl_type)
{
    if (ctrl_type == CTRL_C_EVENT || ctrl_type == CTRL_CLOSE_EVENT) {
        printf("\nCtrl+C received, exiting...\n");
        g_running = FALSE;
        return TRUE;
    }
    return FALSE;
}

/* 纯 C 版本的日志函数，格式：[HH:MM:SS.mmm] PureC Test    | message */
void Log(const char* fmt, ...)
{
    SYSTEMTIME st;
    GetLocalTime(&st);

    printf("[%02d:%02d:%02d.%03d] PureC Test | ",
           st.wHour,
           st.wMinute,
           st.wSecond,
           st.wMilliseconds);

    va_list args;
    va_start(args, fmt);
    vprintf(fmt, args);
    va_end(args);

    printf("\n");
}

int main(void)
{
    HMODULE h = LoadLibrary("mcu_serial_bridge.dll");
    if (!h) {
        printf("Failed to load DLL\n");
        return -1;
    }
    void (*mcu_serial_bridge_get_api)(MCUSerialBridgeAPI*) = (void (*)(
            MCUSerialBridgeAPI*))GetProcAddress(h, "mcu_serial_bridge_get_api");
    if (!mcu_serial_bridge_get_api) {
        printf("Failed to get mcu_serial_bridge_get_api symbol\n");
        return -1;
    }
    MCUSerialBridgeAPI api;
    mcu_serial_bridge_get_api(&api);

    msb_handle* handle = NULL;

    SetConsoleCtrlHandler(console_ctrl_handler, TRUE);

    MCUSerialBridgeError ret = api.msb_open(&handle, "COM18", 1000000);
    if (ret != MSB_Error_OK) {
        Log("MCU Open FAILED: 0x%08X", ret);
        return -1;
    }
    Log("MCU Open OK");

    VersionInfoC version;
    memset(&version, 0, sizeof(version));
    ret = api.msb_version(handle, &version, 1000);
    if (ret == MSB_Error_OK) {
        Log("MCU Version OK");
        Log("  PDN       : %.*s", (int)sizeof(version.PDN), version.PDN);
        Log("  Tag       : %.*s", (int)sizeof(version.Tag), version.Tag);
        Log("  Commit    : %.*s", (int)sizeof(version.Commit), version.Commit);
        Log("  BuildTime : %.*s",
            (int)sizeof(version.BuildTime),
            version.BuildTime);
    } else {
        Log("MCU Version FAILED: 0x%08X", ret);
        return -1;
    }

    MCUStateC state;
    ret = api.mcu_state(handle, &state, 1000);
    if (ret == MSB_Error_OK) {
        Log("MCU State: 0x%08X", state);
    } else {
        Log("MCU State FAILED: 0x%08X", ret);
        return -1;
    }

    // Reset MCU
    ret = api.msb_reset(handle, 1000);
    if (ret == MSB_Error_OK) {
        Log("MCU Reset OK");
    } else {
        Log("MCU Reset FAILED: 0x%08X", ret);
    }
    Sleep(500);

    // ======================== 配置端口 ========================
    PortConfigC ports[6] = {0};

    // Port 0~3: Serial, Baud=115200, ReceiveFrameMs=0
    for (int i = 0; i < 4; i++) {
        ports[i].port_type = PortType_Serial;
        SerialPortConfigC* s = (SerialPortConfigC*)&ports[i];
        s->baud = 115200;
        s->receive_frame_ms = 0;
    }

    // Port 4~5: CAN, Baud=250000, RetryTimeMs=10
    for (int i = 4; i < 6; i++) {
        ports[i].port_type = PortType_CAN;
        CANPortConfigC* c = (CANPortConfigC*)&ports[i];
        c->baud = 250000;
        c->retry_time_ms = 10;
    }

    Log("=== Port Configuration ===");
    for (int i = 0; i < 6; i++) {
        if (ports[i].port_type == PortType_Serial) {
            SerialPortConfigC* s = (SerialPortConfigC*)&ports[i];
            Log("Port %d: Serial, Baud=%u, ReceiveFrameMs=%u",
                i,
                s->baud,
                s->receive_frame_ms);
        } else if (ports[i].port_type == PortType_CAN) {
            CANPortConfigC* c = (CANPortConfigC*)&ports[i];
            Log("Port %d: CAN, Baud=%u, RetryTimeMs=%u",
                i,
                c->baud,
                c->retry_time_ms);
        }
    }
    Log("=========================");

    uint32_t configure_timeout = 500;
    ret = api.msb_configure(handle, 6, ports, configure_timeout);
    if (ret == MSB_Error_OK) {
        Log("MCU Configure OK");
    } else {
        Log("MCU Configure FAILED: 0x%08X", ret);
    }

    ret = api.mcu_state(handle, &state, 1000);
    if (ret == MSB_Error_OK) {
        Log("MCU State: 0x%08X", state);
    } else {
        Log("MCU State FAILED: 0x%08X", ret);
        return -1;
    }

    Sleep(2000);

    Log("=== Main Loop Start ===");

    const int STEP_DELAY_MS = 100;
    const int TIMEOUT_MS = 50;

    uint32_t ioStep = 0;
    uint8_t canIdBase = 10;

    uint8_t testData[32];
    for (int i = 0; i < 32; i++) {
        testData[i] = (uint8_t)(0x30 + (i % 10));  // "0123456789..." 循环
    }

    while (g_running) {
        MCUSerialBridgeError err;

        // 1. IO 流水灯
        uint32_t ioValue = 1u << ioStep;
        uint8_t ioBuf[4];
        memcpy(ioBuf, &ioValue, 4);

        err = api.msb_write_output(handle, ioBuf, TIMEOUT_MS);
        Log("IO Write bit %u (0x%04X) -> %s",
            ioStep,
            ioValue,
            (err == MSB_Error_OK) ? "OK" : "FAILED");

        // 立即读取 IO 输入
        uint8_t ioReadBuf[4];
        err = api.msb_read_input(handle, ioReadBuf, TIMEOUT_MS);
        if (err == MSB_Error_OK) {
            uint32_t ioReadValue = 0;
            memcpy(&ioReadValue, ioReadBuf, 4);

            Log("IO Read  raw value: 0x%08X", ioReadValue);

            // 构建低 16 bit 字符串：从低位到高位，每 8 bit 一组，用空格分隔
            char bitStr[32] = {0};  // 16 bit + 1 空格 + 结尾0 足够
            char* p = bitStr;

            for (int bit = 0; bit < 16; bit++) {  // bit 0 ~ bit 15
                *p++ = ((ioReadValue & (1u << bit)) ? '1' : '0');
                if ((bit % 8 == 7) &&
                    (bit != 15)) {  // 每 8 bit 后加空格（除最后）
                    *p++ = ' ';
                }
            }
            *p = '\0';

            Log("IO Read  bits(0-15): %s", bitStr);
        } else {
            Log("IO Read FAILED");
        }

        Sleep(STEP_DELAY_MS);

        // 2. Serial Port 3 自发自收
        err = api.msb_write_port(handle, 3, testData, 32, TIMEOUT_MS);
        Log("Write Serial Port 3 (32 bytes) -> %s",
            (err == MSB_Error_OK) ? "OK" : "FAILED");

        uint8_t recvBuf[256];
        uint32_t recvLen = 0;
        err = api.msb_read_port(
                handle, 3, recvBuf, sizeof(recvBuf), &recvLen, TIMEOUT_MS);
        if (err == MSB_Error_OK && recvLen > 0) {
            Log("Read Serial Port 3 SUCCESS");
            printf("Received hex  : ");
            for (uint32_t i = 0; i < recvLen; i++)
                printf("%02X ", recvBuf[i]);
            printf("\n");
        } else {
            Log("Read Serial Port 3 FAILED or No Data");
        }

        Sleep(STEP_DELAY_MS);

        // 3. Serial Port 0 → Serial Port 1 (485 对发)
        err = api.msb_write_port(handle, 0, testData, 32, TIMEOUT_MS);
        Log("Write Serial Port 0 (32 bytes) -> %s",
            (err == MSB_Error_OK) ? "OK" : "FAILED");

        recvLen = 0;
        err = api.msb_read_port(
                handle, 1, recvBuf, sizeof(recvBuf), &recvLen, TIMEOUT_MS);
        if (err == MSB_Error_OK && recvLen > 0) {
            Log("Read Serial Port 1 SUCCESS (from Port 0)");
            printf("Received hex  : ");
            for (uint32_t i = 0; i < recvLen; i++)
                printf("%02X ", recvBuf[i]);
            printf("\n");
        } else {
            Log("Read Serial Port 1 FAILED or No Data");
        }

        Sleep(STEP_DELAY_MS);

        // 4. CAN Port 4 → CAN Port 5
        uint8_t canPayload[8];
        for (int i = 0; i < 8; i++) {
            canPayload[i] = (uint8_t)(canIdBase + i + 1);
        }

        // 打包 2 字节头 + 8 字节 payload
        uint8_t canFrame[10];
        uint16_t id_info =
                (canIdBase & 0x7FF) | (0 << 11) | (8 << 12);  // RTR=0, DLC=8
        canFrame[0] = (uint8_t)(id_info & 0xFF);
        canFrame[1] = (uint8_t)(id_info >> 8);
        memcpy(canFrame + 2, canPayload, 8);

        err = api.msb_write_port(handle, 4, canFrame, 10, TIMEOUT_MS);
        Log("Write CAN Port 4 ID=0x%02X DLC=8 -> %s",
            canIdBase,
            (err == MSB_Error_OK) ? "OK" : "FAILED");

        Sleep(STEP_DELAY_MS);

        recvLen = 0;
        err = api.msb_read_port(
                handle, 5, recvBuf, sizeof(recvBuf), &recvLen, TIMEOUT_MS);
        if (err == MSB_Error_OK && recvLen > 0) {
            Log("Read CAN Port 5 SUCCESS");
            printf("Received hex  : ");
            for (uint32_t i = 0; i < recvLen; i++)
                printf("%02X ", recvBuf[i]);
            printf("\n");
        } else {
            Log("Read CAN Port 5 FAILED or No Data");
        }

        canIdBase++;

        // IO 步进
        if (++ioStep >= 14) {
            ioStep = 0;
        }

        Sleep(STEP_DELAY_MS);
    }

    api.msb_close(handle);
    Log("MCU Closed");
    Log("Program exited");

    return 0;
}