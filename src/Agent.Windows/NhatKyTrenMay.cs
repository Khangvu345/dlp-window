using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class NhatKyTrenMay : Form
{
    private readonly Func<IReadOnlyList<AuditEvent>> docSuKien;
    private readonly ListView bang = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true };
    private readonly System.Windows.Forms.Timer lamMoi = new() { Interval = 1000 };
    private int coThayDoi = 1;

    public NhatKyTrenMay(Func<IReadOnlyList<AuditEvent>> docSuKien)
    {
        this.docSuKien = docSuKien;
        Text = "Repo DLP · 200 sự kiện gần nhất trên máy";
        ClientSize = new Size(920, 420);
        bang.Columns.Add("Thời gian", 150);
        bang.Columns.Add("Ứng dụng", 145);
        bang.Columns.Add("Hành vi", 100);
        bang.Columns.Add("Kết quả", 100);
        bang.Columns.Add("Quy tắc", 170);
        bang.Columns.Add("Chi tiết", 235);
        Controls.Add(bang);
        lamMoi.Tick += (_, _) => capNhatBang();
    }

    public void danhDauThayDoi() => Interlocked.Exchange(ref coThayDoi, 1);

    public void moNhatKy()
    {
        danhDauThayDoi();
        capNhatBang();
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        lamMoi.Start();
    }

    private void capNhatBang()
    {
        if (Interlocked.Exchange(ref coThayDoi, 0) == 0) return;
        var dong = docSuKien().Select(muc =>
        {
            string chiTiet = muc.Detail == "source-unknown"
                ? "Chưa xác định tệp nguồn · Bản ghi cũ chỉ lưu trạng thái nguồn" : muc.Detail ?? "";
            return new ListViewItem(new[] {
                muc.OccurredAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"), muc.Application,
                muc.Behavior == AuditBehavior.Clipboard ? "Clipboard" : "Chụp màn hình",
                muc.Result switch { DlpAction.Block => "Đã chặn", DlpAction.Observe => "Chỉ quan sát", _ => "Cho qua" },
                muc.RuleId ?? "", chiTiet
            }) { ToolTipText = chiTiet };
        }).ToArray();
        bang.BeginUpdate();
        try
        {
            bang.Items.Clear();
            bang.Items.AddRange(dong);
        }
        finally { bang.EndUpdate(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            lamMoi.Stop();
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lamMoi.Dispose();
        base.Dispose(disposing);
    }
}
