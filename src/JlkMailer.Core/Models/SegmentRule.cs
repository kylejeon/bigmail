namespace JlkMailer.Core.Models;

/// <summary>
/// 진료과 원본값 → 세그먼트 분류 규칙. 설계 §06 segment_rules 테이블.
/// </summary>
/// <param name="Priority">
/// 낮을수록 먼저 검사. first-match-wins.
/// 설계 §07 경고: S5의 '부장$'·'실장$' 패턴이 '영상의학과 부장'을 가로채므로
/// 임상 세그먼트(S1~S4)가 반드시 먼저 와야 한다.
/// </param>
public sealed record SegmentRule(int Priority, string Segment, string Pattern, bool Enabled = true);
