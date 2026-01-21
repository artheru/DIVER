#include "bsp/digital_io.h"

#include "chip/periph.h"
#include "hal/delay.h"
#include "hal/gpio.h"
#include "hal/spi.h"

#define BSP_DIGITAL_IO_INPUT_NUM 16
#define BSP_DIGITAL_IO_OUTPUT_NUM 14

static GPIOHandle bsp_inputs[BSP_DIGITAL_IO_INPUT_NUM];

static GPIOHandle bsp_outputs[BSP_DIGITAL_IO_OUTPUT_NUM];

// 当前 IO 状态（供外部读取）
volatile uint32_t g_bsp_digital_inputs = 0;
volatile uint32_t g_bsp_digital_outputs = 0;

void bsp_init_digital_io()
{
    const GPIOConfig bsp_input_configs[BSP_DIGITAL_IO_INPUT_NUM] = {
            {.port = 'A', .pin = 12},  // 0
            {.port = 'A', .pin = 11},  // 1
            {.port = 'D', .pin = 3},   // 2
            {.port = 'D', .pin = 4},   // 3
            {.port = 'D', .pin = 5},   // 4
            {.port = 'D', .pin = 6},   // 5
            {.port = 'D', .pin = 7},   // 6
            {.port = 'B', .pin = 9},   // 7
            {.port = 'E', .pin = 0},   // 8
            {.port = 'E', .pin = 1},   // 9
            {.port = 'D', .pin = 15},  // 10
            {.port = 'D', .pin = 14},  // 11
            {.port = 'D', .pin = 13},  // 12
            {.port = 'D', .pin = 12},  // 13
            {.port = 'D', .pin = 11},  // 14
            {.port = 'D', .pin = 10},  // 15
    };

    const GPIOConfig bsp_output_configs[BSP_DIGITAL_IO_OUTPUT_NUM] = {
            {.port = 'B', .pin = 10},  // 0
            {.port = 'E', .pin = 15},  // 1
            {.port = 'E', .pin = 13},  // 2
            {.port = 'E', .pin = 12},  // 3
            {.port = 'E', .pin = 11},  // 4
            {.port = 'E', .pin = 10},  // 5
            {.port = 'E', .pin = 9},   // 6
            {.port = 'E', .pin = 8},   // 7
            {.port = 'E', .pin = 7},   // 8
            {.port = 'B', .pin = 2},   // 9
            {.port = 'B', .pin = 1},   // 10
            {.port = 'B', .pin = 0},   // 11
            {.port = 'C', .pin = 5},   // 12
            {.port = 'C', .pin = 4},   // 13
    };

    for (uint32_t i = 0; i < BSP_DIGITAL_IO_INPUT_NUM; ++i) {
        GPIOConfig input_config = bsp_input_configs[i];
        input_config.pull_direction = 0;
        input_config.pull_enable = 1;
        input_config.mode = GPIOMode_Input;
        bsp_inputs[i] = hal_gpio_register(input_config);
    }

    for (uint32_t i = 0; i < BSP_DIGITAL_IO_OUTPUT_NUM; ++i) {
        GPIOConfig output_config = bsp_output_configs[i];
        output_config.mode = GPIOMode_Output;
        bsp_outputs[i] = hal_gpio_register(output_config);
    }
}

void bsp_set_outputs(uint32_t outputs)
{
    g_bsp_digital_outputs = outputs;  // 保存输出状态
    for (uint32_t i = 0; i < BSP_DIGITAL_IO_OUTPUT_NUM; ++i) {
        hal_gpio_set(bsp_outputs[i], outputs & 0x01);
        outputs = outputs >> 1;
    }
}

uint32_t bsp_get_inputs()
{
    uint32_t input = 0;
    for (int32_t i = BSP_DIGITAL_IO_INPUT_NUM - 1; i >= 0; --i) {
        input = input << 1;
        input |= hal_gpio_get(bsp_inputs[i]);
    }
    g_bsp_digital_inputs = input;  // 保存输入状态
    return input;
}

uint32_t bsp_get_digital_input_count()
{
    return BSP_DIGITAL_IO_INPUT_NUM;
}

uint32_t bsp_get_digital_output_count()
{
    return BSP_DIGITAL_IO_OUTPUT_NUM;
}