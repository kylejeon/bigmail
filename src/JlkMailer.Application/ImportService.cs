using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;

namespace JlkMailer.Application;

/// <summary>불러오기 결과 요약. 설계 §11 화면2 상단 배지에 그대로 대응한다.</summary>
public sealed record ImportSummary(
    int TotalRows,
    int Sendable,
    int NoEmail,
    int NeedsFix,
    int Invalid,
    int Duplicate,
    int NeedsReview,
    int Suppressed,
    int Hospitals,
    int DistinctDeptRaw,
    IReadOnlyDictionary<string, int> BySegment);

/// <summary>
/// 엑셀 원시 행 → 검토 가능한 수신자 목록. 설계 §03 · §07.
/// 순서가 중요하다: 분류 → 이메일 검증/교정제안 → 수신거부 → 중복.
/// </summary>
public sealed class ImportService(SegmentClassifier classifier)
{
    public (List<Recipient> Recipients, ImportSummary Summary) Build(
        IEnumerable<RawRow> rows,
        IReadOnlySet<string>? suppressions = null)
    {
        var recipients = new List<Recipient>();

        foreach (var row in rows)
        {
            var r = new Recipient
            {
                RowNo = row.RowNo,
                Hospital = row.Hospital,
                Name = row.Name,
                DeptRaw = row.Dept,
                Phone = row.Phone,
                EmailRaw = row.Email,
                EmailNorm = EmailAddressUtil.Normalize(row.Email),
            };

            classifier.Apply(r);
            ApplyEmailStatus(r);

            // 이메일이 멀쩡해도 세그먼트를 모르면 보내지 않는다. 설계 §07.
            if (r.Status == RecipientStatus.Ready && r.Segment == SegmentCatalog.S7)
            {
                r.Status = RecipientStatus.NeedsReview;
                r.Issue = r.DeptRaw.Length == 0
                    ? "진료과가 비어 있습니다. 세그먼트를 지정하거나 제외하세요."
                    : $"'{r.DeptRaw}' 는 어떤 규칙에도 걸리지 않았습니다. 세그먼트를 지정하거나 제외하세요.";
            }

            recipients.Add(r);
        }

        if (suppressions is { Count: > 0 })
            foreach (var r in recipients)
                if (r.EmailNorm.Length > 0 && suppressions.Contains(r.EmailNorm))
                {
                    r.Status = RecipientStatus.Suppressed;
                    r.Issue = "수신거부 목록에 있는 주소입니다.";
                }

        DedupeService.Apply(recipients);

        return (recipients, Summarize(recipients));
    }

    private static void ApplyEmailStatus(Recipient r)
    {
        if (r.EmailNorm.Length == 0)
        {
            r.Status = RecipientStatus.NoEmail;
            r.Issue = "이메일이 비어 있습니다.";
            return;
        }

        if (EmailAddressUtil.IsValid(r.EmailNorm))
        {
            r.Status = RecipientStatus.Ready;
            return;
        }

        if (EmailAddressUtil.TrySuggestFix(r.EmailNorm, out var suggestion))
        {
            r.SuggestedEmail = suggestion;
            r.Status = RecipientStatus.NeedsFix;
            r.Issue = $"형식 오류. 교정 제안: {suggestion}";
            return;
        }

        r.Status = RecipientStatus.Invalid;
        r.Issue = "이메일 형식이 올바르지 않고 자동 교정도 불가능합니다.";
    }

    private static ImportSummary Summarize(List<Recipient> recipients)
    {
        int Count(RecipientStatus s) => recipients.Count(r => r.Status == s);

        var bySegment = recipients
            .GroupBy(r => r.Segment)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ImportSummary(
            TotalRows: recipients.Count,
            Sendable: recipients.Count(r => r.IsSendable),
            NoEmail: Count(RecipientStatus.NoEmail),
            NeedsFix: Count(RecipientStatus.NeedsFix),
            Invalid: Count(RecipientStatus.Invalid),
            Duplicate: Count(RecipientStatus.Duplicate),
            NeedsReview: Count(RecipientStatus.NeedsReview),
            Suppressed: Count(RecipientStatus.Suppressed),
            Hospitals: recipients.Select(r => r.Hospital).Distinct(StringComparer.Ordinal).Count(),
            DistinctDeptRaw: recipients.Select(r => r.DeptRaw).Distinct(StringComparer.Ordinal).Count(),
            BySegment: bySegment);
    }
}
