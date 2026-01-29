using CoralinkerSDK;
using MCUBootloaderCLR;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

/// <summary>
/// Firmware upgrade service
/// Manages the complete upgrade flow: MSB → Bootloader → MBL
/// </summary>
public sealed class FirmwareUpgradeService
{
    private readonly TerminalBroadcaster _broadcaster;
    private readonly ILogger<FirmwareUpgradeService> _logger;

    /// <summary>
    /// Upgrade progress stage
    /// </summary>
    public enum UpgradeStage
    {
        Connecting,
        SendingUpgradeCommand,
        WaitingBootloader,
        ConnectingBootloader,
        ReadingMcuInfo,
        Erasing,
        Writing,
        Verifying,
        Complete,
        Error
    }

    /// <summary>
    /// Upgrade progress information
    /// </summary>
    public record UpgradeProgress(
        string NodeId,
        int Progress,
        UpgradeStage Stage,
        string? Message = null);

    /// <summary>
    /// UPG file parse result
    /// </summary>
    public record UPGParseResult(
        bool Success,
        string? Error = null,
        FirmwareMetadata? Metadata = null);

    /// <summary>
    /// Upgrade result
    /// </summary>
    public record UpgradeResult(
        bool Success,
        string? Error = null,
        FirmwareMetadata? McuInfo = null,
        FirmwareMetadata? UpgInfo = null);

