using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class DocumentViewer : Form
{
    private const string ApplicationName = "Repo DLP · Trình xem tài liệu";
    private static DocumentViewer? activeViewer;
    private static ScreenshotShortcutMonitor? shortcuts;
    private readonly string documentPath;
    private readonly string documentSha256;
    private readonly string documentText;
    private readonly Func<string?, string, DlpDecision?> evaluateScreenshot;
    private readonly Action<AuditEvent> writeAudit;
    private readonly Func<string, string, bool>? copyFromDocument;
    private readonly RichTextBox document = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        HideSelection = false,
        BackColor = System.Drawing.SystemColors.Window
    };
    private readonly ToolStripStatusLabel status = new("Đang kiểm tra policy…");
    private readonly ContextMenuStrip copyMenu = new();
    private readonly System.Windows.Forms.Timer policyTimer = new() { Interval = 350 };
    private Task pendingAudit = Task.CompletedTask;
    private DlpDecision? decision;
    private bool refreshPending;
    private bool excluded;
    private bool visibleText;
    private string affinityStatus = "Đang kiểm tra policy";

    private DocumentViewer(string path, byte[] bytes,
        Func<string?, string, DlpDecision?> evaluateScreenshot, Action<AuditEvent> writeAudit,
        Func<string, string, bool>? copyFromDocument)
    {
        documentPath = path;
        documentSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        documentText = reader.ReadToEnd();
        this.evaluateScreenshot = evaluateScreenshot;
        this.writeAudit = writeAudit;
        this.copyFromDocument = copyFromDocument;

        Text = ApplicationName;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new System.Drawing.Size(680, 420);
        MinimumSize = new System.Drawing.Size(360, 240);

        var tools = new ToolStrip();
        tools.Items.Add("Sao chép", null, (_, _) => saoChepPhanChon());
        copyMenu.Items.Add("Sao chép", null, (_, _) => saoChepPhanChon());
        copyMenu.Items.Add("Chọn tất cả", null, (_, _) => document.SelectAll());
        document.ContextMenuStrip = copyMenu;

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        Controls.Add(document);
        Controls.Add(tools);
        Controls.Add(statusStrip);

        policyTimer.Tick += (_, _) => capNhatPolicy();
    }

    public static void moTaiLieu(Func<string?, string, DlpDecision?> evaluateScreenshot,
        Action<AuditEvent> writeAudit, Func<string, string, bool>? copyFromDocument = null)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Mở tài liệu thử",
            Filter = "Tài liệu TXT (*.txt)|*.txt",
            CheckFileExists = true
        };
        if (picker.ShowDialog() != DialogResult.OK) return;

        var path = Path.GetFullPath(picker.FileName);
        try
        {
            var viewer = new DocumentViewer(path, File.ReadAllBytes(path), evaluateScreenshot,
                writeAudit, copyFromDocument);
            dongTaiLieu();
            activeViewer = viewer;
            viewer.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            MessageBox.Show($"Không đọc được tài liệu TXT.\n{error.Message}", ApplicationName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static void dongTaiLieu() => activeViewer?.Close();

    public static void dungTheoDoiChupManHinh()
    {
        shortcuts?.Dispose();
        shortcuts = null;
    }

    private static void dungTheoDoiKhiKhongCan()
    {
        if (activeViewer is null && shortcuts?.DangChoNha == false)
            dungTheoDoiChupManHinh();
    }

    public static DlpDecision? layChinhSachAnhClipboard(string tenUngDung)
    {
        if (activeViewer is not { visibleText: true, IsHandleCreated: true } viewer ||
            !ScreenshotShortcutMonitor.laCuaSoDangHien(viewer.Handle))
            return null;

        if (!ScreenshotShortcutMonitor.laUngDungChup(tenUngDung) &&
            (tenUngDung != ClipboardMonitor.UnknownApplication || !ScreenshotShortcutMonitor.tienCanhLaUngDungChup()))
            return null;

        return viewer.evaluateScreenshot(viewer.documentPath, viewer.documentSha256);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        shortcuts ??= new ScreenshotShortcutMonitor(
            () => activeViewer is { IsHandleCreated: true } viewer ? viewer.Handle : 0,
            () => activeViewer?.decision?.Action == DlpAction.Block,
            (method, blocked) => activeViewer?.ghiNhanPhimChup(method, blocked),
            dungTheoDoiKhiKhongCan);
        capNhatPolicy();
        policyTimer.Start();
    }

    private async void capNhatPolicy()
    {
        if (refreshPending) return;
        refreshPending = true;
        try
        {
            var current = await Task.Run(() => evaluateScreenshot(documentPath, documentSha256));
            if (IsDisposed) return;
            decision = current;
            if (current is null)
            {
                hienNoiDung(false);
                affinityStatus = "Chờ phân loại phiên bản tài liệu đang mở";
            }
            else
            {
                apDungBaoVe(current.Action == DlpAction.Block);
            }
        }
        catch (Exception error)
        {
            Trace.WriteLine($"Không đọc được policy chụp màn hình ({error.GetType().Name}).");
            if (IsDisposed) return;
            decision = null;
            hienNoiDung(false);
            affinityStatus = "Không đọc được policy chụp màn hình";
        }
        finally
        {
            refreshPending = false;
        }

        status.Text = shortcuts?.IsActive == false
            ? $"{affinityStatus} · Không theo dõi được phím chụp"
            : affinityStatus;
    }

    private void apDungBaoVe(bool shouldExclude)
    {
        if (shouldExclude != excluded)
        {
            if (shouldExclude) hienNoiDung(false);
            if (ScreenshotShortcutMonitor.datBaoVeCuaSo(Handle, shouldExclude, out string reason))
                excluded = shouldExclude;
            else
            {
                affinityStatus = reason;
                return;
            }
        }

        hienNoiDung(true);
        affinityStatus = excluded
            ? "Windows đã nhận yêu cầu loại cửa sổ khỏi ảnh chụp"
            : "Cửa sổ hiển thị bình thường";
    }

    private void hienNoiDung(bool show)
    {
        if (visibleText == show) return;
        document.Text = show ? documentText : string.Empty;
        Text = show ? $"{Path.GetFileName(documentPath)} · Repo DLP" : ApplicationName;
        visibleText = show;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.Insert))
        {
            saoChepPhanChon();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void saoChepPhanChon()
    {
        if (!visibleText || document.SelectionLength == 0) return;
        string selectedText = document.SelectedText;
        try
        {
            if (evaluateScreenshot(documentPath, documentSha256) is null)
            {
                hienNoiDung(false);
                return;
            }
            if (copyFromDocument is null)
            {
                Clipboard.SetText(selectedText);
                return;
            }
            if (copyFromDocument(documentPath, selectedText)) return;
        }
        catch (Exception error)
        {
            Trace.WriteLine($"Không sao chép được văn bản thử ({error.GetType().Name}).");
        }

        MessageBox.Show(this, "Không sao chép được văn bản. Hãy thử lại.", ApplicationName,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ghiNhanPhimChup(string method, bool blocked)
    {
        DlpAction result = blocked ? DlpAction.Block
            : decision?.Action == DlpAction.Allow && !excluded ? DlpAction.Allow : DlpAction.Observe;
        string detail = blocked ? "Đã chặn sự kiện phím chụp" : $"{affinityStatus} · Chưa xác minh ảnh thu được";
        var entry = new AuditEvent(DateTimeOffset.UtcNow, ApplicationName, AuditBehavior.Screenshot,
            result, decision?.RuleId, $"{method} · {detail}");

        // Đưa thao tác ghi SQLite ra khỏi luồng xử lý phím của Windows.
        pendingAudit = pendingAudit.ContinueWith(_ =>
        {
            try
            {
                writeAudit(entry);
            }
            catch (Exception error)
            {
                Trace.WriteLine($"Không ghi được sự kiện chụp màn hình ({error.GetType().Name}).");
            }
        }, TaskScheduler.Default);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (ReferenceEquals(activeViewer, this)) activeViewer = null;
            policyTimer.Dispose();
            dungTheoDoiKhiKhongCan();
            pendingAudit.GetAwaiter().GetResult();
            copyMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
