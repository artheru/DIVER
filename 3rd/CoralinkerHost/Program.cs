using CoralinkerHost.Services;
using CoralinkerHost.Web;

namespace CoralinkerHost;

internal static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls("http://0.0.0.0:4499");

        // 配置全局 JSON 序列化选项（使用 JsonHelper.Options）
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonHelper.Options.PropertyNamingPolicy;
            options.SerializerOptions.PropertyNameCaseInsensitive = JsonHelper.Options.PropertyNameCaseInsensitive;
        });

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<ProjectStore>();
        builder.Services.AddSingleton<FileTreeService>();
        builder.Services.AddSingleton<TerminalBroadcaster>();
        builder.Services.AddSingleton<DiverBuildService>();
        builder.Services.AddSingleton<RuntimeSessionService>();
        builder.Services.AddHostedService<VariableInspectorPushService>();

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

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapHub<TerminalHub>("/hubs/terminal");

        ApiRoutes.Map(app);

        app.Run();
    }
}