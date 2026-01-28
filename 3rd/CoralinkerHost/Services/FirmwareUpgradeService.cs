using CoralinkerSDK;
using MCUBootloaderCLR;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

/// <summary>
/// 固件升级服务
/// 管理 MSB → Bootloader → MBL 的完整升级流程
/// </summary>
public sealed class FirmwareUpgradeService
{
    private readonly TerminalBroadcaster _broadcaster;
    private readonly ILogger<FirmwareUpgradeService> _logger;

    /// <summary>
    /// 升级进度阶段
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
    /// 升级进度信息
    /// </summary>
    public record UpgradeProgress(
        string NodeId,
        int Progress,
        UpgradeStage Stage,
        string? Message = null);

    /// <summary>
    /// UPG 文件解析结果
    /// </summary>
    public record UPGParseResult(
        bool Success,
        string? Error = null,
        FirmwareMetadata? Metadata = null);

    /// <summary>
    /// 升级结果
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
    /// 解析 UPG 文件（从 Stream）
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
            _logger.LogError(ex, "UPG 文件解析失败");
            return new UPGParseResult(Success: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// 解析 UPG 文件（从字节数组）
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
            _logger.LogError(ex, "UPG 文件解析失败");
            return new UPGParseResult(Success: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// 执行固件升级
    /// </summary>
    /// <param name="mcuUri">MCU URI（如 serial://vid=xxx&pid=xxx&baudrate=xxx）</param>
    /// <param name="upgData">UPG 文件数据</param>
    /// <param name="nodeId">节点 ID（用于 SignalR 推送）</param>
    /// <param name="ct">取消令牌</param>
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
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, $"UPG 解析失败: {ex.Message}", ct);
            return new UpgradeResult(false, $"UPG 解析失败: {ex.Message}");
        }

