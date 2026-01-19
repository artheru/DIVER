#pragma once

#include "common.h"
#include "hal/dcan.h"
#include "hal/usart.h"
#include "msb_protocol.h"

extern const uint32_t ports_can_num;
extern const uint32_t ports_serial_num;

#define ports_total_num (ports_can_num + ports_serial_num)

extern DirectCANConfig bsp_can_configs[];

extern USARTConfig bsp_serial_configs[];

/**
 * @brief 获取硬件布局信息
 * 
 * 填充 LayoutInfoC 结构体，包括数字 IO 数量和端口信息
 * 
 * @param[out] layout 输出布局信息指针
 */
void bsp_get_layout(LayoutInfoC* layout);
