namespace RepoDlp.Agent.Windows;

internal sealed class BoChonThuMuc : Control
{
    private readonly Func<string?> layThuMucHienTai;
    private bool dangMo;

    public BoChonThuMuc(Func<string?> layThuMucHienTai)
    {
        this.layThuMucHienTai = layThuMucHienTai;
        // Tạo handle trên luồng STA trước khi web nhận yêu cầu.
        CreateHandle();
    }

    public Task<string?> chonThuMucAsync(CancellationToken cancellationToken) =>
        InvokeAsync(hienThiHopThoai, cancellationToken);

    private string? hienThiHopThoai()
    {
        if (dangMo)
            return null;

        dangMo = true;
        try
        {
            using var hopThoai = new FolderBrowserDialog
            {
                Description = "Chọn thư mục cần quét",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = layThuMucHienTai() ?? ""
            };
            return hopThoai.ShowDialog() == DialogResult.OK ? hopThoai.SelectedPath : null;
        }
        finally
        {
            dangMo = false;
        }
    }
}
