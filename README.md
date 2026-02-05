# DIVER (Dotnet Integrated Vehicle Embedded Runtime)

DIVER is a specialized runtime and compiler system that enables running C# code on microcontroller units (MCUs), especially for automotive and robotics applications.

The typical deployment consists of a host system (PC/Server or powerful embedded system like Nvidia Orin) running a .NET application, communicating with one or more MCUs running the DIVER runtime via serial port or ethernet. DIVER provides a robust "terminal nerve" architecture where MCUs handle real-time IO while the host handles complex logic.

## Features

- **C# to MCU Bytecode Compilation** - Write MCU logic in familiar C# syntax
- **RTOS-like Runtime** - Deterministic execution with configurable scan intervals
- **Bidirectional Data Exchange** - UpperIO (Host→MCU) and LowerIO (MCU→Host)
- **Hardware IO Abstraction** - CAN, Serial, GPIO, Modbus support
- **Debug Source Maps** - Map runtime errors back to C# source locations
- **Web-based Control Panel** - CoralinkerHost provides visual node management

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Host System (.NET)                              │
│  ┌───────────────────────┐    ┌───────────────────────┐                     │
│  │   DIVERVehicle        │    │   LadderLogic<T>      │                     │
│  │   - IO Variables      │    │   - Operation()       │ ─── DiverCompiler ──┼──> .bin
│  │   - SetMCUProgram()   │    │   - cart reference    │                     │
│  │   - SendUpperData()   │    └───────────────────────┘                     │
│  │   - NotifyLowerData() │                                                  │
│  └───────────┬───────────┘                                                  │
│              │ Serial/Ethernet                                              │
└──────────────┼──────────────────────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              MCU (DIVER Runtime)                             │
│  ┌───────────────────────┐    ┌───────────────────────┐                     │
│  │   vm_run(iteration)   │    │   Hardware IO         │                     │
│  │   - Execute bytecode  │    │   - CAN, Serial, GPIO │                     │
│  │   - Process UpperIO   │    │   - Event/Stream/     │                     │
│  │   - Generate LowerIO  │    │     Snapshot buffers  │                     │
│  └───────────────────────┘    └───────────────────────┘                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Core Principles

### 1. Separation of Concerns
- **MCU**: Real-time IO handling, basic control loops, deterministic timing
- **Host**: Complex logic, data logging, user interfaces, machine learning
- Clear boundary prevents timing issues from affecting real-time operations

### 2. Data Flow (UpperIO / LowerIO)

| Direction | Attribute | Description | Example |
|-----------|-----------|-------------|---------|
| Host → MCU | `[AsUpperIO]` | Control commands, setpoints | `motorSpeed`, `targetPosition` |
| MCU → Host | `[AsLowerIO]` | Sensor data, status | `actualSpeed`, `temperature` |
| Bidirectional | (none) | Mutual exchange | `sharedCounter` |

### 3. Execution Cycle

Each scan interval (e.g., 50ms):
```
1. MCU collects hardware IO (GPIO, CAN, Serial)
2. MCU receives UpperIO from host
3. DIVER runtime executes Operation(iteration)
4. MCU sends LowerIO back to host (includes iteration counter)
5. Repeat
```

## Project Structure

```
DIVER/
├── DiverCompiler/          # C# to MCU bytecode compiler (Fody weaver)
│   ├── Processor.cs        # Main compilation logic
│   └── Processor.Builtin.cs # Builtin method handling
├── MCURuntime/             # C runtime for MCU execution
│   ├── mcu_runtime.c       # VM interpreter, IO handling
│   └── mcu_runtime.h       # Public API
├── DiverTest/              # Test harness and examples
│   ├── DIVER/
│   │   └── DIVERInterface.cs  # DIVERVehicle, LocalDebugDIVERVehicle
│   ├── TestLogic.cs        # Example MCU logic
│   └── RunOnMCU.cs         # MCU API stubs (ReadEvent, WriteStream, etc.)
├── MCUSerialBridge/        # Serial communication bridge (C + C# wrapper)
│   ├── c_core/             # Native C implementation
│   ├── mcu/                # MCU firmware using the bridge
│   └── wrapper/            # C# P/Invoke wrapper
└── 3rd/
    ├── CoralinkerHost/     # Web-based control panel (ASP.NET + Vue 3)
    └── CoralinkerSDK/      # DIVERSession for multi-node management
```

## Class Hierarchy

```
CartDefinition (abstract)
    └── DIVERVehicle (abstract) - Communication interface
            │   SetMCUProgram(mcuUri, program)   // Download program to MCU
            │   SendUpperData(mcuUri, data)      // Send UpperIO to MCU
            │   NotifyLowerData(mcuUri, data)    // Process LowerIO from MCU
            │
            └── LocalDebugDIVERVehicle (abstract) - PC-based testing
                    │   Uses MCUTestRunner (native DLL) to simulate MCU
                    │   No real hardware required
                    │
                    └── YourVehicle : LocalDebugDIVERVehicle
                            [AsLowerIO] sensorValue
                            [AsUpperIO] motorSpeed

LadderLogic<T> where T : CartDefinition
    └── YourLogic : LadderLogic<YourVehicle>
            cart     // Reference to vehicle instance
            Operation(int iteration)  // Called each scan cycle
```

## Getting Started

### Prerequisites

- Visual Studio 2022
- .NET 8.0 SDK
- ARM GCC Toolchain (for real MCU builds)

### Building

