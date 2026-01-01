using System.Globalization;
using System.Reflection;

namespace CoralinkerHost.Services;

public sealed class VariableInspectorPushService : BackgroundService
{
    private readonly RuntimeSessionService _runtime;
    private readonly TerminalBroadcaster _terminal;

    public VariableInspectorPushService(RuntimeSessionService runtime, TerminalBroadcaster terminal)
    {
        _runtime = runtime;
        _terminal = terminal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var target = _runtime.GetCartTarget();
                if (target != null)
                {
                    var snapshot = BuildSnapshot(target);
                    await _terminal.VarsSnapshotAsync(snapshot, stoppingToken);
                }
            }
            catch
            {
                // ignore polling errors
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    private static object BuildSnapshot(object target)
    {
        var t = target.GetType();
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var items = new List<object>(fields.Length);
        foreach (var f in fields)
        {
            if (f.IsStatic) continue;
            var (dir, icon) = GetIoDirection(f);
            var val = f.GetValue(target);
            items.Add(new
            {
                name = f.Name,
                type = f.FieldType.Name,
                direction = dir,
                icon,
                value = FormatValue(val)
            });
        }

        return new
        {
            targetType = t.FullName ?? t.Name,
            fields = items
        };
    }

    private static (string direction, string icon) GetIoDirection(FieldInfo f)
    {
        // Attributes come from the loaded logic assembly; match by name to avoid type identity issues.
        var names = f.GetCustomAttributes(inherit: true).Select(a => a.GetType().Name).ToArray();
        if (names.Any(n => n.Contains("AsLowerIO", StringComparison.OrdinalIgnoreCase)))
            return ("lower", "arrow-down");
        if (names.Any(n => n.Contains("AsUpperIO", StringComparison.OrdinalIgnoreCase)))
            return ("upper", "arrow-up");
        return ("none", "circle");
    }

    private static string FormatValue(object? v)
    {
        if (v == null) return "null";
        if (v is string s) return s;
        if (v is Array a)
        {
            var parts = new List<string>();
            foreach (var e in a) parts.Add(FormatValue(e));
            return "[" + string.Join(", ", parts) + "]";
        }
        if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return v.ToString() ?? "";
    }
}


