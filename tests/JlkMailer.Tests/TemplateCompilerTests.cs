using System.Text;
using System.Text.RegularExpressions;
using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Infrastructure.Html;
using Xunit;

namespace JlkMailer.Tests;

public class TemplateCompilerTests
{
    private static Campaign SampleCampaign() => new()
    {
        Id = 1,
        FromName = "제이엘케이",
        FromAddress = "cs@jlkgroup.com",
        SenderDisplayName = "홍길동",
        IncludeUnsubscribe = true,
        UnsubscribeTarget = "cs@jlkgroup.com",
    };

    private static Recipient SampleRecipient(string segment = SegmentCatalog.S2) => new()
    {
        Hospital = "분당서울대학교병원",
        Name = "박현미",
        DeptRaw = "신경과",
        Segment = segment,
        DeptLabel = SegmentCatalog.Get(segment).DeptLabel,
        Honorific = SegmentCatalog.Get(segment).Honorific,
        EmailNorm = "test@example.com",
    };

    private static RenderBundle Build() =>
        new RenderService().BuildFromFile(TestData.Html, SampleCampaign(), DefaultTemplates.All);

    /// <summary>설계 §02 차단 1 / §09: 852KB base64 → CID 첨부. 이것이 이 앱의 존재 이유 중 하나다.</summary>
    [Fact]
    public void base64_인라인_이미지가_CID_첨부로_바뀐다()
    {
        var bundle = Build();

        Assert.Equal(3, bundle.Assets.Images.Count);
        Assert.All(bundle.Assets.Images, i => Assert.NotEmpty(i.ContentId));

        var html = bundle.Plans[0].HtmlWithTokens;
        Assert.DoesNotContain("data:image", html);
        Assert.Equal(3, Regex.Matches(html, @"src=""cid:").Count);

        foreach (var image in bundle.Assets.Images)
            Assert.Contains($"cid:{image.ContentId}", html);
    }

    /// <summary>Gmail 은 본문 102KB 를 넘으면 자른다. 변환 후 본문은 그 한참 아래여야 한다.</summary>
    [Fact]
    public void 변환_후_본문이_Gmail_클리핑_한계보다_훨씬_작다()
    {
        var bundle = Build();
        var originalBytes = new FileInfo(TestData.Html).Length;

        Assert.True(originalBytes > 800 * 1024, "원본은 800KB 를 넘는 파일이어야 한다");

        foreach (var plan in bundle.Plans)
        {
            Assert.True(plan.Stats.HtmlBytes < 102 * 1024,
                $"{plan.Segment} 본문이 {plan.Stats.HtmlBytes / 1024}KB 입니다.");
        }
    }

    /// <summary>설계 §09 목표: 이미지 843KB → 180KB 이하.</summary>
    [Fact]
    public void 이미지_총량이_180KB_이하로_줄어든다()
    {
        var bundle = Build();
        var total = bundle.Assets.Images.Sum(i => i.Bytes.Length);
        Assert.True(total <= 180 * 1024, $"이미지 총량이 {total / 1024}KB 입니다.");
    }

    [Fact]
    public void 웹폰트_import_가_제거된다() =>
        Assert.DoesNotContain("@import", Build().Plans[0].HtmlWithTokens);

    /// <summary>@media 는 인라인할 수 없으므로 style 블록은 남겨야 모바일 레이아웃이 산다.</summary>
    [Fact]
    public void 미디어쿼리는_style_블록에_남는다() =>
        Assert.Contains("@media", Build().Plans[0].HtmlWithTokens);

    /// <summary>Outlook 은 linear-gradient 를 무시한다. 폴백 색이 없으면 흰 배경에 흰 글씨가 된다.</summary>
    [Fact]
    public void 그라디언트에_배경색_폴백이_붙는다()
    {
        var html = Build().Plans[0].HtmlWithTokens;
        var gradients = Regex.Matches(html, @"style=""[^""]*linear-gradient[^""]*""");

        Assert.NotEmpty(gradients);
        foreach (Match m in gradients)
            Assert.Contains("background-color:", m.Value);
    }

    /// <summary>Outlook 은 flex 를 무시한다. 번호와 내용이 세로로 쌓이지 않도록 table 로 바꾼다.</summary>
    [Fact]
    public void flex_블록이_table_로_바뀐다()
    {
        var html = Build().Plans[0].HtmlWithTokens;
        Assert.Equal(3, Regex.Matches(html, @"<table[^>]*class=""benefit""").Count);
        Assert.DoesNotContain("<div class=\"benefit\"", html);
    }

    /// <summary>Outlook 은 img 의 퍼센트 폭에서 원본 픽셀로 튄다. 픽셀 고정이 필요하다.</summary>
    [Fact]
    public void 이미지_폭이_픽셀로_고정된다()
    {
        var html = Build().Plans[0].HtmlWithTokens;
        foreach (Match m in Regex.Matches(html, @"<img[^>]*>"))
        {
            Assert.Contains("width=\"620\"", m.Value);
            Assert.DoesNotContain("height=", m.Value);
        }
    }

