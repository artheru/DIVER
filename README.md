# DIVER (Dotnet Integrated Vehicle Embedded Runtime)

DIVER is a specialized runtime and compiler system that enables running C# code on microcontroller units (MCUs), especially for automotive applications.
The typical use is there is a host system running .NET application, and a MCU running DIVER, and they exchange data through serial port/ethernet, host system is typically a PC/Server or very powerful embedded system like Nvidia Orin, etc.
DIVER give a robust "terminal nerve" like architecture,  

## Features

- C# to MCU bytecode compilation, makes embedding programming for vehicles super easy.
- RTOS-like runtime.

## Core Principles

1. **Separation of Concerns**
   - MCU: Handles all real-time IO and basic control
   - Host: Handles compute-intensive operations
   - Clear boundary between real-time and non-real-time tasks

2. **Data Flow Architecture**
   ```
   Host System                    MCU
   +-----------+                +-----------+
   |  Complex  |  Upper IO     |           |
   |  Logic    | ----------->  |  Real-time|
   |           |               |   Control  |
   |           |  Lower IO     |           |
   |           | <-----------  |           |
   +-----------+                +-----------+
   ```

3. **IO Management**
   - All hardware IO (CAN, Modbus, GPIO) handled by MCU
   - Event, Stream, and Snapshot. event: like CAN, Modbus, etc. stream: like RS232, etc. snapshot: like GPIO state, analog input/output, etc.

4. **Data Exchange Pattern**
   - **Lower IO (`[AsLowerIO]`)**
     - MCU → Host direction
     - Read-only from host perspective
     - Represents sensor data, status information
     - Example:
```csharp
[AsLowerIO] public int SensorValue; // MCU sends sensor readings to host
```
   - **Upper IO (`[AsUpperIO]`)**
     - Host → MCU direction
     - Control commands, parameters
     - Wire format: `[typeid:1B][value:NB]` per field in cart definition order
     - See `MCURuntime/mcu_runtime.c` (`vm_put_upper_memory`) for detailed format
     - Example:
```csharp
[AsUpperIO] public int motorSpeed; // Host sends motor speed command to MCU
```

5. **Execution Cycle**
   ```
   1. MCU collects all IO data (GPIO, CAN, Modbus)
   2. MCU caches data including Upper IO from host
   3. DIVER runtime executes logic (interruptible by ISRs)
   4. MCU buffers output data
   5. Exchange data with host.
   ```

## Project Structure

- **DiverCompiler**: Main compiler that transforms C# code into MCU bytecode
- **MCURuntime**: C/C++ implementation of the runtime system for MCUs
- **DiverTest**: Sample implementations and test cases

## Getting Started

### Model of control


### Prerequisites

- Visual Studio 2022
- .NET 8.0 SDK
- ARM GCC Toolchain (for MCU builds)

### Building

1. Clone the repository
2. Open the solution in Visual Studio
3. Build the DiverCompiler project first
4. Build the MCURuntime project
5. Run tests using the DiverTest project

### Basic Usage

2. Implement communiation and define Vehicle IO variable abstraction:
```csharp
public class MyVehicle : DIVERVehicle {
    public void SetMCUProgram(string mcu_device_url, byte[] program){
        new Thread(()=>{
            // 1> send program bytes to MCU.
            while(true){
                // 2> read communication bytes from host, then call NotifyLowerData to let DIVER runtime know there is new data.
                NotifyLowerData(mcu_device_url, buffer); // this will also call SendUpperData to send exchange data to MCU.
            }
        }).Start();
    }
    public void SendUpperData(string mcu_device_url, byte[] data){
        // implement this method to send exchange data to MCU.
    }

    // IO variables
    [AsLowerIO] public int MotorActualSpeed;
    [AsUpperIO] public int MotorRequiredSpeed;
}
```

1. Create a class inheriting from `LadderLogic<T>`, for example, a motor speed control logic:
```csharp
[LogicRunOnMCU(scanInterval = 50)]
public class MyMCURoutine : LadderLogic<MyVehicle>
{
    public override void Operation(int iteration)
    {
        // Your MCU logic here
        RunOnMCU.WriteEvent([MotorRequiredSpeed && 0xff, MotorRequiredSpeed >> 8], 0x123, 0x456);
        var actual_speed_packet = RunOnMCU.ReadEvent(0x123, 0x789);
        if (actual_speed_packet != null){
            cart.MotorActualSpeed = actual_speed_packet[0] | (actual_speed_packet[1] << 8);
        }
    }
}
```

