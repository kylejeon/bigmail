namespace JlkMailer.Core.Models;

/// <summary>
/// 세그먼트별 문안. 설계 §06 templates 테이블 / §08 본문 슬롯.
/// HTML 골격은 하나이고 아래 슬롯만 교체된다.
/// </summary>
public sealed class MailTemplate
{
    public string Segment { get; set; } = "";

    /// <summary>제목. 토큰 포함. 60자 이내 권장(§08).</summary>
    public string Subject { get; set; } = "";

    /// <summary>인사말 문단 (HTML 조각 허용)</summary>
    public string Greeting { get; set; } = "";

    /// <summary>도입 문단 — 세그먼트 차별화의 대부분이 여기서 나온다</summary>
    public string Intro { get; set; } = "";

    /// <summary>장점 3블록 위 리드 문장</summary>
    public string BenefitLead { get; set; } = "";

    /// <summary>미팅 요청 문구</summary>
    public string Closing { get; set; } = "";

    public MailTemplate Clone() => (MailTemplate)MemberwiseClone();
}
