using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;

namespace JlkMailer.Infrastructure.Html;

/// <summary>
/// 수신자 1명 → 메일 1통. 무거운 작업은 전부 TemplateCompiler 가 끝냈고 여기서는 토큰 치환만 한다.
/// 설계 §08 두 축: 세그먼트가 '어떤 문장 세트'를, 토큰 치환이 '그 안의 값'을 정한다.
/// </summary>
public sealed class EmailComposer : IEmailComposer
{
    private readonly IReadOnlyDictionary<string, SegmentRenderPlan> _plans;
    private readonly IReadOnlyList<InlineImage> _images;
    private readonly string? _listUnsubscribe;

    public EmailComposer(IEnumerable<SegmentRenderPlan> plans, IReadOnlyList<InlineImage> images, Campaign campaign)
    {
        _plans = plans.ToDictionary(p => p.Segment, StringComparer.OrdinalIgnoreCase);
        _images = images;
        _listUnsubscribe = BuildListUnsubscribe(campaign);
    }

    /// <summary>
    /// 설계 §12. Gmail 이 '구독 취소' 버튼을 띄우는 근거이며,
    /// 수신자가 스팸 신고 대신 수신거부를 쓰게 만들어 도메인 평판을 지킨다.
    /// </summary>
    private static string? BuildListUnsubscribe(Campaign campaign)
    {
        if (!campaign.IncludeUnsubscribe || string.IsNullOrWhiteSpace(campaign.UnsubscribeTarget))
            return null;

        var t = campaign.UnsubscribeTarget.Trim();
        if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return $"<{t}>";

        var address = t.Replace("mailto:", "", StringComparison.OrdinalIgnoreCase);
        return $"<mailto:{address}?subject=unsubscribe>";
    }

    public ComposedMail Compose(Recipient recipient, Campaign campaign, MailTemplate template)
    {
        if (!_plans.TryGetValue(recipient.Segment, out var plan))
            throw new InvalidOperationException(
                $"세그먼트 '{recipient.Segment}' 의 렌더 계획이 없습니다. (행 {recipient.RowNo}, {recipient.Hospital} {recipient.Name})");

        var values = TokenValues.From(recipient, campaign);

        return new ComposedMail(
            Subject: TokenRenderer.RenderSubject(plan.SubjectTemplate, values),
            Html: TokenRenderer.RenderHtml(plan.HtmlWithTokens, values),
            PlainText: TokenRenderer.RenderPlain(plan.PlainWithTokens, values),
            Images: _images,
            ListUnsubscribe: _listUnsubscribe);
    }
}
