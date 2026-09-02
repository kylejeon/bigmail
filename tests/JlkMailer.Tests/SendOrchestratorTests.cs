using JlkMailer.Application;
using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Sending;
using JlkMailer.Infrastructure.Storage;
using Xunit;

namespace JlkMailer.Tests;

/// <summary>
/// 설계 §10 발송 파이프라인. 시계와 대기를 주입해 실시간을 기다리지 않고 검증한다.
/// </summary>
public sealed class SendOrchestratorTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly SqliteCampaignStore _store;
    private readonly Campaign _campaign;

    /// <summary>
    /// 고정된 과거 시각. 2026-01-07 은 수요일이고 09:30 은 오전 발송 창 안이다.
    ///
    /// '오늘'을 쓰면 안 된다. 예전에는 실행 당일과 주입 시계의 날짜가 우연히 같아서
    /// 일 상한 버그(sent_at 은 실제 시계, 상한 판정은 주입 시계)가 가려졌고,
    /// 날짜가 넘어간 다음 날에야 드러났다.
    /// 고정 과거 날짜를 쓰면 두 시계가 어긋난 순간 항상 실패한다.
    /// </summary>
    private DateTime _now = DateTime.Parse("2026-01-07 09:30");
    private TimeSpan _slept = TimeSpan.Zero;

    public SendOrchestratorTests()
    {
        _store = new SqliteCampaignStore(_temp.Path);
        _store.Initialize();
        _campaign = new Campaign { Name = "테스트", DailyCap = 100, FromAddress = "a@jlk.com", FromName = "JLK" };
        _store.UpsertCampaign(_campaign);
    }

    private (IReadOnlyDictionary<long, Recipient>, IReadOnlyDictionary<string, MailTemplate>) Seed(int count)
    {
        var list = Enumerable.Range(1, count).Select(i => new Recipient
        {
            RowNo = i + 1,
            Hospital = "테스트병원",
            Name = $"수신자{i}",
            DeptRaw = "신경과",
            Segment = SegmentCatalog.S2,
            DeptLabel = "신경과",
            Honorific = "선생님",
            EmailRaw = $"user{i}@example.com",
            EmailNorm = $"user{i}@example.com",
            Status = RecipientStatus.Ready,
        }).ToList();

        _store.ReplaceRecipients(list);
        var stored = _store.GetRecipients();
        _store.EnqueueMissing(_campaign.Id, stored);

        return (stored.ToDictionary(r => r.Id),
                DefaultTemplates.All.ToDictionary(t => t.Segment));
    }

    private SendOrchestrator Build(IMailSender sender, ThrottlePolicy? policy = null) =>
        new(_store, sender, new StubComposer(), policy ?? new ThrottlePolicy { IntervalSeconds = 1, JitterSeconds = 0 },
            clock: () => _now,
            delay: (d, _) => { _slept += d; return Task.CompletedTask; });

    [Fact]
    public async Task 큐를_비우면_Completed_로_끝난다()
    {
        var (recipients, templates) = Seed(3);
        var sender = new FakeMailSender();

        var outcome = await Build(sender).RunAsync(_campaign, recipients, templates);

        Assert.Equal(StopReason.Completed, outcome.Reason);
        Assert.Equal(3, outcome.Sent);
        Assert.Equal(3, sender.SentTo.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), _slept);   // 간격이 실제로 적용된다
    }

    /// <summary>4xx 는 재시도 대상. 3회까지 시도한 뒤 Failed 로 남는다.</summary>
    [Fact]
    public async Task 일시오류는_재시도_후_Failed_로_남는다()
    {
        var (recipients, templates) = Seed(1);
        var sender = new FakeMailSender(fallback: FakeMailSender.Transient());

        var outcome = await Build(sender, new ThrottlePolicy { IntervalSeconds = 1, JitterSeconds = 0, ConsecutiveFailureLimit = 99 })
            .RunAsync(_campaign, recipients, templates);

        var entry = _store.GetLog(_campaign.Id).Single();
        Assert.Equal(SendState.Retrying, entry.State);      // 첫 시도 후 재시도 대기
        Assert.Equal(1, entry.Attempt);
        Assert.NotNull(entry.NextAttemptAt);
        Assert.Equal(StopReason.Completed, outcome.Reason); // 대기 시각이 미래라 지금 꺼낼 것이 없다
        Assert.Empty(sender.SentTo);
    }

    /// <summary>5xx 는 재시도하지 않고, 존재하지 않는 주소는 수신거부 목록에 자동 등록한다.</summary>
    [Fact]
    public async Task 영구오류는_반송_처리되고_수신거부_목록에_등록된다()
    {
        var (recipients, templates) = Seed(1);
        var sender = new FakeMailSender(fallback: FakeMailSender.Permanent());

        await Build(sender).RunAsync(_campaign, recipients, templates);

        var entry = _store.GetLog(_campaign.Id).Single();
        Assert.Equal(SendState.Bounced, entry.State);
        Assert.Equal("550", entry.SmtpCode);
        Assert.Contains("user1@example.com", _store.GetSuppressions());
    }

    /// <summary>설계 §10 차단기: 계정이 잠긴 채 1,000건을 실패 처리하는 사고를 막는다.</summary>
    [Fact]
    public async Task 연속_실패가_한계에_닿으면_발송을_중단한다()
    {
        var (recipients, templates) = Seed(50);
        var sender = new FakeMailSender(fallback: FakeMailSender.Permanent());
        var policy = new ThrottlePolicy { IntervalSeconds = 1, JitterSeconds = 0, ConsecutiveFailureLimit = 10 };

        var outcome = await Build(sender, policy).RunAsync(_campaign, recipients, templates);

        Assert.Equal(StopReason.CircuitBreakerTripped, outcome.Reason);
        Assert.Equal(10, outcome.Failed);
        Assert.Contains("연속 실패", outcome.Detail);

        // 나머지 40건은 손대지 않은 채 큐에 남아 있다
        Assert.Equal(40, _store.Counts(_campaign.Id).Queued);
    }

    [Fact]
    public async Task 성공하면_연속_실패_카운터가_초기화된다()
    {
        var (recipients, templates) = Seed(20);
        var script = new List<SendResult>();
        for (var i = 0; i < 5; i++) script.Add(FakeMailSender.Permanent());
        script.Add(SendResult.Ok("<ok@test>"));
        for (var i = 0; i < 5; i++) script.Add(FakeMailSender.Permanent());

        var sender = new FakeMailSender(SendResult.Ok("<ok@test>"), script.ToArray());
        var policy = new ThrottlePolicy { IntervalSeconds = 1, JitterSeconds = 0, ConsecutiveFailureLimit = 10 };

        var outcome = await Build(sender, policy).RunAsync(_campaign, recipients, templates);

        // 6번째 성공이 카운터를 끊었으므로 차단기가 걸리지 않는다
        Assert.Equal(StopReason.Completed, outcome.Reason);
        Assert.Equal(10, outcome.Failed);
    }

    [Fact]
    public async Task 일_상한에_닿으면_멈춘다()
    {
        _campaign.DailyCap = 3;
        _store.UpsertCampaign(_campaign);

        var (recipients, templates) = Seed(10);
        var outcome = await Build(new FakeMailSender()).RunAsync(_campaign, recipients, templates);

        Assert.Equal(StopReason.DailyCapReached, outcome.Reason);
        Assert.Equal(3, outcome.Sent);
        Assert.Equal(7, _store.Counts(_campaign.Id).Queued);
    }

    /// <summary>
    /// 회귀 방지: sent_at 을 실제 시계로, 상한 판정을 주입 시계로 하면
    /// 두 날짜가 어긋나 상한이 영원히 채워지지 않는다.
    /// 발송한 건은 반드시 '발송 시점의 시계' 날짜로 집계되어야 한다.
    /// </summary>
    [Fact]
    public async Task 발송_기록은_주입된_시계의_날짜로_집계된다()
    {
        var (recipients, templates) = Seed(3);
        await Build(new FakeMailSender()).RunAsync(_campaign, recipients, templates);

        var sentDate = DateOnly.FromDateTime(_now);

        Assert.Equal(3, _store.CountSentOn(_campaign.Id, sentDate));
        Assert.Equal(0, _store.CountSentOn(_campaign.Id, DateOnly.FromDateTime(DateTime.Now)));
        Assert.All(_store.GetLog(_campaign.Id),
                   e => Assert.Equal(sentDate, DateOnly.FromDateTime(e.SentAt!.Value.LocalDateTime)));
    }

    [Fact]
    public async Task 발송_시간대_밖이면_시작하지_않고_다음_시각을_알려준다()
    {
        _now = DateTime.Parse("2026-01-07 12:30");   // 점심시간
        var (recipients, templates) = Seed(5);

        var outcome = await Build(new FakeMailSender()).RunAsync(_campaign, recipients, templates);

        Assert.Equal(StopReason.OutsideWindow, outcome.Reason);
        Assert.Equal(0, outcome.Sent);
        Assert.Contains("14:00", outcome.Detail);
    }

    [Fact]
    public async Task 수신거부_목록의_주소는_건너뛴다()
    {
        var (recipients, templates) = Seed(3);
        _store.AddSuppression(new Suppression("user2@example.com", Suppression.Unsubscribe, DateTimeOffset.Now));

        var sender = new FakeMailSender();
        await Build(sender).RunAsync(_campaign, recipients, templates);

        Assert.Equal(2, sender.SentTo.Count);
        Assert.DoesNotContain("user2@example.com", sender.SentTo);

        var skipped = _store.GetLog(_campaign.Id).Single(e => e.EmailNorm == "user2@example.com");
        Assert.Equal(SendState.Skipped, skipped.State);
    }

    [Fact]
    public async Task 취소하면_해당_건이_큐로_돌아온다()
    {
        var (recipients, templates) = Seed(5);
        using var cts = new CancellationTokenSource();

        var sender = new FakeMailSender();
        var orchestrator = new SendOrchestrator(_store, sender, new StubComposer(),
            new ThrottlePolicy { IntervalSeconds = 1, JitterSeconds = 0 },
            clock: () => _now,
            delay: (_, _) => { cts.Cancel(); return Task.CompletedTask; });

        var outcome = await orchestrator.RunAsync(_campaign, recipients, templates, ct: cts.Token);

        Assert.Equal(StopReason.Cancelled, outcome.Reason);
        Assert.Equal(1, outcome.Sent);
        Assert.Equal(4, _store.Counts(_campaign.Id).Queued);
    }

    [Fact]
    public async Task 재실행하면_보낸_건은_건너뛰고_남은_건만_보낸다()
    {
        _campaign.DailyCap = 2;
        _store.UpsertCampaign(_campaign);
        var (recipients, templates) = Seed(5);

        var first = new FakeMailSender();
        await Build(first).RunAsync(_campaign, recipients, templates);
        Assert.Equal(2, first.SentTo.Count);

        // 다음 날 재개
        _now = _now.AddDays(1);
        _campaign.DailyCap = 100;
        _store.UpsertCampaign(_campaign);

        var second = new FakeMailSender();
        var outcome = await Build(second).RunAsync(_campaign, recipients, templates);

        Assert.Equal(StopReason.Completed, outcome.Reason);
        Assert.Equal(3, second.SentTo.Count);
        Assert.Empty(second.SentTo.Intersect(first.SentTo));   // 겹치지 않는다
        Assert.Equal(5, _store.Counts(_campaign.Id).Sent);
    }

    private sealed class StubComposer : IEmailComposer
    {
        public ComposedMail Compose(Recipient r, Campaign c, MailTemplate t) =>
            new($"[{r.Hospital}] 안내", $"<p>{r.Name}</p>", r.Name, [], null);
    }

    public void Dispose()
    {
        _store.Dispose();
        _temp.Dispose();
    }
}
