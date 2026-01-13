#pragma once

#include "common.h"
#include "hal/dcan.h"

void upload_serial_packet(
        const void* data,
        uint32_t length,
        uint32_t port_index);

void upload_can_packet(
        CANIDInfo id_info,
        uint32_t data_0_3,
        uint32_t data_4_7,
        uint32_t port_index);