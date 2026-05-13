using System;
using CoralinkerHost.Services;
using CoralinkerHost.Web;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Logging;

namespace CoralinkerHost;

internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls("http://0.0.0.0:4499");
        // Reduce framework noise in terminal:
        // - default: Warning+
        // - app categories: keep Information for runtime diagnostics
        builder.Logging.AddFilter((category, level) =>
        {
            if (!string.IsNullOrEmpty(category) &&
                (category.StartsWith("CoralinkerHost", StringComparison.Ordinal) ||
                 category.StartsWith("CoralinkerSDK", StringComparison.Ordinal) ||
                 category.StartsWith("DIVERSession", StringComparison.Ordinal))) {
                return level >= LogLevel.Information;
            }

            return level >= LogLevel.Warning;
        });
        builder.Logging.AddFilter("Microsoft.WebTools.BrowserLink", LogLevel.Error);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Watch.BrowserRefresh", LogLevel.Error);

        // 配置全局 JSON 序列化选项（使用 JsonHelper.Options）
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonHelper.Options.PropertyNamingPolicy;
            options.SerializerOptions.PropertyNameCaseInsensitive = JsonHelper.Options.PropertyNameCaseInsensitive;
        });

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
                "application/javascript",
                "text/css",
                "application/json"
            ]);
        });

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ProjectStore>();
        builder.Services.AddSingleton<FileTreeService>();
        builder.Services.AddSingleton<TerminalBroadcaster>();
        builder.Services.AddSingleton<GitHistoryService>();
        builder.Services.AddSingleton<DiverBuildService>();
        builder.Services.AddSingleton<RuntimeSessionService>();
        builder.Services.AddSingleton<FirmwareUpgradeService>();
        builder.Services.AddHostedService<VariableInspectorPushService>();
        builder.Services.AddSingleton<WireTapAggregatorService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WireTapAggregatorService>());

        var app = builder.Build();

        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json; charset=utf-8";

                var feat = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var ex = feat?.Error;
                var payload = new
                {
                    ok = false,
                    error = ex?.Message ?? "Unhandled server error"
                };
                await context.Response.WriteAsJsonAsync(payload);
            });
        });

        app.UseResponseCompression();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapHub<TerminalHub>("/hubs/terminal");

        ApiRoutes.Map(app);

        app.Run();
    }
}