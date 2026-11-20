using System.Text.Json;
using RepoDlp.Core;

namespace RepoDlp.Infrastructure;

public sealed class DlpRuntime : IAsyncDisposable
{
    private static readonly TimeSpan ScanRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string settingsPath;
    private readonly string policyPath;
    private readonly DlpSqlite index;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim configurationGate = new(1, 1);
    private AgentSettings settings;
    private CompiledPolicy activePolicy;
    private string? scanError;
    private long scanGeneration;
    private long phienCauHinh;
    private bool scanReady;
    private CancellationTokenSource? scanCancellation;
    private Task scanning = Task.CompletedTask;
    private bool disposed;

    private DlpRuntime(string settingsPath, string policyPath, DlpSqlite index,
        AgentSettings settings, CompiledPolicy activePolicy)
    {
        this.settingsPath = settingsPath;
        this.policyPath = policyPath;
        this.index = index;
        this.settings = settings;
        this.activePolicy = activePolicy;
        khoiDongBoQuet();
    }

    public static DlpRuntime khoiDong(string dataDirectory)
    {
        dataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        var policyPath = Path.Combine(dataDirectory, "rules.json");
        var storagePath = Path.Combine(dataDirectory, "index.sqlite3");
        var settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(settingsPath))
                ?? throw new JsonException("Thiếu cấu hình agent.")
            : new AgentSettings(null, true);

        if (settings.ScanDirectory is not null)
            settings = settings with { ScanDirectory = chuanHoaThuMuc(settings.ScanDirectory) };

