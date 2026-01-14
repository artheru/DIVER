#pragma once

// DIVER: Dotnet Integrating Vehicle Embedded Runtime

typedef unsigned char uchar;
typedef unsigned short ushort;

/*

┓┏       •          ┓  
┣┫┏┓┓┏┏  ┓╋  ┓┏┏┏┓┏┓┃┏┏
┛┗┗┛┗┻┛  ┗┗  ┗┻┛┗┛┛ ┛┗┛
                       
MCU:
void host_comm_ISR(uchar* buffer)
{
    // cache buffer.
}
void CAN/Modbus_comm_ISR()
{
    vm_put_event_buffer(...);
}
void Serial_comm_ISR()
{
    vm_put_stream_buffer(...);
}
void main()
{
    // read program from host system
    vm_set_program(program);

	int i=0;
    while(true)
    {
        if (wait_for_host_data(timeout))
			vm_put_event_buffer(...) // an internally defined watchdog event.
        vm_put_snapshot_buffer(do_GPIO_scan()...)
		vm_put_upper_memory(upper_buffer, uppser_size);
        vm_run(i++);
        upload_to_host(vm_get_lower_memory(), vm_get_lower_memory_size());
    }
}


interaction with host system:
   1. Host -> download program. 
   2. iteration:{
       1. Host -> MCU all cart.io 
       2. MCU -> Host all modified cart.io.
       3. Host: take modified io except UpperIO.
   }

*/


/*
┏┓  ┓┓   ┓      
┃ ┏┓┃┃  ╋┣┓┏┓┏┏┓
┗┛┗┻┗┗  ┗┛┗┗ ┛┗ 
*/

// Initialize: first allocate a buffer of size, then fill the buffer first sequence of bytes with program data.
// the size should be larger than program data size.
int vm_set_program(uchar* vm_memory, int vm_memory_size); //return interval in milliseconds.
void vm_run(int iteration); //if operation_id is same between previous/current call, it's a medulla communication timed out event.

// MCU - Medulla interface (use config protocol)
void vm_put_upper_memory(uchar* buffer, int size);
uchar* vm_get_lower_memory();
int vm_get_lower_memory_size();

// MCU - device interface.
// snap_shot buffer layout:
// {layout}|{payload}
// layout: (|1B io_type|2B components|)*N|0xFF|, io_type is 0-8 same to typeid. payload:boolean is byte-padded
void vm_put_snapshot_buffer(uchar* buffer, int size); // put IO/analog data here, as a whole snapshot.
void vm_put_stream_buffer(int streamID, uchar* buffer, int size); // put serial-like buffer here.
void vm_put_event_buffer(int portID, int eventID, uchar* buffer, int size); // put CAN/modbus similar data here.

/*
// MCU Serial Bridge Protocol (MCU ↔ PC Communication Protocol)
// Frame structure: |BB AA|Len(2B)|LenRev(2B)|Payload(N)|CRC16(2B)|EE EE|
// Payload structure: |CommandType(1B)|Sequence(4B)|Timestamp(4B)|ErrorCode(4B)|OtherData(...)|
//
// Command definitions (CommandType):
// PC → MCU (Request commands):
//   0x01: CommandConfigure - Set MCU port configuration (serial/CAN ports)
//         Response: 0x81 (same sequence, no additional data)
//   0x02: CommandReset - Reset MCU
//         Response: 0x82 (same sequence, no additional data), MCU resets after ~200ms
//   0x03: CommandState - Read MCU state
//         Response: 0x83 (same sequence, 4 bytes MCUState)
//   0x04: CommandVersion - Read MCU version info
//         Response: 0x84 (same sequence, VersionInfo structure)
//   0x05: CommandEnableWireTap - Enable wire tap mode (upload port data even in DIVER mode)
//         Response: 0x85 (same sequence, no additional data)
//   0x0F: CommandStart - Start MCU execution (DIVER mode or bridge mode)
//         Response: 0x8F (same sequence, no additional data)
//   0x10: CommandWritePort - Write data to specified port (serial/CAN)
//         Response: 0x90 (same sequence, no additional data)
//   0x30: CommandWriteOutput - Write MCU IO output (4 bytes)
//         Response: 0xB0 (same sequence, no additional data)
//   0x40: CommandReadInput - Read MCU IO input
//         Response: 0xC0 (same sequence, 4 bytes input data)
//   0x50: CommandProgram - Download program to MCU
//         If data length = 0: switch to Bridge (passthrough) mode
//         If data length > 0: switch to DIVER mode and load program (supports chunked transfer)
//         Response: 0xD0 (same sequence, no additional data)
//   0x60: CommandMemoryUpperIO - PC → MCU memory exchange (UpperIO in DIVER mode)
//         Response: 0xE0 (same sequence, no additional data)
//
// MCU → PC (Upload/Response commands):
//   0x20: CommandUploadPort - MCU uploads port received data (passthrough mode or wire tap enabled)
//         No response needed, sequence = 0
//   0x70: CommandMemoryLowerIO - MCU → PC memory exchange (LowerIO in DIVER mode)
//         No response needed, sequence = 0
//   0x80-0xFF: Response commands (0x80 | request_command)
//
//   0xFF: CommandError - Fatal error report (bidirectional)
//         sequence = 0, usually carries error code
//
// Special notes:
//   - Response commands: 0x80 | request_command (e.g., 0x81 = response to 0x01)
//   - Upload commands (CommandUploadPort, CommandMemoryLowerIO) have sequence = 0
//   - Program download supports chunked transfer using offset and total_len fields
//   - Wire tap mode allows port data upload even when MCU is in DIVER mode
*/

/*
 *
┳     ┓             ┓      
┃┏┳┓┏┓┃┏┓┏┳┓┏┓┏┓╋  ╋┣┓┏┓┏┏┓
┻┛┗┗┣┛┗┗ ┛┗┗┗ ┛┗┗  ┗┛┗┗ ┛┗ 
    ┛                      
*/

void write_snapshot(uchar* buffer, int size); // size is equal to "vm_put_snapshot_buffer", called per iteration
void write_stream(int streamID, uchar* buffer, int size); // called to write bytes into serial. called anytime needed.
void write_event(int portID, int eventID, uchar* buffer, int size); // called to write bytes into CAN/modbus similar ports. called anytime needed.

void report_error(int il_offset, uchar* error_str); // should report error and terminate execution, enter safe mode.
void print_line(uchar* error_str); // should upload text info.

#ifdef IS_MCU
// For implementation of inline functions
#include "appl/vm_inline.h"
#else
inline void enter_critical();
inline void leave_critical();

inline int get_cyclic_millis();
inline int get_cyclic_micros();
inline int get_cyclic_seconds();
#endif