namespace JlkMailer.Core.Models;

/// <summary>설계 §06 recipients.status</summary>
public enum RecipientStatus
{
    /// <summary>발송 가능</summary>
    Ready,
    /// <summary>이메일 칸이 비어 있음 (실측 19건)</summary>
    NoEmail,
    /// <summary>이메일 형식 오류이며 자동 교정 제안도 만들 수 없음</summary>
    Invalid,
    /// <summary>형식 오류이나 교정 제안이 있음. 사용자가 승인해야 Ready 로 승격 (실측 6건)</summary>
    NeedsFix,
    /// <summary>같은 주소가 다른 행에서 이미 채택됨 (실측 70주소 / 75건)</summary>
    Duplicate,
    /// <summary>세그먼트 S7 등, 사람이 확인해야 하는 행 (실측 73건)</summary>
    NeedsReview,
    /// <summary>사용자가 명시적으로 제외</summary>
    Excluded,
    /// <summary>수신거부 목록에 있음</summary>
    Suppressed,
}

/// <summary>설계 §10 상태 전이도</summary>
public enum SendState
{
    Queued,
    Sending,
    Sent,
    Retrying,
    Failed,
    Skipped,
    Bounced,
}

/// <summary>SMTP 응답을 어떻게 다룰지에 대한 분류. 설계 §10.</summary>
public enum SmtpOutcome
{
    /// <summary>250 계열. 성공.</summary>
    Success,
    /// <summary>421 / 450 / 451 등 4xx. 지수 백오프 후 재시도.</summary>
    Transient,
    /// <summary>550 등 5xx. 재시도하지 않음. 수신거부 목록 등록 대상.</summary>
    Permanent,
}
