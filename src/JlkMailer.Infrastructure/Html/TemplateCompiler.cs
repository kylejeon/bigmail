using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;
using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;
using PreMailer.Net;

namespace JlkMailer.Infrastructure.Html;

public sealed record TemplateStats(int HtmlBytes, int ImageBytes, int ImageCount)
{
    public int TotalBytes => HtmlBytes + ImageBytes;
}

/// <summary>이미지 처리까지 끝난 공통 자산. 세그먼트와 무관하므로 캠페인당 1회만 만든다.</summary>
public sealed record TemplateAssets(
    string HtmlWithCids,
    IReadOnlyList<InlineImage> Images,
    IReadOnlyList<string> Warnings,
    string SourceHash);

/// <summary>세그먼트별로 슬롯이 채워지고 CSS 인라인화까지 끝난 결과. 남은 것은 {{토큰}} 치환뿐.</summary>
public sealed record SegmentRenderPlan(
    string Segment,
    string SubjectTemplate,
    string HtmlWithTokens,
    string PlainWithTokens,
    TemplateStats Stats,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 설계 §09 HTML 메일 변환 파이프라인.
///   1) 파싱  2) 리사이즈·재인코딩  3) CID 치환  4) CSS 인라인화  5) Outlook 폴백  6) text/plain
/// 이미지 처리(1~3)는 캠페인당 1회, 슬롯 주입과 인라인화(4~6)는 세그먼트당 1회 수행한다.
/// 수신자 1,778명마다 반복하면 안 된다.
/// </summary>
public sealed class TemplateCompiler
{
    private readonly EmailBuildOptions _options;

    public TemplateCompiler(EmailBuildOptions? options = null) => _options = options ?? new EmailBuildOptions();

    /// <summary>슬롯 이름 → 앵커를 찾는 방법. data-slot 속성이 있으면 그쪽이 우선한다.</summary>
    private static readonly (string Slot, string Class, int Index)[] SlotAnchors =
    [
        ("greeting", "body", 0),      // .body 의 첫 <p>
        ("intro", "body", 1),         // .body 의 둘째 <p>
        ("closing", "closing", 0),    // .closing 의 첫 <p>
    ];

    // ---------- 1~3단계: 이미지 ----------

    public TemplateAssets PrepareAssets(string rawHtml)
    {
        var warnings = new List<string>();
        var doc = Load(rawHtml);
        var images = new List<InlineImage>();

        var imgNodes = doc.DocumentNode.SelectNodes("//img") ?? new HtmlNodeCollection(null);
        var index = 0;

        foreach (var img in imgNodes)
        {
            var src = img.GetAttributeValue("src", "");
            if (!src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"외부 URL 이미지가 있습니다. 수신자가 '이미지 표시'를 눌러야 보입니다: {src}");
                continue;
            }

            var comma = src.IndexOf(',');
            if (comma < 0) { warnings.Add("깨진 data URI 이미지를 건너뜁니다."); continue; }

            byte[] source;
            try { source = Convert.FromBase64String(src[(comma + 1)..]); }
            catch (FormatException) { warnings.Add("base64 디코딩에 실패한 이미지를 건너뜁니다."); continue; }

            var optimized = ImageOptimizer.Optimize(source, _options);
            var cid = $"img{++index:00}@jlk-ctp";
            var fileName = $"jlk-ctp-{index:00}.{optimized.Extension}";

            images.Add(new InlineImage(cid, fileName, optimized.MediaType, optimized.Bytes));

            img.SetAttributeValue("src", $"cid:{cid}");
            img.SetAttributeValue("border", "0");
            // width 속성은 CSS 인라인화 이후에 OutlookFallback 이 넣는다.
            // 여기서 넣으면 PreMailer 가 CSS 의 width:100% 로 덮어써 버린다.

            var style = img.GetAttributeValue("style", "");
            if (!style.Contains("max-width", StringComparison.OrdinalIgnoreCase))
                img.SetAttributeValue("style", $"{style};width:100%;max-width:{_options.DisplayWidth}px;height:auto;display:block".TrimStart(';'));

            var savedPercent = 100.0 * (1 - (double)optimized.Bytes.Length / source.Length);
            warnings.Add($"[정보] 이미지 {index}: {source.Length / 1024}KB → {optimized.Bytes.Length / 1024}KB ({savedPercent:F0}% 감소, {optimized.Width}×{optimized.Height})");
        }

        if (images.Count == 0)
            warnings.Add("인라인 이미지를 찾지 못했습니다. 템플릿이 예상과 다릅니다.");

        StripFontImports(doc, warnings);

        return new TemplateAssets(doc.DocumentNode.OuterHtml, images, warnings, Sha256(rawHtml));
    }

    /// <summary>
    /// 설계 §09 4단계. @import 구글폰트는 메일 클라이언트가 거의 로드하지 않으므로 제거하고
    /// font-family 폴백 스택만 남긴다. 남겨두면 일부 클라이언트에서 스팸 점수가 올라간다.
    /// </summary>
    private static void StripFontImports(HtmlDocument doc, List<string> warnings)
    {
        var styles = doc.DocumentNode.SelectNodes("//style");
        if (styles is null) return;

        foreach (var style in styles)
        {
            var css = style.InnerHtml;
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                css, @"@import\s+url\([^)]*\)\s*;?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (cleaned == css) continue;
            style.InnerHtml = cleaned;
            warnings.Add("[정보] @import 웹폰트 선언을 제거했습니다. font-family 폴백 스택으로 표시됩니다.");
        }
    }

    // ---------- 4~6단계: 슬롯 주입 · 인라인화 · 폴백 · 평문 ----------

    public SegmentRenderPlan CompileForSegment(TemplateAssets assets, MailTemplate template, Campaign campaign)
    {
        var warnings = new List<string>();
        var doc = Load(assets.HtmlWithCids);
        var root = doc.DocumentNode;

        InjectSlots(root, template, campaign, warnings);

        // CSS 인라인화. @media 는 인라인이 불가능하므로 <style> 은 남긴다(모바일 대응).
        var inlined = PreMailer.Net.PreMailer.MoveCssInline(
            doc.DocumentNode.OuterHtml,
            removeStyleElements: false,
            ignoreElements: null,
            css: null,
            stripIdAndClassAttributes: false,
            removeComments: false);

        foreach (var w in inlined.Warnings) warnings.Add($"[PreMailer] {w}");

        var doc2 = Load(inlined.Html);

        if (_options.ApplyOutlookFallbacks)
            OutlookFallback.Apply(doc2.DocumentNode, _options, warnings);

        var html = doc2.DocumentNode.OuterHtml;
        var plain = PlainTextGenerator.FromHtml(html);

        var htmlBytes = Encoding.UTF8.GetByteCount(html);
        var imageBytes = assets.Images.Sum(i => i.Bytes.Length);
        var stats = new TemplateStats(htmlBytes, imageBytes, assets.Images.Count);

        if (htmlBytes > _options.GmailClipWarningBytes)
            warnings.Add($"[경고] 본문이 {htmlBytes / 1024}KB 입니다. Gmail 은 102KB 를 넘으면 본문을 자릅니다.");

        var unknown = TokenRenderer.FindUnknownTokens(html);
        if (unknown.Count > 0)
            warnings.Add($"[경고] 알 수 없는 토큰: {string.Join(", ", unknown)} — 수신자가 이 문자열을 그대로 읽게 됩니다.");

        var subject = template.Subject;
        if (campaign.AdPrefix && !subject.StartsWith("(광고)", StringComparison.Ordinal))
            subject = "(광고) " + subject;

        return new SegmentRenderPlan(template.Segment, subject, html, plain, stats, warnings);
    }

    private static void InjectSlots(HtmlNode root, MailTemplate template, Campaign campaign, List<string> warnings)
    {
        SetSlot(root, "greeting", template.Greeting, warnings);
        SetSlot(root, "intro", template.Intro, warnings);
        SetSlot(root, "closing", template.Closing, warnings);

        // 서명의 ○○○ 자리. 템플릿 원본은 <p class="sig-name">○○○ 배상</p>.
        var sig = HtmlDomHelpers.BySlot(root, "signature") ?? HtmlDomHelpers.FirstByClass(root, "sig-name");
        if (sig is not null) sig.InnerHtml = $"{TokenRenderer.SenderName} 배상";
        else warnings.Add("서명(.sig-name) 앵커를 찾지 못했습니다. {{발신자명}} 이 치환되지 않습니다.");

        // 장점 블록 위 리드 문장. 원본에는 없는 요소이므로 있을 때만 추가한다.
        if (!string.IsNullOrWhiteSpace(template.BenefitLead))
        {
            var benefits = HtmlDomHelpers.FirstByClass(root, "benefits");
            if (benefits?.ParentNode is not null)
            {
                var p = root.OwnerDocument.CreateElement("p");
                p.SetAttributeValue("class", "benefit-lead");
                p.SetAttributeValue("style", "font-size:15px;line-height:1.85;color:#333E4C;margin:0 0 4px 0;padding:0 32px");
                p.InnerHtml = template.BenefitLead;
                benefits.ParentNode.InsertBefore(p, benefits);
            }
            else warnings.Add(".benefits 앵커를 찾지 못해 리드 문장을 넣지 못했습니다.");
        }

        if (campaign.IncludeUnsubscribe)
            AppendUnsubscribe(root, campaign, warnings);
    }

    private static void SetSlot(HtmlNode root, string slot, string content, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        var node = HtmlDomHelpers.BySlot(root, slot);
        if (node is null)
        {
            var anchor = SlotAnchors.FirstOrDefault(a => a.Slot == slot);
            if (anchor.Class is not null)
            {
                var container = HtmlDomHelpers.FirstByClass(root, anchor.Class);
                var paragraphs = container?.SelectNodes("./p");
                if (paragraphs is not null && paragraphs.Count > anchor.Index)
                    node = paragraphs[anchor.Index];
            }
        }

        if (node is null)
        {
            warnings.Add($"슬롯 '{slot}' 앵커를 찾지 못했습니다. 템플릿 구조가 바뀌었다면 data-slot=\"{slot}\" 속성을 추가하세요.");
            return;
        }

        node.InnerHtml = content;
    }

    /// <summary>
    /// 설계 §12. 본문 하단 수신거부 안내. List-Unsubscribe 헤더는 MailKitSender 가 붙인다.
    /// 스팸 신고 대신 수신거부를 유도해 도메인 평판을 지키는 장치다.
    /// </summary>
    private static void AppendUnsubscribe(HtmlNode root, Campaign campaign, List<string> warnings)
    {
        var footer = HtmlDomHelpers.BySlot(root, "unsubscribe") ?? HtmlDomHelpers.FirstByClass(root, "footer");
        if (footer is null) { warnings.Add(".footer 를 찾지 못해 수신거부 안내를 넣지 못했습니다."); return; }

        var target = campaign.UnsubscribeTarget;
        var href = target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"mailto:{target}?subject=수신거부";

        var p = root.OwnerDocument.CreateElement("p");
        p.SetAttributeValue("class", "unsubscribe");
        p.SetAttributeValue("style",
            "margin:18px 0 0 0;font-size:11px;line-height:1.7;color:#7F8FA6;text-align:center");
        p.InnerHtml =
            "본 메일은 (주)제이엘케이가 발송한 제품 안내 메일입니다.<br>" +
            $"수신을 원하지 않으시면 <a href=\"{HtmlText.Escape(href)}\" style=\"color:#9FB3CC;text-decoration:underline\">수신거부</a>를 클릭해 주십시오.";

        footer.AppendChild(p);
    }

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument
        {
            OptionOutputOriginalCase = true,
            OptionWriteEmptyNodes = true,
            OptionFixNestedTags = true,
        };
        doc.LoadHtml(html);
        return doc;
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
