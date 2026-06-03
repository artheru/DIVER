using System.Runtime.InteropServices;

namespace CoralinkerSimNodeHost;

internal static class McuRuntimeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void BytesCallback(IntPtr data, int length, uint timestampMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void TextCallback(IntPtr message, uint timestampMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PortBytesCallback(byte portIndex, byte direction, IntPtr data, int length, uint timestampMs);

    [DllImport("sim_node_runtime", EntryPoint = "sim_set_callbacks", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetCallbacks(
        BytesCallback lowerCallback,
        TextCallback consoleCallback,
        BytesCallback snapshotCallback,
        PortBytesCallback streamCallback,
        PortBytesCallback eventCallback
    );

    [DllImport("sim_node_runtime", EntryPoint = "sim_load_program", CallingConvention = CallingConvention.Cdecl)]
    public static extern int LoadProgram(byte[] program, int programLength, int memorySize);

    [DllImport("sim_node_runtime", EntryPoint = "sim_put_upper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int PutUpper(byte[] data, int length);

    [DllImport("sim_node_runtime", EntryPoint = "sim_put_port_input", CallingConvention = CallingConvention.Cdecl)]
    public static extern int PutPortInput(byte portIndex, byte[] data, int length);

    [DllImport("sim_node_runtime", EntryPoint = "sim_step", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Step(uint timestampMs);

    [DllImport("sim_node_runtime", EntryPoint = "sim_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Destroy();
}
