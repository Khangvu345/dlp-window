using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RepoDlp.Core;
using RepoDlp.Infrastructure;

var root = Directory.CreateTempSubdirectory("repo-dlp-classification-");
var data = Directory.CreateDirectory(Path.Combine(root.FullName, "data"));
var documents = Directory.CreateDirectory(Path.Combine(root.FullName, "documents"));
var internalPath = Path.Combine(documents.FullName, "internal.txt");
var publicPath = Path.Combine(documents.FullName, "public.txt");
var corruptPath = Path.Combine(documents.FullName, "corrupt.docx");
var renamedPath = Path.Combine(documents.FullName, "renamed.txt");
var secondDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "second"));
var secondPath = Path.Combine(secondDirectory.FullName, "other.txt");
var now = DateTimeOffset.UtcNow;

try
{
    File.WriteAllText(internalPath, "Tài liệu nội bộ");
    File.WriteAllText(publicPath, "Tài liệu chia sẻ");
    File.WriteAllText(corruptPath, "Không phải DOCX");
    File.WriteAllText(secondPath, "Tài liệu chia sẻ");

    string version;
    string hash;
    await using (var runtime = DlpRuntime.khoiDong(data.FullName))
    {
        await runtime.capNhatCauHinhAsync(new AgentSettings(documents.FullName, true));
        await doiDieuKienAsync(() => runtime.layDanhSachTaiLieu().Count == 3, "quét ban đầu");
        yeuCau(runtime.timTaiLieu(internalPath) is { Label: "Nội bộ", RuleId: "classify-internal" },
            "Tài liệu nội bộ bị gắn nhãn sai.");
        yeuCau(runtime.timTaiLieu(publicPath) is { Label: "Thông thường", RuleId: null },
            "Tài liệu thường bị gắn nhãn sai.");
        yeuCau(runtime.timTaiLieu(corruptPath) is { Status: ClassificationStatus.ReadError, Label: null },
            "Tài liệu lỗi đọc phải có trạng thái riêng.");

        yeuCau(runtime.danhGiaClipboard(null, "Số giả 012345678901") ==
            new DlpDecision(DlpAction.Block, "clipboard-fake-12-digits"), "Regex clipboard phải áp cả khi chưa biết nguồn.");
        yeuCau(runtime.danhGiaClipboard(internalPath, "Nội dung thường") ==
            new DlpDecision(DlpAction.Block, "clipboard-internal"), "Không áp quy tắc nhãn nội bộ cho clipboard.");
        yeuCau(runtime.danhGiaClipboard(publicPath, "Nội dung thường").Action == DlpAction.Allow,
            "Nội dung thường trong tài liệu thường phải được cho qua.");
        yeuCau(runtime.danhGiaChupManHinh(internalPath, "hash-khong-khop") is null,
            "Không được dùng nhãn cho dữ liệu có hash khác.");
        string internalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(internalPath)));
        yeuCau(runtime.danhGiaChupManHinh(internalPath, internalHash) ==
            new DlpDecision(DlpAction.Block, "screenshot-internal"), "Không áp quy tắc chụp màn hình cho hash khớp.");

        File.WriteAllText(internalPath, "Bỏ từ khóa");
        await doiDieuKienAsync(() => runtime.timTaiLieu(internalPath)?.Label == "Thông thường", "đổi nội dung");

        var storedBefore = runtime.timTaiLieu(publicPath)!;
        var updated = new PolicyDefinition(runtime.Policy.Rules.Select(rule => rule.Id == "classify-internal"
            ? rule with { Pattern = "Tài liệu chia sẻ" } : rule).ToArray());
        yeuCau((await runtime.capNhatChinhSachAsync(updated)).Count == 0, "Policy hợp lệ bị từ chối.");
        await doiDieuKienAsync(() => runtime.timTaiLieu(publicPath)?.Label == "Nội bộ", "đổi policy");
        var storedAfter = runtime.timTaiLieu(publicPath)!;
        yeuCau(storedAfter.PolicyVersion != storedBefore.PolicyVersion && storedAfter.Sha256 == storedBefore.Sha256,
            "Đổi policy phải phân loại lại dù byte tài liệu giữ nguyên.");

        version = runtime.PolicyVersion;
        hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(publicPath)));
        yeuCau(storedAfter.Sha256 == hash, "SHA-256 lưu trong SQLite khác hash tính trực tiếp.");

        var invalid = new PolicyDefinition(runtime.Policy.Rules.Select(rule => rule.Id == "clipboard-fake-12-digits"
            ? rule with { Pattern = "[" } : rule).ToArray());
        string configBefore = File.ReadAllText(Path.Combine(data.FullName, "rules.json"));
        var errors = await runtime.capNhatChinhSachAsync(invalid);
        yeuCau(errors is [{ RuleIndex: 1, RuleId: "clipboard-fake-12-digits" }] &&
            runtime.PolicyVersion == version &&
            File.ReadAllText(Path.Combine(data.FullName, "rules.json")) == configBefore,
            "Regex lỗi phải gắn đúng rule và giữ nguyên policy đang chạy lẫn rules.json.");

        File.Move(publicPath, renamedPath);
        await doiDieuKienAsync(() => runtime.timTaiLieu(publicPath) is null &&
            runtime.timTaiLieu(renamedPath)?.Label == "Nội bộ", "đổi tên đường dẫn");
        File.Delete(internalPath);
        await doiDieuKienAsync(() => runtime.timTaiLieu(internalPath) is null &&
            runtime.layDanhSachTaiLieu().Count == 2, "xóa tài liệu");

        runtime.ghiSuKien(new AuditEvent(now, "Demo", AuditBehavior.Clipboard,
            DlpAction.Block, "clipboard-internal", "source-known"));
        runtime.ghiSuKien(new AuditEvent(now.AddMinutes(1), "Demo", AuditBehavior.Screenshot,
            DlpAction.Observe, null, null));
        await runtime.capNhatCauHinhAsync(runtime.Settings with { ProtectionEnabled = false });
        yeuCau(runtime.danhGiaClipboard(renamedPath, "012345678901").Action == DlpAction.Allow,
            "Clipboard vẫn bị chặn khi tắt bảo vệ.");
    }

    await using (var reopened = DlpRuntime.khoiDong(data.FullName))
    {
        await doiDieuKienAsync(() => reopened.timTaiLieu(renamedPath)?.Label == "Nội bộ", "khởi động lại");
        yeuCau(reopened.PolicyVersion == version && reopened.timTaiLieu(renamedPath)?.Sha256 == hash,
            "Khởi động lại phải giữ đúng phiên bản policy và hash.");
        yeuCau(!reopened.Settings.ProtectionEnabled && reopened.Settings.ScanDirectory == documents.FullName,
            "Cấu hình đã lưu không được nạp đúng khi khởi động lại.");
        yeuCau(reopened.laySuKien(gioiHan: 1) is [{ Behavior: AuditBehavior.Screenshot, Result: DlpAction.Observe }],
            "Nhật ký trên máy phải lấy đúng sự kiện mới nhất từ SQLite sau khi mở lại.");
        var entries = reopened.laySuKien(now, now.AddMinutes(1), AuditBehavior.Clipboard);
        yeuCau(entries is [{ RuleId: "clipboard-internal", Result: DlpAction.Block }] &&
            reopened.laySuKien(now, now.AddMinutes(1)).Count == 1 &&
            reopened.laySuKien(behavior: AuditBehavior.Screenshot).Count == 1,
            "Lọc theo thời gian/hành vi hoặc lưu nhật ký qua lần khởi động lại bị sai.");

        await reopened.capNhatCauHinhAsync(reopened.Settings with { ScanDirectory = secondDirectory.FullName });
        await doiDieuKienAsync(() => reopened.timTaiLieu(secondPath)?.Label == "Nội bộ", "đổi thư mục quét");
        yeuCau(reopened.timTaiLieu(renamedPath) is null && reopened.layDanhSachTaiLieu().Count == 1,
            "Thư mục cũ còn trả về nhãn sau khi đổi thư mục quét.");
    }

    var allFormats = Directory.CreateDirectory(Path.Combine(root.FullName, "formats"));
    string txtPath = Path.Combine(allFormats.FullName, "cung-tu-khoa.txt");
    string docxPath = Path.Combine(allFormats.FullName, "cung-tu-khoa.docx");
    string pdfPath = Path.Combine(allFormats.FullName, "cung-tu-khoa.pdf");
    string imagePath = Path.Combine(allFormats.FullName, "can-ocr.pdf");
    const string keyword = "Hồ sơ Nội bộ";
    File.WriteAllText(txtPath, keyword);
    taoDocxTrongBang(docxPath, keyword);
    saoChepPdf("tieng-viet.pdf", pdfPath);
    saoChepPdf("chi-co-anh.pdf", imagePath);

    await using (var crossFormat = DlpRuntime.khoiDong(data.FullName))
    {
        var policy = new PolicyDefinition([
            new("lower-priority", RuleScope.Classification, 2, RuleKind.Keyword, keyword, "Nhãn thấp", null),
            new("first-at-priority", RuleScope.Classification, 3, RuleKind.Keyword, keyword, "Nội bộ", null),
            new("second-at-priority", RuleScope.Classification, 3, RuleKind.Keyword, keyword, "Nhãn sau", null)
        ]);
        yeuCau((await crossFormat.capNhatChinhSachAsync(policy)).Count == 0, "Không áp được policy thử cả ba định dạng.");
        await crossFormat.capNhatCauHinhAsync(crossFormat.Settings with { ScanDirectory = allFormats.FullName });
        await doiDieuKienAsync(() => crossFormat.layDanhSachTaiLieu().Count == 4, "đọc ba định dạng");
        foreach (var path in new[] { txtPath, docxPath, pdfPath })
        {
            yeuCau(crossFormat.timTaiLieu(path) is { Label: "Nội bộ", RuleId: "first-at-priority" },
                $"Kết quả phân loại hoặc ưu tiên quy tắc sai cho {path}.");
        }
        yeuCau(crossFormat.timTaiLieu(imagePath) is { Status: ClassificationStatus.NeedsOcr, Label: null },
            "PDF ảnh phải có trạng thái cần OCR.");
    }

    Console.WriteLine("PASS — Phân loại TXT/DOCX/PDF, OCR, SHA-256, đổi policy, SQLite, đổi thư mục và bộ lọc nhật ký.");
}
finally
{
    root.Delete(recursive: true);
}

static void yeuCau(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task doiDieuKienAsync(Func<bool> condition, string description)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try
    {
        while (!condition())
            await Task.Delay(50, deadline.Token);
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
    {
        throw new TimeoutException($"Chờ quá lâu. Tác vụ {description}.");
    }
}

static void taoDocxTrongBang(string path, string text)
{
    using var file = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
    var part = file.AddMainDocumentPart();
    part.Document = new Document(new Body(new Table(new TableRow(
        new TableCell(new Paragraph(new Run(new Text(text))))))));
    part.Document.Save();
}

static void saoChepPdf(string fileName, string destination)
{
    using var sample = typeof(Program).Assembly.GetManifestResourceStream("MauPhanLoai." + fileName)
        ?? throw new InvalidOperationException($"Thiếu mẫu {fileName}.");
    using var file = File.Create(destination);
    sample.CopyTo(file);
}
