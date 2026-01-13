#pragma once

#include "common.h"
#include "hal/core_dump.h"
#include "hal/usart.h"

extern USARTHandle uplink_usart;

void init_threads();
