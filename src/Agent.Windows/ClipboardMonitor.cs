using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class ClipboardMonitor : NativeWindow, IDisposable
{
    private const int ClipboardUpdate = 0x031D;
    private const uint Bitmap = 2;
    private const uint Dib = 8;
    private const uint UnicodeText = 13;
    private const uint DibV5 = 17;
    private const uint MoveableMemory = 0x0002;
    private const int MaxRetries = 5;
    private const string DemoApplication = "Repo DLP";
    internal const string UnknownApplication = "Không xác định";
    private const string CopyBlockedMessage = "Nội dung sao chép đã bị chặn bởi Repo DLP.";
    private const string ScreenshotBlockedMessage = "Ảnh chụp màn hình đã bị chặn bởi Repo DLP.";
    private const string ScreenshotDetail = "Ảnh từ công cụ chụp trên clipboard";

    private readonly Func<string?, string, DlpDecision> evaluate;
    private readonly Action<AuditEvent> addAuditEvent;
    private readonly Func<string, DlpDecision?>? evaluateScreenshot;
    private readonly uint pngFormat = dangKyDinhDangClipboard("PNG");
    private readonly uint mimePngFormat = dangKyDinhDangClipboard("image/png");
    private readonly System.Windows.Forms.Timer retryTimer = new() { Interval = 35 };
    private string? copyOrigin;
    private ClipboardIdentity handledIdentity;
    private ClipboardIdentity retryIdentity;
    private int retries;

    public ClipboardMonitor(
        Func<string?, string, DlpDecision> evaluate,
        Action<AuditEvent> addAuditEvent,
        Func<string, DlpDecision?>? evaluateScreenshot = null)
    {
        this.evaluate = evaluate;
        this.addAuditEvent = addAuditEvent;
        this.evaluateScreenshot = evaluateScreenshot;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // Chỉ nhận thông báo, không tạo cửa sổ hiển thị.
        handledIdentity = layDanhTinhClipboard();
        if (!batDauNhanThongBaoClipboard(Handle))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            Dispose();
            throw error;
        }

        retryTimer.Tick += (_, _) => xuLyThayDoiClipboard();
    }

    public bool saoChepTuTaiLieu(string sourcePath, string text)
    {
        if (text.Length == 0)
            return false;

        var originPath = Path.GetFullPath(sourcePath);
        var memory = taoVanBanClipboard(text);
        if (memory == IntPtr.Zero)
            return false;

        try
        {
            if (!moClipboard(Handle))
                return false;

            try
            {
                if (!lamTrongClipboard())
                    return false;
                copyOrigin = null;
                if (datDuLieuClipboard(UnicodeText, memory) == IntPtr.Zero)
                    return false;

                // Clipboard nhận quyền quản lý vùng nhớ khi ghi thành công.
                memory = IntPtr.Zero;
                copyOrigin = originPath;
            }
            finally
            {
                dongClipboard();
            }
        }
        finally
        {
            if (memory != IntPtr.Zero)
                giaiPhongBoNhoToanCuc(memory);
        }

        xuLyThayDoiClipboard();
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == ClipboardUpdate &&
            (!retryTimer.Enabled || layDanhTinhClipboard() != retryIdentity))
            xuLyThayDoiClipboard();

        base.WndProc(ref message);
    }

    private void xuLyThayDoiClipboard()
    {
        retryTimer.Stop();
        var identity = layDanhTinhClipboard();
        if (daXuLyClipboard(identity))
            return;

        batDauPhienClipboard(identity);

        try
        {
            if (!thuDocClipboard(out var snapshot))
            {
                if (henThuLai())
                    return;

                handledIdentity = identity;
                ghiNhatKy(layTenUngDung(identity.Owner), AuditBehavior.Clipboard, DlpAction.Observe, null,
                    "source-unknown read-failed");
                return;
            }

            identity = snapshot.Identity;
            if (daXuLyClipboard(identity))
                return;

            batDauPhienClipboard(identity);
            var sourcePath = identity.Owner == Handle ? copyOrigin : null;
            var application = sourcePath is null ? layTenUngDung(identity.Owner) : DemoApplication;
            var source = sourcePath is null ? "source-unknown" : "source-known";
            var screenshot = snapshot.HasImage ? evaluateScreenshot?.Invoke(application) : null;

            if (screenshot?.Action == DlpAction.Block)
            {
                xuLyChanClipboard(identity, application, AuditBehavior.Screenshot, screenshot.RuleId,
                    ScreenshotDetail, ScreenshotBlockedMessage);
                return;
            }

            if (snapshot.HasText && snapshot.Text is null && henThuLai())
                return;

            var clipboard = snapshot.Text is { } text ? evaluate(sourcePath, text) : null;
            if (clipboard?.Action == DlpAction.Block)
            {
                if (!xuLyChanClipboard(identity, application, AuditBehavior.Clipboard, clipboard.RuleId,
                        source, CopyBlockedMessage))
                    return;

                if (screenshot is not null)
                    ghiNhatKy(application, AuditBehavior.Screenshot, screenshot.Action, screenshot.RuleId,
                        ScreenshotDetail);
                return;
            }

            handledIdentity = identity;
            if (screenshot is not null)
                ghiNhatKy(application, AuditBehavior.Screenshot, screenshot.Action, screenshot.RuleId,
                    ScreenshotDetail);
            if (screenshot is null || clipboard?.RuleId is not null || (snapshot.HasText && clipboard is null))
                ghiNhatKy(application, AuditBehavior.Clipboard,
                    clipboard?.Action ?? (snapshot.HasText ? DlpAction.Observe : DlpAction.Allow),
                    clipboard?.RuleId, clipboard is not null
                        ? source
                        : source + (snapshot.HasText ? " read-failed" : " no-text"));
        }
        catch (Exception error)
        {
            if (layDanhTinhClipboard() == identity)
                handledIdentity = identity;
            Trace.TraceError("Repo DLP clipboard: {0}", error.GetType().Name);
        }
    }

    private bool xuLyChanClipboard(ClipboardIdentity identity, string application, AuditBehavior behavior,
        string? ruleId, string detail, string message)
    {
        var result = thuThayClipboard(identity, message);
        if (result == ReplaceResult.Busy && henThuLai())
            return false;

        handledIdentity = identity;
        if (result == ReplaceResult.Changed)
            retryTimer.Start();

        var outcome = result is ReplaceResult.Replaced or ReplaceResult.Cleared ? DlpAction.Block : DlpAction.Observe;
        var status = behavior == AuditBehavior.Screenshot ? result switch
        {
            ReplaceResult.Replaced => " · Đã loại ảnh khỏi clipboard",
            ReplaceResult.Cleared => " · Đã loại ảnh khỏi clipboard, ghi thông báo thất bại",
            ReplaceResult.Changed => " · Clipboard đã đổi trước khi xử lý",
            _ => " · Không thể xóa ảnh"
        } : result switch
        {
            ReplaceResult.Replaced => "",
            ReplaceResult.Cleared => " notice-failed",
            ReplaceResult.Changed => " clipboard-changed",
            _ => " clear-failed"
        };
        ghiNhatKy(application, behavior, outcome, ruleId, detail + status);
        return true;
    }

    private bool daXuLyClipboard(ClipboardIdentity identity) =>
        // Cửa sổ listener chỉ sở hữu văn bản từ trình xem đã ghi nguồn hoặc thông báo do agent tạo.
        identity == handledIdentity || (identity.Owner == Handle && copyOrigin is null);

    private static ClipboardIdentity layDanhTinhClipboard() =>
        new(laySoThuTuClipboard(), layCuaSoSoHuuClipboard());

    private void batDauPhienClipboard(ClipboardIdentity identity)
    {
        if (retryIdentity == identity)
            return;

        retryIdentity = identity;
        retries = 0;
    }

    private bool henThuLai()
    {
        if (retries >= MaxRetries)
            return false;

        retries++;
        retryTimer.Start();
        return true;
    }

    private bool thuDocClipboard(out ClipboardSnapshot snapshot)
    {
        snapshot = default;
        if (!moClipboard(Handle))
            return false;

        try
        {
            var hasImage = coDinhDangClipboard(Bitmap) || coDinhDangClipboard(Dib) ||
                coDinhDangClipboard(DibV5) ||
                (pngFormat != 0 && coDinhDangClipboard(pngFormat)) ||
                (mimePngFormat != 0 && coDinhDangClipboard(mimePngFormat));
            var hasText = coDinhDangClipboard(UnicodeText);
            var text = hasText ? docVanBanClipboard() : null;
            snapshot = new ClipboardSnapshot(layDanhTinhClipboard(), hasImage, hasText, text);
            return true;
        }
        finally
        {
            dongClipboard();
        }
    }

    private static string? docVanBanClipboard()
    {
        var data = layDuLieuClipboard(UnicodeText);
        if (data == IntPtr.Zero)
            return null;

        var characterCount = layKichThuocBoNhoToanCuc(data) / sizeof(char);
        if (characterCount == 0 || characterCount > (nuint)int.MaxValue)
            return null;

        var pointer = khoaBoNhoToanCuc(data);
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            var text = Marshal.PtrToStringUni(pointer, (int)characterCount);
            var terminator = text.IndexOf('\0');
            return terminator < 0 ? null : text[..terminator];
        }
        finally
        {
            moKhoaBoNhoToanCuc(data);
        }
    }

    private static nint taoVanBanClipboard(string text)
    {
        var byteCount = checked(text.Length * sizeof(char));
        var memory = capPhatBoNhoToanCuc(MoveableMemory, (nuint)byteCount + sizeof(char));
        if (memory == IntPtr.Zero)
            return IntPtr.Zero;

        var complete = false;
        try
        {
            var destination = khoaBoNhoToanCuc(memory);
            if (destination == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, destination, text.Length);
                Marshal.WriteInt16(destination, byteCount, 0);
            }
            finally
            {
                moKhoaBoNhoToanCuc(memory);
            }

            complete = true;
            return memory;
        }
        finally
        {
            if (!complete)
                giaiPhongBoNhoToanCuc(memory);
        }
    }

    private ReplaceResult thuThayClipboard(ClipboardIdentity identity, string message)
    {
        var memory = taoVanBanClipboard(message);
        try
        {
            if (!moClipboard(Handle))
                return ReplaceResult.Busy;

            try
            {
                if (layDanhTinhClipboard() != identity)
                    return ReplaceResult.Changed;
                if (!lamTrongClipboard())
                    return ReplaceResult.Failed;

                // Clipboard vừa được xóa. Chỉ ghi thông báo và không khôi phục dữ liệu cũ.
                copyOrigin = null;
                if (memory == IntPtr.Zero || datDuLieuClipboard(UnicodeText, memory) == IntPtr.Zero)
                    return ReplaceResult.Cleared;

                memory = IntPtr.Zero;
                return ReplaceResult.Replaced;
            }
            finally
            {
                dongClipboard();
            }
        }
        finally
        {
            if (memory != IntPtr.Zero)
                giaiPhongBoNhoToanCuc(memory);
        }
    }

    private void ghiNhatKy(string application, AuditBehavior behavior, DlpAction outcome, string? ruleId, string detail)
    {
        addAuditEvent(new AuditEvent(
            DateTimeOffset.UtcNow, application, behavior, outcome, ruleId, detail));
    }

    private static string layTenUngDung(nint owner)
    {
        if (owner == IntPtr.Zero || layIdLuongCuaSo(owner, out var processId) == 0)
            return UnknownApplication;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return UnknownApplication;
        }
    }

    public void Dispose()
    {
        retryTimer.Dispose();
        if (Handle != IntPtr.Zero)
        {
            dungNhanThongBaoClipboard(Handle);
            DestroyHandle();
        }
    }

    private readonly record struct ClipboardIdentity(uint Sequence, nint Owner);
    private readonly record struct ClipboardSnapshot(ClipboardIdentity Identity, bool HasImage, bool HasText, string? Text);
    private enum ReplaceResult { Replaced, Cleared, Busy, Changed, Failed }

    [DllImport("user32.dll", EntryPoint = "AddClipboardFormatListener", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool batDauNhanThongBaoClipboard(nint window);

    [DllImport("user32.dll", EntryPoint = "RemoveClipboardFormatListener", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool dungNhanThongBaoClipboard(nint window);

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber", ExactSpelling = true)]
    private static extern uint laySoThuTuClipboard();

    [DllImport("user32.dll", EntryPoint = "GetClipboardOwner", ExactSpelling = true)]
    private static extern nint layCuaSoSoHuuClipboard();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", ExactSpelling = true)]
    private static extern uint layIdLuongCuaSo(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "OpenClipboard", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool moClipboard(nint window);

    [DllImport("user32.dll", EntryPoint = "CloseClipboard", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool dongClipboard();

    [DllImport("user32.dll", EntryPoint = "EmptyClipboard", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool lamTrongClipboard();

    [DllImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool coDinhDangClipboard(uint format);

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint dangKyDinhDangClipboard(string name);

    [DllImport("user32.dll", EntryPoint = "GetClipboardData", ExactSpelling = true)]
    private static extern nint layDuLieuClipboard(uint format);

    [DllImport("user32.dll", EntryPoint = "SetClipboardData", ExactSpelling = true)]
    private static extern nint datDuLieuClipboard(uint format, nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalAlloc", ExactSpelling = true)]
    private static extern nint capPhatBoNhoToanCuc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", EntryPoint = "GlobalFree", ExactSpelling = true)]
    private static extern nint giaiPhongBoNhoToanCuc(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", ExactSpelling = true)]
    private static extern nint khoaBoNhoToanCuc(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool moKhoaBoNhoToanCuc(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalSize", ExactSpelling = true)]
    private static extern nuint layKichThuocBoNhoToanCuc(nint memory);
}
