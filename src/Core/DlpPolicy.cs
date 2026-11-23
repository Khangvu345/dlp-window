namespace RepoDlp.Core;

public enum DlpAction { Allow, Block, Observe }
public enum AuditBehavior { Clipboard, Screenshot }
public enum RuleScope { Classification, Clipboard, Screenshot }
public enum RuleKind { Keyword, Regex, Label }
public enum ClassificationStatus { Classified, NeedsOcr, ReadError }

public sealed record PolicyRule(
    string Id,
    RuleScope Scope,
    int Priority,
    RuleKind Kind,
    string Pattern,
    string? Label,
    DlpAction? Action);

public sealed record PolicyDefinition(IReadOnlyList<PolicyRule> Rules);

public sealed record PolicyError(int? RuleIndex, string? RuleId, string Message);

public sealed record AgentSettings(string? ScanDirectory, bool ProtectionEnabled);

public sealed record DlpDecision(DlpAction Action, string? RuleId);

public sealed record KetQuaClipboard(DlpDecision QuyetDinh, string ChiTiet);

public sealed record AuditEvent(
    DateTimeOffset OccurredAtUtc,
    string Application,
    AuditBehavior Behavior,
    DlpAction Result,
    string? RuleId,
    string? Detail)
{
    public bool canHienCanhBao() =>
        !string.IsNullOrWhiteSpace(RuleId) && Result is DlpAction.Block or DlpAction.Observe;
}

public sealed record DocumentClassification(
    string Path,
    string? Sha256,
    string PolicyVersion,
    ClassificationStatus Status,
    string? Label,
    string? RuleId);
