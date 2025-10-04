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
// MCU - Medulla Protocol, an example:
// |magic 2B|len 2B|hash 1B|payload {len}B|hash_all 1B|... repeat.
// payload: |command 1B|operand XB...
// commands:
    0> PC->MCU: set MCU configuration. operand: configuration bytes (MCU developer should implement)
	1> PC->MCU: set operation program. operand: |len 4B|buffer bytes. {len}B|
    2> PC->MCU: upperIO buffer. operand: |len 4B|buffer bytes. {len}B|
    3> PC->MCU: stream buffer. operand: |streamID 4B|len 4B|payload {len}B|
    4> PC->MCU: event buffer. operand: |portID 4B|eventID 4B|len 2B|payload {len}B|
    5> PC->MCU: snapshot buffer. operand: |len 4B|payload {len}B|
    6> MCU->PC: stream buffer. same to [3]
    7> MCU->PC: event buffer. same to [4]
    8> MCU->PC: snapshot buffer. same to [5]
    9> MCU->PC: lowerIO buffer. operand: |len 4B|buffer bytes {len}B|
Special notes:
	configuration should have:
	1> snapshot layout.
    2> n-th stream configure to what speed, should VM or Medulla process the data?
    3> what event should processed by VM/Medulla.
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

void report_error(uchar* error_str); // should report error and terminate execution, enter safe mode.
void print_line(uchar* error_str); // should upload text info.

inline void enter_critical();
inline void leave_critical();

inline int get_cyclic_millis();
inline int get_cyclic_micros();
inline int get_cyclic_seconds();