3. To run DIVER on read device, we need to implement host program and MCU program.
implement main host program like this:
```csharp  
static void Main(string[] args)
{
    new MyVehicle().Start(Assembly.GetAssembly(typeof(Program))); // start DIVER runtime on host system.
}
```

implement MCU program like this:
```c
void host_comm_ISR()
{
    upper_buffer = buffer; upper_size=size; //cache the communication buffer.
}
void CAN_Modbus_comm_ISR()
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
		vm_put_upper_memory(upper_buffer, upper_size);
        vm_run(i++);
        upload_to_host(vm_get_lower_memory(), vm_get_lower_memory_size());
    }
}
```

## Custom functions.
1. Create ExtraMethods.cs
First, create a file called ExtraMethods.cs with your method declarations. The methods should be in a static class and marked with NotImplementedException since the actual implementation will be on the MCU side.
```csharp
namespace TEST
{
    public static class TESTCls
    {
        public static int MyCustomMethod(int value1, float value2)
        {
            // Implementation will be on MCU side
            throw new NotImplementedException();
        }
    }
}
```

2. Generate Builtin Header
Run the DiverCompiler with -g flag to generate the builtin header template. Looking at the DiverTest project structure, this is done in the PreBuild event:
```xml
	<Target Name="PreBuild" BeforeTargets="PreBuildEvent">
	  <Exec Command="DiverCompiler.exe -g" />
	  <Exec Command="build_cpp.bat" EnvironmentVariables="OutputPath=$(OutputPath)" />
	</Target>
```

3. Implement the Method
Edit the generated additional_builtins.h to implement your custom method. The header will contain helpful comments about how to implement builtin functions. Here's an example implementation:
```c
// Auto-generated header with implementation
void builtin_MyCustomMethod(uchar** reptr) {
    // Arguments are popped in reverse order
    float value2 = pop_float(reptr);
    int value1 = pop_int(reptr);
    
    // Your implementation here
    int result = value1 + (int)(value2 * 100);
    
    // Push result back to stack
    push_int(reptr, result);
}
```

4. Use in MCU logic:
```csharp
[LogicRunOnMCU(scanInterval = 50)]
public class MyMCURoutine : LadderLogic<MyVehicle>
{
    public override void Operation(int iteration)
    {
        // Call your custom method
        int result = TESTCls.MyCustomMethod(10, 20.5f);
        cart.someValue = result;
    }
}
```

Important Notes: Arguments are popped from stack in reverse order.

## Debug Source Map (`*.diver.map.json`)

When the DiverCompiler compiles C# code to MCU bytecode, it generates a source map file (`{LogicName}.diver.map.json`) that maps IL (Intermediate Language) offsets to source code locations. This is essential for debugging MCU runtime errors.

### Map Format

The map is a JSON array where each entry contains:

| Field | Type | Description |
|-------|------|-------------|
| `ilOffset` | number | Absolute byte offset in the generated program |
| `methodIndex` | number | Index of the method in the compiled output |
| `diverLine` | number | Line number in the `.diver` disassembly file |
| `methodName` | string | Fully qualified method name (e.g., `Namespace.Class.Method(Args)`) |
| `sourceFile` | string | Original C# source file name (e.g., `TestLogic.cs`) |
| `sourceLine` | number | Line number in the original C# source file |

### Example

```json
[
  {"ilOffset":239,"methodIndex":0,"diverLine":1,"methodName":"DiverTest.TestLogic.Operation(Int32)","sourceFile":"TestLogic.cs","sourceLine":15},
  {"ilOffset":242,"methodIndex":0,"diverLine":2,"methodName":"DiverTest.TestLogic.Operation(Int32)","sourceFile":"TestLogic.cs","sourceLine":16}
]
```

### Usage for Error Handling

When an MCU fatal error occurs (ASSERT failure or HardFault), the error payload contains an `ilOffset` value. To find the corresponding source location:

1. Load the `{LogicName}.diver.map.json` file
2. Find the entry with the largest `ilOffset` that is less than or equal to the error's `ilOffset`
3. The `sourceFile` and `sourceLine` fields indicate the original C# code location

This is used by the CoralinkerHost frontend to provide click-to-jump functionality from error dialogs to source code.