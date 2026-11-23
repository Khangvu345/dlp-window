using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;

namespace RepoDlp.Agent.Windows;

internal static class NguonNotepad
{
    private const uint CuaSoGoc = 2;
    private const uint TruyVanTienTrinh = 0x1000;
    private const int KhongCoGoiUngDung = 15700;
    private const int GioiHanNoiDung = 1_000_000;
    private static readonly TimeSpan ThoiGianCho = TimeSpan.FromMilliseconds(160);
    private static int dangTruyVan;

    internal static string? timDuongDan(nint chuClipboard, string noiDung)
    {
        if (chuClipboard == 0 || noiDung.Length is 0 or > GioiHanNoiDung ||
            layChuClipboard() != chuClipboard)
            return null;

        var foreground = layCuaSoTienCanh();
        if (foreground == 0 || layLuongCuaSo(foreground, out var foregroundPid) == 0 ||
            layLuongCuaSo(chuClipboard, out var ownerPid) == 0 || foregroundPid != ownerPid)
            return null;

        var sequence = laySoThuTuClipboard();
        if (Interlocked.CompareExchange(ref dangTruyVan, 1, 0) != 0)
            return null;

        // UI Automation có thể chờ provider. Chỉ một luồng nền được truy vấn tại một thời điểm.
        var query = Task.Run(() =>
        {
            try
            {
                return traCuuTab(chuClipboard, foreground, foregroundPid, sequence, noiDung);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Interlocked.Exchange(ref dangTruyVan, 0);
            }
        });

        try
        {
            return query.WaitAsync(ThoiGianCho).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static string? traCuuTab(nint owner, nint foreground, uint pid, uint sequence, string text)
    {
        if (!conDungClipboard(owner, foreground, sequence) || !laNotepad(pid) ||
            (layCuaSoToTien(owner, CuaSoGoc) != foreground && !laCuaSoOle(owner)))
            return null;

        var window = AutomationElement.FromHandle(foreground);
        var tabs = window.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, (int)pid),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
            new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true)));
        if (tabs.Count != 1)
            return null;

        var tab = tabs[0];
        var path = docDuongDanTab(tab);
        if (path is null)
            return null;

        var focus = AutomationElement.FocusedElement;
        if (focus is null || (uint)focus.Current.ProcessId != pid ||
            !laTrinhSoanThao(focus) || !thuocCuaSo(focus, window, foreground) ||
            !focus.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) ||
            !khopVungChon((TextPattern)pattern, text))
            return null;

        if (!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var tabPattern) ||
            !((SelectionItemPattern)tabPattern).Current.IsSelected ||
            !string.Equals(docDuongDanTab(tab), path, StringComparison.OrdinalIgnoreCase) ||
            AutomationElement.FocusedElement is not { } currentFocus || !Automation.Compare(focus, currentFocus) ||
            !conDungClipboard(owner, foreground, sequence))
            return null;

        return path;
    }

    private static bool conDungClipboard(nint owner, nint foreground, uint sequence) =>
        layCuaSoTienCanh() == foreground && layChuClipboard() == owner && laySoThuTuClipboard() == sequence;

    private static bool laNotepad(uint pid)
    {
        using var process = Process.GetProcessById((int)pid);
        if (!process.ProcessName.Equals("Notepad", StringComparison.OrdinalIgnoreCase))
            return false;

        using var handle = moTienTrinh(TruyVanTienTrinh, false, pid);
        if (handle.IsInvalid)
            return false;

        var family = new StringBuilder(128);
        uint size = (uint)family.Capacity;
        var status = layHoGoiUngDung(handle, ref size, family);
        if (status == 0)
            return family.ToString().Equals("Microsoft.WindowsNotepad_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase);
        if (status != KhongCoGoiUngDung)
            return false;

        var image = process.MainModule?.FileName;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.Equals(image, Path.Combine(windows, "notepad.exe"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(image, Path.Combine(windows, "System32", "notepad.exe"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(image, Path.Combine(windows, "SysWOW64", "notepad.exe"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool laCuaSoOle(nint owner)
    {
        var name = new StringBuilder(64);
        return layTenLopCuaSo(owner, name, name.Capacity) > 0 &&
            name.ToString().Equals("OleMainThreadWndClass", StringComparison.OrdinalIgnoreCase);
    }

    private static string? docDuongDanTab(AutomationElement tab)
    {
        var info = tab.Current;
        var candidates = new List<string> { info.Name, info.HelpText, info.ItemStatus };
        if (tab.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            candidates.Add(((ValuePattern)pattern).Current.Value);

        string? path = null;
        foreach (var value in candidates)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
                value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                continue;

            var candidate = Path.GetFullPath(value);
            if (path is not null && !path.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return null;
            path = candidate;
        }

        // Runtime đối chiếu đường dẫn với chỉ mục của thư mục đang bảo vệ.
        return path;
    }

    private static bool laTrinhSoanThao(AutomationElement focus)
    {
        var info = focus.Current;
        return info.ControlType == ControlType.Document ||
            (info.ControlType == ControlType.Edit &&
                info.ClassName.Equals("RichEditD2DPT", StringComparison.OrdinalIgnoreCase));
    }

    private static bool thuocCuaSo(AutomationElement focus, AutomationElement window, nint handle)
    {
        var nativeFocus = new IntPtr(focus.Current.NativeWindowHandle);
        if (nativeFocus != 0)
            return layCuaSoToTien(nativeFocus, CuaSoGoc) == handle;

        AutomationElement? element = focus;
        for (var depth = 0; element is not null && depth < 24; depth++)
        {
            if (Automation.Compare(element, window))
                return true;
            element = TreeWalker.RawViewWalker.GetParent(element);
        }

        return false;
    }

    private static bool khopVungChon(TextPattern pattern, string text)
    {
        var selection = pattern.GetSelection();
        if (selection.Length != 1)
            return false;

        // Đọc thêm một ký tự để nội dung dài hơn không khớp chỉ vì bị cắt ở giới hạn.
        var selected = selection[0].GetText((text.Length * 2) + 1);
        return string.Equals(chuanHoaXuongDong(selected), chuanHoaXuongDong(text), StringComparison.Ordinal);
    }

    private static string chuanHoaXuongDong(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", ExactSpelling = true)]
    private static extern nint layCuaSoTienCanh();

    [DllImport("user32.dll", EntryPoint = "GetClipboardOwner", ExactSpelling = true)]
    private static extern nint layChuClipboard();

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber", ExactSpelling = true)]
    private static extern uint laySoThuTuClipboard();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", ExactSpelling = true)]
    private static extern uint layLuongCuaSo(nint window, out uint pid);

    [DllImport("user32.dll", EntryPoint = "GetAncestor", ExactSpelling = true)]
    private static extern nint layCuaSoToTien(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int layTenLopCuaSo(nint window, StringBuilder name, int capacity);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", ExactSpelling = true)]
    private static extern SafeProcessHandle moTienTrinh(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", EntryPoint = "GetPackageFamilyName", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int layHoGoiUngDung(SafeProcessHandle process, ref uint length, StringBuilder name);
}
