# MCURuntime 调试说明

本文记录 `MCURuntime` 原有虚拟机调试方式，以及 Host 模拟节点引入后的开发边界。目标是保留原来的 Visual Studio / `DiverTest` 调试体验，不把模拟节点发布需求和 runtime 本体调试混在一起。

## MCURuntime 原本怎么运行

`MCURuntime/mcu_runtime.c` 是 DIVER MCU 侧虚拟机的 C 实现。上层 C# 逻辑经过 `DiverCompiler` 编译成 VM program bytes 后，由这里的 VM 执行：

- `vm_set_program(...)`：加载编译后的 VM 内存镜像。
- `vm_run(iteration)`：执行一次扫描周期，也就是一次 `Operation()`。
- `vm_put_upper_memory(...)`：Host/Root 写入 UpperIO。
- `vm_get_lower_memory()` / `vm_get_lower_memory_size()`：读取 LowerIO 变化。
- `vm_put_snapshot_buffer(...)`：输入数字 IO 快照。
- `vm_put_stream_buffer(...)` / `vm_put_event_buffer(...)`：输入串口/CAN 等端口数据。
- `write_snapshot(...)` / `write_stream(...)` / `write_event(...)` / `print_line(...)` / `report_error(...)`：VM 中用户代码向外部环境输出 IO、日志和错误。

也就是说，`mcu_runtime.c` 本身不是一个完整设备模拟器。它是 VM 内核，外部世界由调用方喂输入、接输出。

## 同事原来的本地调试方式

原来的 PC 调试入口主要是 `DiverTest`，不是 `CoralinkerHost`。

路径如下：

1. `DiverTest/DiverTest.csproj` 在 PreBuild 阶段执行：
   - 重建 `DiverCompiler`。
   - 执行 `DiverCompiler.exe -g` 生成 VM 相关文件。
   - 执行 `DiverTest/build_cpp.bat` 编译 PC 版 `MCURuntime.dll`。
2. `DiverTest/build_cpp.bat` 通过 Visual Studio 的 `vcvars64.bat` 初始化 MSVC 环境，然后执行：
   - `cl /W0 /LD /MDd /I"." /Zi /EHsc ../MCURuntime/mcu_runtime.c /Fe:%OutputPath%mcuruntime.dll /link /DEBUG`
3. `DiverTest/DIVER/DIVERInterface.cs` 通过 P/Invoke 调用 `MCURuntime.dll`：
   - `set_lowerio_cb(...)`
   - `set_error_report_cb(...)`
   - `put_upper(...)`
   - `test(...)`
4. `test(...)` 是 legacy 调试入口。它会：
   - 调用 `vm_set_program(...)` 加载程序。
   - 循环执行若干次 `vm_run(i)`。
   - 每次运行前注入 snapshot/event 测试输入。
   - 每次运行后通过 lower IO callback 把结果传回 C#。

因此，开发 VM 内核时最直接的方式仍然是：

```powershell
dotnet build .\DiverTest\DiverTest.csproj -c Debug
```

或者在 Visual Studio 里启动/调试 `DiverTest`，在 `MCURuntime/mcu_runtime.c` 中下 C 断点。因为 `build_cpp.bat` 用的是 `/Zi /DEBUG`，会生成 PDB，适合 native 断点调试。

## Host 模拟节点和原调试入口的关系

模拟节点需要服务 Host 运行时，因此新增了一组更通用的动态库导出：

- `sim_set_callbacks(...)`
- `sim_load_program(...)`
- `sim_put_upper(...)`
- `sim_put_port_input(...)`
- `sim_step(...)`
- `sim_destroy(...)`

这些入口用于 `CoralinkerSimNodeHost` 子进程按 Host 生命周期执行 VM。它们不应该替换原来的 `test(...)` 调试入口。

保留原则：

- `test(...)`、`put_upper(...)`、`set_lowerio_cb(...)`、`set_error_report_cb(...)` 必须继续保留，供 `DiverTest` 和已有本地调试方式使用。
- `MCURuntime.vcxproj` 的 Visual Studio 调试配置不应被改成只服务发布包。
- 发布/跨平台构建脚本可以生成 `mcu_runtime.dll` / `libmcu_runtime.so`，但不应移除 `DiverTest/build_cpp.bat` 的 MSVC Debug DLL 路径。
- 如果修改 VM 内核行为，优先用 `DiverTest` 复现和单步调试；如果修改 Host 模拟节点 IPC，再用 `CoralinkerSimNodeHost` 路径验证。

## 后续开发建议

调试 VM 指令、栈、heap、builtin 方法时，继续使用 `DiverTest`。这是最接近原作者工作流的路径，能直接下 C 断点。

调试 Host 模拟节点时，关注三层边界：

- `CoralinkerSDK/SimulatedMcuNode.cs`：父进程 wrapper，负责节点生命周期和 Host 事件转发。
- `CoralinkerSimNodeHost`：每个模拟节点一个子进程，负责 NDJSON IPC 和加载 native runtime。
- `MCURuntime` 的 `sim_*` 导出：只做 VM 程序加载、step、IO 注入和 callback 转发。

不要把多实例状态塞进 `mcu_runtime.c` 的全局变量里。当前快速方案的多节点隔离依赖“每个模拟节点一个进程”，这样可以继续容忍 VM 内核使用全局状态。
