using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoDlp.Core;

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

    public static async Task<LocalDashboard> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        try
        {
            var status = new AgentStatus(DateTimeOffset.UtcNow);
            app.MapGet("/api/status", () => Results.Ok(status));
            MapAsset(app, "/", "index.html", "text/html; charset=utf-8");
            MapAsset(app, "/app.css", "app.css", "text/css; charset=utf-8");
            MapAsset(app, "/app.js", "app.js", "text/javascript; charset=utf-8");

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

    private static void MapAsset(WebApplication app, string path, string filename, string mime)
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
