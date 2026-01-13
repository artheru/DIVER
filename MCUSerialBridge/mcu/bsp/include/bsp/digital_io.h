#pragma once

#include "common.h"

void bsp_init_digital_io();

void bsp_set_outputs(uint32_t outputs);

uint32_t bsp_get_inputs();
