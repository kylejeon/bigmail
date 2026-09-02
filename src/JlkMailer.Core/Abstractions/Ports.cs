using JlkMailer.Core.Models;

namespace JlkMailer.Core.Abstractions;

/// <summary>설계 §05: Core 는 Infrastructure 를 인터페이스로만 만난다.</summary>

/// <summary>엑셀에서 읽어온 원시 행. 정제 전 상태.</summary>
public sealed record RawRow(int RowNo, string Hospital, string Name, string Dept, string Phone, string Email);

/// <summary>엑셀 컬럼 매핑. 설계 §11 화면1 — 자동 인식 후 사용자가 수정할 수 있다.</summary>
public sealed record ColumnMap(string Hospital = "B", string Name = "C", string Dept = "D", string Phone = "E", string Email = "F")
{
    public static ColumnMap Default => new();
}

public interface IRecipientReader
{
    /// <summary>시트 이름 목록</summary>
    IReadOnlyList<string> ListSheets(string path);

    /// <summary>헤더 행에서 컬럼 위치를 추측한다. 실패하면 Default.</summary>
    ColumnMap GuessColumns(string path, string sheet, int headerRow);

    IReadOnlyList<RawRow> Read(string path, string sheet, int headerRow, ColumnMap map);
}

/// <summary>렌더링이 끝난 한 통의 메일.</summary>
public sealed record ComposedMail(
    string Subject,
    string Html,
    string PlainText,
    IReadOnlyList<InlineImage> Images,
    string? ListUnsubscribe);

/// <summary>CID 로 참조되는 인라인 이미지. 설계 §09.</summary>
public sealed record InlineImage(string ContentId, string FileName, string MediaType, byte[] Bytes);

public interface IEmailComposer
{
    ComposedMail Compose(Recipient recipient, Campaign campaign, MailTemplate template);
}

/// <summary>SMTP 한 번의 결과. 응답 원문을 그대로 보존한다(설계 §06).</summary>
public sealed record SendResult(SmtpOutcome Outcome, string? Code, string? Message, string? MessageId)
{
    public static SendResult Ok(string? messageId, string? message = "250 OK") =>
        new(SmtpOutcome.Success, "250", message, messageId);
}

public interface IMailSender : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<SendResult> SendAsync(string toAddress, string toName, ComposedMail mail, Campaign campaign, CancellationToken ct = default);
    /// <summary>설계 §10: 100통마다 재연결한다.</summary>
    Task ReconnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
}

public interface ICampaignStore
{
    void Initialize();

    long UpsertCampaign(Campaign campaign);
    Campaign? GetCampaign(long id);

    void ReplaceRecipients(IEnumerable<Recipient> recipients);
    IReadOnlyList<Recipient> GetRecipients(string? segment = null);
    void UpdateRecipient(Recipient recipient);

    void SaveRules(IEnumerable<SegmentRule> rules);
    IReadOnlyList<SegmentRule> GetRules();

    void SaveTemplate(MailTemplate template);
    IReadOnlyList<MailTemplate> GetTemplates();

    /// <summary>이미 큐에 있거나 발송된 주소를 제외하고 새 항목만 넣는다. 반환값은 실제 삽입 건수.</summary>
    int EnqueueMissing(long campaignId, IEnumerable<Recipient> recipients);

    IReadOnlyList<SendLogEntry> TakeQueued(long campaignId, int max, DateTimeOffset now);
    void UpdateLog(SendLogEntry entry);
    IReadOnlyList<SendLogEntry> GetLog(long campaignId);
    int CountSentOn(long campaignId, DateOnly localDate);
    (int Sent, int Failed, int Queued) Counts(long campaignId);

    /// <summary>앱 시작 시 'sending' 상태를 'queued' 로 되돌린다. 설계 §10 재개.</summary>
    int ResetStuckSending(long campaignId);

    void AddSuppression(Suppression s);
    IReadOnlySet<string> GetSuppressions();
}
