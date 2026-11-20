namespace RepoDlp.Agent.Windows;

internal sealed class BoChonThuMuc : Form
{
    private readonly Func<string?> layThuMucHienTai;
    private bool dangMo;

    public BoChonThuMuc(Func<string?> layThuMucHienTai)
    {
        this.layThuMucHienTai = layThuMucHienTai;
        Text = "Repo DLP · Chọn thư mục quét";
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        Size = new Size(320, 80);
        // Tạo handle trên luồng STA dùng chung với biểu tượng khay hệ thống.
        _ = Handle;
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
            // Cửa sổ sở hữu giúp người dùng tìm được hộp thoại từ thanh tác vụ khi trình duyệt đang ở trước.
            Show();
            Activate();
            using var hopThoai = new FolderBrowserDialog
            {
                Description = "Chọn thư mục cần quét",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = layThuMucHienTai() ?? ""
            };
            return hopThoai.ShowDialog(this) == DialogResult.OK ? hopThoai.SelectedPath : null;
        }
        finally
        {
            Hide();
            dangMo = false;
        }
    }
}
