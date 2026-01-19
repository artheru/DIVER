#pragma once

#include "common.h"

void bsp_init_digital_io();

void bsp_set_outputs(uint32_t outputs);

uint32_t bsp_get_inputs();

/** @brief 获取数字输入数量 */
uint32_t bsp_get_digital_input_count();

/** @brief 获取数字输出数量 */
uint32_t bsp_get_digital_output_count();