1. Clone the repository
2. Open `DIVER.sln` in Visual Studio
3. Build DiverCompiler first
4. Build DiverTest (this compiles MCURuntime as a DLL for testing)
5. Run DiverTest to verify everything works

### Quick Start: Local Debug Mode

For testing without real hardware, use `LocalDebugDIVERVehicle`:

**1. Define your vehicle (IO variables):**
```csharp
public class MyVehicle : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int sensorValue;    // MCU → Host
    [AsUpperIO] public int motorSpeed;     // Host → MCU
}
```

**2. Create MCU logic:**
```csharp
[LogicRunOnMCU(scanInterval = 50)]  // 50ms scan interval
public class MyLogic : LadderLogic<MyVehicle>
{
    public override void Operation(int iteration)
    {
        // Read sensor, apply control logic
        if (cart.sensorValue > 100)
            cart.motorSpeed = 0;
        else
            cart.motorSpeed = 50;
        
        Console.WriteLine($"Iteration {iteration}: sensor={cart.sensorValue}");
    }
}
```

**3. Run the host:**
```csharp
static void Main()
{
    var vehicle = new MyVehicle();
    vehicle.Start(Assembly.GetExecutingAssembly());
}
```

### Real Hardware Deployment

For real MCU deployment, implement `DIVERVehicle` directly:

**Host side:**
```csharp
public class RealVehicle : DIVERVehicle
{
    private SerialPort _port;
    
    public override void SetMCUProgram(string mcuUri, byte[] program)
    {
        // Send program bytes to MCU via serial
        _port.Write(program, 0, program.Length);
    }
    
    public override void SendUpperData(string mcuUri, byte[] data)
    {
        // Send UpperIO data to MCU
        _port.Write(data, 0, data.Length);
    }
    
    // Call NotifyLowerData() when receiving data from MCU
}
```

**MCU side (C):**
```c
void main()
{
    vm_set_program(program_from_host);
    
    int iteration = 0;
    while (1)
    {
        // Collect hardware IO
        vm_put_snapshot_buffer(gpio_states, gpio_size);
        vm_put_event_buffer(can_messages, can_size);
        
        // Receive UpperIO from host
        vm_put_upper_memory(upper_buffer, upper_size);
        
        // Execute one iteration
        vm_run(iteration++);
        
        // Send LowerIO to host
        send_to_host(vm_get_lower_memory(), vm_get_lower_memory_size());
    }
}
```

## Hardware IO Types

| Type | API | Use Case |
|------|-----|----------|
| **Event** | `ReadEvent(port, id)` / `WriteEvent(data, port, id)` | CAN messages, Modbus frames |
| **Stream** | `ReadStream(port)` / `WriteStream(data, port)` | Serial UART data |
| **Snapshot** | `ReadSnapshot()` / `WriteSnapshot(data)` | GPIO states, analog values |

## Custom Builtin Functions

To add native C functions callable from C#:

**1. Declare stub in C#:**
```csharp
// ExtraMethods.cs
public static class MyBuiltins
{
    public static int FastCalculation(int a, int b)
    {
        throw new NotImplementedException(); // Implemented in C
    }
}
```

**2. Generate header:**
```bash
DiverCompiler.exe -g
```

**3. Implement in C:**
```c
// additional_builtins.h
void builtin_FastCalculation(uchar** reptr)
{
    int b = pop_int(reptr);  // Arguments in reverse order!
    int a = pop_int(reptr);
    int result = a * b + (a >> 2);
    push_int(reptr, result);
}
```

**4. Use in logic:**
```csharp
public override void Operation(int iteration)
{
    int result = MyBuiltins.FastCalculation(10, 20);
    cart.outputValue = result;
}
```

## Debug Source Map (`*.diver.map.json`)

The compiler generates source maps for debugging MCU errors:

| Field | Description |
|-------|-------------|
| `ilOffset` | Byte offset in compiled program |
| `methodIndex` | Method index in output |
| `diverLine` | Line in `.diver` disassembly |
| `methodName` | Fully qualified method name |
| `sourceFile` | Original C# filename |
| `sourceLine` | Line in C# source |

**Example:**
```json
[
  {"ilOffset":239,"methodIndex":0,"diverLine":1,"methodName":"MyLogic.Operation(Int32)","sourceFile":"MyLogic.cs","sourceLine":15}
]
```

When an MCU fatal error occurs, find the entry with the largest `ilOffset <= error.ilOffset` to locate the C# source.

## CoralinkerHost (Web Control Panel)

The `3rd/CoralinkerHost` project provides a web interface for:

- **Node Management**: Add/remove/configure MCU nodes
- **Visual Programming**: Vue-flow based node graph
- **Variable Inspector**: Real-time UpperIO/LowerIO monitoring
- **Build System**: Compile C# logic and deploy to nodes
- **Error Handling**: Fatal error dialogs with source jump
- **WireTap Port Monitor**: Real-time Serial/CAN data monitoring with protocol parsing
  - MODBUS RTU: Function codes, CRC validation, register values
  - CANOpen: NMT, SDO, PDO, Heartbeat, Emergency messages
  - CiA 301/402: Object dictionary lookup with protocol source identification

See `3rd/CoralinkerHost/README.md` for details.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Missing `extra_methods.txt` | Run `DiverCompiler.exe -g` |
| "Unknown cart descriptor" | Check field type in vehicle class |
| Host crash on builtin call | Verify pop/push order in C (reversed!) |
| MCU HardFault | Check source map, enable `_VERBOSE` in runtime |

## License

[Your license here]
