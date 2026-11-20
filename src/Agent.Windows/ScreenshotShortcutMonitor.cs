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
    private const uint RootWindow = 2;
    private const uint ExcludeFromCapture = 0x11;

    private readonly nint window;
    private readonly Action<string> observe;
    private readonly KeyboardCallback callback;
    private nint hook;
    private bool printScreenDown;
    private bool letterSDown;

    public ScreenshotShortcutMonitor(nint window, Action<string> observe)
    {
        this.window = window;
        this.observe = observe;
        callback = xuLyPhim;
        hook = datHookBanPhim(KeyboardHook, callback, layModule(null), 0);
    }

    public bool IsActive => hook != 0;

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
        try
        {
            if (code >= 0)
            {
                int key = Marshal.ReadInt32(data);
                if (key is PrintScreen or LetterS) ghiNhanNeuLaPhimChup(key, (int)message);
            }
        }
        catch (Exception error)
        {
            Trace.WriteLine($"Không ghi nhận được phím chụp ({error.GetType().Name}).");
        }

        return goiHookKeTiep(hook, code, message, data);
    }

    private void ghiNhanNeuLaPhimChup(int key, int message)
    {
        ref bool pressed = ref (key == PrintScreen ? ref printScreenDown : ref letterSDown);
        if (message is KeyUp or SysKeyUp)
        {
            pressed = false;
            return;
        }
        if (message is not (KeyDown or SysKeyDown) || pressed) return;
        pressed = true;

        if (layCuaSoTienCanh() != window || dangNhan(Control) || dangNhan(Alt)) return;
        bool windowsDown = dangNhan(LeftWindows) || dangNhan(RightWindows);
        bool shiftDown = dangNhan(Shift);
        if (key == PrintScreen && !windowsDown && !shiftDown) observe("PrintScreen");
        if (key == LetterS && windowsDown && shiftDown) observe("Win+Shift+S");
    }

    private static bool dangNhan(int key) => (layTrangThaiPhim(key) & 0x8000) != 0;

    public void Dispose()
    {
        if (hook == 0) return;
        goHookBanPhim(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }

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

    [DllImport("user32.dll", EntryPoint = "SetWindowDisplayAffinity", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool datCheDoHienThi(nint window, uint affinity);
}
