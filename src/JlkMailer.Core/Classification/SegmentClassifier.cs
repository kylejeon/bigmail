using System.Text.RegularExpressions;
using JlkMailer.Core.Models;

namespace JlkMailer.Core.Classification;

/// <summary>
/// 진료과 원본값(실측 112종) → 세그먼트. 설계 §07.
/// first-match-wins 이며 규칙 순서에 전적으로 의존한다. 순수 함수이므로 단위 테스트로 고정한다.
/// </summary>
public sealed class SegmentClassifier
{
    private readonly (SegmentRule Rule, Regex Regex)[] _rules;

    public SegmentClassifier(IEnumerable<SegmentRule>? rules = null)
    {
        _rules = (rules ?? SegmentCatalog.DefaultRules)
            .Where(r => r.Enabled)
            .OrderBy(r => r.Priority)
            .Select(r => (r, new Regex(r.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .ToArray();
    }

    /// <summary>매칭된 세그먼트 코드. 어떤 규칙에도 걸리지 않으면 S7.</summary>
    public string Classify(string? deptRaw)
    {
        var v = (deptRaw ?? "").Trim();
        if (v.Length == 0) return SegmentCatalog.S7;

        foreach (var (rule, rx) in _rules)
            if (rx.IsMatch(v))
                return rule.Segment;

        return SegmentCatalog.S7;
    }

    /// <summary>어느 규칙이 잡았는지까지 돌려준다. 규칙 편집 UI 의 '왜 이렇게 분류됐나' 설명용.</summary>
    public (string Segment, SegmentRule? MatchedBy) Explain(string? deptRaw)
    {
        var v = (deptRaw ?? "").Trim();
        if (v.Length == 0) return (SegmentCatalog.S7, null);

        foreach (var (rule, rx) in _rules)
            if (rx.IsMatch(v))
                return (rule.Segment, rule);

        return (SegmentCatalog.S7, null);
    }

    /// <summary>Recipient 에 세그먼트·진료과 표시명·호칭을 채운다.</summary>
    public void Apply(Recipient r)
    {
        r.Segment = Classify(r.DeptRaw);
        var def = SegmentCatalog.Get(r.Segment);
        r.DeptLabel = def.DeptLabel;
        r.Honorific = def.Honorific;
    }
}
