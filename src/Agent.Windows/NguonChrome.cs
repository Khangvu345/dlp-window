using System.Globalization;
using System.Text;

namespace RepoDlp.Agent.Windows;

internal static class NguonChrome
{
    internal const int GioiHanTieuDe = 8192;

    internal static string? layDuongDan(ReadOnlySpan<byte> duLieu)
    {
        // Offset của CF_HTML tính theo byte. Chỉ tin SourceURL trong phần tiêu đề.
        if (!duLieu.StartsWith("Version:"u8)) return null;
        var dau = "StartHTML:"u8;
        int viTri = duLieu.IndexOf(dau);
        if (viTri <= 0 || duLieu[viTri - 1] != '\n') return null;
        var so = duLieu[(viTri + dau.Length)..];
        int xuongDong = so.IndexOf((byte)'\n');
        if (xuongDong < 1 || !int.TryParse(Encoding.ASCII.GetString(so[..xuongDong]).Trim(),
                NumberStyles.None, CultureInfo.InvariantCulture, out int kichThuoc) ||
            kichThuoc <= viTri + dau.Length + xuongDong || kichThuoc > duLieu.Length)
            return null;

        if (duLieu[..kichThuoc].IndexOfAny((byte)'<', (byte)0) >= 0) return null;
        string? diaChi = null;
        foreach (var dong in Encoding.UTF8.GetString(duLieu[..kichThuoc]).Split('\n'))
        {
            if (!dong.StartsWith("SourceURL:", StringComparison.OrdinalIgnoreCase)) continue;
            if (diaChi is not null) return null;
            diaChi = dong["SourceURL:".Length..].TrimEnd('\r');
        }
        if (diaChi is null || !diaChi.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(diaChi, UriKind.Absolute, out var uri) || !uri.IsFile ||
            uri.Host.Length != 0 || uri.IsUnc)
            return null;

        string path = uri.LocalPath;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            (path[2] is '\\' or '/') && path.IndexOf('\0') < 0 ? path : null;
    }
}
