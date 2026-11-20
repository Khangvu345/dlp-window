using Microsoft.Data.Sqlite;
using RepoDlp.Core;

namespace RepoDlp.Infrastructure;

internal sealed class DlpSqlite : IDisposable
{
    private readonly SqliteConnection connection;

    internal DlpSqlite(string path)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());

        try
        {
            connection.Open();
            using var command = taoLenh("""
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS documents (
                    path_key TEXT PRIMARY KEY,
                    scan_root TEXT NOT NULL,
                    path TEXT NOT NULL,
                    sha256 TEXT,
                    policy_version TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    label TEXT,
                    rule_id TEXT
                );
                CREATE INDEX IF NOT EXISTS documents_root_version
                    ON documents(scan_root, policy_version);
                CREATE TABLE IF NOT EXISTS audit_events (
                    id INTEGER PRIMARY KEY,
                    occurred_utc_ticks INTEGER NOT NULL,
                    application TEXT NOT NULL,
                    behavior INTEGER NOT NULL,
                    result INTEGER NOT NULL,
                    rule_id TEXT,
                    detail TEXT
                );
                CREATE INDEX IF NOT EXISTS audit_events_time ON audit_events(occurred_utc_ticks);
                """);
            command.ExecuteNonQuery();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal void capNhatTaiLieu(string directory, bool isSnapshot,
        IReadOnlyList<DocumentClassification> upserts, IReadOnlyList<string> removals)
    {
        using var transaction = connection.BeginTransaction();
        if (isSnapshot)
        {
            using var clear = taoLenh("DELETE FROM documents;", transaction);
            clear.ExecuteNonQuery();
        }
        else if (removals.Count > 0)
        {
            using var remove = taoLenh("DELETE FROM documents WHERE path_key = $key AND scan_root = $root;", transaction);
            remove.Parameters.AddWithValue("$root", taoKhoaDuongDan(directory));
            var key = remove.Parameters.Add("$key", SqliteType.Text);
            foreach (var path in removals)
            {
                key.Value = taoKhoaDuongDan(path);
                remove.ExecuteNonQuery();
            }
        }

        using var upsert = taoLenh("""
            INSERT INTO documents (path_key, scan_root, path, sha256, policy_version, status, label, rule_id)
            VALUES ($key, $root, $path, $hash, $version, $status, $label, $rule)
            ON CONFLICT (path_key) DO UPDATE SET
                scan_root = excluded.scan_root, path = excluded.path, sha256 = excluded.sha256,
                policy_version = excluded.policy_version, status = excluded.status,
                label = excluded.label, rule_id = excluded.rule_id;
            """, transaction);
        upsert.Parameters.AddWithValue("$root", taoKhoaDuongDan(directory));
        var pathKey = upsert.Parameters.Add("$key", SqliteType.Text);
        var fullPath = upsert.Parameters.Add("$path", SqliteType.Text);
        var hash = upsert.Parameters.Add("$hash", SqliteType.Text);
        var version = upsert.Parameters.Add("$version", SqliteType.Text);
        var status = upsert.Parameters.Add("$status", SqliteType.Integer);
        var label = upsert.Parameters.Add("$label", SqliteType.Text);
        var rule = upsert.Parameters.Add("$rule", SqliteType.Text);
        foreach (var document in upserts)
        {
            pathKey.Value = taoKhoaDuongDan(document.Path);
            fullPath.Value = document.Path;
            hash.Value = (object?)document.Sha256 ?? DBNull.Value;
            version.Value = document.PolicyVersion;
            status.Value = (int)document.Status;
            label.Value = (object?)document.Label ?? DBNull.Value;
            rule.Value = (object?)document.RuleId ?? DBNull.Value;
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    internal IReadOnlyList<DocumentClassification> layDanhSachTaiLieu(string directory, string policyVersion)
    {
        using var command = taoLenh("""
            SELECT path, sha256, policy_version, status, label, rule_id
            FROM documents WHERE scan_root = $root AND policy_version = $version
            ORDER BY path_key;
            """);
        command.Parameters.AddWithValue("$root", taoKhoaDuongDan(directory));
        command.Parameters.AddWithValue("$version", policyVersion);
        using var reader = command.ExecuteReader();
        var documents = new List<DocumentClassification>();
        while (reader.Read())
            documents.Add(docTaiLieu(reader));

        return documents;
    }

    internal DocumentClassification? timTaiLieu(string directory, string path, string policyVersion)
    {
        using var command = taoLenh("""
            SELECT path, sha256, policy_version, status, label, rule_id
            FROM documents WHERE path_key = $key AND scan_root = $root AND policy_version = $version;
            """);
        command.Parameters.AddWithValue("$key", taoKhoaDuongDan(path));
        command.Parameters.AddWithValue("$root", taoKhoaDuongDan(directory));
        command.Parameters.AddWithValue("$version", policyVersion);
        using var reader = command.ExecuteReader();
        return reader.Read() ? docTaiLieu(reader) : null;
    }

    internal void ghiSuKien(AuditEvent entry)
    {
        using var command = taoLenh("""
            INSERT INTO audit_events (occurred_utc_ticks, application, behavior, result, rule_id, detail)
            VALUES ($time, $app, $behavior, $result, $rule, $detail);
            """);
        command.Parameters.AddWithValue("$time", entry.OccurredAtUtc.UtcTicks);
        command.Parameters.AddWithValue("$app", entry.Application);
        command.Parameters.AddWithValue("$behavior", (int)entry.Behavior);
        command.Parameters.AddWithValue("$result", (int)entry.Result);
        command.Parameters.AddWithValue("$rule", (object?)entry.RuleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$detail", (object?)entry.Detail ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    internal IReadOnlyList<AuditEvent> laySuKien(DateTimeOffset? fromUtc,
        DateTimeOffset? untilUtc, AuditBehavior? behavior, int gioiHan)
    {
        using var command = taoLenh("""
            SELECT occurred_utc_ticks, application, behavior, result, rule_id, detail
            FROM audit_events
            WHERE ($from IS NULL OR occurred_utc_ticks >= $from)
              AND ($until IS NULL OR occurred_utc_ticks < $until)
              AND ($behavior IS NULL OR behavior = $behavior)
            ORDER BY occurred_utc_ticks DESC, id DESC LIMIT $gioihan;
            """);
        command.Parameters.AddWithValue("$gioihan", gioiHan);
        command.Parameters.AddWithValue("$from", (object?)fromUtc?.UtcTicks ?? DBNull.Value);
        command.Parameters.AddWithValue("$until", (object?)untilUtc?.UtcTicks ?? DBNull.Value);
        command.Parameters.AddWithValue("$behavior", behavior is null ? (object)DBNull.Value : (int)behavior);
        using var reader = command.ExecuteReader();
        var events = new List<AuditEvent>();
        while (reader.Read())
        {
            events.Add(new AuditEvent(
                new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero),
                reader.GetString(1),
                (AuditBehavior)reader.GetInt32(2),
                (DlpAction)reader.GetInt32(3),
                docChuoi(reader, 4),
                docChuoi(reader, 5)));
        }
        return events;
    }

    private static DocumentClassification docTaiLieu(SqliteDataReader reader) =>
        new(reader.GetString(0), docChuoi(reader, 1), reader.GetString(2),
            (ClassificationStatus)reader.GetInt32(3), docChuoi(reader, 4), docChuoi(reader, 5));

    private static string? docChuoi(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private SqliteCommand taoLenh(string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static string taoKhoaDuongDan(string path) =>
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    public void Dispose() => connection.Dispose();
}
