using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;

namespace JlkMailer.Application;

/// <summary>
/// 설계 §03 중복 처리. 같은 주소가 서로 다른 진료과로 여러 번 들어 있는 경우가 실측 70주소 있다.
/// 이메일 정규화 키 기준 1건만 남기고, 어느 행을 채택할지는 세그먼트 우선순위로 정한다(설계 §14-4).
/// 탈락한 행은 삭제하지 않고 Duplicate 로 표시해 추적 가능하게 둔다.
/// </summary>
public static class DedupeService
{
    public static int Apply(IList<Recipient> recipients)
    {
        var groups = recipients
            .Where(r => r.EmailNorm.Length > 0)
            .Where(r => r.Status is RecipientStatus.Ready or RecipientStatus.NeedsFix or RecipientStatus.NeedsReview)
            .GroupBy(r => r.EmailNorm, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        var demoted = 0;

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(r => SegmentCatalog.Get(r.Segment).DedupePriority)  // 임상 우선
                .ThenBy(r => r.Status == RecipientStatus.Ready ? 0 : 1)      // 문제 없는 행 우선
                .ThenBy(r => r.RowNo)                                        // 그래도 같으면 엑셀 위쪽
                .ToList();

            var winner = ordered[0];

            foreach (var loser in ordered.Skip(1))
            {
                loser.Status = RecipientStatus.Duplicate;
                loser.Issue = $"같은 주소가 {winner.RowNo}행({SegmentCatalog.Get(winner.Segment).Name})에서 이미 채택되었습니다.";
                demoted++;
            }
        }

        return demoted;
    }
}
