# Implementation Note for AI

## Project Topology
- `DiverCompiler/`: Fody weaver + compiler that emits MCU bytecode (`*.bin`) and descriptor JSON.
- `DiverTest/`: host-side harness; PreBuild regenerates extra builtins and compiles `MCURuntime/mcu_runtime.c` into `mcuruntime.dll` via `build_cpp.bat`.
- `MCURuntime/`: C runtime consumed by the host simulator and real MCU ports.
- `3rd/Coralinker/`: external MCU host project (kept for reference).

## Development Flow
- Prereqs: VS2022, .NET 8 SDK, download `MedullaCore.dll` + `CartActivator.dll` into `tools/` (see `Build.md`).
- Run `powershell -ExecutionPolicy Bypass -File .\setenv.ps1` in repo root before native builds to prime VS env.
- Build order: `DiverCompiler` ➜ `MCURuntime` ➜ `DiverTest`. `DiverTest` triggers `DiverCompiler.exe -g` then `build_cpp.bat` automatically.
- Host test run: `dotnet run --project .\DiverTest\DiverTest.csproj`.
- MCU deployment path: embed `*.bin` resource from compiler output into MCU firmware and link runtime (`mcu_runtime.c`) with platform glue (see `README.md`).

## Builtin Extension
- To add a builtin call:
  1. Declare stub in `DiverTest/ExtraMethods.cs` (throw `NotImplementedException`).
  2. Run `DiverCompiler.exe -g [ExtraMethods.cs]` (or let prebuild do it) to regenerate `additional_builtins.h`, `extra_methods.txt`, `ExtraMethods.dll`.
  3. Implement the generated C stub in `MCURuntime/additional_builtins.h` (`builtin_<Name>(uchar** reptr)` using stack helpers).
  4. Ensure `add_additional_builtins()` appends function pointer; runtime auto includes when `setup_builtin_methods()` calls it.

## Debugging Strategy
- Managed side: set startup project to `DiverTest`; breakpoints inside `TestLogic.Operation`. Weaver logs (`ModuleWeaver.cs`) show cart field layout and descriptor metadata during build.
- Native side: build `MCURuntime` in Debug to keep `_DEBUG`; optionally `#define _VERBOSE` in `mcu_runtime.c` for extra prints. Attach native debugger to `dotnet run` process to catch `DOOM()` macros.
- Descriptor issues: `Processor.cs` emits `CartDescriptorKind` (0=Primitive,1=Array,2=Struct,3=String,4=Reference). Runtime `DOOM("Unknown cart descriptor kind %d")` indicates compiler produced unsupported kind; inspect `EnsureDescriptor` path.
- Memory layout awareness: statics + stack + heap share `vm_memory`; stack frames pack type tags + payload; references use `ReferenceID (16)`.

## Key Internals
- `Processor.Process` walks ladder logic IL, builds method tables, and outputs: bytecode (`ResultDLL.bytes`), cart field metadata, descriptor table.
- Runtime builtins array size = 256; updating `NUM_BUILTIN_METHODS` requires adjusting both compiler constants and runtime storage.
- IO annotations: `[AsLowerIO]` = MCU➜host (read-only on host), `[AsUpperIO]` = host➜MCU. Descriptor IDs map to these fields in `TestVehicle`.
- Execution loop (`vm_run`) interprets stack machine IL; `vm_put_*` functions enqueue IO writes (snapshot/stream/event) executed each tick.

## Troubleshooting Checklist
- Missing `extra_methods.txt` during weaving → ensure prebuild ran or manually execute `DiverCompiler.exe -g`.
- `Unknown cart descriptor kind` → log `descriptor.Kind` when building descriptors and add mapping for new type to existing enum bucket.
- Host crash when calling builtin → verify pop/push order in `additional_builtins.h` (stack is last-in-first-out, args arrive reversed).
- Native build failures → confirm VS environment variables (`vcvars64.bat`) loaded via `setenv.ps1` or rerun `build_cpp.bat` manually inside VS developer prompt.

## Build/Run Shortcuts
- Regenerate + run quick loop: `powershell -ExecutionPolicy Bypass -File .\setenv.ps1; dotnet build; dotnet run --project .\DiverTest\DiverTest.csproj`.
- Inspect embedded output: `dotnet run --project .\DiverTest` (logs `mcu program sz=...`), `ildasm` or `dotnet tool` can extract `TestLogic.bin` resource for analysis.

## Misc Notes
- Keep ASCII in generated files; runtime assumes 1-byte char encoding for debug prints.
- When modifying runtime memory constants (`NUM_BUILTIN_METHODS`, heap/stack sizes), sync with compiler metadata to avoid overflow DOOMs.
- Host/MCU handshake expects periodic `vm_run(i++)`; replicas should maintain call parity with IO buffer flushes.
