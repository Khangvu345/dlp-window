using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RepoDlp.Core;
using RepoDlp.Infrastructure;
using RepoDlp.Web;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
};
var root = Directory.CreateTempSubdirectory("repo-dlp-web-smoke-").FullName;
try
{
    string firstFolder = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
    string secondFolder = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
    string firstPath = Path.Combine(firstFolder, "mau.txt");
    string secondPath = Path.Combine(secondFolder, "mau-moi.txt");
    await File.WriteAllTextAsync(firstPath, "Tài liệu nội bộ — Tiếng Việt");
    await File.WriteAllTextAsync(secondPath, "Mẫu dành cho khách hàng");

    await using var runtime = DlpRuntime.khoiDong(Path.Combine(root, "state"));
    var date = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
    runtime.ghiSuKien(new AuditEvent(date, "Repo DLP", AuditBehavior.Clipboard, DlpAction.Block, "clipboard-demo", null));
    runtime.ghiSuKien(new AuditEvent(date.AddHours(1), "Repo DLP", AuditBehavior.Screenshot, DlpAction.Observe, "screen-demo", null));
    runtime.ghiSuKien(new AuditEvent(date.AddDays(1), "Repo DLP", AuditBehavior.Clipboard, DlpAction.Allow, null, null));

    await using var dashboard = await LocalDashboard.khoiDongAsync(runtime, _ => Task.FromResult<string?>(secondFolder));
    kiemTra(IPAddress.TryParse(dashboard.Address.Host, out var host) && IPAddress.IsLoopback(host),
        "Web phải lắng nghe trên loopback.");
    using var client = new HttpClient { BaseAddress = dashboard.Address };
    foreach (var path in new[] { "/", "/app.css", "/app.js" })
    {
        var content = await client.GetStringAsync(path);
        kiemTra(!string.IsNullOrWhiteSpace(content), $"Tài nguyên rỗng {path}.");
    }
    var status = await client.GetFromJsonAsync<AgentStatus>("/api/status");
    kiemTra(status is not null && status.StartedAtUtc != default, "API không trả thời điểm khởi động.");

    using var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
    wrongHost.Headers.Host = $"outside.invalid:{dashboard.Address.Port}";
    using var wrongHostResponse = await client.SendAsync(wrongHost);
    kiemTra(wrongHostResponse.StatusCode == HttpStatusCode.Forbidden, "API chấp nhận Host bên ngoài.");
    using var crossOrigin = new HttpRequestMessage(HttpMethod.Put, "/api/settings")
    {
        Content = JsonContent.Create(new AgentSettings(firstFolder, false))
    };
    crossOrigin.Headers.Add("Origin", "https://outside.invalid");
    using var crossOriginResponse = await client.SendAsync(crossOrigin);
    kiemTra(crossOriginResponse.StatusCode == HttpStatusCode.Forbidden && runtime.Settings.ProtectionEnabled,
        "API cho phép thay đổi cấu hình từ origin ngoài.");

    using var response = await client.PutAsJsonAsync("/api/settings", new AgentSettings(firstFolder, true));
    response.EnsureSuccessStatusCode();
    await doiNhanAsync(firstPath, "Nội bộ");

    var policy = new PolicyDefinition([
        new PolicyRule("gui-test", RuleScope.Classification, 10, RuleKind.Keyword, "Tiếng Việt", "Nhãn mới", null)
    ]);
    using var savePolicy = await client.PutAsJsonAsync("/api/policy", policy, json);
    savePolicy.EnsureSuccessStatusCode();
    var original = await doiNhanAsync(firstPath, "Nhãn mới");

    var invalid = new PolicyDefinition([
        new PolicyRule("gui-test", RuleScope.Classification, 10, RuleKind.Regex, "(", "Sai", null)
    ]);
    using var validatePolicy = await client.PostAsJsonAsync("/api/policy/validate", invalid, json);
    validatePolicy.EnsureSuccessStatusCode();
    var validation = await validatePolicy.Content.ReadFromJsonAsync<PolicyValidation>();
    kiemTra(validation?.Errors is [{ RuleIndex: 0, RuleId: "gui-test" }],
        "Kiểm tra regex phải trả lỗi ở đúng quy tắc.");
    using var rejectPolicy = await client.PutAsJsonAsync("/api/policy", invalid, json);
    kiemTra(rejectPolicy.StatusCode == HttpStatusCode.BadRequest, "API lưu regex sai.");
    var rejected = await rejectPolicy.Content.ReadFromJsonAsync<PolicyValidation>();
    kiemTra(rejected?.Errors is [{ RuleIndex: 0, RuleId: "gui-test" }],
        "Lưu regex sai phải trả lỗi ở đúng quy tắc.");
    var after = await doiNhanAsync(firstPath, "Nhãn mới");
    kiemTra(original.PolicyVersion == after.PolicyVersion, "Regex sai thay đổi policy đang chạy.");

    using var noOrigin = await client.PostAsync("/api/folder-picker", null);
    kiemTra(noOrigin.StatusCode == HttpStatusCode.Forbidden, "Hộp chọn thư mục phải yêu cầu origin nội bộ.");
    using var picker = new HttpRequestMessage(HttpMethod.Post, "/api/folder-picker");
    picker.Headers.Add("Origin", dashboard.Address.GetLeftPart(UriPartial.Authority));
    using var pickerResponse = await client.SendAsync(picker);
    pickerResponse.EnsureSuccessStatusCode();
    var selected = await pickerResponse.Content.ReadFromJsonAsync<FolderSelection>();
    kiemTra(selected?.Path == secondFolder, "Hộp chọn thư mục trả đường dẫn sai.");
    using var changeFolder = await client.PutAsJsonAsync("/api/settings", new AgentSettings(selected!.Path, false));
    changeFolder.EnsureSuccessStatusCode();
    await doiNhanAsync(secondPath, "Thông thường");
    var documents = await client.GetFromJsonAsync<DocumentClassification[]>("/api/documents", json);
    kiemTra(documents is [{ Path: var pathNow }] && pathNow == secondPath && !runtime.Settings.ProtectionEnabled,
        "Danh sách tài liệu hoặc trạng thái bảo vệ chưa cập nhật.");

    var events = await client.GetFromJsonAsync<AuditEvent[]>(
        "/api/events?fromUtc=2026-05-10T00:00:00Z&untilUtc=2026-05-11T00:00:00Z&behavior=clipboard", json);
    kiemTra(events is [{ Result: DlpAction.Block, RuleId: "clipboard-demo", OccurredAtUtc: var eventTime }]
        && eventTime == date, "Bộ lọc nhật ký trả sai sự kiện.");
    var screenshotEvents = await client.GetFromJsonAsync<AuditEvent[]>("/api/events?behavior=screenshot", json);
    kiemTra(screenshotEvents is [{ Result: DlpAction.Observe, RuleId: "screen-demo" }],
        "Nhật ký chụp màn hình trả sai kết quả quan sát.");
    Console.WriteLine("PASS — Localhost, cấu hình quét, policy, lỗi regex và lọc nhật ký.");

    async Task<DocumentClassification> doiNhanAsync(string path, string label)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var rows = await client.GetFromJsonAsync<DocumentClassification[]>("/api/documents", json);
            if (rows?.FirstOrDefault(row => row.Path == path && row.Label == label) is { } match)
                return match;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        throw new InvalidOperationException($"Không thấy nhãn {label} cho {path}.");
    }
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void kiemTra(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed record PolicyValidation(IReadOnlyList<PolicyError> Errors);
sealed record FolderSelection(string? Path);
