using System.Text.RegularExpressions;
using JlkMailer.Core.Models;

namespace JlkMailer.Core.Text;

/// <summary>토큰 치환에 쓰이는 값 묶음. 설계 §08 표.</summary>
public sealed record TokenValues(
    string Hospital,
    string Name,
    string Honorific,
    string Dept,
    string SenderName,
    string UnsubscribeUrl)
{
    public static TokenValues From(Recipient r, Campaign c) => new(
        Hospital: r.Hospital,
        Name: r.Name,
        Honorific: r.Honorific,
        Dept: r.DeptLabel,
        SenderName: c.SenderDisplayName,
        UnsubscribeUrl: c.UnsubscribeTarget);
}

/// <summary>
/// {{토큰}} 치환. 설계 §08.
/// 두 개의 축 중 '값을 채우는' 쪽. 어떤 문장 세트를 쓸지는 SegmentClassifier 가 정한다.
/// Core 에 있으므로 외부 의존이 없고 단위 테스트로 고정된다.
/// </summary>
public static partial class TokenRenderer
{
    public const string Hospital = "{{병원명}}";
    public const string Name = "{{성함}}";
    public const string Honorific = "{{호칭}}";
    public const string Dept = "{{진료과}}";
    public const string SenderName = "{{발신자명}}";
    public const string UnsubscribeUrl = "{{수신거부URL}}";

    public static readonly IReadOnlyList<string> Known =
        [Hospital, Name, Honorific, Dept, SenderName, UnsubscribeUrl];

    [GeneratedRegex(@"\{\{[^{}]{1,40}\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>
    /// HTML 본문용. 값을 HTML 이스케이프해서 넣는다.
    /// 치환 후 이중 공백을 접는다 — 행정 세그먼트는 {{진료과}} 가 빈 값이다.
    /// </summary>
    public static string RenderHtml(string template, TokenValues v) =>
        Render(template, v, escape: true);

    /// <summary>
    /// 제목(Subject)용. 이스케이프하지 않고 원문 그대로 넣되 제어문자를 제거한다.
    /// RFC 2047 인코딩은 MimeKit 이 처리한다.
    /// </summary>
    public static string RenderSubject(string template, TokenValues v) =>
        HtmlText.SanitizeHeaderValue(Render(template, v, escape: false));

    /// <summary>text/plain 대체본용. 이스케이프 없이, 공백만 정리.</summary>
    public static string RenderPlain(string template, TokenValues v) =>
        Render(template, v, escape: false);

    private static string Render(string template, TokenValues v, bool escape)
    {
        if (string.IsNullOrEmpty(template)) return "";

        string E(string s) => escape ? HtmlText.Escape(s) : s ?? "";

        var s = template
            .Replace(Hospital, E(v.Hospital))
            .Replace(Name, E(v.Name))
            .Replace(Honorific, E(v.Honorific))
            .Replace(Dept, E(v.Dept))
            .Replace(SenderName, E(v.SenderName))
            .Replace(UnsubscribeUrl, v.UnsubscribeUrl ?? "");

        return HtmlText.CollapseSpaces(s);
    }

    /// <summary>
    /// 템플릿에 들어 있는 미지의 토큰 목록. 편집 UI 에서 오타를 잡기 위한 검증.
    /// 렌더링은 미지 토큰을 그대로 두므로, 검증 없이 보내면 수신자가 {{병원명}} 을 그대로 읽게 된다.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownTokens(string? template)
    {
        if (string.IsNullOrEmpty(template)) return [];
        return TokenPattern().Matches(template)
            .Select(m => m.Value)
            .Where(t => !Known.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
