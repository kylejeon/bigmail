using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Infrastructure.Storage;
using Xunit;

namespace JlkMailer.Tests;

public sealed class SqliteCampaignStoreTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly SqliteCampaignStore _store;

    public SqliteCampaignStoreTests()
    {
        _store = new SqliteCampaignStore(_temp.Path);
        _store.Initialize();
    }

    private static Recipient Make(int row, string email, string segment = SegmentCatalog.S2) => new()
    {
        RowNo = row,
        Hospital = "테스트병원",
        Name = $"수신자{row}",
        DeptRaw = "신경과",
        Segment = segment,
        DeptLabel = "신경과",
        Honorific = "선생님",
        EmailRaw = email,
        EmailNorm = email,
        Status = RecipientStatus.Ready,
    };

    private long SeedCampaign() => _store.UpsertCampaign(new Campaign { Name = "테스트", DailyCap = 100 });

    /// <summary>설계 §06: UNIQUE(campaign_id, email_norm) 하나가 중복발송 방지의 전부다.</summary>
    [Fact]
    public void 같은_주소를_두_번_큐에_넣어도_한_건만_들어간다()
    {
        var id = SeedCampaign();
        _store.ReplaceRecipients([Make(2, "a@b.com"), Make(3, "a@b.com")]);
        var stored = _store.GetRecipients();

        Assert.Equal(1, _store.EnqueueMissing(id, stored));
        Assert.Equal(0, _store.EnqueueMissing(id, stored));   // 재실행해도 늘지 않는다
        Assert.Single(_store.GetLog(id));
    }

    [Fact]
    public void 발송_완료된_주소는_다시_큐에_들어가지_않는다()
    {
        var id = SeedCampaign();
        _store.ReplaceRecipients([Make(2, "a@b.com")]);
        var stored = _store.GetRecipients();
        _store.EnqueueMissing(id, stored);

        var entry = _store.TakeQueued(id, 10, DateTimeOffset.Now).Single();
        entry.State = SendState.Sent;
        entry.SentAt = DateTimeOffset.Now;
        _store.UpdateLog(entry);

        Assert.Equal(0, _store.EnqueueMissing(id, stored));
        Assert.Empty(_store.TakeQueued(id, 10, DateTimeOffset.Now));
    }

    [Fact]
    public void 발송_불가_상태의_수신자는_큐에_들어가지_않는다()
    {
        var id = SeedCampaign();
        var blocked = Make(2, "a@b.com");
        blocked.Status = RecipientStatus.NeedsReview;
        _store.ReplaceRecipients([blocked]);

        Assert.Equal(0, _store.EnqueueMissing(id, _store.GetRecipients()));
    }

    /// <summary>설계 §10 재개: 앱 시작 시 Sending 을 Queued 로 되돌린다.</summary>
    [Fact]
    public void 중단된_Sending_상태가_재개_시_큐로_돌아온다()
    {
        var id = SeedCampaign();
        _store.ReplaceRecipients([Make(2, "a@b.com")]);
        _store.EnqueueMissing(id, _store.GetRecipients());

        var entry = _store.TakeQueued(id, 10, DateTimeOffset.Now).Single();
        entry.State = SendState.Sending;
        _store.UpdateLog(entry);

        Assert.Empty(_store.TakeQueued(id, 10, DateTimeOffset.Now));
        Assert.Equal(1, _store.ResetStuckSending(id));
        Assert.Single(_store.TakeQueued(id, 10, DateTimeOffset.Now));
    }

    [Fact]
    public void 재시도_대기중인_항목은_시각이_지나야_꺼내진다()
    {
        var id = SeedCampaign();
        _store.ReplaceRecipients([Make(2, "a@b.com")]);
        _store.EnqueueMissing(id, _store.GetRecipients());

        var entry = _store.TakeQueued(id, 10, DateTimeOffset.Now).Single();
        entry.State = SendState.Retrying;
        entry.NextAttemptAt = DateTimeOffset.Now.AddMinutes(10);
        _store.UpdateLog(entry);

        Assert.Empty(_store.TakeQueued(id, 10, DateTimeOffset.Now));
        Assert.Single(_store.TakeQueued(id, 10, DateTimeOffset.Now.AddMinutes(11)));
    }

    [Fact]
    public void 일_상한_계산은_로컬_날짜_기준이다()
    {
        var id = SeedCampaign();
        _store.ReplaceRecipients([Make(2, "a@b.com"), Make(3, "c@d.com")]);
        _store.EnqueueMissing(id, _store.GetRecipients());

        var entries = _store.TakeQueued(id, 10, DateTimeOffset.Now);
        entries[0].State = SendState.Sent;
        entries[0].SentAt = DateTimeOffset.Now;
        _store.UpdateLog(entries[0]);

        entries[1].State = SendState.Sent;
        entries[1].SentAt = DateTimeOffset.Now.AddDays(-1);
        _store.UpdateLog(entries[1]);

        Assert.Equal(1, _store.CountSentOn(id, DateOnly.FromDateTime(DateTime.Now)));
        Assert.Equal(1, _store.CountSentOn(id, DateOnly.FromDateTime(DateTime.Now.AddDays(-1))));
    }

    [Fact]
    public void 수신거부_목록은_중복_등록되지_않는다()
    {
        _store.AddSuppression(new Suppression("a@b.com", Suppression.HardBounce, DateTimeOffset.Now));
        _store.AddSuppression(new Suppression("a@b.com", Suppression.Manual, DateTimeOffset.Now));

        Assert.Single(_store.GetSuppressions());
        Assert.Contains("a@b.com", _store.GetSuppressions());
    }

    [Fact]
    public void 규칙과_템플릿이_왕복_저장된다()
    {
        _store.SaveRules(SegmentCatalog.DefaultRules);
        var rules = _store.GetRules();
        Assert.Equal(SegmentCatalog.DefaultRules.Count, rules.Count);
        Assert.Equal(SegmentCatalog.S1, rules[0].Segment);   // 우선순위 1번은 반드시 S1

        foreach (var t in Application.DefaultTemplates.All) _store.SaveTemplate(t);
        Assert.Equal(Application.DefaultTemplates.All.Count, _store.GetTemplates().Count);
    }

    [Fact]
    public void 캠페인_설정이_왕복_저장된다()
    {
        var campaign = new Campaign
        {
            Name = "JLK-CTP 소개", FromName = "제이엘케이", FromAddress = "cs@jlkgroup.com",
            SenderDisplayName = "홍길동", AdPrefix = true, IncludeUnsubscribe = true,
            UnsubscribeTarget = "cs@jlkgroup.com", DailyCap = 150,
        };

        var loaded = _store.GetCampaign(_store.UpsertCampaign(campaign))!;

        Assert.Equal("홍길동", loaded.SenderDisplayName);
        Assert.True(loaded.AdPrefix);
        Assert.Equal(150, loaded.DailyCap);
    }

    public void Dispose()
    {
        _store.Dispose();
        _temp.Dispose();
    }
}
