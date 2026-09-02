namespace JlkMailer.Core.Models;

/// <summary>설계 §06 send_log 테이블. UNIQUE(campaign_id, email_norm) 로 중복발송을 원천 차단한다.</summary>
public sealed class SendLogEntry
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public long RecipientId { get; set; }
    public string EmailNorm { get; set; } = "";
    public SendState State { get; set; } = SendState.Queued;
    public int Attempt { get; set; }
    public string? SmtpCode { get; set; }

    /// <summary>서버 응답 원문. 반송 분석의 근거이므로 요약하지 말 것.</summary>
    public string? SmtpMessage { get; set; }

    public string? MessageId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

/// <summary>설계 §06 suppressions 테이블. 캠페인과 무관하게 영구 보관.</summary>
public sealed record Suppression(string EmailNorm, string Reason, DateTimeOffset AddedAt)
{
    public const string Unsubscribe = "unsubscribe";
    public const string HardBounce = "hard_bounce";
    public const string Manual = "manual";
}
