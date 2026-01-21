#pragma once

#include "common.h"

/** @brief 当前数字输入状态（位图），由 bsp_get_inputs() 更新 */
extern volatile uint32_t g_bsp_digital_inputs;

/** @brief 当前数字输出状态（位图），由 bsp_set_outputs() 更新 */
extern volatile uint32_t g_bsp_digital_outputs;

void bsp_init_digital_io();

void bsp_set_outputs(uint32_t outputs);

uint32_t bsp_get_inputs();

/** @brief 获取数字输入数量 */
uint32_t bsp_get_digital_input_count();

/** @brief 获取数字输出数量 */
uint32_t bsp_get_digital_output_count();