using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using Xunit;

namespace JlkMailer.Tests;

public class DedupeServiceTests
{
    private static Recipient Make(int row, string segment, string email, RecipientStatus status = RecipientStatus.Ready) =>
        new()
        {
            RowNo = row,
            Segment = segment,
            EmailNorm = email,
            EmailRaw = email,
            Status = status,
            Hospital = "테스트병원",
            Name = $"수신자{row}",
        };

    /// <summary>설계 §14-4 기본 우선순위: S2 &gt; S3 &gt; S4 &gt; S1 &gt; S6 &gt; S5.</summary>
    [Fact]
    public void 중복_주소는_세그먼트_우선순위로_채택한다()
    {
        var list = new List<Recipient>
        {
            Make(10, SegmentCatalog.S5, "a@b.com"),
            Make(11, SegmentCatalog.S1, "a@b.com"),
            Make(12, SegmentCatalog.S2, "a@b.com"),
        };

        Assert.Equal(2, DedupeService.Apply(list));

        Assert.Equal(RecipientStatus.Ready, list.Single(r => r.RowNo == 12).Status);
        Assert.Equal(RecipientStatus.Duplicate, list.Single(r => r.RowNo == 10).Status);
        Assert.Equal(RecipientStatus.Duplicate, list.Single(r => r.RowNo == 11).Status);
    }

    [Fact]
    public void 같은_세그먼트면_엑셀_위쪽_행을_채택한다()
    {
        var list = new List<Recipient> { Make(50, SegmentCatalog.S2, "a@b.com"), Make(20, SegmentCatalog.S2, "a@b.com") };
        DedupeService.Apply(list);

        Assert.Equal(RecipientStatus.Ready, list.Single(r => r.RowNo == 20).Status);
        Assert.Equal(RecipientStatus.Duplicate, list.Single(r => r.RowNo == 50).Status);
    }

    [Fact]
    public void 탈락한_행은_삭제되지_않고_사유가_남는다()
    {
        var list = new List<Recipient> { Make(1, SegmentCatalog.S2, "a@b.com"), Make(2, SegmentCatalog.S1, "a@b.com") };
        DedupeService.Apply(list);

        var loser = list.Single(r => r.RowNo == 2);
        Assert.Equal(2, list.Count);
        Assert.Contains("1행", loser.Issue);
        Assert.Contains("신경과", loser.Issue);
    }

    [Fact]
    public void 대소문자가_달라도_같은_주소로_본다()
    {
        var list = new List<Recipient> { Make(1, SegmentCatalog.S2, "a@b.com"), Make(2, SegmentCatalog.S2, "A@B.com") };
        Assert.Equal(1, DedupeService.Apply(list));
    }

    [Fact]
    public void 이메일이_없는_행은_중복_판정에서_제외한다()
    {
        var list = new List<Recipient>
        {
            Make(1, SegmentCatalog.S2, "", RecipientStatus.NoEmail),
            Make(2, SegmentCatalog.S2, "", RecipientStatus.NoEmail),
        };
        Assert.Equal(0, DedupeService.Apply(list));
    }

    /// <summary>설계 §03: 실제 엑셀에서 중복으로 인한 초과 발송 75건이 제거된다.</summary>
    [Fact]
    public void 실제_엑셀에서_중복_75건이_제거된다()
    {
        var (recipients, summary) = SegmentClassifierTests.ImportRealExcel();

        Assert.Equal(75, summary.Duplicate);

        // 발송 대상 안에서는 주소가 유일하다 — 이것이 중복 제거의 목적이다.
        var sendable = recipients.Where(r => r.IsSendable).Select(r => r.EffectiveEmail).ToList();
        Assert.Equal(sendable.Count, sendable.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
