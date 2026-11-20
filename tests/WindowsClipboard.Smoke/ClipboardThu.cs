using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RepoDlp.WindowsClipboard.Smoke;

internal static class ClipboardThu
{
    private const uint DinhDangBitmap = 2;
    private const uint DinhDangUnicode = 13;
    private const uint BoNhoDiDong = 0x0002;
    private static readonly uint DinhDangPng = dangKyDinhDangClipboard("PNG");

    public static bool thuDatAnhVaText(nint cuaSo, string? vanBan, Bitmap? bitmap, byte[]? png)
    {
        if (!moClipboard(cuaSo))
            return false;

        try
        {
            if (!xoaClipboard())
                throw baoLoi("Xóa clipboard thử");
            if (vanBan is not null)
                datByte(DinhDangUnicode, Encoding.Unicode.GetBytes(vanBan + '\0'));
            if (bitmap is not null)
                datBitmap(bitmap);
            if (png is not null)
                datByte(DinhDangPng, png);
            return true;
        }
        finally
        {
            dongClipboard();
        }
    }

    public static bool coBitmap() => coDinhDang(DinhDangBitmap);

    public static bool coPng() => coDinhDang(DinhDangPng);

    public static bool anhVaPngConNguyen(nint cuaSo, Bitmap bitmap, byte[] png)
    {
        if (!moClipboard(cuaSo))
            return false;

        try
        {
            nint hBitmap = layDuLieuClipboard(DinhDangBitmap);
            if (hBitmap == IntPtr.Zero)
                return false;
            using var daDoc = Image.FromHbitmap(hBitmap);
            if (daDoc.Size != bitmap.Size)
                return false;

            for (int hang = 0; hang < bitmap.Height; hang++)
                for (int cot = 0; cot < bitmap.Width; cot++)
                    if (daDoc.GetPixel(cot, hang).ToArgb() != bitmap.GetPixel(cot, hang).ToArgb())
                        return false;

            return kiemTraPng(png);
        }
        finally
        {
            dongClipboard();
        }
    }

    private static bool kiemTraPng(byte[] mongDoi)
    {
        nint boNho = layDuLieuClipboard(DinhDangPng);
        if (boNho == IntPtr.Zero || layKichThuoc(boNho) < (nuint)mongDoi.Length)
            return false;
        nint conTro = khoaBoNho(boNho);
        if (conTro == IntPtr.Zero)
            return false;

        try
        {
            var duLieu = new byte[mongDoi.Length];
            Marshal.Copy(conTro, duLieu, 0, duLieu.Length);
            return duLieu.AsSpan().SequenceEqual(mongDoi);
        }
        finally
        {
            moKhoaBoNho(boNho);
        }
    }

    private static void datBitmap(Bitmap bitmap)
    {
        nint boNho = bitmap.GetHbitmap();
        if (datDuLieuClipboard(DinhDangBitmap, boNho) == IntPtr.Zero)
        {
            var loi = baoLoi("Ghi bitmap thử");
            xoaDoiTuong(boNho);
            throw loi;
        }
    }

    private static void datByte(uint dinhDang, byte[] duLieu)
    {
        nint boNho = capPhatBoNho(BoNhoDiDong, (nuint)duLieu.Length);
        if (boNho == IntPtr.Zero)
            throw baoLoi("Cấp phát giá trị clipboard thử");

        try
        {
            nint conTro = khoaBoNho(boNho);
            if (conTro == IntPtr.Zero)
                throw baoLoi("Khóa giá trị clipboard thử");

            try
            {
                Marshal.Copy(duLieu, 0, conTro, duLieu.Length);
            }
            finally
            {
                moKhoaBoNho(boNho);
            }

            if (datDuLieuClipboard(dinhDang, boNho) == IntPtr.Zero)
                throw baoLoi("Ghi dữ liệu clipboard thử");

            // Windows sở hữu vùng nhớ sau khi ghi thành công.
            boNho = IntPtr.Zero;
        }
        finally
        {
            if (boNho != IntPtr.Zero)
                giaiPhongBoNho(boNho);
        }
    }

    private static Win32Exception baoLoi(string thongBao) => new(Marshal.GetLastWin32Error(), thongBao);

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint dangKyDinhDangClipboard(string ten);

    [DllImport("user32.dll", EntryPoint = "OpenClipboard", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool moClipboard(nint cuaSo);

    [DllImport("user32.dll", EntryPoint = "CloseClipboard", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool dongClipboard();

    [DllImport("user32.dll", EntryPoint = "EmptyClipboard", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool xoaClipboard();

    [DllImport("user32.dll", EntryPoint = "SetClipboardData", ExactSpelling = true, SetLastError = true)]
    private static extern nint datDuLieuClipboard(uint dinhDang, nint duLieu);

    [DllImport("user32.dll", EntryPoint = "GetClipboardData", ExactSpelling = true)]
    private static extern nint layDuLieuClipboard(uint dinhDang);

    [DllImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool coDinhDang(uint dinhDang);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool xoaDoiTuong(nint doiTuong);

    [DllImport("kernel32.dll", EntryPoint = "GlobalAlloc", ExactSpelling = true, SetLastError = true)]
    private static extern nint capPhatBoNho(uint co, nuint kichThuoc);

    [DllImport("kernel32.dll", EntryPoint = "GlobalFree", ExactSpelling = true)]
    private static extern nint giaiPhongBoNho(nint boNho);

    [DllImport("kernel32.dll", EntryPoint = "GlobalSize", ExactSpelling = true)]
    private static extern nuint layKichThuoc(nint boNho);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", ExactSpelling = true, SetLastError = true)]
    private static extern nint khoaBoNho(nint boNho);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool moKhoaBoNho(nint boNho);
}
