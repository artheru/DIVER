# Implementation Note for AI

## Project Topology
- `DiverCompiler/`: Fody weaver + compiler that emits MCU bytecode (`*.bin`) and descriptor JSON.
- `DiverTest/`: host-side harness; PreBuild regenerates extra builtins and compiles `MCURuntime/mcu_runtime.c` into `mcuruntime.dll` via `build_cpp.bat`.
- `MCURuntime/`: C runtime consumed by the host simulator and real MCU ports.
- `3rd/Coralinker/`: no need to see.

## Builtin Extension
- To add a builtin call:
  1. Declare stub in `DiverTest/ExtraMethods.cs` (throw `NotImplementedException`).
  2. Run `DiverCompiler.exe -g [ExtraMethods.cs]` (or let prebuild do it) to regenerate `additional_builtins.h`, `extra_methods.txt`, `ExtraMethods.dll`.
  3. Implement the generated C stub in `MCURuntime/additional_builtins.h` (`builtin_<Name>(uchar** reptr)` using stack helpers).
  4. Ensure `add_additional_builtins()` appends function pointer; runtime auto includes when `setup_builtin_methods()` calls it.

## Key Debug Insights (2025-10)
- Builtins registry and ctor CLSIDs
  - Compiler: `BuiltInMethods` changed to `List<(string name, ushort ctor_clsid)>` in `DiverCompiler/Processor.Builtin.cs` to carry builtin ctor class IDs.
  - Delegate clsids: `Action..ctor`=0xF000, `Action`1..ctor`=0xF001, `Func`1..ctor`=0xF002, `Func`2..ctor`=0xF003, etc.
  - Containers: `List<T>..ctor`=0xF00C, `Queue<T>..ctor`=0xF00D, `Stack<T>..ctor`=0xF00E, `Dictionary<TKey,TValue>..ctor`=0xF00F, `HashSet<T>..ctor`=0xF010.
  - `Processor.cs` updates:
    - `Code.Newobj`: if builtin, write clsid from tuple into the Newobj operand so runtime allocates the correct layout.
    - `HandleMethodCall`: treat builtin ctors specially (consume only ctor params; do not consume `this`), emit A7 for builtin calls.

- Runtime ctor and builtin_arg0
  - Newobj (opcode 0x7A): for builtin ctors, runtime sets `builtin_arg0` to the freshly allocated object before invoking the builtin method.
  - Builtin ctors for delegates and containers MUST use `builtin_arg0` and MUST NOT pop `this` from the evaluation stack.
  - We updated `builtin_List_ctor`, `builtin_Queue_ctor`, `builtin_Stack_ctor`, `builtin_Dictionary_ctor`, `builtin_HashSet_ctor` to read from `builtin_arg0`.

- Delegates: construction and invocation
  - `delegate_ctor(uchar** reptr, unsigned short clsid)` expects stack top to be a `MethodPointer` (from `ldftn`), with target object (or 0) just beneath. It fills the new delegate at `builtin_arg0`.
  - `delegate_ivk(uchar** reptr, unsigned short clsid, int argN)` expects stack `[..., delegate_ref, arg0..argN-1]`. It rewrites the delegate slot to the captured `this` and invokes `vm_push_stack(method_id, -1, reptr)`. Ensure caller frame `evaluation_pointer` is synchronized before calling.
  - Common faults fixed: "wrong type of clsid" (ctor mismatch), "POP exceeds range" (bad eval pointer/arg popping order).

- LINQ builtins
  - `Enumerable.Where` now supports both arrays and `List<T>` sources. It invokes the predicate via `delegate_ivk` (push `predicate_ref` + one element) and consumes exactly one result per element.
  - For list sources, avoid returning the original array when no elements are filtered out; always materialize a new array when source is a list.
  - `Enumerable.Select` determines result element type from the first element and processes both arrays and lists.
  - `Enumerable.ToArray` accepts arrays directly; when fed a `List<T>` via `Where`, a materialized array is produced upstream.

- String formatting parity
  - `String.Join(IEnumerable<T>)`: previously printed placeholders like `[Value]`. Implemented Int32 formatting so sequences of `int` render as `"1,3,5"`, matching C# for the simple tests. Non-Int32 primitives still fall back to a placeholder until extended.

- VM and stack conventions to remember
  - `vm_push_stack(methodId, new_obj_id, &eptr)` pops `n_args` from the caller according to callee metadata. For ctors, pass `new_obj_id`>0 so `this` is injected by callee setup.
  - Do not manually pop `this` for builtin ctors; use `builtin_arg0`.
  - `ldftn` pushes a `MethodPointer` value; delegate ctor reads that directly.

- Builtin class IDs
  - Runtime macros: `#define BUILTIN_CLSIDX_*` enumerate builtin class indices; `#define BUILTIN_CLSID(idx) (0xF000 + (idx))` derives class IDs. Keep these synchronized with compiler tuple entries.

