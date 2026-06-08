# DIVER ABI Versioning

This document defines the binary contract ("ABI") between **DiverCompiler** (which
produces program binaries, `*.bin` / `.diver`) and **MCURuntime** (`mcu_runtime.c`,
shared by the MCU firmware and the SimNode host). It applies to the **whole project**:
any code that produces or consumes a DIVER program binary must follow this scheme.

## Program header / ABI gate

Every compiled program binary begins with an 8-byte ABI gate:

```
[ magic 4B = 'DIVR' (0x52564944) ] [ abi_version 4B ]
... then the meta_data header (interval, entry id, ... see mcu_runtime.c) ...
```

The runtime (`vm_set_program`) validates the gate **before parsing anything else**
and refuses to run an incompatible program (`report_error` → safe mode, returns `-1`).
This stops an old/new MCU from running a binary built for a different layout and
"running wild".

## Version format: SemVer `X.Y.Z` packed into 4 bytes

```
abi_version = 0x00_XX_YY_ZZ      (the top byte is always 0)
```

| Field | Byte | Meaning | When to bump |
|-------|------|---------|--------------|
| `X` major | byte 2 | **Incompatible** change: binary layout change, execution-model change, anything an older runtime cannot parse. | Bump `X`, reset `Y` and `Z` to `0`. |
| `Y` minor | byte 1 | **Additive** change: new built-in methods / new opcodes. | Bump `Y`, reset `Z` to `0`. |
| `Z` patch | byte 0 | **Bug fix** only, fully compatible both directions. | Bump `Z`. |

### Compatibility rule the runtime enforces

A program is allowed to run **iff**:

```
program.magic == runtime.magic
AND program.major == runtime.major
AND runtime.minor >= program.minor
```

- Different **major** → refuse (incompatible layout / execution).
- Program built against a **newer minor** than the runtime → refuse (the runtime is
  missing built-ins/opcodes the program needs).
- Older-or-equal minor, any patch → OK (runtime is a superset / fixes only).

## Where the version lives

- **Authoritative value:** `MCURuntime/mcu_runtime.h`
  - `DIVER_PROGRAM_MAGIC`, `DIVER_ABI_VERSION` (via `DIVER_ABI_MAKE(x,y,z)`),
    and helpers `DIVER_ABI_MAJOR/MINOR/PATCH`.
- **Compiler mirror (must stay in sync):** `DiverCompiler/Processor.cs`
  - `DiverProgramMagic`, `DiverAbiVersion` (via `MakeAbiVersion(x,y,z)`).

When you change one, change the other in the same commit.

## History

| Version | Change |
|---------|--------|
| _legacy_ | No magic/version prefix, 9-int meta header. Predates this check; cannot be detected by value. Conceptually "1.x". |
| **2.0.0** | Added magic+version prefix; meta header gains the cctor-table chunk-size field + trailing `.cctor` method-id table; static constructors (`.cctor`) now execute. **Layout change → major bump.** |

## Note on already-deployed (legacy) firmware

Legacy firmware has no ABI gate, so it cannot self-detect a new binary — it will
misparse the header. The runtime gate only protects firmware that already contains
the gate. To protect legacy units, the **host** should refuse to download a program
whose ABI is incompatible with the MCU's reported runtime ABI (see
`dev_todo` / `CommandVersion`). After flashing gate-aware firmware once, every unit
is self-protecting.
