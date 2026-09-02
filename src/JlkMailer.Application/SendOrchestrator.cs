using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using JlkMailer.Core.Sending;
using JlkMailer.Infrastructure.Mail;

namespace JlkMailer.Application;

public sealed record SendProgress(
    int Sent, int Failed, int Remaining, string LastEmail, string LastMessage, SendState LastState);

public enum StopReason
{
    Completed,
    DailyCapReached,
    OutsideWindow,
    CircuitBreakerTripped,
    Cancelled,
    AuthenticationFailed,
}

public sealed record SendOutcome(StopReason Reason, int Sent, int Failed, string? Detail = null);

/// <summary>
/// 설계 §10 발송 파이프라인.
/// 상태 전이 · 지수 백오프 · 일 상한 · 시간대 제한 · 연속실패 차단기 · 재개를 모두 여기서 다룬다.
/// 시계와 대기를 주입받아 테스트에서 실시간을 기다리지 않는다.
/// </summary>
public sealed class SendOrchestrator(
    ICampaignStore store,
    IMailSender sender,
    IEmailComposer composer,
    ThrottlePolicy policy,
    Func<DateTime>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.Now);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Random _rng = new();

    public async Task<SendOutcome> RunAsync(
        Campaign campaign,
        IReadOnlyDictionary<long, Recipient> recipientsById,
        IReadOnlyDictionary<string, MailTemplate> templatesBySegment,
        IProgress<SendProgress>? progress = null,
        CancellationToken ct = default)
    {
        // 설계 §10 재개: 이전 실행이 비정상 종료되며 남긴 'Sending' 을 되돌린다.
        store.ResetStuckSending(campaign.Id);

        var suppressions = store.GetSuppressions();
        var consecutiveFailures = 0;
        var sentThisRun = 0;
        var failedThisRun = 0;

        while (!ct.IsCancellationRequested)
        {
            var now = _now();

            if (store.CountSentOn(campaign.Id, DateOnly.FromDateTime(now)) >= campaign.DailyCap)
                return Stop(StopReason.DailyCapReached, $"오늘 상한 {campaign.DailyCap}통에 도달했습니다.");

            if (!policy.IsOpen(now))
                return Stop(StopReason.OutsideWindow,
                    $"발송 시간대가 아닙니다. 다음 발송 가능 시각: {policy.NextOpening(now):yyyy-MM-dd HH:mm}");

            var batch = store.TakeQueued(campaign.Id, 1, now);
            if (batch.Count == 0)
                return Stop(StopReason.Completed, "큐가 비었습니다.");

            var entry = batch[0];

            if (!recipientsById.TryGetValue(entry.RecipientId, out var recipient))
            {
                Finish(entry, SendState.Skipped, "SKIP", "수신자 레코드를 찾지 못했습니다.");
                continue;
            }

            if (suppressions.Contains(entry.EmailNorm))
            {
                Finish(entry, SendState.Skipped, "SUPPRESSED", "수신거부 목록에 있는 주소입니다.");
                continue;
            }

            entry.State = SendState.Sending;
            store.UpdateLog(entry);

            SendResult result;
            try
            {
                var template = templatesBySegment.TryGetValue(recipient.Segment, out var t)
                    ? t
                    : DefaultTemplates.For(recipient.Segment);

                var mail = composer.Compose(recipient, campaign, template);
                result = await sender.SendAsync(entry.EmailNorm, recipient.Name, mail, campaign, ct);
            }
            catch (MailAuthenticationFailedException ex)
            {
                // 인증 실패는 재시도 대상이 아니다. 큐를 되돌리고 즉시 멈춘다.
                entry.State = SendState.Queued;
                store.UpdateLog(entry);
                return Stop(StopReason.AuthenticationFailed, ex.Message);
            }
            catch (OperationCanceledException)
            {
                entry.State = SendState.Queued;
                store.UpdateLog(entry);
                return Stop(StopReason.Cancelled, "사용자가 중지했습니다.");
            }
            catch (Exception ex)
            {
                result = new SendResult(SmtpOutcome.Transient, "ERR", ex.Message, null);
            }

            switch (result.Outcome)
            {
                case SmtpOutcome.Success:
                    entry.State = SendState.Sent;
                    entry.SentAt = DateTimeOffset.Now;
                    entry.Attempt++;
                    consecutiveFailures = 0;
                    sentThisRun++;
                    break;

                case SmtpOutcome.Transient when entry.Attempt + 1 < ThrottlePolicy.MaxAttempts:
                    entry.Attempt++;
                    entry.State = SendState.Retrying;
                    entry.NextAttemptAt = DateTimeOffset.Now + ThrottlePolicy.BackoffFor(entry.Attempt);
                    consecutiveFailures++;
                    break;

                case SmtpOutcome.Transient:
                    // 3회까지 실패. 다음 캠페인 실행에서 다시 시도할 수 있도록 Failed 로 남긴다.
                    entry.Attempt++;
                    entry.State = SendState.Failed;
                    consecutiveFailures++;
                    failedThisRun++;
                    break;

                case SmtpOutcome.Permanent:
                    entry.Attempt++;
                    entry.State = SendState.Bounced;
                    consecutiveFailures++;
                    failedThisRun++;
                    // 존재하지 않는 주소는 다음 캠페인에서 자동 제외한다. 설계 §10.
                    store.AddSuppression(new Suppression(entry.EmailNorm, Suppression.HardBounce, DateTimeOffset.Now));
                    break;
            }

            entry.SmtpCode = result.Code;
            entry.SmtpMessage = result.Message;
            entry.MessageId = result.MessageId;
            store.UpdateLog(entry);

            var counts = store.Counts(campaign.Id);
            progress?.Report(new SendProgress(counts.Sent, counts.Failed, counts.Queued,
                entry.EmailNorm, result.Message ?? "", entry.State));

            // 설계 §10 차단기: 계정이 잠긴 상태로 1,000건을 실패 처리하는 사고를 막는다.
            if (consecutiveFailures >= policy.ConsecutiveFailureLimit)
                return Stop(StopReason.CircuitBreakerTripped,
                    $"{consecutiveFailures}건 연속 실패하여 발송을 중단했습니다. 계정 상태와 마지막 SMTP 응답을 확인하세요.");

            await _delay(policy.NextInterval(_rng), ct);
        }

        return Stop(StopReason.Cancelled, "사용자가 중지했습니다.");

        SendOutcome Stop(StopReason reason, string detail) => new(reason, sentThisRun, failedThisRun, detail);

        void Finish(SendLogEntry e, SendState state, string code, string message)
        {
            e.State = state;
            e.SmtpCode = code;
            e.SmtpMessage = message;
            store.UpdateLog(e);
        }
    }
}
