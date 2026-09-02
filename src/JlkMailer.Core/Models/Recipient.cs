namespace JlkMailer.Core.Models;

/// <summary>
/// 엑셀 한 행. 설계 §06 recipients 테이블과 1:1 대응.
/// </summary>
public sealed class Recipient
{
    public long Id { get; set; }

    /// <summary>엑셀 원본 행번호(1-based, 헤더 포함). 문제 추적용 — 사용자가 엑셀에서 바로 찾을 수 있어야 한다.</summary>
    public int RowNo { get; set; }

    /// <summary>B: 병원 및 업체명</summary>
    public string Hospital { get; set; } = "";

    /// <summary>C: 성함</summary>
    public string Name { get; set; } = "";

    /// <summary>D: 진료과 원본값 (실측 112종 중 하나)</summary>
    public string DeptRaw { get; set; } = "";

    /// <summary>정규화된 진료과 표시명. {{진료과}} 토큰에 들어가는 값. 설계 §07.</summary>
    public string DeptLabel { get; set; } = "";

    /// <summary>S1..S7</summary>
    public string Segment { get; set; } = "";

    /// <summary>{{호칭}} — 임상 세그먼트는 '선생님', 행정/IT는 '담당자님'. 설계 §08.</summary>
    public string Honorific { get; set; } = "";

    /// <summary>E: 연락처 (실측 126행만 채워져 있음)</summary>
    public string Phone { get; set; } = "";

    /// <summary>F: 이메일 원본</summary>
    public string EmailRaw { get; set; } = "";

    /// <summary>lower(trim()). 중복 판정 및 send_log UNIQUE 키.</summary>
    public string EmailNorm { get; set; } = "";

    /// <summary>자동 교정 제안. 설계 §03 — 자동 적용하지 않고 사용자 승인을 받는다.</summary>
    public string? SuggestedEmail { get; set; }

    /// <summary>사용자가 교정 제안을 승인했는지.</summary>
    public bool FixAccepted { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.Ready;

    /// <summary>검토 화면에 띄울 사유. 사람이 읽는 문장.</summary>
    public string Issue { get; set; } = "";

    /// <summary>실제 발송에 쓸 주소. 교정이 승인되었으면 교정본.</summary>
    public string EffectiveEmail => FixAccepted && SuggestedEmail is { Length: > 0 } ? SuggestedEmail : EmailNorm;

    public bool IsSendable => Status is RecipientStatus.Ready && EffectiveEmail.Length > 0;
}
