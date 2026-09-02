using JlkMailer.Core.Models;
using JlkMailer.Core.Text;
using Xunit;

namespace JlkMailer.Tests;

public class EmailAddressUtilTests
{
    [Theory]
    [InlineData("  User@Example.COM ", "user@example.com")]
    [InlineData("", "")]
    public void 정규화는_trim_후_소문자로_만든다(string input, string expected) =>
        Assert.Equal(expected, EmailAddressUtil.Normalize(input));

    /// <summary>설계 §03 표의 형식 오류 6건. 교정 결과가 표와 정확히 일치해야 한다.</summary>
    [Theory]
    [InlineData("light26@han mail.net", "light26@hanmail.net")]
    [InlineData("lizkim@han mail.net", "lizkim@hanmail.net")]
    [InlineData("shheo73@han mail.net", "shheo73@hanmail.net")]
    [InlineData("hedrik74@cha. ac.kr", "hedrik74@cha.ac.kr")]
    [InlineData("juhngsk@wonkwang@ac.kr", "juhngsk@wonkwang.ac.kr")]
    [InlineData("mkkim@chonnam@ac.kr", "mkkim@chonnam.ac.kr")]
    public void 실측_형식오류_6건을_교정_제안한다(string raw, string expected)
    {
        Assert.True(EmailAddressUtil.TrySuggestFix(raw, out var suggestion));
        Assert.Equal(expected, suggestion);
        Assert.True(EmailAddressUtil.IsValid(suggestion));
    }

    [Theory]
    [InlineData("정상주소@example.com")]   // 이미 유효하므로 제안하지 않는다
    [InlineData("@@@")]
    [InlineData("이름만있음")]
    public void 교정할_수_없거나_필요없으면_제안하지_않는다(string raw) =>
        Assert.False(EmailAddressUtil.TrySuggestFix(raw, out _));

    [Fact]
    public void 교정을_승인해야_실제_발송주소가_바뀐다()
    {
        var r = new Recipient
        {
            EmailNorm = "light26@han mail.net",
            SuggestedEmail = "light26@hanmail.net",
            Status = RecipientStatus.NeedsFix,
        };

        Assert.Equal("light26@han mail.net", r.EffectiveEmail);   // 미승인
        Assert.False(r.IsSendable);

        r.FixAccepted = true;
        r.Status = RecipientStatus.Ready;

        Assert.Equal("light26@hanmail.net", r.EffectiveEmail);
        Assert.True(r.IsSendable);
    }

    /// <summary>설계 §03: 실제 엑셀에 형식오류가 정확히 6건 있고 전부 교정 가능해야 한다.</summary>
    [Fact]
    public void 실제_엑셀의_형식오류는_6건이며_전부_교정_가능하다()
    {
        var (recipients, summary) = SegmentClassifierTests.ImportRealExcel();

        Assert.Equal(6, summary.NeedsFix);
        Assert.Equal(0, summary.Invalid);
        Assert.Equal(19, summary.NoEmail);
        Assert.All(recipients.Where(r => r.Status == RecipientStatus.NeedsFix),
                   r => Assert.True(EmailAddressUtil.IsValid(r.SuggestedEmail)));
    }
}