        // 解析 mcuUri 获取串口名称和波特率
        var (portName, baudrate) = ParseMcuUri(mcuUri);
        if (string.IsNullOrEmpty(portName))
        {
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, "无法解析串口", ct);
            return new UpgradeResult(false, "无法解析串口");
        }

        _logger.LogInformation("开始固件升级: {Port} @ {Baud}", portName, baudrate);
        await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Connecting, "正在连接 MCU...", ct);

        // 第一步：尝试通过 MSB 发送升级命令（MCU 可能没有固件，已经在 Bootloader 模式）
        bool msbSuccess = false;
        using (var msb = new MCUSerialBridge())
        {
            var msbErr = msb.Open(portName, baudrate);
            if (msbErr == MCUSerialBridgeError.OK)
            {
                await BroadcastProgressAsync(nodeId, 5, UpgradeStage.SendingUpgradeCommand, "发送升级命令...", ct);
                msbErr = msb.Upgrade(200);
                if (msbErr == MCUSerialBridgeError.OK)
                {
                    msbSuccess = true;
                    _logger.LogInformation("MSB 升级命令发送成功");
                }
                else
                {
                    _logger.LogWarning("MSB 升级命令失败: {Error}，尝试直接连接 Bootloader", msbErr);
                }
                msb.Close();
            }
            else
            {
                _logger.LogWarning("MSB 连接失败: {Error}，尝试直接连接 Bootloader（MCU 可能已在 Bootloader 模式）", msbErr);
            }
        }

        // 等待 MCU 重启进入 Bootloader（如果 MSB 成功则等待，否则直接尝试连接）
        if (msbSuccess)
        {
            await BroadcastProgressAsync(nodeId, 10, UpgradeStage.WaitingBootloader, "等待 MCU 进入 Bootloader...", ct);
            await Task.Delay(300, ct);
        }
        else
        {
            await BroadcastProgressAsync(nodeId, 10, UpgradeStage.WaitingBootloader, "尝试直接连接 Bootloader...", ct);
        }

        // 第二步：使用 MBL 连接 Bootloader
        await BroadcastProgressAsync(nodeId, 15, UpgradeStage.ConnectingBootloader, "连接 Bootloader...", ct);
        using var mbl = new MCUBootloaderHandler();

        // 自动探测波特率（baud = 0）
        var mblErr = mbl.Open(portName, 0);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"Bootloader 连接失败: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        _logger.LogInformation("Bootloader 连接成功，波特率: {Baud}", mbl.Baudrate);

        // 读取 MCU 固件信息
        await BroadcastProgressAsync(nodeId, 20, UpgradeStage.ReadingMcuInfo, "读取 MCU 信息...", ct);
        mblErr = mbl.CommandRead(out var mcuInfo, 1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"读取 MCU 信息失败: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        var mcuMetadata = mcuInfo.ToMetadata();
        var upgMetadata = upg.GetMetadata();
        _logger.LogInformation("MCU 信息: {Info}", mcuInfo);
        _logger.LogInformation("UPG 信息: ProductName={Pdn}, Tag={Tag}", upgMetadata.ProductName, upgMetadata.Tag);

        // PDN 预检查：产品型号必须匹配（MCU 无固件时 PDN 可能为空或全 FF）
        var mcuPdn = (mcuMetadata.ProductName ?? "").Trim('\0', ' ');
        var upgPdn = (upgMetadata.ProductName ?? "").Trim('\0', ' ');
        bool mcuHasValidPdn = !string.IsNullOrEmpty(mcuPdn) && !mcuPdn.StartsWith("FF");
        
        if (mcuHasValidPdn && !string.Equals(mcuPdn, upgPdn, StringComparison.OrdinalIgnoreCase))
        {
            var errMsg = $"产品型号不匹配: MCU={mcuPdn}, UPG={upgPdn}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg, mcuMetadata, upgMetadata);
        }

        // 注册进度回调
        mbl.RegisterProgressCallback((progress, error) =>
        {
            if (error == MCUBootloaderError.OK)
            {
                // 写入进度映射到 30-90
                int mappedProgress = 30 + (int)(progress * 0.6);
                _ = BroadcastProgressAsync(nodeId, mappedProgress, UpgradeStage.Writing, $"写入中 {progress}%", ct);
            }
        });

        // 擦除固件
        await BroadcastProgressAsync(nodeId, 25, UpgradeStage.Erasing, "擦除固件区域...", ct);
        mblErr = mbl.CommandErase(upg, 10000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"擦除失败: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        // 写入固件
        await BroadcastProgressAsync(nodeId, 30, UpgradeStage.Writing, "开始写入固件...", ct);
        mblErr = mbl.WriteFirmware(upg, 1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            var errMsg = $"写入失败: {mblErr}";
            await BroadcastProgressAsync(nodeId, 0, UpgradeStage.Error, errMsg, ct);
            return new UpgradeResult(false, errMsg);
        }

        // 退出 Bootloader
        await BroadcastProgressAsync(nodeId, 95, UpgradeStage.Verifying, "重启 MCU...", ct);
        mblErr = mbl.CommandExit(1000);
        if (mblErr != MCUBootloaderError.OK)
        {
            _logger.LogWarning("退出 Bootloader 失败: {Error}（可能已经重启）", mblErr);
        }

        await BroadcastProgressAsync(nodeId, 100, UpgradeStage.Complete, "升级完成！", ct);
        _logger.LogInformation("固件升级完成");

        return new UpgradeResult(true, McuInfo: mcuMetadata, UpgInfo: upgMetadata);
    }

    /// <summary>
    /// 解析 MCU URI 获取串口名称和波特率
    /// </summary>
    private (string? portName, uint baudrate) ParseMcuUri(string mcuUri)
    {
        // 格式：serial://vid=xxx&pid=xxx&baudrate=xxx
        // 或：serial://name=COMx&baudrate=xxx
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

        // 解析波特率
        uint baudrate = 0;
        if (parameters.TryGetValue("baudrate", out var baudStr))
        {
            uint.TryParse(baudStr, out baudrate);
        }

        // 尝试通过 name 获取
        if (parameters.TryGetValue("name", out var name))
        {
            var resolved = SerialPortResolver.ResolveByName(name);
            if (!string.IsNullOrEmpty(resolved))
                return (resolved, baudrate);
        }

        // 尝试通过 vid/pid 获取
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
    /// 广播升级进度
    /// </summary>
    private async Task BroadcastProgressAsync(
        string nodeId,
        int progress,
        UpgradeStage stage,
        string? message,
        CancellationToken ct)
    {
        _logger.LogDebug("升级进度: {NodeId} {Progress}% {Stage} {Message}", nodeId, progress, stage, message);
        await _broadcaster.UpgradeProgressAsync(nodeId, progress, stage.ToString(), message, ct);
    }
}
