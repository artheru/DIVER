using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CoralinkerSDK;

/// <summary>
/// 串口发现工具，用于根据名称或 VID/PID 查找串口
/// 仅负责发现端口名称，不负责打开端口
/// </summary>
public static class SerialPortResolver
{
    /// <summary>
    /// 根据端口名称解析（验证端口是否存在）
    /// </summary>
    /// <param name="nameOrUri">端口名称（如 "COM3"）或 URI（如 "serial://name=COM3"）</param>
    /// <returns>端口名称，如果不存在则返回 null</returns>
    public static string? ResolveByName(string nameOrUri)
    {
        // 从 URI 中提取端口名称
        string portName = nameOrUri;
        if (nameOrUri.StartsWith("serial://", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = ParseUri(nameOrUri);
            if (!parameters.TryGetValue("name", out var name))
            {
                return null;
            }
            portName = name;
        }

        // 验证端口是否存在
        var availablePorts = SerialPort.GetPortNames();
        return availablePorts.Contains(portName, StringComparer.OrdinalIgnoreCase) ? portName : null;
    }

    /// <summary>
    /// 根据 VID/PID 查找串口
    /// </summary>
    /// <param name="vid">USB Vendor ID（如 "1234"）</param>
    /// <param name="pid">USB Product ID（如 "5678"）</param>
    /// <returns>匹配的端口名称数组</returns>
    public static string[] ResolveByVidPid(string vid, string pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindPortsByVidPidWindows(vid, pid);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindPortsByVidPidLinux(vid, pid);
        else
            throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }

    /// <summary>
    /// 列出所有可用串口
    /// </summary>
    /// <returns>所有可用串口名称</returns>
    public static string[] ListAllPorts()
    {
        return SerialPort.GetPortNames();
    }

    private static Dictionary<string, string> ParseUri(string uri)
    {
        var paramString = uri.Substring("serial://".Length);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in paramString.Split('&'))
        {
            var keyValue = param.Split('=');
            if (keyValue.Length == 2)
                parameters[keyValue[0]] = keyValue[1];
        }

        return parameters;
    }

    private static string[] FindPortsByVidPidWindows(string vid, string pid)
    {
        var result = new List<string>();
        try
        {
            // 查找匹配的 PnP 设备
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");

            foreach (ManagementObject obj in searcher.Get())
            {
                string pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";

                if (pnpDeviceId.Contains($"VID_{vid.ToUpper()}", StringComparison.OrdinalIgnoreCase) &&
                    pnpDeviceId.Contains($"PID_{pid.ToUpper()}", StringComparison.OrdinalIgnoreCase))
                {
                    // 尝试从注册表获取端口名称
                    string deviceInstanceId = pnpDeviceId.Replace(@"USB\", "");
                    string registryPath = $@"SYSTEM\CurrentControlSet\Enum\USB\{deviceInstanceId}\Device Parameters";

                    using var key = Registry.LocalMachine.OpenSubKey(registryPath);
                    if (key != null)
                    {
                        string? portName = key.GetValue("PortName")?.ToString();
                        if (!string.IsNullOrEmpty(portName))
                        {
                            result.Add(portName);
                            continue;
                        }
                    }

                    // 回退: 尝试通过 Win32_SerialPort 查找
                    var portSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                    foreach (ManagementObject portObj in portSearcher.Get())
                    {
                        if (portObj["PNPDeviceID"]?.ToString() == pnpDeviceId)
                        {
                            string? portName = portObj["DeviceID"]?.ToString();
                            if (!string.IsNullOrEmpty(portName))
                            {
                                result.Add(portName);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SerialPortResolver] FindPortsByVidPidWindows error: {ex.Message}");
        }

        return result.ToArray();
    }

    private static string[] FindPortsByVidPidLinux(string vid, string pid)
    {
        var result = new List<string>();
        try
        {
            string byIdPath = "/dev/serial/by-id/";
            if (!Directory.Exists(byIdPath))
                return result.ToArray();

            foreach (var file in Directory.GetFiles(byIdPath))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Contains(vid, StringComparison.OrdinalIgnoreCase) &&
                    fileName.Contains(pid, StringComparison.OrdinalIgnoreCase))
                {
                    string devicePath = Path.GetFullPath(file);
                    if (File.Exists(devicePath))
                    {
                        result.Add(devicePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SerialPortResolver] FindPortsByVidPidLinux error: {ex.Message}");
        }

        return result.ToArray();
    }
}
