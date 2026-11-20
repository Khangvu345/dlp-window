using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class CanhBaoViPham : Form
{
    private readonly Label noiDung = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(16),
        AutoEllipsis = true
    };

    public CanhBaoViPham()
    {
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 135);

        var dong = new Button { Text = "Đóng", Dock = DockStyle.Bottom, Height = 36 };
        dong.Click += (_, _) => Hide();
        Controls.Add(noiDung);
        Controls.Add(dong);
        _ = Handle;
    }

    protected override bool ShowWithoutActivation => true;

    public void thongBao(AuditEvent suKien)
    {
        if (suKien.Result == DlpAction.Allow)
            return;

        BeginInvoke(() =>
        {
            bool daChan = suKien.Result == DlpAction.Block;
            Text = daChan ? "Repo DLP · Cảnh báo vi phạm" : "Repo DLP · Cảnh báo";
            string hanhVi = suKien.Behavior == AuditBehavior.Clipboard ? "Sao chép" : "Chụp màn hình";
            string thongDiep = daChan
                ? "Thao tác đã bị chặn theo chính sách. Vui lòng liên hệ quản trị viên (admin) nếu cần hỗ trợ."
                : suKien.RuleId is null
                    ? "Hệ thống đã ghi nhận thao tác này để kiểm tra."
                    : "Hệ thống đã ghi nhận vi phạm này trong nhật ký.";
            noiDung.Text = $"{hanhVi} · {suKien.Application}\n\n{thongDiep}";
            if (!Visible)
            {
                var vung = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(vung.Right - Width - 16, vung.Bottom - Height - 16);
                Show();
            }
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }
}
