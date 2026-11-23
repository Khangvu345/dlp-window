using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RepoDlp.Core;

public sealed class CompiledPolicy
{
    public const string DefaultLabel = "Thông thường";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private readonly IReadOnlyList<CompiledRule> orderedRules;

    public PolicyDefinition Definition { get; }
    public string Version { get; }

    private CompiledPolicy(PolicyRule[] rules, IReadOnlyList<CompiledRule> compiledRules)
    {
        Definition = new PolicyDefinition(Array.AsReadOnly(rules));
        orderedRules = compiledRules.OrderByDescending(rule => rule.Definition.Priority).ToArray();
        Version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ghiJson())));
    }

    public static PolicyDefinition docJson(string json) =>
        JsonSerializer.Deserialize<PolicyDefinition>(json, Json) ?? throw new JsonException("Thiếu policy.");

    public string ghiJson() => JsonSerializer.Serialize(Definition, Json);

    public static IReadOnlyList<PolicyError> thuBienDich(PolicyDefinition? definition, out CompiledPolicy? policy)
    {
        policy = null;
        if (definition?.Rules is null)
            return [new(null, null, "Policy phải có danh sách quy tắc.")];

        var rules = definition.Rules.ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<PolicyError>();
        var compiled = new List<CompiledRule>();
        for (int i = 0; i < rules.Length; i++)
        {
            PolicyRule? rule = rules[i];
            string? error = kiemTraQuyTac(rule, ids);
            if (error is not null || rule is null)
            {
                errors.Add(new(i, rule?.Id, error ?? "Thiếu quy tắc."));
                continue;
            }

            try
            {
                var regex = rule.Kind == RuleKind.Regex
                    ? new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout)
                    : null;
                compiled.Add(new CompiledRule(rule, regex));
            }
            catch (ArgumentException exception)
            {
                errors.Add(new(i, rule.Id, $"Regex không hợp lệ. {exception.Message}"));
            }
        }
        if (errors.Count == 0)
            policy = new CompiledPolicy(rules, compiled);

        return errors.AsReadOnly();
    }

    private static string? kiemTraQuyTac(PolicyRule? rule, HashSet<string> ids)
    {
        if (rule is null)
            return "Thiếu quy tắc.";
        if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id != rule.Id.Trim())
            return "ID quy tắc phải có giá trị và không có khoảng trắng ở đầu hoặc cuối.";
        if (!ids.Add(rule.Id))
            return "ID quy tắc đã được sử dụng.";
        if (!Enum.IsDefined(rule.Scope) || !Enum.IsDefined(rule.Kind))
            return "Phạm vi hoặc kiểu quy tắc không hợp lệ.";
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return "Nhập từ khóa, regex hoặc nhãn dùng để so khớp.";

        return rule.Scope switch
        {
            RuleScope.Classification when rule.Kind == RuleKind.Label || string.IsNullOrWhiteSpace(rule.Label)
                || rule.Action is not null => "Quy tắc phân loại cần từ khóa hoặc regex và một nhãn đích.",
            RuleScope.Clipboard when rule.Kind == RuleKind.Keyword =>
                "Quy tắc clipboard cần regex hoặc nhãn.",
            RuleScope.Screenshot when rule.Kind != RuleKind.Label =>
                "Quy tắc chụp màn hình cần một nhãn.",
            RuleScope.Clipboard or RuleScope.Screenshot when rule.Action is null || !Enum.IsDefined(rule.Action.Value) =>
                "Chọn kết quả cho quy tắc bảo vệ.",
            _ => null
        };
    }

    public (string Label, string? RuleId) phanLoai(string text)
    {
        var rule = timQuyTacKhop(RuleScope.Classification, text, null);
        return (rule?.Label ?? DefaultLabel, rule?.Id);
    }

    public DlpDecision danhGiaClipboard(string? sourceLabel, string text) =>
        danhGiaChiTietClipboard(sourceLabel, text).QuyetDinh;

    public KetQuaClipboard danhGiaChiTietClipboard(string? nhan, string noiDung)
    {
        int soRegex = 0;
        int soNhanBoQua = 0;
        int soQuaHan = 0;
        foreach (var muc in orderedRules)
        {
            var quyTac = muc.Definition;
            if (quyTac.Scope != RuleScope.Clipboard) continue;
            if (quyTac.Kind == RuleKind.Label && nhan is null)
            {
                soNhanBoQua++;
                continue;
            }

            if (quyTac.Kind == RuleKind.Regex) soRegex++;
            if (!muc.khop(noiDung, nhan, out bool quaHan))
            {
                if (quaHan) soQuaHan++;
                continue;
            }

            string dieuKien = quyTac.Kind == RuleKind.Label ? "nhãn nguồn" : "regex";
            return new(new DlpDecision(quyTac.Action!.Value, quyTac.Id),
                $"Khớp {dieuKien} · Ưu tiên {quyTac.Priority}" + moTaKiemTra());
        }

        return new(new DlpDecision(DlpAction.Allow, null), "Cho qua mặc định" + moTaKiemTra());

        string moTaKiemTra()
        {
            string chiTiet = $" · Đã xét {soRegex} regex";
            if (soNhanBoQua > 0) chiTiet += $" · Thiếu nhãn nguồn để xét {soNhanBoQua} quy tắc";
            if (soQuaHan > 0) chiTiet += $" · {soQuaHan} regex hết thời gian kiểm tra";
            return chiTiet;
        }
    }

    public DlpDecision danhGiaChupManHinh(string? sourceLabel) =>
        danhGia(RuleScope.Screenshot, null, sourceLabel);

    private DlpDecision danhGia(RuleScope scope, string? text, string? label)
    {
        var rule = timQuyTacKhop(scope, text, label);
        return new DlpDecision(rule?.Action ?? DlpAction.Allow, rule?.Id);
    }

    private PolicyRule? timQuyTacKhop(RuleScope scope, string? text, string? label)
    {
        foreach (var rule in orderedRules)
            if (rule.Definition.Scope == scope && rule.khop(text, label, out _))
                return rule.Definition;
        return null;
    }

    private sealed record CompiledRule(PolicyRule Definition, Regex? Regex)
    {
        public bool khop(string? text, string? label, out bool quaHan)
        {
            quaHan = false;
            return Definition.Kind switch
            {
                RuleKind.Label => string.Equals(label, Definition.Pattern, StringComparison.OrdinalIgnoreCase),
                RuleKind.Keyword => text?.Contains(Definition.Pattern, StringComparison.OrdinalIgnoreCase) == true,
                RuleKind.Regex => khopRegex(text, out quaHan),
                _ => false
            };
        }

        private bool khopRegex(string? text, out bool quaHan)
        {
            quaHan = false;
            try
            {
                return text is not null && Regex!.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                quaHan = true;
                return false;
            }
        }
    }
}
