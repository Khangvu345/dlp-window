using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoDlp.Core;
using RepoDlp.Infrastructure;

namespace RepoDlp.Web;

public sealed class LocalDashboard : IAsyncDisposable
{
    private readonly WebApplication app;
    public Uri Address { get; }

    private LocalDashboard(WebApplication app, Uri address)
    {
        this.app = app;
        Address = address;
    }

    public static async Task<LocalDashboard> khoiDongAsync(
        DlpRuntime? runtime = null,
        Func<CancellationToken, Task<string?>>? chooseFolder = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
        var app = builder.Build();
        try
        {
            var status = new AgentStatus(DateTimeOffset.UtcNow);
            app.Use(kiemTraYeuCauNoiBo);
            app.MapGet("/api/status", () => Results.Ok(status));
            if (runtime is not null)
                dangKyApiQuanLy(app, runtime, chooseFolder);
            dangKyTaiNguyen(app, "/", "index.html", "text/html; charset=utf-8");
            dangKyTaiNguyen(app, "/app.css", "app.css", "text/css; charset=utf-8");
            dangKyTaiNguyen(app, "/app.js", "app.js", "text/javascript; charset=utf-8");

            await app.StartAsync().ConfigureAwait(false);
            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>();
            var address = new Uri(addresses!.Addresses.Single());
            return new LocalDashboard(app, address);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task kiemTraYeuCauNoiBo(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        var host = request.Host;
        if (host.Port != context.Connection.LocalPort ||
            !string.Equals(host.Host, IPAddress.Loopback.ToString(), StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string origin = request.Headers.Origin.ToString();
        string requestOrigin = "http://" + host;
        bool isApi = request.Path.StartsWithSegments("/api");
        if ((origin.Length > 0 && !string.Equals(origin, requestOrigin, StringComparison.Ordinal)) ||
            (isApi && request.Headers["Sec-Fetch-Site"].ToString().Equals("cross-site", StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (isApi)
            context.Response.Headers.CacheControl = "no-store";
        await next(context);
    }

    private static void dangKyApiQuanLy(
        WebApplication app,
        DlpRuntime runtime,
        Func<CancellationToken, Task<string?>>? chooseFolder)
    {
        app.MapGet("/api/settings", () => Results.Ok(
            new SettingsResponse(runtime.Settings, runtime.ScanError, chooseFolder is not null)));
        app.MapPut("/api/settings", async (AgentSettings settings, CancellationToken ct) =>
        {
            try
            {
                await runtime.capNhatCauHinhAsync(settings, ct);
                return Results.Ok(new SettingsResponse(runtime.Settings, runtime.ScanError, chooseFolder is not null));
            }
            catch (Exception error) when (error is ArgumentException or DirectoryNotFoundException
                or UnauthorizedAccessException or NotSupportedException)
            {
                return Results.BadRequest(new { message = "Thư mục quét phải là đường dẫn tuyệt đối đến thư mục có thể truy cập." });
            }
        });
        if (chooseFolder is not null)
        {
            app.MapPost("/api/folder-picker", async (HttpRequest request, CancellationToken ct) =>
                request.Headers.Origin.Count == 1
                    ? Results.Ok(new { path = await chooseFolder(ct) })
                    : Results.StatusCode(StatusCodes.Status403Forbidden));
        }
        app.MapGet("/api/documents", () => Results.Ok(runtime.layDanhSachTaiLieu()));
        app.MapGet("/api/policy", () => Results.Ok(new PolicyResponse(runtime.Policy, runtime.PolicyVersion)));
        app.MapPost("/api/policy/validate", (PolicyDefinition policy) =>
            Results.Ok(new { errors = runtime.kiemTraChinhSach(policy) }));
        app.MapPut("/api/policy", async (PolicyDefinition policy, CancellationToken ct) =>
        {
            var errors = await runtime.capNhatChinhSachAsync(policy, ct);
            return errors.Count == 0
                ? Results.Ok(new PolicyResponse(runtime.Policy, runtime.PolicyVersion))
                : Results.BadRequest(new { errors });
        });
        app.MapGet("/api/events", (DateTimeOffset? fromUtc, DateTimeOffset? untilUtc, string? behavior) =>
        {
            if (fromUtc >= untilUtc)
                return Results.BadRequest(new { message = "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu." });
            AuditBehavior? auditBehavior = behavior switch
            {
                "clipboard" => AuditBehavior.Clipboard,
                "screenshot" => AuditBehavior.Screenshot,
                _ => null
            };
            if (behavior is not null && auditBehavior is null)
                return Results.BadRequest(new { message = "Hành vi phải là clipboard hoặc screenshot." });
            return Results.Ok(runtime.laySuKien(fromUtc, untilUtc, auditBehavior));
        });
    }

    private sealed record SettingsResponse(AgentSettings Settings, string? ScanError, bool HasFolderPicker);
    private sealed record PolicyResponse(PolicyDefinition Policy, string PolicyVersion)
    {
        public string DefaultLabel => CompiledPolicy.DefaultLabel;
    }

    private static void dangKyTaiNguyen(WebApplication app, string path, string filename, string mime)
    {
        var assembly = typeof(LocalDashboard).Assembly;
        app.MapGet(path, () =>
        {
            var stream = assembly.GetManifestResourceStream("RepoDlp.Web." + filename)
                ?? throw new InvalidOperationException($"Thiếu tài nguyên giao diện {filename}.");
            return Results.Stream(stream, mime);
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
