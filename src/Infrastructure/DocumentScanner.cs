using System.Threading.Channels;

namespace RepoDlp.Infrastructure;

public sealed class DocumentScanner
{
    private const int EventCapacity = 2048;
    private static readonly TimeSpan EventBatchDelay = TimeSpan.FromMilliseconds(175);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(1);
    internal static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task theoDoiThuMucAsync(
        string directory,
        Func<ScanBatch, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var events = Channel.CreateBounded<FileChange>(new BoundedChannelOptions(EventCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var known = new Dictionary<string, (string? Sha256, DocumentReadStatus Status)>(PathComparer);
        int needsSnapshot = 0;
        int needsRestart = 0;

        void yeuCauQuetLai(bool restartWatcher = false)
        {
            if (restartWatcher)
                Interlocked.Exchange(ref needsRestart, 1);
            if (Interlocked.Exchange(ref needsSnapshot, 1) == 0)
                events.Writer.TryWrite(default);
        }

        void ghiNhanThayDoi(string path, bool structural)
        {
            if ((structural || DocumentReader.hoTroDinhDang(path)) && !events.Writer.TryWrite(new(path, structural)))
                yeuCauQuetLai();
        }

        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
        };
        watcher.Changed += (_, change) => ghiNhanThayDoi(change.FullPath, structural: false);
        watcher.Created += (_, change) => ghiNhanThayDoi(change.FullPath, structural: true);
        watcher.Deleted += (_, change) => ghiNhanThayDoi(change.FullPath, structural: true);
        watcher.Renamed += (_, change) =>
        {
            if (PathComparer.Equals(change.OldFullPath, change.FullPath))
            {
                yeuCauQuetLai();
                return;
            }
            ghiNhanThayDoi(change.OldFullPath, structural: true);
            ghiNhanThayDoi(change.FullPath, structural: true);
        };
        watcher.Error += (_, error) => yeuCauQuetLai(error.GetException() is not InternalBufferOverflowException);

        // Bắt sự kiện trước khi quét để giữ những thay đổi xảy ra trong lúc khởi động.
        watcher.EnableRaisingEvents = true;
        await using var reconcile = new Timer(_ => yeuCauQuetLai(), null, ReconcileInterval, ReconcileInterval);
        await quetToanBoAsync();
        while (await events.Reader.WaitToReadAsync(cancellationToken))
        {
            await Task.Delay(EventBatchDelay, cancellationToken);
            var pending = new Dictionary<string, bool>(PathComparer);
            for (int count = 0; count < EventCapacity && events.Reader.TryRead(out var change); count++)
            {
                if (change.Path is { } path)
                    pending[path] = change.Structural || pending.GetValueOrDefault(path);
            }

            if (Interlocked.Exchange(ref needsSnapshot, 0) != 0 ||
                pending.Any(item => item.Value && thayDoiThuMuc(item.Key, known.Keys)))
            {
                if (Interlocked.Exchange(ref needsRestart, 0) != 0)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.EnableRaisingEvents = true;
                }
                await quetToanBoAsync();
                continue;
            }

            var upserts = new List<ScannedDocument>();
            var removals = new List<string>();
            foreach (string path in pending.Keys.Where(DocumentReader.hoTroDinhDang))
            {
                var document = await DocumentReader.docTepAsync(root, path, cancellationToken);
                if (document is null)
                {
                    if (known.Remove(path))
                        removals.Add(path);
                    continue;
                }

                var latest = (document.Sha256, document.Status);
                if (known.TryGetValue(path, out var previous) && previous == latest)
                    continue;

                known[path] = latest;
                upserts.Add(document);
            }

            if (upserts.Count > 0 || removals.Count > 0)
                await apply(new ScanBatch(false, upserts, removals), cancellationToken);
        }

        async Task quetToanBoAsync()
        {
            var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });
            var upserts = new List<ScannedDocument>();
            known.Clear();
            foreach (string path in files.Where(DocumentReader.hoTroDinhDang))
            {
                var document = await DocumentReader.docTepAsync(root, path, cancellationToken);
                if (document is null)
                    continue;

                known[path] = (document.Sha256, document.Status);
                upserts.Add(document);
            }
            await apply(new ScanBatch(true, upserts, []), cancellationToken);
        }
    }

    private static bool thayDoiThuMuc(string path, IEnumerable<string> indexedPaths)
    {
        if (Directory.Exists(path))
            return true;

        string prefix = Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        return indexedPaths.Any(indexed => indexed.StartsWith(prefix, PathComparison));
    }

    private readonly record struct FileChange(string? Path, bool Structural);
}
