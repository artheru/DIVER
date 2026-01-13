#pragma once

#include "common.h"
#include "hal/dcan.h"
#include "hal/usart.h"

extern const uint32_t ports_can_num;
extern const uint32_t ports_serial_num;

#define ports_total_num (ports_can_num + ports_serial_num)

extern DirectCANConfig bsp_can_configs[];

extern USARTConfig bsp_serial_configs[];
