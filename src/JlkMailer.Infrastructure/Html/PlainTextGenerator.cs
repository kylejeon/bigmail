using System.Text;
using HtmlAgilityPack;
using JlkMailer.Core.Text;

namespace JlkMailer.Infrastructure.Html;

/// <summary>
/// 설계 §09 6단계. text/plain 대체본이 없는 메일은 스팸 점수가 올라간다.
/// </summary>
public static class PlainTextGenerator
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "tr", "table", "section", "header", "footer", "blockquote",
    };

    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script", "head", "title", "meta", "link",
    };

    public static string FromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        Walk(body, sb);

        // 3줄 이상 연속 개행은 2줄로
        var lines = sb.ToString().Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        var blankRun = 0;

        foreach (var raw in lines)
        {
            var line = HtmlText.CollapseSpaces(raw);
            if (line.Length == 0)
            {
                if (++blankRun > 1) continue;
            }
            else blankRun = 0;

            output.Add(line);
        }

        return string.Join("\r\n", output).Trim();
    }

    private static void Walk(HtmlNode node, StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                    sb.Append(HtmlEntity.DeEntitize(child.InnerText).Replace(' ', ' '));
                    break;

                case HtmlNodeType.Element when SkipTags.Contains(child.Name):
                    break;

                case HtmlNodeType.Element when child.Name.Equals("br", StringComparison.OrdinalIgnoreCase):
                    sb.Append('\n');
                    break;

                case HtmlNodeType.Element when child.Name.Equals("img", StringComparison.OrdinalIgnoreCase):
                {
                    // 이미지는 alt 텍스트로 대체한다. alt 가 없으면 아무것도 남기지 않는다.
                    var alt = child.GetAttributeValue("alt", "").Trim();
                    if (alt.Length > 0) sb.Append("\n[").Append(alt).Append("]\n");
                    break;
                }

                case HtmlNodeType.Element when child.Name.Equals("a", StringComparison.OrdinalIgnoreCase):
                {
                    var text = HtmlEntity.DeEntitize(child.InnerText).Trim();
                    var href = child.GetAttributeValue("href", "").Trim();
                    sb.Append(text);
                    // 링크 주소가 본문에 이미 드러나 있으면 중복 표기하지 않는다.
                    if (href.Length > 0 && !href.StartsWith('#') && !text.Contains(href, StringComparison.OrdinalIgnoreCase))
                        sb.Append(" (").Append(href).Append(')');
                    break;
                }

                case HtmlNodeType.Element:
                {
                    var isBlock = BlockTags.Contains(child.Name);
                    if (isBlock) sb.Append('\n');
                    Walk(child, sb);
                    if (isBlock) sb.Append('\n');
                    break;
                }
            }
        }
    }
}
