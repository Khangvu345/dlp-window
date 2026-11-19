using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace RepoDlp.Infrastructure;

internal static class DocumentReader
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300)];

    internal static bool hoTroDinhDang(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".docx" or ".pdf";

    internal static async Task<ScannedDocument?> docTepAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (namNgoaiPhamVi(root, path))
                    return null;

                // Giữ nguyên tệp trên Windows trong lúc băm và trích xuất.
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                stream.Position = 0;
                return await trichXuatAsync(path, stream, hash, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new ScannedDocument(path, null, null, DocumentReadStatus.ReadError);
            }
        }
    }

    private static bool namNgoaiPhamVi(string root, string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            return true;

        for (string? parent = Path.GetDirectoryName(path); parent is not null &&
            !DocumentScanner.PathComparer.Equals(parent, root); parent = Path.GetDirectoryName(parent))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                return true;
        }
        return false;
    }

    private static async Task<ScannedDocument> trichXuatAsync(
        string path,
        Stream stream,
        string hash,
        CancellationToken cancellationToken)
    {
        try
        {
            string? text = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".txt" => await docVanBanAsync(stream, cancellationToken),
                ".docx" => docDocx(stream, cancellationToken),
                ".pdf" => docPdf(stream, cancellationToken),
                _ => throw new NotSupportedException()
            };
            return new ScannedDocument(path, text, hash,
                text is null ? DocumentReadStatus.NeedsOcr : DocumentReadStatus.Success);
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            // Các thư viện đọc tài liệu có thể báo nhiều kiểu lỗi cho cùng một tệp hỏng.
            return new ScannedDocument(path, null, hash, DocumentReadStatus.ReadError);
        }
    }

    private static async Task<string> docVanBanAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string docDocx(Stream stream, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body
            ?? throw new InvalidDataException("Tệp DOCX thiếu phần nội dung.");
        var output = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var element in paragraph.Descendants())
            {
                if (element is not (Text or TabChar or Break or CarriageReturn) ||
                    element.Ancestors<Paragraph>().FirstOrDefault() != paragraph)
                    continue;

                switch (element)
                {
                    case Text fragment:
                        output.Append(fragment.Text);
                        break;
                    case TabChar:
                        output.Append('\t');
                        break;
                    default:
                        output.AppendLine();
                        break;
                }
            }
            output.AppendLine();
        }
        return output.ToString();
    }

    private static string? docPdf(Stream stream, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(stream);
        var output = new StringBuilder();
        bool hasImages = false;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text))
                output.AppendLine(text);
            else if (output.Length == 0 && !hasImages)
                hasImages = page.GetImages().Any();
        }
        return output.Length == 0 && hasImages ? null : output.ToString();
    }
}
