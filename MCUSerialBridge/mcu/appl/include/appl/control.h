#pragma once

#include "common.h"
#include "msb_protocol.h"

extern volatile uint32_t g_inputs;
extern volatile uint32_t g_outputs;

extern volatile MCUStateC g_mcu_state;

MCUSerialBridgeError control_on_configure(
        const uint8_t* data,
        uint32_t data_length);

MCUSerialBridgeError control_on_reset(
        const uint8_t* data,
        uint32_t data_length);

MCUSerialBridgeError control_on_write_port(
        const uint8_t* data,
        uint32_t data_length,
        uint32_t sequence,
        uint8_t* async);

MCUSerialBridgeError control_on_write_output(
        const uint8_t* data,
        uint32_t data_length);