        var policy = docChinhSach(policyPath);
        return new DlpRuntime(settingsPath, policyPath, new DlpSqlite(storagePath), settings, policy);
    }

    private static CompiledPolicy docChinhSach(string path)
    {
        bool exists = File.Exists(path);
        string json;
        if (exists)
        {
            json = File.ReadAllText(path);
        }
        else
        {
            using var stream = typeof(DlpRuntime).Assembly
                .GetManifestResourceStream("RepoDlp.Infrastructure.DefaultRules.json")
                ?? throw new InvalidOperationException("Thiếu policy mẫu.");
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        var errors = CompiledPolicy.thuBienDich(CompiledPolicy.docJson(json), out var policy);
        if (policy is null)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(error =>
                error.RuleId is null ? error.Message : $"Quy tắc {error.RuleId}. {error.Message}")));

        if (!exists)
            ghiTapTin(path, json);
        return policy;
    }

    public AgentSettings Settings
    {
        get { lock (stateLock) return settings; }
    }

    public PolicyDefinition Policy
    {
        get { lock (stateLock) return activePolicy.Definition; }
    }

    public string PolicyVersion
    {
        get { lock (stateLock) return activePolicy.Version; }
    }

    public long PhienCauHinh => Interlocked.Read(ref phienCauHinh);

    public string? ScanError
    {
        get { lock (stateLock) return scanError; }
    }

    public async Task capNhatCauHinhAsync(AgentSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = chuanHoaThuMuc(settings.ScanDirectory);
        if (directory is not null && !Directory.Exists(directory))
            throw new DirectoryNotFoundException("Thư mục không tồn tại hoặc agent không có quyền truy cập.");

        var updated = settings with { ScanDirectory = directory };
        await configurationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool restart;
            lock (stateLock)
            {
                if (this.settings == updated)
                    return;
                restart = !string.Equals(this.settings.ScanDirectory, directory, PathComparison);
            }

            ghiTapTin(settingsPath, JsonSerializer.Serialize(updated));
            lock (stateLock)
            {
                this.settings = updated;
                Interlocked.Increment(ref phienCauHinh);
                if (restart)
                    datLaiTrangThaiQuet();
            }

            if (restart)
            {
                await dungBoQuetAsync().ConfigureAwait(false);
                khoiDongBoQuet();
            }
        }
        finally
        {
            configurationGate.Release();
        }
    }

    public IReadOnlyList<PolicyError> kiemTraChinhSach(PolicyDefinition? policy) =>
        CompiledPolicy.thuBienDich(policy, out _);

    public async Task<IReadOnlyList<PolicyError>> capNhatChinhSachAsync(
        PolicyDefinition? policy, CancellationToken ct = default)
    {
        var errors = CompiledPolicy.thuBienDich(policy, out var compiled);
        if (compiled is null)
            return errors;

        await configurationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (stateLock)
            {
                if (compiled.Version == activePolicy.Version)
                    return errors;
            }

            ghiTapTin(policyPath, compiled.ghiJson());
            lock (stateLock)
            {
                activePolicy = compiled;
                Interlocked.Increment(ref phienCauHinh);
                datLaiTrangThaiQuet();
            }
            await dungBoQuetAsync().ConfigureAwait(false);
            khoiDongBoQuet();
            return errors;
        }
        finally
        {
            configurationGate.Release();
        }
    }

    public IReadOnlyList<DocumentClassification> layDanhSachTaiLieu()
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return scanReady && settings.ScanDirectory is { } directory
                ? index.layDanhSachTaiLieu(directory, activePolicy.Version)
                : Array.Empty<DocumentClassification>();
        }
    }

    public DocumentClassification? timTaiLieu(string path)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return timTaiLieuTrongKhoa(path);
        }
    }

    public DlpDecision danhGiaClipboard(string? sourcePath, string text)
    {
        CompiledPolicy policy;
        string? label;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!settings.ProtectionEnabled)
                return new DlpDecision(DlpAction.Allow, null);

            policy = activePolicy;
            label = timTaiLieuTrongKhoa(sourcePath)?.Label;
        }
        return policy.danhGiaClipboard(label, text);
    }

    public DlpDecision danhGiaChupManHinh(string? sourcePath)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return settings.ProtectionEnabled
                ? activePolicy.danhGiaChupManHinh(timTaiLieuTrongKhoa(sourcePath)?.Label)
                : new DlpDecision(DlpAction.Allow, null);
        }
    }

    public DlpDecision? danhGiaChupManHinh(string? sourcePath, string expectedSha256)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!thuocThuMucBaoVe(sourcePath) || !activePolicy.Definition.Rules.Any(r => r.Scope == RuleScope.Screenshot))
                return new DlpDecision(DlpAction.Allow, null);

            var nhan = timNhanDungPhienBan(sourcePath, expectedSha256);
            return nhan is null ? null : activePolicy.danhGiaChupManHinh(nhan);
        }
    }

    public bool coTheSaoChepTaiLieu(string path, string sha256)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return !thuocThuMucBaoVe(path) ||
                !activePolicy.Definition.Rules.Any(r => r.Scope == RuleScope.Clipboard && r.Kind == RuleKind.Label) ||
                timNhanDungPhienBan(path, sha256) is not null;
        }
    }

    private string? timNhanDungPhienBan(string? path, string sha256)
    {
        var taiLieu = timTaiLieuTrongKhoa(path);
        return taiLieu is { Status: ClassificationStatus.Classified, Sha256: not null } &&
            string.Equals(taiLieu.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ? taiLieu.Label : null;
    }

    private bool thuocThuMucBaoVe(string? path)
    {
        if (!settings.ProtectionEnabled || settings.ScanDirectory is not { } thuMuc ||
            string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;

        string tuongDoi = Path.GetRelativePath(thuMuc, path);
        return !Path.IsPathRooted(tuongDoi) && tuongDoi is not ("." or "..") &&
            !tuongDoi.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public void ghiSuKien(AuditEvent entry)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            index.ghiSuKien(entry);
        }
    }

    public IReadOnlyList<AuditEvent> laySuKien(DateTimeOffset? fromUtc = null,
        DateTimeOffset? untilUtc = null, AuditBehavior? behavior = null, int gioiHan = -1)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return index.laySuKien(fromUtc, untilUtc, behavior, gioiHan);
        }
    }

    private DocumentClassification? timTaiLieuTrongKhoa(string? path)
    {
        if (!scanReady || settings.ScanDirectory is not { } directory || string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path))
            return null;

        try
        {
            return index.timTaiLieu(directory, Path.GetFullPath(path), activePolicy.Version);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void datLaiTrangThaiQuet()
    {
        scanGeneration++;
        scanReady = false;
        scanError = null;
    }

    private void khoiDongBoQuet()
    {
        if (settings.ScanDirectory is not { } directory)
            return;

        scanCancellation = new CancellationTokenSource();
        CancellationToken token = scanCancellation.Token;
        CompiledPolicy policy = activePolicy;
        long generation = scanGeneration;
        scanning = Task.Run(() => duyTriBoQuetAsync(directory, policy, generation, token));
    }

    private async Task dungBoQuetAsync()
    {
        if (scanCancellation is not { } cancellation)
            return;

        scanCancellation = null;
        using (cancellation)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            await scanning.ConfigureAwait(false);
        }
    }

    private async Task duyTriBoQuetAsync(string directory, CompiledPolicy policy,
        long generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await new DocumentScanner().theoDoiThuMucAsync(directory,
                    (batch, ct) => luuKetQuaQuet(directory, policy, generation, batch, ct), token)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                lock (stateLock)
                {
                    if (scanGeneration != generation || disposed)
                        return;

                    scanReady = false;
                    scanError = error.Message;
                }
            }

            try
            {
                await Task.Delay(ScanRetryDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private Task luuKetQuaQuet(string directory, CompiledPolicy policy, long generation,
        ScanBatch batch, CancellationToken token)
    {
        var classified = new List<DocumentClassification>(batch.Upserts.Count);
        foreach (var document in batch.Upserts)
        {
            token.ThrowIfCancellationRequested();
            classified.Add(phanLoaiTaiLieu(document, policy));
        }
        lock (stateLock)
        {
            if (scanGeneration != generation || token.IsCancellationRequested || disposed)
                return Task.CompletedTask;

            index.capNhatTaiLieu(directory, batch.IsSnapshot, classified, batch.Removals);
            scanReady = true;
            scanError = null;
        }
        return Task.CompletedTask;
    }

    private static DocumentClassification phanLoaiTaiLieu(ScannedDocument document, CompiledPolicy policy)
    {
        if (document.Status == DocumentReadStatus.Success && document.Text is { } text)
        {
            var result = policy.phanLoai(text);
            return new DocumentClassification(document.Path, document.Sha256, policy.Version,
                ClassificationStatus.Classified, result.Label, result.RuleId);
        }

        var status = document.Status == DocumentReadStatus.NeedsOcr
            ? ClassificationStatus.NeedsOcr : ClassificationStatus.ReadError;
        return new DocumentClassification(document.Path, document.Sha256, policy.Version, status, null, null);
    }

    private static string? chuanHoaThuMuc(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ghiTapTin(string path, string text)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, text);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await configurationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                if (disposed)
                    return;
                disposed = true;
            }

            try
            {
                await dungBoQuetAsync().ConfigureAwait(false);
            }
            finally
            {
                index.Dispose();
            }
        }
        finally
        {
            configurationGate.Release();
        }
    }
}
