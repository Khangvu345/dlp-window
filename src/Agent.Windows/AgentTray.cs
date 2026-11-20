using System.Diagnostics;

namespace RepoDlp.Agent.Windows;

internal sealed class AgentTray : ApplicationContext
{
    private readonly ContextMenuStrip menu = new();
    private readonly NotifyIcon icon;
    private readonly Uri address;

    public AgentTray(Uri address, Action moNhatKy, Action thuCanhBao, Action? openDocument = null, Func<Task>? chonThuMuc = null)
    {
        this.address = address;
        menu.Items.Add("Mở trang quản lý", null, (_, _) => moTrangQuanLy());
        menu.Items.Add("Xem nhật ký trên máy", null, (_, _) => moNhatKy());
        menu.Items.Add("Thử popup cảnh báo", null, (_, _) => thuCanhBao());
        if (chonThuMuc is not null)
            menu.Items.Add("Chọn thư mục quét…", null, async (_, _) =>
            {
                try
                {
                    await chonThuMuc();
                }
                catch (Exception loi)
                {
                    MessageBox.Show(loi.Message, "Không thể chọn thư mục", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        if (openDocument is not null)
            menu.Items.Add("Mở tài liệu thử", null, (_, _) => openDocument());
        menu.Items.Add("Thoát agent", null, (_, _) => ExitThread());
        icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Repo DLP",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => moTrangQuanLy();
    }

    private void moTrangQuanLy()
    {
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            MessageBox.Show($"Mở đường dẫn {address} trong trình duyệt.\n{error.Message}", "Repo DLP");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
