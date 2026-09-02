namespace JlkMailer.Core.Models;

/// <summary>
/// 세그먼트 정의. 설계 §07.
/// </summary>
/// <param name="Code">S1..S7</param>
/// <param name="Name">사람이 읽는 이름</param>
/// <param name="DeptLabel">{{진료과}} 토큰 값. 행정/IT 세그먼트는 진료과가 없으므로 빈 문자열.</param>
/// <param name="Honorific">{{호칭}} 토큰 값</param>
/// <param name="IsClinical">임상 세그먼트 여부</param>
/// <param name="DedupePriority">
/// 중복 주소에서 어느 행을 채택할지의 우선순위. 낮을수록 우선.
/// 설계 §14-4 기본값: S2 &gt; S3 &gt; S4 &gt; S1 &gt; S6 &gt; S5.
/// 분류 규칙 순서(§07 priority)와는 별개의 개념이다.
/// </param>
/// <param name="SendByDefault">기본 발송 대상 여부. S7은 false — 분류를 모르는 채 나가는 메일이 없어야 한다.</param>
public sealed record SegmentDef(
    string Code,
    string Name,
    string DeptLabel,
    string Honorific,
    bool IsClinical,
    int DedupePriority,
    bool SendByDefault);
