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
        using var clipboard = new ClipboardMonitor(runtime.danhGiaClipboard, runtime.ghiSuKien);
        using var boChonThuMuc = new BoChonThuMuc(() => runtime.Settings.ScanDirectory);
        var dashboard = LocalDashboard.khoiDongAsync(runtime, boChonThuMuc.chonThuMucAsync)
            .GetAwaiter().GetResult();
        try
        {
            using var tray = new AgentTray(
                dashboard.Address,
                () => DocumentViewer.moTaiLieu(runtime.danhGiaChupManHinh, runtime.ghiSuKien, clipboard.saoChepTuTaiLieu));
            Application.Run(tray);
        }
        finally
        {
            DocumentViewer.dongTaiLieu();
            dashboard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
