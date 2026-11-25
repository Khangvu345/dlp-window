using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RepoDlp.Infrastructure;

const string mauTiengViet = "Hồ sơ Nội bộ";
const string mauTrongBang = "Điều khoản trong bảng";
string thuMuc = Directory.CreateTempSubdirectory("repo-dlp-scan-").FullName;
string txt = Path.Combine(thuMuc, "mau.TXT");
string docx = Path.Combine(thuMuc, "mau.docx");
string pdf = Path.Combine(thuMuc, "mau.pdf");
string pdfAnh = Path.Combine(thuMuc, "anh.pdf");
string pdfHong = Path.Combine(thuMuc, "hong.pdf");
string choSave = Path.Combine(thuMuc, "dang-luu.txt");
string doiTen = Path.Combine(thuMuc, "da-doi-ten.txt");
string thuMucCu = Path.Combine(thuMuc, "cu.pdf");
string thuMucMoi = Path.Combine(thuMuc, "moi");
const string tenTepCon = "tai-lieu-con.txt";
string tepConCu = Path.Combine(thuMucCu, tenTepCon);
string tepConMoi = Path.Combine(thuMucMoi, tenTepCon);

try
{
    taoDocx(docx, mauTiengViet, mauTrongBang);
    await saoChepPdfMauAsync("tieng-viet.pdf", pdf);
    await saoChepPdfMauAsync("chi-co-anh.pdf", pdfAnh);
    await File.WriteAllTextAsync(pdfHong, "dữ liệu PDF hỏng");
    await File.WriteAllTextAsync(txt, mauTiengViet, new UTF8Encoding(true));
    await File.WriteAllTextAsync(Path.Combine(thuMuc, "bo-qua.tmp"), mauTiengViet);

    await theoDoiNhanhAsync(async (dotQuet, token) =>
    {
        var dau = await dotQuet.ReadAsync(token);
        kiemTra(dau.IsSnapshot && dau.Upserts.Count == 5, "Quét đầu tiên có đúng năm tệp hỗ trợ.");
        var ketQua = dau.Upserts.ToDictionary(x => x.Path);
        foreach (string tep in new[] { txt, docx, pdf })
        {
            kiemTra(ketQua[tep] is { Status: DocumentReadStatus.Success, Text: { } vanBan } &&
                vanBan.Contains(mauTiengViet, StringComparison.Ordinal),
                $"Đọc tiếng Việt trong {tep}. Nhận {ketQua[tep].Status}: {ketQua[tep].Text}");
            kiemTra(ketQua[tep].Sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tep))),
                $"Băm theo byte của {tep}.");
        }
        kiemTra(ketQua[docx].Text!.Contains(mauTrongBang, StringComparison.Ordinal), "Đọc ô bảng DOCX.");
        kiemTra(ketQua[pdfAnh] is { Status: DocumentReadStatus.NeedsOcr, Text: null }, "PDF ảnh cần OCR.");
        kiemTra(ketQua[pdfHong] is { Status: DocumentReadStatus.ReadError, Text: null }, "PDF hỏng báo lỗi.");

        await using (var stream = new FileStream(choSave, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(mauTiengViet), token);
            await stream.FlushAsync(token);
            await Task.Delay(325, token);
        }
        await doiKetQuaAsync(dotQuet, ketQua, () =>
            ketQua.GetValueOrDefault(choSave)?.Text == mauTiengViet, token);

        await File.WriteAllTextAsync(choSave, "Đã sửa", token);
        await doiKetQuaAsync(dotQuet, ketQua, () => ketQua.GetValueOrDefault(choSave)?.Text == "Đã sửa", token);
        File.Move(choSave, doiTen);
        await doiKetQuaAsync(dotQuet, ketQua, () => !ketQua.ContainsKey(choSave) && ketQua.ContainsKey(doiTen), token);
        File.Delete(doiTen);
        await doiKetQuaAsync(dotQuet, ketQua, () => !ketQua.ContainsKey(doiTen), token);

        Directory.CreateDirectory(thuMucCu);
        await File.WriteAllTextAsync(tepConCu, "Tệp trong thư mục con", token);
        kiemTra(Directory.EnumerateFiles(thuMucCu).Contains(tepConCu),
            $"Không liệt kê được tệp mẫu vừa ghi trong thư mục thử: {tepConCu}");
        await doiKetQuaAsync(dotQuet, ketQua, () => ketQua.ContainsKey(tepConCu), token);
        Directory.Move(thuMucCu, thuMucMoi);
        await doiKetQuaAsync(dotQuet, ketQua, () => !ketQua.ContainsKey(tepConCu) && ketQua.ContainsKey(tepConMoi), token);
        kiemTra(ketQua.Keys.ToHashSet().SetEquals([txt, docx, pdf, pdfAnh, pdfHong, tepConMoi]),
            "Chỉ mục khớp với nội dung thư mục sau các thao tác.");
    });

    File.Delete(pdfHong);
    await File.WriteAllTextAsync(txt, "Đã sửa lúc dừng");
    await theoDoiNhanhAsync(async (dotQuet, token) =>
    {
        var dau = await dotQuet.ReadAsync(token);
        kiemTra(dau.IsSnapshot &&
            dau.Upserts.Select(result => result.Path).ToHashSet().SetEquals([txt, docx, pdf, pdfAnh, tepConMoi]),
            "Quét sau khi khởi động lại khớp danh sách thực tế.");
        kiemTra(dau.Upserts.Single(result => result.Path == txt).Text == "Đã sửa lúc dừng",
            "Tệp đã sửa khi bộ quét đang dừng được đọc lại.");
    });
    Console.WriteLine("PASS — Tiếng Việt trong TXT/DOCX/PDF, ô bảng, OCR, tệp lỗi và theo dõi thư mục.");
}
finally
{
    Directory.Delete(thuMuc, recursive: true);
}