    /// <summary>
    /// 회귀 방지: 원형 배지 스타일을 td 에 걸면 셀이 행 높이(오른쪽 본문 높이)까지 늘어나
    /// 원이 세로로 긴 타원이 된다. 배지는 반드시 셀 안의 고정 크기 div 여야 한다.
    /// </summary>
    [Fact]
    public void 번호_배지가_td가_아니라_고정크기_div_에_그려진다()
    {
        var html = Build().Plans[0].HtmlWithTokens;
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var badgeCells = doc.DocumentNode.SelectNodes("//table[contains(@class,'benefit')]//td[1]");
        Assert.NotNull(badgeCells);
        Assert.Equal(3, badgeCells.Count);

        foreach (var td in badgeCells)
        {
            var tdStyle = td.GetAttributeValue("style", "");

            // td 에는 크기만. 배경·라운딩이 있으면 셀 전체가 칠해진다.
            Assert.DoesNotContain("border-radius", tdStyle);
            Assert.DoesNotContain("background", tdStyle);

            var badge = td.SelectSingleNode("./div");
            Assert.NotNull(badge);

            var badgeStyle = badge.GetAttributeValue("style", "");
            Assert.Contains("border-radius", badgeStyle);
            Assert.Contains("width:34px", badgeStyle);
            Assert.Contains("height:34px", badgeStyle);
            Assert.Contains("line-height:34px", badgeStyle);   // 세로 가운데 정렬 (flex 대체)
            Assert.Contains("text-align:center", badgeStyle);  // 가로 가운데 정렬
            Assert.DoesNotContain("display:flex", badgeStyle);
        }

        // 번호가 1·2·3 순서로 남아 있다
        Assert.Equal(["1", "2", "3"], badgeCells.Select(td => td.InnerText.Trim()).ToArray());
    }

    [Fact]
    public void text_plain_대체본이_생성된다()
    {
        var plan = Build().Plans.Single(p => p.Segment == SegmentCatalog.S2);

        Assert.NotEmpty(plan.PlainWithTokens);
        Assert.DoesNotContain("<", plan.PlainWithTokens);
        Assert.Contains("[JLK-CTP 판독 화면]", plan.PlainWithTokens);   // img alt 로 대체
        Assert.Contains("비급여로 처방이 가능합니다", plan.PlainWithTokens);
    }

    [Fact]
    public void 수신거부_안내와_List_Unsubscribe_가_붙는다()
    {
        var bundle = Build();
        var mail = bundle.Composer.Compose(SampleRecipient(), SampleCampaign(), DefaultTemplates.For(SegmentCatalog.S2));

        Assert.Contains("수신거부", mail.Html);
        Assert.Equal("<mailto:cs@jlkgroup.com?subject=unsubscribe>", mail.ListUnsubscribe);
    }

    [Fact]
    public void 수신거부를_끄면_아무것도_붙지_않는다()
    {
        var campaign = SampleCampaign();
        campaign.IncludeUnsubscribe = false;

        var bundle = new RenderService().BuildFromFile(TestData.Html, campaign, DefaultTemplates.All);
        var mail = bundle.Composer.Compose(SampleRecipient(), campaign, DefaultTemplates.For(SegmentCatalog.S2));

        Assert.Null(mail.ListUnsubscribe);
        Assert.DoesNotContain("class=\"unsubscribe\"", mail.Html);
    }

    /// <summary>템플릿의 ○○○ 자리표시자가 하나도 남으면 안 된다.</summary>
    [Fact]
    public void 자리표시자와_미치환_토큰이_남지_않는다()
    {
        var bundle = Build();

        foreach (var def in SegmentCatalog.All)
        {
            var recipient = SampleRecipient(def.Code);
            var mail = bundle.Composer.Compose(recipient, SampleCampaign(), DefaultTemplates.For(def.Code));

            Assert.DoesNotContain("○○○", mail.Html);
            Assert.DoesNotContain("{{", mail.Html);
            Assert.DoesNotContain("{{", mail.Subject);
            Assert.DoesNotContain("{{", mail.PlainText);
            Assert.Contains("홍길동", mail.Html);
            Assert.Contains(recipient.Hospital, mail.Html);
        }
    }

    /// <summary>설계 §12: (광고) 접두어는 토글이며, 켜면 제목 맨 앞에 붙는다.</summary>
    [Fact]
    public void 광고_접두어_토글이_제목에_반영된다()
    {
        var campaign = SampleCampaign();
        campaign.AdPrefix = true;

        var bundle = new RenderService().BuildFromFile(TestData.Html, campaign, DefaultTemplates.All);
        var mail = bundle.Composer.Compose(SampleRecipient(), campaign, DefaultTemplates.For(SegmentCatalog.S2));

        Assert.StartsWith("(광고)", mail.Subject);
    }

    /// <summary>병원명에 &amp; 가 들어가도 HTML 이 깨지지 않는다. 설계 §08 경고.</summary>
    [Fact]
    public void 특수문자가_든_병원명이_HTML을_깨뜨리지_않는다()
    {
        var bundle = Build();
        var recipient = SampleRecipient();
        recipient.Hospital = "A&B <메디컬> \"센터\"";

        var mail = bundle.Composer.Compose(recipient, SampleCampaign(), DefaultTemplates.For(SegmentCatalog.S2));

        Assert.Contains("A&amp;B &lt;메디컬&gt;", mail.Html);
        Assert.DoesNotContain("<메디컬>", mail.Html);
        Assert.Contains("A&B <메디컬>", mail.Subject);   // 제목은 원문 그대로
    }

    [Fact]
    public void 세그먼트별로_도입_문단이_실제로_다르다()
    {
        var bundle = Build();
        var bodies = new List<string>();

        foreach (var def in SegmentCatalog.All.Where(d => d.Code != SegmentCatalog.S7))
        {
            var mail = bundle.Composer.Compose(SampleRecipient(def.Code), SampleCampaign(), DefaultTemplates.For(def.Code));
            bodies.Add(mail.Html);
        }

        Assert.Equal(bodies.Count, bodies.Distinct().Count());
    }

    [Fact]
    public void 렌더_계획이_없는_세그먼트는_명확한_예외를_던진다()
    {
        var bundle = Build();
        var recipient = SampleRecipient();
        recipient.Segment = "S99";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            bundle.Composer.Compose(recipient, SampleCampaign(), DefaultTemplates.For("S99")));

        Assert.Contains("S99", ex.Message);
    }
}
