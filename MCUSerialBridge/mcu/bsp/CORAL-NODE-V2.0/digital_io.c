#include "bsp/digital_io.h"

#include "chip/periph.h"
#include "hal/delay.h"
#include "hal/gpio.h"
#include "hal/spi.h"

static SPIHandle io_spi;
static GPIOHandle out_load;
static GPIOHandle out_noe;
static GPIOHandle in_load;

#define IN_F1_BIT_POS 0
#define IN_F2_BIT_POS 7
#define IN_F1_BIT_MASK ((uint32_t)(1 << IN_F1_BIT_POS))
#define IN_F2_BIT_MASK ((uint32_t)(1 << IN_F2_BIT_POS))
static GPIOHandle in_f1_emg_stop;
static GPIOHandle in_f2_safety_edge;

uint32_t output_data_raw;
uint32_t input_data_raw;

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

    const GPIOConfig out_load_cfg = {
            .mode = GPIOMode_Output,
            .port = 'C',
            .pin = 0,
            .speed = 5,
    };
    const GPIOConfig out_noe_cfg = {
            .mode = GPIOMode_Output,
            .port = 'C',
            .pin = 2,
            .speed = 5,
            .initial_state = 1,
    };
    const GPIOConfig in_load_cfg = {
            .mode = GPIOMode_Output,
            .port = 'C',
            .pin = 1,
            .speed = 5,
    };
    out_load = hal_gpio_register(out_load_cfg);
    out_noe = hal_gpio_register(out_noe_cfg);
    in_load = hal_gpio_register(in_load_cfg);

    const GPIOConfig in_f1_emg_stop_cfg = {
            .mode = GPIOMode_Input,
            .port = 'C',
            .pin = 13,
            .speed = 1,
            .pull_enable = 1,
            .pull_direction = 0,
    };
    in_f1_emg_stop = hal_gpio_register(in_f1_emg_stop_cfg);
    const GPIOConfig in_f2_safety_edge_cfg = {
            .mode = GPIOMode_Input,
            .port = 'C',
            .pin = 14,
            .speed = 1,
            .pull_enable = 1,
            .pull_direction = 1,
    };
    in_f2_safety_edge = hal_gpio_register(in_f2_safety_edge_cfg);
}

void bsp_digital_io_refresh(uint32_t* input, const uint32_t* output)
{
    uint32_t input_raw;
    uint32_t output_raw = *output;
    // Load parallel inputs into 74HCT165 shift registers
    hal_gpio_set(in_load, 0);  // Set S/L low to load parallel data
    delay_ns(200);
    hal_gpio_set(in_load, 1);  // Set S/L high to enable shifting
    delay_ns(200);
    // Perform SPI transaction: send output data and receive input data
    // simultaneously
    hal_spi_master_transmit_sync(io_spi, &output_raw, &input_raw, 4);
    delay_ns(200);

    // Since, Input is NPN type
    input_raw = ~input_raw;

    // Read F1 and F2
    input_raw &= ~(IN_F1_BIT_MASK | IN_F1_BIT_MASK);
    if (hal_gpio_get(in_f1_emg_stop)) {
        input_raw |= IN_F1_BIT_MASK;
    }
    if (hal_gpio_get(in_f2_safety_edge)) {
        input_raw |= IN_F2_BIT_MASK;
    }

    // Copy value to arg ptr
    *input = input_raw;

    // Latch the output data to the 74HCT595 output registers
    hal_gpio_set(out_load, 0);  // Ensure RCLK is low
    delay_ns(200);
    hal_gpio_set(out_load, 1);  // Pulse RCLK high to latch data
    delay_ns(200);
    hal_gpio_set(out_load, 0);  // Return RCLK to low
    delay_ns(200);

    // Set out_noe to 0 to enable outputs (active low)
    hal_gpio_set(out_noe, 0);
}
