using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class ClipboardMonitor : NativeWindow, IDisposable
{
    private const int ClipboardUpdate = 0x031D;
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;
    private const int MaxRetries = 5;
    private const string DemoApplication = "Repo DLP";
    private const string UnknownApplication = "Không xác định";

    private readonly Func<string?, string, DlpDecision> evaluate;
    private readonly Action<AuditEvent> addAuditEvent;
    private readonly System.Windows.Forms.Timer retryTimer = new() { Interval = 35 };
    private CopyOrigin? copyOrigin;
    private uint handledSequence;
    private uint retrySequence;
    private int retries;

    public ClipboardMonitor(
        Func<string?, string, DlpDecision> evaluate,
        Action<AuditEvent> addAuditEvent)
    {
        this.evaluate = evaluate;
        this.addAuditEvent = addAuditEvent;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // Chỉ nhận thông báo, không tạo cửa sổ hiển thị.
        handledSequence = laySoThuTuClipboard();
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
        var byteCount = checked(text.Length * sizeof(char));
        var memory = capPhatBoNhoToanCuc(MoveableMemory, (nuint)byteCount + sizeof(char));
        if (memory == IntPtr.Zero)
            return false;

        try
        {
            var destination = khoaBoNhoToanCuc(memory);
            if (destination == IntPtr.Zero)
                return false;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, destination, text.Length);
                Marshal.WriteInt16(destination, byteCount, 0);
            }
            finally
            {
                moKhoaBoNhoToanCuc(memory);
            }

            if (!moClipboard(Handle))
                return false;

            try
            {
                if (!lamTrongClipboard())
                    return false;
                if (datDuLieuClipboard(UnicodeText, memory) == IntPtr.Zero)
                {
                    handledSequence = laySoThuTuClipboard();
                    return false;
                }

                // Clipboard nhận quyền quản lý vùng nhớ khi ghi thành công.
                memory = IntPtr.Zero;
                copyOrigin = new CopyOrigin(laySoThuTuClipboard(), originPath);
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
            (!retryTimer.Enabled || laySoThuTuClipboard() != retrySequence))
            xuLyThayDoiClipboard();

        base.WndProc(ref message);
    }

    private void xuLyThayDoiClipboard()
    {
        retryTimer.Stop();
        var sequence = laySoThuTuClipboard();
        if (sequence == handledSequence)
            return;

        batDauPhienClipboard(sequence);

        try
        {
            if (!thuDocClipboard(out var snapshot))
            {
                if (henThuLai())
                    return;

                handledSequence = sequence;
                ghiNhatKy(layTenUngDung(layCuaSoSoHuuClipboard()), DlpAction.Observe, null, "source-unknown read-failed");
                return;
            }

            sequence = snapshot.Sequence;
            if (sequence == handledSequence)
                return;

            batDauPhienClipboard(sequence);
            var sourcePath = snapshot.Owner == Handle && copyOrigin is { } origin && origin.Sequence == sequence
                ? origin.Path
                : null;
            var application = sourcePath is null ? layTenUngDung(snapshot.Owner) : DemoApplication;
            var source = sourcePath is null ? "source-unknown" : "source-known";

            if (snapshot.Text is null)
            {
                handledSequence = sequence;
                ghiNhatKy(application, DlpAction.Observe, null, source + " no-text");
                return;
            }

            var decision = evaluate(sourcePath, snapshot.Text);
            if (decision.Action != DlpAction.Block)
            {
                handledSequence = sequence;
                ghiNhatKy(application, decision.Action, decision.RuleId, source);
                return;
            }

            var result = thuXoaClipboard(sequence);
            switch (result)
            {
                case ClearResult.Cleared:
                    ghiNhatKy(application, DlpAction.Block, decision.RuleId, source);
                    break;
                case ClearResult.Busy when henThuLai():
                    break;
                case ClearResult.Changed:
                    handledSequence = sequence;
                    ghiNhatKy(application, DlpAction.Observe, decision.RuleId, source + " clipboard-changed");
                    break;
                default:
                    handledSequence = sequence;
                    ghiNhatKy(application, DlpAction.Observe, decision.RuleId, source + " clear-failed");
                    break;
            }
        }
        catch (Exception error)
        {
            if (laySoThuTuClipboard() == sequence)
                handledSequence = sequence;
            Trace.TraceError("Repo DLP clipboard: {0}", error.GetType().Name);
        }
    }

    private void batDauPhienClipboard(uint sequence)
    {
        if (retrySequence == sequence)
            return;

        retrySequence = sequence;
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
            if (!coDinhDangClipboard(UnicodeText))
            {
                snapshot = new ClipboardSnapshot(laySoThuTuClipboard(), layCuaSoSoHuuClipboard(), null);
                return true;
            }

            var data = layDuLieuClipboard(UnicodeText);
            if (data == IntPtr.Zero)
                return false;

            var characterCount = layKichThuocBoNhoToanCuc(data) / sizeof(char);
            if (characterCount == 0 || characterCount > (nuint)int.MaxValue)
                return false;

            var pointer = khoaBoNhoToanCuc(data);
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                var text = Marshal.PtrToStringUni(pointer, (int)characterCount);
                var terminator = text.IndexOf('\0');
                if (terminator < 0)
                    return false;

                snapshot = new ClipboardSnapshot(
                    laySoThuTuClipboard(), layCuaSoSoHuuClipboard(), text[..terminator]);
                return true;
            }
            finally
            {
                moKhoaBoNhoToanCuc(data);
            }
        }
        finally
        {
            dongClipboard();
        }
    }

    private ClearResult thuXoaClipboard(uint sequence)
    {
        if (!moClipboard(Handle))
            return ClearResult.Busy;

        try
        {
            if (laySoThuTuClipboard() != sequence)
                return ClearResult.Changed;
            if (!lamTrongClipboard())
                return ClearResult.Failed;

            // Ghi nhận số thứ tự khi còn giữ khóa để mỗi lần xóa chỉ phát sinh một log.
            handledSequence = laySoThuTuClipboard();
            copyOrigin = null;
            return ClearResult.Cleared;
        }
        finally
        {
            dongClipboard();
        }
    }

    private void ghiNhatKy(string application, DlpAction outcome, string? ruleId, string detail)
    {
        addAuditEvent(new AuditEvent(
            DateTimeOffset.UtcNow, application, AuditBehavior.Clipboard, outcome, ruleId, detail));
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

    private sealed record CopyOrigin(uint Sequence, string Path);
    private readonly record struct ClipboardSnapshot(uint Sequence, nint Owner, string? Text);
    private enum ClearResult { Cleared, Busy, Changed, Failed }

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
