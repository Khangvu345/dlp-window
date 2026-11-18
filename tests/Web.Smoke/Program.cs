using System.Net;
using System.Net.Http.Json;
using RepoDlp.Core;
using RepoDlp.Web;

await using var dashboard = await LocalDashboard.StartAsync();
if (!IPAddress.TryParse(dashboard.Address.Host, out var host) || !IPAddress.IsLoopback(host))
    throw new InvalidOperationException("Web phải lắng nghe trên loopback.");

using var client = new HttpClient { BaseAddress = dashboard.Address };
foreach (var path in new[] { "/", "/app.css", "/app.js" })
{
    var content = await client.GetStringAsync(path);
    if (string.IsNullOrWhiteSpace(content))
        throw new InvalidOperationException($"Tài nguyên rỗng {path}");
}
var status = await client.GetFromJsonAsync<AgentStatus>("/api/status");
if (status is null || status.StartedAtUtc == default)
    throw new InvalidOperationException("API không trả thời điểm khởi động.");
Console.WriteLine("PASS — Trang quản lý, CSS, JavaScript và API trạng thái.");