- Diagnostics and verbosity
  - Enable `_VERBOSE` in `mcu_runtime.c` for detailed IL traces. Additional per-iteration memory dump hooks are available near the bottom of `vm_run` (e.g., `print_hex(vm_get_lower_memory(), vm_get_lower_memory_size())`).
  - Typical failure signatures:
    - "POP exceeds range" → argument setup/evaluation pointer mismanagement before `vm_push_stack`.
    - "wrong type of clsid" → ctor used with a mismatched or missing builtin `clsid`.

- Sanity test (parity achieved)
  - Simple LINQ + string interpolation test now matches C# output over iterations: `arr=[]`, `arr=[1]`, `arr=[1,3]`, `arr=[1,3,5]`, ...

## Implementation Details (canonical references)
- Newobj instruction format (runtime `case 0x7A`)
  - Byte layout: `[ short clsid ][ byte call_kind ][ short method_id ]`
    - `call_kind` = `0xA6` (custom ctor → `vm_push_stack(method_id, new_id, &eptr)`) or `0xA7` (builtin ctor → `builtin_arg0=new_id; builtin_methods[method_id](...)`).
  - Compiler responsibilities:
    - For builtin types, encode the concrete builtin clsid (0xF000-based) directly into `clsid` so runtime allocates the correct layout.
    - For non-builtin types, encode the instanceable class id; linker fills it if unknown at codegen time.

- Builtin call vs custom call
  - General call opcodes encode as `0xA6` (custom) and `0xA7` (builtin); builtin calls index into `builtin_methods[]`.
  - Compiler emits `A7` for all BuiltInMethods; for ctors, the compiler also ensures the `Newobj` path carries the clsid.

- Stack discipline and `vm_push_stack`
  - Caller pushes arguments in IL evaluation order; before `vm_push_stack`, the caller frame `evaluation_pointer` should point AFTER the last pushed argument.
  - `vm_push_stack` backtracks `n_args` slots from the caller’s `evaluation_pointer` to copy-into callee args (and, when `new_obj_id>0`, it injects `this` as the first arg). Do not manually pop arguments before calling `vm_push_stack`.

- Delegates
  - `ldftn` produces a value with type id `MethodPointer (14)`; our struct is `{ byte type(0=builtin,1=custom), short id }`.
  - Delegate ctor sequence for `.ctor(object, IntPtr)`:
    - Stack top: `MethodPointer`; beneath: `target object` (or `0` for static-like capture); runtime has `builtin_arg0` pre-set to the new delegate object.
    - `delegate_ctor` validates `MethodPointer`, pops it, then pops target object, and writes fields into `builtin_arg0` (this id, method id).
  - Delegate invoke via `delegate_ivk(reptr, clsid, argN)`:
    - Expected stack: `[..., delegate_ref, arg0, ..., argN-1]`.
    - Rewrites the delegate slot in-place to captured `this` (`ReferenceID`), syncs caller `evaluation_pointer`, then calls `vm_push_stack(method_id, -1, &reptr)`.
    - Callee returns a single value on the caller’s evaluation stack; consumer should `POP` it when only the truthiness is needed.

- LINQ builtins interplay
  - `Where`: push `predicate_ref`, then one element; call `delegate_ivk` with `argN=1`; consume one result; do not restore the evaluation pointer; proceed to next element.
  - `ToArray`: expects an array input (errors on non-array); when chaining `Where(...).ToArray()`, ensure `Where` returns a materialized array for list sources.
  - `Select`: probes first element to discover result type, allocates the result array, then iterates elements; supports both arrays and `List<T>` as sources.

- String.Join over IEnumerable
  - For primitive arrays, we currently format `Int32` precisely with `snprintf("%d")`; other primitives default to a placeholder until extended. For `ReferenceID` arrays, strings are concatenated; non-string references print as `[Object]`.

- Builtin class layouts
  - Delegate: `{ ReferenceID target, Int32 method_id }`.
  - `List<T>`: `{ ReferenceID storage, Int32 count, Int32 capacity, Int32 elementType }`.
  - `Queue<T>`: `{ ReferenceID storage, Int32 head, Int32 tail, Int32 count, Int32 capacity, Int32 elementType }`.
  - `Stack<T>`: `{ ReferenceID storage, Int32 count, Int32 capacity, Int32 elementType }`.
  - `Dictionary<TKey,TValue>`: `{ ReferenceID storage, Int32 count, Int32 capacity, Int32 keyType, Int32 valueType }`.
  - `HashSet<T>`: `{ ReferenceID storage, Int32 count, Int32 capacity, Int32 elementType }`.

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
- Regenerate + run quick loop: `powershell -ExecutionPolicy Bypass -Command "cd D:\src\DIVER\DiverTest; dotnet build --no-incremental -c Debug; dotnet run -c Debug"`.

## Misc Notes
- Keep ASCII in generated files; runtime assumes 1-byte char encoding for debug prints.
- When modifying runtime memory constants (`NUM_BUILTIN_METHODS`, heap/stack sizes), sync with compiler metadata to avoid overflow DOOMs.
- Host/MCU handshake expects periodic `vm_run(i++)`; replicas should maintain call parity with IO buffer flushes.
