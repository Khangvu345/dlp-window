using System.Runtime.InteropServices;
using System.Threading.Channels;
using RepoDlp.Core;

namespace RepoDlp.Agent.Windows;

internal sealed class CanhBaoViPham : IDisposable
{
    private const uint BieuTuongCanhBao = 0x30;
    private const uint DuaLenTruoc = 0x10000;
    private const uint NamTrenCung = 0x40000;
    private readonly Channel<(string TieuDe, string NoiDung)> choHien = Channel.CreateUnbounded<(string, string)>(
        new UnboundedChannelOptions { SingleReader = true });

    public CanhBaoViPham()
    {
        _ = Task.Run(hienCanhBaoAsync);
    }

    public void thongBao(AuditEvent suKien)
    {
        if (suKien.Result == DlpAction.Allow) return;
        bool daChan = suKien.Result == DlpAction.Block;
        string hanhVi = suKien.Behavior == AuditBehavior.Clipboard ? "Sao chép" : "Chụp màn hình";
        string thongDiep = daChan
            ? "Thao tác đã bị chặn theo chính sách. Vui lòng liên hệ quản trị viên (admin) nếu cần hỗ trợ."
            : suKien.RuleId is null
                ? "Hệ thống đã ghi nhận thao tác này để kiểm tra."
                : "Hệ thống đã ghi nhận vi phạm này trong nhật ký.";
        choHien.Writer.TryWrite((daChan ? "Repo DLP · Đã chặn" : "Repo DLP · Chỉ quan sát",
            $"{hanhVi} · {suKien.Application}\n\n{thongDiep}"));
    }

    public void thuCanhBao() => choHien.Writer.TryWrite(("Repo DLP · Thử cảnh báo",
        "Đây là thông báo thử trên máy đang chạy agent."));

    private async Task hienCanhBaoAsync()
    {
        // Chờ người dùng đóng từng hộp thoại trên luồng riêng để agent tiếp tục xử lý clipboard.
        await foreach (var muc in choHien.Reader.ReadAllAsync().ConfigureAwait(false))
            hienHopThoai(0, muc.NoiDung, muc.TieuDe, BieuTuongCanhBao | DuaLenTruoc | NamTrenCung);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int hienHopThoai(nint chuSoHuu, string noiDung, string tieuDe, uint tuyChon);

    public void Dispose() => choHien.Writer.TryComplete();
}
