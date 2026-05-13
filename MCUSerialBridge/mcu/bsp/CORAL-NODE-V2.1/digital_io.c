#include "bsp/digital_io.h"

#include "chip/periph.h"
#include "hal/delay.h"
#include "hal/gpio.h"
#include "hal/spi.h"

/* CORAL-NODE-V2.1 数字 IO 适配
 *
 * 移位寄存器链 (74HC595 输出 / 74HC165 输入) 通过 SPI2(PB13/14/15) 串行扫描，
 * 控制脚移到了 PB10/PB11/PB12（V2.0 在 PC0/PC1/PC2）。
 *
 *  PB10 = NOE       (输出禁止, 低有效)
 *  PB11 = IN_LOAD   (74HC165 S/L: 低=并行采样, 高=移位)
 *  PB12 = OUT_LOAD  (74HC595 RCLK: 上升沿锁存)
 *  PB13 = SPI2 SCK
 *  PB14 = SPI2 MISO (IN_DATA)
 *  PB15 = SPI2 MOSI (OUT_DATA)
 *
 * 输入 bitmap:
 *  bit 0..23 = 74HC165 链路顺序扫描得到的 24 路输入
 *  bit 24    = 24V 检测 (PB0)，高电平表示 24V 存在
 *  bit 25    = F1 急停 (PB2)，高电平表示急停未按下、允许运行
 *  bit 26    = F2 触边 (PB1)，高电平表示触边被按下、禁止运行
 */

#define IN_24V_BIT_POS 24
#define IN_F1_BIT_POS 25
#define IN_F2_BIT_POS 26

#define IN_24V_BIT_MASK ((uint32_t)(1u << IN_24V_BIT_POS))
#define IN_F1_BIT_MASK ((uint32_t)(1u << IN_F1_BIT_POS))
#define IN_F2_BIT_MASK ((uint32_t)(1u << IN_F2_BIT_POS))

#define BSP_SHIFT_INPUT_NUM 24
#define BSP_SHIFT_OUTPUT_NUM 20
#define BSP_DIGITAL_IO_INPUT_NUM 27
#define BSP_DIGITAL_IO_OUTPUT_NUM BSP_SHIFT_OUTPUT_NUM
#define BSP_SHIFT_IO_BYTES 3
#define BSP_SHIFT_INPUT_MASK ((uint32_t)((1u << BSP_SHIFT_INPUT_NUM) - 1u))
#define BSP_SHIFT_OUTPUT_MASK ((uint32_t)((1u << BSP_SHIFT_OUTPUT_NUM) - 1u))

static SPIHandle io_spi;
static GPIOHandle out_load;
static GPIOHandle out_noe;
static GPIOHandle in_load;

static GPIOHandle in_f1_emg_stop;
static GPIOHandle in_f2_safety_edge;
static GPIOHandle in_24v_detect;

volatile uint32_t g_bsp_digital_inputs = 0;
volatile uint32_t g_bsp_digital_outputs = 0;

void bsp_init_digital_io()
{
    const SPIConfig io_spi_cfg = {
            .spi_no = 2,
            .clock_mode = 0,
            .clock_frequency = 1000000,
            .lsb_first = 1,
            .mosi_gpio =
                    {
                            .mode = GPIOMode_AlternativeFunction,
                            .port = 'B',
                            .pin = 15,
                            .speed = 5,
                    },
            .miso_gpio =
                    {
                            .mode = GPIOMode_AlternativeFunction,
                            .port = 'B',
                            .pin = 14,
                            .speed = 5,
                    },
            .sck_gpio =
                    {
                            .mode = GPIOMode_AlternativeFunction,
                            .port = 'B',
                            .pin = 13,
                            .speed = 5,
                    },
            .usage = "IO",
    };
    io_spi = hal_spi_register(io_spi_cfg);

    const GPIOConfig out_noe_cfg = {
            .mode = GPIOMode_Output,
            .port = 'B',
            .pin = 10,
            .speed = 5,
            .initial_state = 1,
    };
    const GPIOConfig in_load_cfg = {
            .mode = GPIOMode_Output,
            .port = 'B',
            .pin = 11,
            .speed = 5,
    };
    const GPIOConfig out_load_cfg = {
            .mode = GPIOMode_Output,
            .port = 'B',
            .pin = 12,
            .speed = 5,
    };
    out_noe = hal_gpio_register(out_noe_cfg);
    in_load = hal_gpio_register(in_load_cfg);
    out_load = hal_gpio_register(out_load_cfg);

    const GPIOConfig in_24v_cfg = {
            .mode = GPIOMode_Input,
            .port = 'B',
            .pin = 0,
            .speed = 1,
            .pull_enable = 1,
            .pull_direction = 0,
    };
    const GPIOConfig in_f2_cfg = {
            .mode = GPIOMode_Input,
            .port = 'B',
            .pin = 1,
            .speed = 1,
            .pull_enable = 1,
            .pull_direction = 0,
    };
    const GPIOConfig in_f1_cfg = {
            .mode = GPIOMode_Input,
            .port = 'B',
            .pin = 2,
            .speed = 1,
            .pull_enable = 1,
            .pull_direction = 0,
    };
    in_24v_detect = hal_gpio_register(in_24v_cfg);
    in_f2_safety_edge = hal_gpio_register(in_f2_cfg);
    in_f1_emg_stop = hal_gpio_register(in_f1_cfg);
}

void bsp_digital_io_refresh()
{
    uint32_t input_raw = 0;
    uint32_t output_raw = g_bsp_digital_outputs & BSP_SHIFT_OUTPUT_MASK;

    hal_gpio_set(in_load, 0);
    delay_ns(200);
    hal_gpio_set(in_load, 1);
    delay_ns(200);

    hal_spi_master_transmit_sync(
            io_spi, &output_raw, &input_raw, BSP_SHIFT_IO_BYTES);
    delay_ns(200);

    /* 74HC165 输入侧外接 NPN 漏极开路传感器，硬件取反一次回到正逻辑 */
    input_raw = (~input_raw) & BSP_SHIFT_INPUT_MASK;

    /* 固定输入追加到 74HC165 的 24 路输入之后 */
    if (hal_gpio_get(in_24v_detect)) {
        input_raw |= IN_24V_BIT_MASK;
    }
    if (hal_gpio_get(in_f1_emg_stop)) {
        input_raw |= IN_F1_BIT_MASK;
    }
    if (hal_gpio_get(in_f2_safety_edge)) {
        input_raw |= IN_F2_BIT_MASK;
    }

    g_bsp_digital_inputs = input_raw;

    hal_gpio_set(out_load, 0);
    delay_ns(200);
    hal_gpio_set(out_load, 1);
    delay_ns(200);
    hal_gpio_set(out_load, 0);
    delay_ns(200);

    hal_gpio_set(out_noe, 0);
}

void bsp_set_outputs(uint32_t outputs)
{
    g_bsp_digital_outputs = outputs;
    bsp_digital_io_refresh();
}

uint32_t bsp_get_inputs()
{
    bsp_digital_io_refresh();
    return g_bsp_digital_inputs;
}

uint32_t bsp_get_digital_input_count()
{
    return BSP_DIGITAL_IO_INPUT_NUM;
}

uint32_t bsp_get_digital_output_count()
{
    return BSP_DIGITAL_IO_OUTPUT_NUM;
}