async Task theoDoiNhanhAsync(Func<ChannelReader<ScanBatch>, CancellationToken, Task> kiemThu)
{
    using var huy = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var dotQuet = Channel.CreateUnbounded<ScanBatch>();
    var scanner = new DocumentScanner();
    var dangQuet = scanner.theoDoiThuMucAsync(thuMuc, (dot, token) => dotQuet.Writer.WriteAsync(dot, token).AsTask(),
        huy.Token);
    try
    {
        await kiemThu(dotQuet.Reader, huy.Token);
    }
    finally
    {
        await huy.CancelAsync();
        try
        {
            await dangQuet;
        }
        catch (OperationCanceledException) when (huy.IsCancellationRequested)
        {
        }
    }
}

static async Task doiKetQuaAsync(ChannelReader<ScanBatch> dotQuet, Dictionary<string, ScannedDocument> ketQua,
    Func<bool> dieuKien, CancellationToken token)
{
    while (!dieuKien())
    {
        var dot = await dotQuet.ReadAsync(token);
        if (dot.IsSnapshot)
            ketQua.Clear();
        foreach (string path in dot.Removals)
            ketQua.Remove(path);
        foreach (var tep in dot.Upserts)
            ketQua[tep.Path] = tep;
    }
}

static void kiemTra(bool hopLe, string thongBao)
{
    if (!hopLe)
        throw new InvalidOperationException(thongBao);
}

static void taoDocx(string path, string vanBan, string noiDungBang)
{
    using var f = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
    var part = f.AddMainDocumentPart();
    part.Document = new Document(new Body(
        new Paragraph(new Run(new Text(vanBan))),
        new Table(new TableRow(new TableCell(new Paragraph(
            new Run(new Text(noiDungBang[..10])),
            new Run(new Text(noiDungBang[10..]))))))));
    part.Document.Save();
}

static async Task saoChepPdfMauAsync(string tenTep, string dich)
{
    await using var mau = Assembly.GetExecutingAssembly().GetManifestResourceStream("MauQuet." + tenTep)
        ?? throw new InvalidOperationException($"Thiếu tệp mẫu {tenTep}.");
    await using var tep = File.Create(dich);
    await mau.CopyToAsync(tep);
}
