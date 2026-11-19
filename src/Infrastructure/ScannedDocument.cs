namespace RepoDlp.Infrastructure;

public enum DocumentReadStatus
{
    Success,
    NeedsOcr,
    ReadError
}

public sealed record ScannedDocument(string Path, string? Text, string? Sha256, DocumentReadStatus Status);

// Bản quét toàn phần thay thế chỉ mục. Đợt cập nhật áp dụng thay đổi và xóa cùng nhau.
public sealed record ScanBatch(
    bool IsSnapshot,
    IReadOnlyList<ScannedDocument> Upserts,
    IReadOnlyList<string> Removals);
