using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RepoDlp.Agent.Windows;

internal sealed class ScreenshotShortcutMonitor : IDisposable
{
    private const int KeyboardHook = 13;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SysKeyDown = 0x0104;
    private const int SysKeyUp = 0x0105;
    private const int PrintScreen = 0x2C;
    private const int LetterS = 0x53;
    private const int Shift = 0x10;
    private const int Control = 0x11;
    private const int Alt = 0x12;
    private const int LeftWindows = 0x5B;
    private const int RightWindows = 0x5C;
    private const int AltDownFlag = 0x20;
    private const uint RootWindow = 2;
    private const uint ExcludeFromCapture = 0x11;
    private const uint CloakedWindow = 14;

    private readonly Func<nint> getDocumentWindow;
    private readonly Func<bool> shouldBlock;
    private readonly Action<string, bool> observe;
    private readonly Action releaseSuppressedKeys;
    private readonly KeyboardCallback callback;
    private nint hook;
    private TrangThaiPhim printScreenState;
    private TrangThaiPhim letterSState;

    public ScreenshotShortcutMonitor(Func<nint> getDocumentWindow, Func<bool> shouldBlock,
        Action<string, bool> observe, Action releaseSuppressedKeys)
    {
        this.getDocumentWindow = getDocumentWindow;
        this.shouldBlock = shouldBlock;
        this.observe = observe;
        this.releaseSuppressedKeys = releaseSuppressedKeys;
        callback = xuLyPhim;
        hook = datHookBanPhim(KeyboardHook, callback, layModule(null), 0);
    }

    public bool IsActive => hook != 0;

    public bool DangChoNha => printScreenState == TrangThaiPhim.DaChan || letterSState == TrangThaiPhim.DaChan;

    public static bool laCuaSoDangHien(nint window) =>
        laCuaSoHien(window) && !laCuaSoThuNho(window) &&
        (layThuocTinhDwm(window, CloakedWindow, out uint cloaked, sizeof(uint)) != 0 || cloaked == 0);

    public static bool laUngDungChup(string application) =>
        application.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase) ||
        application.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase) ||
        application.Equals("ScreenSketch", StringComparison.OrdinalIgnoreCase);

    public static bool tienCanhLaUngDungChup()
    {
        var foreground = layCuaSoTienCanh();
        if (foreground == 0 || layTienTrinhCuaSo(foreground, out uint processId) == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return laUngDungChup(process.ProcessName);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public static bool datBaoVeCuaSo(nint window, bool exclude, out string reason)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            reason = "Cần Windows 10 phiên bản 2004 trở lên để loại cửa sổ khỏi ảnh chụp";
            return false;
        }

        layTienTrinhCuaSo(window, out uint processId);
        if (processId != (uint)Environment.ProcessId || layCuaSoGoc(window, RootWindow) != window)
        {
            reason = "Không xác nhận được cửa sổ cấp cao do agent sở hữu";
            return false;
        }

        if (datCheDoHienThi(window, exclude ? ExcludeFromCapture : 0))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Windows báo lỗi {Marshal.GetLastWin32Error()} khi cập nhật bảo vệ cửa sổ";
        return false;
    }

    private nint xuLyPhim(int code, nint message, nint data)
    {
        if (code == 0)
        {
            int key = Marshal.ReadInt32(data);
            if (key is PrintScreen or LetterS)
            {
                bool wasSuppressed = DangChoNha;
                bool blocked = xuLyTrangThaiPhim(key, (int)message, data);
                if (wasSuppressed && !DangChoNha) releaseSuppressedKeys();
                if (blocked) return 1;
            }
        }

        return goiHookKeTiep(hook, code, message, data);
    }

    private bool xuLyTrangThaiPhim(int key, int message, nint data)
    {
        ref TrangThaiPhim state = ref (key == PrintScreen ? ref printScreenState : ref letterSState);
        if (message is KeyUp or SysKeyUp)
        {
            var previous = state;
            state = TrangThaiPhim.DaNha;
            // Một số cấu hình Windows chỉ chuyển sự kiện PrintScreen khi nhả phím.
            return previous == TrangThaiPhim.DaChan ||
                (previous == TrangThaiPhim.DaNha && key == PrintScreen && kiemTraPhimChup(key, data));
        }
        if (message is not (KeyDown or SysKeyDown)) return false;
        if (state == TrangThaiPhim.DaNha)
            state = kiemTraPhimChup(key, data) ? TrangThaiPhim.DaChan : TrangThaiPhim.DaChuyenTiep;
        return state == TrangThaiPhim.DaChan;
    }

    private bool kiemTraPhimChup(int key, nint data)
    {
        var window = getDocumentWindow();
        if (window == 0 || layCuaSoTienCanh() != window) return false;

        bool windowsDown = dangNhan(LeftWindows) || dangNhan(RightWindows);
        bool shiftDown = dangNhan(Shift);
        bool controlDown = dangNhan(Control);
        bool altDown = (Marshal.ReadInt32(data, 2 * sizeof(int)) & AltDownFlag) != 0 || dangNhan(Alt);
        if (key == LetterS && (!windowsDown || !shiftDown || controlDown || altDown))
            return false;

        string method = key == LetterS ? "Win+Shift+S" : string.Concat(
            windowsDown ? "Win+" : "", controlDown ? "Ctrl+" : "", altDown ? "Alt+" : "",
            shiftDown ? "Shift+" : "", "PrintScreen");
        bool blocked = shouldBlock();
        try
        {
            observe(method, blocked);
        }
        catch (Exception error)
        {
            Trace.WriteLine($"Không ghi nhận được phím chụp ({error.GetType().Name}).");
        }
        return blocked;
    }

    private static bool dangNhan(int key) => (layTrangThaiPhim(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (hook == 0) return;
        goHookBanPhim(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }

    private enum TrangThaiPhim { DaNha, DaChuyenTiep, DaChan }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint KeyboardCallback(int code, nint message, nint data);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint layModule(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint datHookBanPhim(int hookType, KeyboardCallback callback, nint module, uint threadId);

    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool goHookBanPhim(nint hook);

    [DllImport("user32.dll", EntryPoint = "CallNextHookEx")]
    private static extern nint goiHookKeTiep(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint layCuaSoTienCanh();

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short layTrangThaiPhim(int key);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint layTienTrinhCuaSo(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern nint layCuaSoGoc(nint window, uint flag);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool laCuaSoHien(nint window);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool laCuaSoThuNho(nint window);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int layThuocTinhDwm(nint window, uint attribute, out uint value, uint size);

    [DllImport("user32.dll", EntryPoint = "SetWindowDisplayAffinity", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool datCheDoHienThi(nint window, uint affinity);
}
