using RepoDlp.Core;
using RepoDlp.Infrastructure;
using RepoDlp.Web;

namespace RepoDlp.Agent.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\RepoDlp.Agent.Windows", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Repo DLP đang chạy. Hãy mở biểu tượng ở khay hệ thống.", "Repo DLP");
            return;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            var thuMucDuLieu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoDlp");
            var runtime = DlpRuntime.khoiDong(thuMucDuLieu);
            try
            {
                chayPhien(runtime);
            }
            finally
            {
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Lỗi Repo DLP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void chayPhien(DlpRuntime runtime)
    {
        using var nhatKy = new NhatKyTrenMay(() => runtime.laySuKien(gioiHan: 200));
        using var canhBao = new CanhBaoViPham();

        void ghiNhatKy(AuditEvent suKien)
        {
            runtime.ghiSuKien(suKien);
            nhatKy.danhDauThayDoi();
            canhBao.thongBao(suKien);
        }

        using var clipboard = new ClipboardMonitor(runtime.danhGiaClipboard, ghiNhatKy,
            DocumentViewer.layChinhSachAnhClipboard);
        using var boChonThuMuc = new BoChonThuMuc(() => runtime.Settings.ScanDirectory);
        var dashboard = LocalDashboard.khoiDongAsync(runtime, boChonThuMuc.chonThuMucAsync)
            .GetAwaiter().GetResult();
        try
        {
            using var tray = new AgentTray(
                dashboard.Address, nhatKy.moNhatKy, canhBao.thuCanhBao,
                () => DocumentViewer.moTaiLieu(runtime, ghiNhatKy, clipboard.saoChepTuTaiLieu),
                async () =>
                {
                    var duongDan = await boChonThuMuc.chonThuMucAsync(CancellationToken.None);
                    if (duongDan is not null)
                        await runtime.capNhatCauHinhAsync(runtime.Settings with { ScanDirectory = duongDan });
                });
            Application.Run(tray);
        }
        finally
        {
            DocumentViewer.dongTaiLieu();
            DocumentViewer.dungTheoDoiChupManHinh();
            dashboard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