    public FirmwareUpgradeService(
        TerminalBroadcaster broadcaster,
        ILogger<FirmwareUpgradeService> logger)
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Parse UPG file (from Stream)
    /// </summary>
    public UPGParseResult ParseUpgFile(Stream stream)
    {
        try
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();
            var upg = new UPGFile(data);

            return new UPGParseResult(Success: true, Metadata: upg.GetMetadata());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse UPG file");
            return new UPGParseResult(Success: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// Parse UPG file (from byte array)
    /// </summary>
    public UPGParseResult ParseUpgFile(byte[] data)
    {
        try
        {
            var upg = new UPGFile(data);
            return new UPGParseResult(Success: true, Metadata: upg.GetMetadata());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse UPG file");
            return new UPGParseResult(Success: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// Execute firmware upgrade
    /// </summary>
    /// <param name="mcuUri">MCU URI (e.g., serial://vid=xxx&pid=xxx&baudrate=xxx)</param>
    /// <param name="upgData">UPG file data</param>
    /// <param name="nodeId">Node ID (for SignalR push)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<UpgradeResult> UpgradeAsync(
        string mcuUri,
        byte[] upgData,
        string nodeId,
        CancellationToken ct = default)
    {
        UPGFile upg;
        try
        {
            upg = new UPGFile(upgData);
        }
        catch (Exception ex)
        {
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, $"UPG parse failed: {ex.Message}", ct);
            return new UpgradeResult(false, $"UPG parse failed: {ex.Message}");
        }

        // Parse mcuUri to get serial port name and baudrate
        var (portName, baudrate) = ParseMcuUri(mcuUri);
        if (string.IsNullOrEmpty(portName))
        {
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, "Unable to resolve serial port", ct);
            return new UpgradeResult(false, "Unable to resolve serial port");
        }

        _logger.LogInformation("Starting firmware upgrade: {Port} @ {Baud}", portName, baudrate);
        await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Connecting, "Connecting to MCU...", ct);

        // Step 1: Try sending upgrade command via MSB (MCU may not have firmware, already in Bootloader mode)
        bool msbSuccess = false;
        using (var msb = new MCUSerialBridge())
        {
            var msbErr = msb.Open(portName, baudrate);
            if (msbErr == MCUSerialBridgeError.OK)
            {
                await BroadcastProgressAsync(nodeId, 5, UpgradeStage.SendingUpgradeCommand, "Sending upgrade command...", ct);
                msbErr = msb.Upgrade(200);
                if (msbErr == MCUSerialBridgeError.OK)
                {
                    msbSuccess = true;
                    _logger.LogInformation("MSB upgrade command sent successfully");
                }
                else
                {
                    _logger.LogWarning("MSB upgrade command failed: {Error}, trying to connect Bootloader directly", msbErr);
                }
                msb.Close();
            }
            else
            {
                _logger.LogWarning("MSB connection failed: {Error}, trying to connect Bootloader directly (MCU may already be in Bootloader mode)", msbErr);
            }
        }

        // Wait for MCU to restart and enter Bootloader (wait if MSB succeeded, otherwise try connecting directly)
        if (msbSuccess)
        {
            await BroadcastProgressAsync(nodeId, 10, UpgradeStage.WaitingBootloader, "Waiting for MCU to enter Bootloader...", ct);
            await Task.Delay(MCUNode.ResetWaitTime, ct);
        }
        else
        {
            await BroadcastProgressAsync(nodeId, 10, UpgradeStage.WaitingBootloader, "Trying to connect Bootloader directly...", ct);
        }

        // Step 2: Connect to Bootloader using MBL
        await BroadcastProgressAsync(nodeId, 15, UpgradeStage.ConnectingBootloader, "Connecting to Bootloader...", ct);
        using var mbl = new MCUBootloaderHandler();

        // Auto-detect baudrate (baud = 0)
        var mblErr = mbl.Open(portName, 0);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"Bootloader connection failed: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        _logger.LogInformation("Bootloader connected successfully, baudrate: {Baud}", mbl.Baudrate);

        // Read MCU firmware info
        await BroadcastProgressAsync(nodeId, 20, UpgradeStage.ReadingMcuInfo, "Reading MCU info...", ct);
        mblErr = mbl.CommandRead(out var mcuInfo, 1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"Failed to read MCU info: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        var mcuMetadata = mcuInfo.ToMetadata();
        var upgMetadata = upg.GetMetadata();
        _logger.LogInformation("MCU info: {Info}", mcuInfo);
        _logger.LogInformation("UPG info: ProductName={Pdn}, Tag={Tag}", upgMetadata.ProductName, upgMetadata.Tag);

        // PDN pre-check: Product name must match (PDN may be empty or all FF when MCU has no firmware)
        var mcuPdn = (mcuMetadata.ProductName ?? "").Trim('\0', ' ');
        var upgPdn = (upgMetadata.ProductName ?? "").Trim('\0', ' ');
        bool mcuHasValidPdn = !string.IsNullOrEmpty(mcuPdn) && !mcuPdn.StartsWith("FF");
        
        if (mcuHasValidPdn && !string.Equals(mcuPdn, upgPdn, StringComparison.OrdinalIgnoreCase))
        {
            var errMsg = $"Product name mismatch: MCU={mcuPdn}, UPG={upgPdn}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg, mcuMetadata, upgMetadata);
        }

        // Register progress callback
        mbl.RegisterProgressCallback((progress, error) =>
        {
            if (error == MCUBootloaderError.OK)
            {
                // Map write progress to 30-90
                int mappedProgress = 30 + (int)(progress * 0.6);
                _ = BroadcastProgressAsync(nodeId, mappedProgress, UpgradeStage.Writing, $"Writing {progress}%", ct);
            }
        });

        // Erase firmware
        await BroadcastProgressAsync(nodeId, 25, UpgradeStage.Erasing, "Erasing firmware area...", ct);
        mblErr = mbl.CommandErase(upg, 20000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"Erase failed: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        // Write firmware
        await BroadcastProgressAsync(nodeId, 30, UpgradeStage.Writing, "Starting firmware write...", ct);
        mblErr = mbl.WriteFirmware(upg, 1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"Write failed: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        // Exit Bootloader
        await BroadcastProgressAsync(nodeId, 95, UpgradeStage.Verifying, "Restarting MCU...", ct);
        mblErr = mbl.CommandExit(1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            _logger.LogWarning("Failed to exit Bootloader: {Error} (may have already restarted)", mblErr);
        }

        await BroadcastProgressAsync(nodeId, 100, UpgradeStage.Complete, "Upgrade complete!", ct);
        _logger.LogInformation("Firmware upgrade completed");

        return new UpgradeResult(true, McuInfo: mcuMetadata, UpgInfo: upgMetadata);
    }

    /// <summary>
    /// Parse MCU URI to get serial port name and baudrate
    /// </summary>
    private (string? portName, uint baudrate) ParseMcuUri(string mcuUri)
    {
        // Format: serial://vid=xxx&pid=xxx&baudrate=xxx
        // Or: serial://name=COMx&baudrate=xxx
        if (!mcuUri.StartsWith("serial://", StringComparison.OrdinalIgnoreCase))
        {
            return (null, 0);
        }

        var paramString = mcuUri.Substring("serial://".Length);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in paramString.Split('&'))
        {
            var keyValue = param.Split('=');
            if (keyValue.Length == 2)
                parameters[keyValue[0]] = keyValue[1];
        }

        // Parse baudrate
        uint baudrate = 0;
        if (parameters.TryGetValue("baudrate", out var baudStr))
        {
            uint.TryParse(baudStr, out baudrate);
        }

        // Try to get by name
        if (parameters.TryGetValue("name", out var name))
        {
            var resolved = SerialPortResolver.ResolveByName(name);
            if (!string.IsNullOrEmpty(resolved))
                return (resolved, baudrate);
        }

        // Try to get by vid/pid
        if (parameters.TryGetValue("vid", out var vid) &&
            parameters.TryGetValue("pid", out var pid))
        {
            var ports = SerialPortResolver.ResolveByVidPid(vid, pid);
            if (ports.Length > 0)
                return (ports[0], baudrate);
        }

        return (null, baudrate);
    }

    /// <summary>
    /// Broadcast upgrade progress
    /// </summary>
    private async Task BroadcastProgressAsync(
        string nodeId,
        int progress,
        UpgradeStage stage,
        string? message,
        CancellationToken ct)
    {
        _logger.LogDebug("Upgrade progress: {NodeId} {Progress}% {Stage} {Message}", nodeId, progress, stage, message);
        await _broadcaster.UpgradeProgressAsync(nodeId, progress, stage.ToString(), message, ct);
    }
}
