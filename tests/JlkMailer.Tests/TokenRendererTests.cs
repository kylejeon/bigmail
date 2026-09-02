using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;
using Xunit;

namespace JlkMailer.Tests;

public class TokenRendererTests
{
    private static TokenValues Values(string hospital = "분당서울대학교병원", string name = "박현미",
                                      string honorific = "선생님", string dept = "신경과",
                                      string sender = "홍길동") =>
        new(hospital, name, honorific, dept, sender, "cs@jlkgroup.com");

    [Fact]
    public void HTML_본문에서는_값을_이스케이프한다()
    {
        var html = TokenRenderer.RenderHtml($"<p>{TokenRenderer.Hospital}</p>", Values(hospital: "A&B <병원>"));
        Assert.Equal("<p>A&amp;B &lt;병원&gt;</p>", html);
    }

    [Fact]
    public void 마크업은_이스케이프하지_않는다()
    {
        var template = $"{TokenRenderer.Hospital} 귀중<br><b>{TokenRenderer.Name}</b>";
        var html = TokenRenderer.RenderHtml(template, Values());
        Assert.Contains("<br>", html);
        Assert.Contains("<b>박현미</b>", html);
    }

    /// <summary>설계 §08: 제목은 이스케이프하지 않되 제어문자는 제거한다. 헤더 인젝션 방지.</summary>
    [Fact]
    public void 제목에서는_이스케이프하지_않고_개행만_제거한다()
    {
        var subject = TokenRenderer.RenderSubject(
            $"[{TokenRenderer.Hospital}] 안내", Values(hospital: "A&B\r\nBcc: attacker@evil.com"));

        Assert.DoesNotContain("&amp;", subject);
        Assert.Contains("A&B", subject);
        Assert.DoesNotContain("\r", subject);
        Assert.DoesNotContain("\n", subject);
    }

    /// <summary>행정 세그먼트는 {{진료과}} 가 빈 값이라 공백이 겹친다.</summary>
    [Fact]
    public void 빈_진료과가_이중공백을_남기지_않는다()
    {
        var template = $"{TokenRenderer.Hospital} {TokenRenderer.Dept} {TokenRenderer.Name} {TokenRenderer.Honorific}, 안녕하십니까.";
        var rendered = TokenRenderer.RenderHtml(template, Values(dept: "", honorific: "담당자님"));

        Assert.Equal("분당서울대학교병원 박현미 담당자님, 안녕하십니까.", rendered);
        Assert.DoesNotContain("  ", rendered);
    }

    [Fact]
    public void 성함이_비면_호칭만_남는다()
    {
        var template = $"{TokenRenderer.Name} {TokenRenderer.Honorific}, 안녕하십니까.";
        Assert.Equal("담당자님, 안녕하십니까.",
            TokenRenderer.RenderHtml(template, Values(name: "", honorific: "담당자님")));
    }

    [Fact]
    public void 미지의_토큰을_찾아낸다()
    {
        var unknown = TokenRenderer.FindUnknownTokens($"{TokenRenderer.Hospital} {{{{병원명2}}}} {{{{직함}}}}");
        Assert.Equal(["{{병원명2}}", "{{직함}}"], unknown);
    }

    [Fact]
    public void 알려진_토큰만_있으면_미지_토큰이_없다() =>
        Assert.Empty(TokenRenderer.FindUnknownTokens(
            string.Join(" ", TokenRenderer.Known)));

    /// <summary>세그먼트가 문장 세트를, 토큰이 값을 정한다. 설계 §08 두 축.</summary>
    [Fact]
    public void 세그먼트마다_다른_제목이_같은_수신자_값으로_렌더된다()
    {
        var recipient = new Recipient { Hospital = "서울대학교병원", Name = "김철수" };
        var campaign = new Campaign { SenderDisplayName = "홍길동" };

        var subjects = new List<string>();
        foreach (var def in SegmentCatalog.All)
        {
            recipient.Segment = def.Code;
            recipient.DeptLabel = def.DeptLabel;
            recipient.Honorific = def.Honorific;

            subjects.Add(TokenRenderer.RenderSubject(
                Application.DefaultTemplates.For(def.Code).Subject, TokenValues.From(recipient, campaign)));
        }

        Assert.All(subjects, s => Assert.Contains("서울대학교병원", s));
        Assert.Equal(subjects.Count, subjects.Distinct().Count());  // 세그먼트마다 제목이 다르다
        Assert.All(subjects, s => Assert.DoesNotContain("{{", s));
    }

    /// <summary>설계 §08: 제목은 60자 이내. 모바일에서 35~40자에 잘린다.</summary>
    [Fact]
    public void 기본_제목_문안은_60자를_넘지_않는다()
    {
        var recipient = new Recipient { Hospital = "가톨릭대학교 서울성모병원", Name = "김철수" };
        var campaign = new Campaign();

        foreach (var template in Application.DefaultTemplates.All)
        {
            var def = SegmentCatalog.Get(template.Segment);
            recipient.Segment = def.Code;
            recipient.DeptLabel = def.DeptLabel;
            recipient.Honorific = def.Honorific;

            var subject = TokenRenderer.RenderSubject(template.Subject, TokenValues.From(recipient, campaign));
            Assert.True(subject.Length <= 60, $"{def.Code} 제목이 {subject.Length}자입니다: {subject}");
        }
    }
}
