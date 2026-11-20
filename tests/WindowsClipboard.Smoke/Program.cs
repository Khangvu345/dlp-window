using System.Diagnostics;
using System.Drawing.Imaging;
using RepoDlp.Agent.Windows;
using RepoDlp.Core;

namespace RepoDlp.WindowsClipboard.Smoke;

internal static class Program
{
    private const string CoChoPhep = "--cho-phep-ghi-clipboard";
    private const string VanBanBiChan = "Nội dung sao chép đã bị chặn bởi Repo DLP.";
    private const string AnhBiChan = "Ảnh chụp màn hình đã bị chặn bởi Repo DLP.";
    private const string DuLieuBaoVe = "Đây là nội dung thử được bảo vệ.";
    private const string VanBanKemAnh = "Chú thích tiếng Việt đi kèm ảnh.";
    private const string QuyTacVanBan = "smoke-clipboard";
    private const string QuyTacAnhChan = "smoke-screenshot-block";
    private const string QuyTacAnhQua = "smoke-screenshot-allow";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not [CoChoPhep] || !OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            Console.Error.WriteLine($"Chạy trong phiên desktop Windows riêng với cờ {CoChoPhep}. Test sẽ thay dữ liệu clipboard.");
            return 2;
        }

        using (var tienTrinh = Process.GetCurrentProcess())
        {
            if (tienTrinh.SessionId == 0)
            {
                Console.Error.WriteLine("Cần chạy trong phiên desktop người dùng Windows.");
                return 2;
            }
        }

        try
        {
            chayKiemTra();
            Console.WriteLine("PASS — Sao chép bị chặn hai lần, dán lặp không thêm log, ảnh bitmap và PNG bị chặn, ảnh được cho qua giữ nguyên.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void chayKiemTra()
    {
        var nhatKy = new List<AuditEvent>();
        var duongDan = Path.GetFullPath("du-lieu-thu.txt");
        DlpDecision? quyTacAnh = new(DlpAction.Block, QuyTacAnhChan);

        using var khungDan = new TextBox();
        _ = khungDan.Handle;
        bool quanSatChu = false;
        var clipboard = new ClipboardMonitor((nguon, noiDung) =>
        {
            if (nguon == duongDan && noiDung == DuLieuBaoVe)
                return new DlpDecision(DlpAction.Block, QuyTacVanBan);
            if (quanSatChu && noiDung == VanBanKemAnh)
                return new DlpDecision(DlpAction.Observe, "smoke-quan-sat-chu");
            return new DlpDecision(DlpAction.Allow, null);
        },
            nhatKy.Add,
            _ => quyTacAnh);

        try
        {
            for (int lan = 1; lan <= 2; lan++)
            {
                choDenKhi(() => clipboard.saoChepTuTaiLieu(duongDan, DuLieuBaoVe), "Ghi văn bản thử vào clipboard");
                kiemTraSuKien(nhatKy, lan, AuditBehavior.Clipboard, DlpAction.Block, QuyTacVanBan);
                kiemTraDanHaiLan(khungDan, VanBanBiChan, nhatKy, lan);
            }

            using var anh = new Bitmap(5, 5);
            for (int hang = 0; hang < anh.Height; hang++)
                for (int cot = 0; cot < anh.Width; cot++)
                    anh.SetPixel(cot, hang, (cot + hang) % 2 == 0 ? Color.OrangeRed : Color.RoyalBlue);
            using var duLieuPng = new MemoryStream();
            anh.Save(duLieuPng, ImageFormat.Png);
            var png = duLieuPng.ToArray();

            datAnhVaText(khungDan.Handle, anh, null);
            kiemTraSuKien(nhatKy, 3, AuditBehavior.Screenshot, DlpAction.Block, QuyTacAnhChan);
            kiemTraAnhDaXoa();
            kiemTraDanHaiLan(khungDan, AnhBiChan, nhatKy, 3);

            datAnhVaText(khungDan.Handle, null, png);
            kiemTraSuKien(nhatKy, 4, AuditBehavior.Screenshot, DlpAction.Block, QuyTacAnhChan);
            kiemTraAnhDaXoa();
            kiemTraDanHaiLan(khungDan, AnhBiChan, nhatKy, 4);

            quyTacAnh = new DlpDecision(DlpAction.Allow, QuyTacAnhQua);
            datAnhVaText(khungDan.Handle, anh, png);
            kiemTraSuKien(nhatKy, 5, AuditBehavior.Screenshot, DlpAction.Allow, QuyTacAnhQua);
            kiemTra(ClipboardThu.anhVaPngConNguyen(khungDan.Handle, anh, png),
                "Ảnh được cho qua đã đổi pixel hoặc byte PNG.");
            kiemTraDanHaiLan(khungDan, VanBanKemAnh, nhatKy, 5);

            foreach (var (quyetDinh, hanhVi) in new (DlpDecision?, AuditBehavior)[]
            {
                (null, AuditBehavior.Clipboard),
                (new(DlpAction.Allow, null), AuditBehavior.Screenshot),
                (new(DlpAction.Observe, "smoke-quan-sat-anh"), AuditBehavior.Screenshot)
            })
            {
                quyTacAnh = quyetDinh;
                int soLuong = nhatKy.Count + 1;
                choDenKhi(() => ClipboardThu.thuDatAnhVaText(khungDan.Handle, null, anh, png), "Ghi ảnh không kèm chữ");
                kiemTraSuKien(nhatKy, soLuong, hanhVi, quyetDinh?.Action ?? DlpAction.Allow, quyetDinh?.RuleId);
                kiemTra(nhatKy[^1].canHienCanhBao() == (quyetDinh?.Action == DlpAction.Observe) &&
                    ClipboardThu.anhVaPngConNguyen(khungDan.Handle, anh, png), "Sai cảnh báo hoặc ảnh bị thay đổi.");
            }

            quanSatChu = true;
            quyTacAnh = new DlpDecision(DlpAction.Allow, null);
            int soTruoc = nhatKy.Count;
            datAnhVaText(khungDan.Handle, anh, png);
            kiemTraSuKien(nhatKy, soTruoc + 2, AuditBehavior.Clipboard, DlpAction.Observe, "smoke-quan-sat-chu");
            kiemTra(nhatKy[soTruoc] is { Behavior: AuditBehavior.Screenshot, Result: DlpAction.Allow, RuleId: null } &&
                !nhatKy[soTruoc].canHienCanhBao() && nhatKy[^1].canHienCanhBao(),
                "Ảnh cho qua đã làm mất hoặc tạo trùng cảnh báo của quy tắc văn bản.");
        }
        finally
        {
            clipboard.Dispose();
            Clipboard.Clear();
        }
    }

    private static void datAnhVaText(nint cuaSo, Bitmap? bitmap, byte[]? png)
    {
        choDenKhi(() => ClipboardThu.thuDatAnhVaText(cuaSo, VanBanKemAnh, bitmap, png), "Ghi ảnh và văn bản vào clipboard");
        kiemTra(ClipboardThu.coBitmap() == (bitmap is not null) && ClipboardThu.coPng() == (png is not null),
            "Định dạng ảnh thử chưa được ghi đúng.");
    }

    private static void kiemTraAnhDaXoa() =>
        kiemTra(!ClipboardThu.coBitmap() && !ClipboardThu.coPng(), "Clipboard vẫn giữ định dạng ảnh đã bị chặn.");

    private static void kiemTraSuKien(IReadOnlyList<AuditEvent> nhatKy, int soLuong,
        AuditBehavior hanhVi, DlpAction ketQua, string? maQuyTac)
    {
        choDenKhi(() => nhatKy.Count >= soLuong, $"Chờ nhật ký cho {maQuyTac}");
        kiemTra(nhatKy.Count == soLuong && nhatKy[^1].Behavior == hanhVi &&
            nhatKy[^1].Result == ketQua && nhatKy[^1].RuleId == maQuyTac,
            $"Nhật ký không khớp cho {maQuyTac}. Quan sát được: {string.Join("; ", nhatKy)}");
    }

    private static void kiemTraDanHaiLan(TextBox khungDan, string mongDoi, IReadOnlyList<AuditEvent> nhatKy, int soLuong)
    {
        for (int lan = 1; lan <= 2; lan++)
        {
            kiemTra(Clipboard.GetText(TextDataFormat.UnicodeText) == mongDoi, $"Văn bản clipboard sai ở lượt đọc {lan}.");
            khungDan.Clear();
            khungDan.Paste();
            kiemTra(khungDan.Text == mongDoi, $"Văn bản nhận được sai ở lượt dán {lan}.");
        }

        // Xử lý cả thông báo Windows và các lần thử lại đang chờ trước khi đếm nhật ký.
        var dongHo = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        } while (dongHo.Elapsed < TimeSpan.FromMilliseconds(300));

        kiemTra(nhatKy.Count == soLuong, "Đọc hoặc dán clipboard tạo thêm nhật ký.");
    }

    private static void choDenKhi(Func<bool> dieuKien, string moTa)
    {
        var dongHo = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (dieuKien())
                return;
            Thread.Sleep(10);
        } while (dongHo.Elapsed < TimeSpan.FromSeconds(5));

        throw new TimeoutException($"{moTa} quá thời hạn.");
    }

    private static void kiemTra(bool dung, string thongBao)
    {
        if (!dung)
            throw new InvalidOperationException(thongBao);
    }
}
