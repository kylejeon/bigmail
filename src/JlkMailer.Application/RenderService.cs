using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using JlkMailer.Infrastructure.Html;

namespace JlkMailer.Application;

public sealed record RenderBundle(
    TemplateAssets Assets,
    IReadOnlyList<SegmentRenderPlan> Plans,
    EmailComposer Composer)
{
    public IReadOnlyList<string> AllWarnings =>
        Assets.Warnings.Concat(Plans.SelectMany(p => p.Warnings)).ToList();
}

/// <summary>
/// 설계 §09. 이미지 처리는 캠페인당 1회, 슬롯 주입·CSS 인라인화는 세그먼트당 1회.
/// 수신자마다 반복되는 것은 토큰 치환뿐이다.
/// </summary>
public sealed class RenderService(EmailBuildOptions? options = null)
{
    private readonly TemplateCompiler _compiler = new(options);

    public RenderBundle Build(string rawHtml, Campaign campaign, IEnumerable<MailTemplate> templates)
    {
        var assets = _compiler.PrepareAssets(rawHtml);
        var plans = templates.Select(t => _compiler.CompileForSegment(assets, t, campaign)).ToList();
        return new RenderBundle(assets, plans, new EmailComposer(plans, assets.Images, campaign));
    }

    public RenderBundle BuildFromFile(string htmlPath, Campaign campaign, IEnumerable<MailTemplate> templates) =>
        Build(File.ReadAllText(htmlPath), campaign, templates);
}
