using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
// For Windows WMI

namespace DiverTest.DIVER.CoralinkerAdaption;

public class SerialPortResolver
{
    private const int _DefaultSerialBaudrate = 2000000;

    static public SerialPort OpenUri(string uri)
    {
        var parameters = ParseUri(uri);
        string portName;
        int baudrate;
        (portName, baudrate) = FindPortNameAndBaudrate(parameters);

        var serialPort = new SerialPort();
        serialPort.PortName = portName;
        serialPort.BaudRate = baudrate;
        serialPort.DataBits = 8;
        serialPort.Parity = Parity.None;
        serialPort.StopBits = StopBits.One;
        serialPort.Handshake = Handshake.None;
        serialPort.Open();

        return serialPort;
    }

    static private Dictionary<string, string> ParseUri(string uri)
    {
        if (!uri.StartsWith("serial://"))
            throw new ArgumentException("URI must start with 'serial://'");

        var paramString = uri.Substring("serial://".Length);
        var paramsArray = paramString.Split('&');
        var parameters = new Dictionary<string, string>();

        foreach (var param in paramsArray)
        {
            var keyValue = param.Split('=');
            if (keyValue.Length == 2)
                parameters[keyValue[0].ToLower()] = keyValue[1];
        }

        if (!parameters.ContainsKey("name") &&
            !(parameters.ContainsKey("vid") && parameters.ContainsKey("pid") && parameters.ContainsKey("serial")))
            throw new ArgumentException("URI must contain either 'name' or 'vid', 'pid', and 'serial'");

        return parameters;
    }

    // Find the port name and baudrate based on the parameters
    static private(string, int) FindPortNameAndBaudrate(Dictionary<string, string> parameters)
    {
        string name = null;
        if (parameters.ContainsKey("name"))
        {
            name = parameters["name"];
        }
        else
        {
            string vid = parameters["vid"];
            string pid = parameters["pid"];
            string serial = parameters["serial"];
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                name = FindPortNameWindows(vid, pid, serial);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                name = FindPortNameLinux(vid, pid, serial);
            else
                throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
        }

        int baudrate = _DefaultSerialBaudrate;
        if (parameters.ContainsKey("baudrate"))
            baudrate = int.Parse(parameters["baudrate"]);

        return (name, baudrate);
    }

    private static string FindPortNameWindows(string vid, string pid, string serial)
    {
        try
        {
            // Step 1: Find the device in Win32_PnPEntity
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");
            foreach (ManagementObject obj in searcher.Get())
            {
                string pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";
                Console.WriteLine($"PNPDeviceID: {pnpDeviceId}");

                if (pnpDeviceId.Contains($"VID_{vid.ToUpper()}") &&
                    pnpDeviceId.Contains($"PID_{pid.ToUpper()}") &&
                    pnpDeviceId.Contains(serial.ToUpper()))
                {
                    Console.WriteLine($"Found matching PNPDeviceID: {pnpDeviceId}");

                    // Step 2: Construct the correct registry path
                    // Remove the "USB\" prefix and keep the original backslash between VID_PID and serial
                    string deviceInstanceId = pnpDeviceId.Replace(@"USB\", "");
                    string registryPath = $@"SYSTEM\CurrentControlSet\Enum\USB\{deviceInstanceId}\Device Parameters";
                    Console.WriteLine($"Registry Path: {registryPath}");

                    // Step 3: Query the registry for the COM port
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
                    {
                        if (key != null)
                        {
                            string portName = key.GetValue("PortName")?.ToString();
                            if (!string.IsNullOrEmpty(portName))
                            {
                                Console.WriteLine($"Found COM port: {portName}");
                                return portName; // e.g., "COM3"
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Registry key not found: {registryPath}");
                        }
                    }

                    // Fallback: Try Win32_SerialPort if registry query fails
                    var portSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                    foreach (ManagementObject portObj in portSearcher.Get())
                    {
                        Console.WriteLine($"PortObj PNPDeviceID: {portObj["PNPDeviceID"]}");
                        if (portObj["PNPDeviceID"]?.ToString() == pnpDeviceId)
                        {
                            string portName = portObj["DeviceID"]?.ToString();
                            Console.WriteLine($"Found COM port via Win32_SerialPort: {portName}");
                            return portName;
                        }
                    }
                }
            }

            throw new Exception($"Serial port with VID={vid}, PID={pid}, Serial={serial} not found.");
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to find serial port on Windows.", ex);
        }
    }

    static private string FindPortNameLinux(string vid, string pid, string serial)
    {
        try
        {
            string byIdPath = "/dev/serial/by-id/";
            if (!Directory.Exists(byIdPath))
                throw new Exception("Serial device directory not found on Linux.");

            foreach (var file in Directory.GetFiles(byIdPath))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Contains(vid.ToLower()) &&
                    fileName.Contains(pid.ToLower()) &&
                    fileName.Contains(serial.ToLower()))
                {
                    string devicePath = Path.GetFullPath(file);
                    if (File.Exists(devicePath))
                        return devicePath;
                }
            }
            throw new Exception($"Serial port with VID={vid}, PID={pid}, Serial={serial} not found.");
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to find serial port on Linux.", ex);
        }
    }
}