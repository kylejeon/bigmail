using JlkMailer.Core.Classification;

namespace JlkMailer.Core.Sending;

/// <summary>설계 §10 워밍업 스케줄 한 줄.</summary>
public sealed record WarmupDay(int Day, int Volume, string[] Segments, int IntervalSeconds, int JitterSeconds, string Check);

/// <summary>
/// 설계 §10 권장 워밍업 스케줄.
/// 신규 발송 이력이 없는 계정에서 갑자기 대량 발송하면 도메인 평판이 떨어진다.
/// </summary>
public static class WarmupPlan
{
    public static readonly IReadOnlyList<WarmupDay> Default =
    [
        new(1, 50,  [SegmentCatalog.S2], 55, 15, "반송률·스팸함 여부 직접 확인"),
        new(2, 150, [SegmentCatalog.S2], 40, 10, "반송률 3% 미만 확인 후 진행"),
        new(3, 300, [SegmentCatalog.S3, SegmentCatalog.S4], 30, 10, "SMTP 4xx 발생 시 즉시 감속"),
        new(4, 400, [SegmentCatalog.S1], 30, 10, "일 상한 유지"),
        new(5, 400, [SegmentCatalog.S1], 30, 10, "일 상한 유지"),
        new(6, 400, [SegmentCatalog.S1], 30, 10, "일 상한 유지"),
        new(7, int.MaxValue, [SegmentCatalog.S5, SegmentCatalog.S6, SegmentCatalog.S7], 30, 10, "잔여 전량"),
    ];
}
