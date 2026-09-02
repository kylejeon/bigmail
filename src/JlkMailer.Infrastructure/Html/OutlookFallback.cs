using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace JlkMailer.Infrastructure.Html;

/// <summary>
/// 설계 §09 5단계. Outlook 데스크톱은 Word 렌더링 엔진이라
/// flex · grid · linear-gradient · box-shadow · border-radius 를 무시한다.
/// 무시당해도 읽히도록 구조를 바꾼다. CSS 인라인화(PreMailer) 이후에 실행되어야 한다.
/// </summary>
public static partial class OutlookFallback
{
    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    public static void Apply(HtmlNode root, EmailBuildOptions options, List<string> warnings)
    {
        AddGradientFallbacks(root, warnings);
        FixImageWidths(root, options.DisplayWidth);
        FlexRowToTable(root, "benefit", numberCellClass: "num", contentCellClass: "content", gapPx: 16, warnings);
        FlexGridToTable(root, "addr-grid", columns: 2, warnings);
        NormalizeInlineFlexBadges(root);
    }

    /// <summary>
    /// linear-gradient 는 Outlook 에서 '배경 없음'이 된다.
    /// 히어로는 흰 배경에 흰 글씨가 되어 글자가 사라지므로, 그라디언트의 첫 색을 background-color 로 깔아준다.
    /// </summary>
    private static void AddGradientFallbacks(HtmlNode root, List<string> warnings)
    {
        var nodes = root.SelectNodes(".//*[contains(@style,'linear-gradient')]");
        if (nodes is null) return;

        foreach (var node in nodes)
        {
            var style = node.GetAttributeValue("style", "");
            var declarations = HtmlDomHelpers.ParseStyle(style);

            if (declarations.Any(d => d.Key == "background-color")) continue;

            var gradient = declarations.FirstOrDefault(d => d.Value.Contains("linear-gradient", StringComparison.OrdinalIgnoreCase));
            var match = HexColor().Match(gradient.Value ?? "");
            if (!match.Success)
            {
                warnings.Add($"linear-gradient 에서 폴백 색을 찾지 못했습니다: <{node.Name} class=\"{node.GetAttributeValue("class", "")}\">");
                continue;
            }

            // background-color 가 먼저 와야 Outlook 이 그것을 쓰고, 나머지 클라이언트는 뒤의 gradient 를 쓴다.
            declarations.Insert(0, new("background-color", match.Value));
            node.SetAttributeValue("style", HtmlDomHelpers.BuildStyle(declarations));
        }
    }

    /// <summary>
    /// display:flex 로 좌(번호)·우(내용) 2열을 만든 블록을 table 로 바꾼다.
    /// Outlook 에서는 flex 가 무시되어 번호가 내용 위에 얹히는데, table 이면 의도대로 나온다.
    /// </summary>
    private static void FlexRowToTable(HtmlNode root, string containerClass, string numberCellClass,
                                       string contentCellClass, int gapPx, List<string> warnings)
    {
        foreach (var container in HtmlDomHelpers.ByClass(root, containerClass).ToList())
        {
            if (container.ParentNode is null) continue;

            var numNode = HtmlDomHelpers.FirstByClass(container, numberCellClass);
            var contentNode = HtmlDomHelpers.FirstByClass(container, contentCellClass);
            if (numNode is null || contentNode is null)
            {
                warnings.Add($".{containerClass} 안에서 .{numberCellClass} / .{contentCellClass} 를 찾지 못해 table 변환을 건너뜁니다.");
                continue;
            }

            var containerStyle = HtmlDomHelpers.StripDeclarations(
                container.GetAttributeValue("style", ""), "display", "gap", "align-items", "justify-content");

            var rawNumStyle = numNode.GetAttributeValue("style", "");
            var numWidth = HtmlDomHelpers.GetDeclaration(rawNumStyle, "width") ?? "34px";
            var numHeight = HtmlDomHelpers.GetDeclaration(rawNumStyle, "height") ?? numWidth;

            // 원형 배지의 스타일은 td 가 아니라 그 안의 div 에 건다.
            // td 에 걸면 셀이 행 높이(오른쪽 본문 높이)까지 늘어나면서 원이 세로로 긴 타원이 된다.
            // flex 정렬은 table 에서 쓸 수 없으므로 text-align + line-height 로 대체한다.
            var badgeStyle = HtmlDomHelpers.StripDeclarations(
                rawNumStyle, "display", "align-items", "justify-content", "flex", "margin");
            badgeStyle += $";width:{numWidth};height:{numHeight};text-align:center;" +
                          $"line-height:{numHeight};mso-line-height-rule:exactly";

            var contentStyle = HtmlDomHelpers.StripDeclarations(contentNode.GetAttributeValue("style", ""), "flex");

            var doc = container.OwnerDocument;
            var table = doc.CreateElement("table");
            table.SetAttributeValue("role", "presentation");
            table.SetAttributeValue("cellpadding", "0");
            table.SetAttributeValue("cellspacing", "0");
            table.SetAttributeValue("border", "0");
            table.SetAttributeValue("width", "100%");
            table.SetAttributeValue("class", container.GetAttributeValue("class", ""));
            table.SetAttributeValue("style", $"border-collapse:collapse;width:100%;{containerStyle}");

            var tbody = doc.CreateElement("tbody");
            var tr = doc.CreateElement("tr");

            var badge = doc.CreateElement("div");
            badge.SetAttributeValue("style", badgeStyle.TrimStart(';'));
            badge.InnerHtml = numNode.InnerHtml;

            var tdNum = doc.CreateElement("td");
            tdNum.SetAttributeValue("valign", "top");
            tdNum.SetAttributeValue("width", numWidth.Replace("px", ""));
            // 셀 자체에는 크기만 준다. 배경·라운딩은 안쪽 div 가 갖는다.
            tdNum.SetAttributeValue("style", $"width:{numWidth};padding:0");
            tdNum.AppendChild(badge);

            var tdGap = doc.CreateElement("td");
            tdGap.SetAttributeValue("width", gapPx.ToString());
            tdGap.SetAttributeValue("style", $"width:{gapPx}px;font-size:0;line-height:0");
            tdGap.InnerHtml = "&nbsp;";

            var tdContent = doc.CreateElement("td");
            tdContent.SetAttributeValue("valign", "top");
            tdContent.SetAttributeValue("style", contentStyle);
            tdContent.InnerHtml = contentNode.InnerHtml;

            tr.AppendChild(tdNum);
            tr.AppendChild(tdGap);
            tr.AppendChild(tdContent);
            tbody.AppendChild(tr);
            table.AppendChild(tbody);

            container.ParentNode.ReplaceChild(table, container);
        }
    }

    /// <summary>flex-wrap 으로 만든 N열 그리드를 table 로. 푸터 주소 4블록(2×2)에 해당한다.</summary>
    private static void FlexGridToTable(HtmlNode root, string containerClass, int columns, List<string> warnings)
    {
        foreach (var container in HtmlDomHelpers.ByClass(root, containerClass).ToList())
        {
            if (container.ParentNode is null) continue;

            var children = container.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
            if (children.Count == 0)
            {
                warnings.Add($".{containerClass} 에 자식 요소가 없어 table 변환을 건너뜁니다.");
                continue;
            }

            var doc = container.OwnerDocument;
            var table = doc.CreateElement("table");
            table.SetAttributeValue("role", "presentation");
            table.SetAttributeValue("cellpadding", "0");
            table.SetAttributeValue("cellspacing", "0");
            table.SetAttributeValue("border", "0");
            table.SetAttributeValue("width", "100%");
            table.SetAttributeValue("class", container.GetAttributeValue("class", ""));
            table.SetAttributeValue("style",
                "border-collapse:collapse;width:100%;" +
                HtmlDomHelpers.StripDeclarations(container.GetAttributeValue("style", ""), "display", "gap", "flex-wrap"));

            var tbody = doc.CreateElement("tbody");
            var cellWidth = $"{Math.Round(100.0 / columns, 2)}%";

            for (var i = 0; i < children.Count; i += columns)
            {
                var tr = doc.CreateElement("tr");
                for (var c = 0; c < columns; c++)
                {
                    var td = doc.CreateElement("td");
                    td.SetAttributeValue("valign", "top");
                    td.SetAttributeValue("width", cellWidth);
                    var childStyle = i + c < children.Count
                        ? HtmlDomHelpers.StripDeclarations(children[i + c].GetAttributeValue("style", ""), "flex")
                        : "";
                    td.SetAttributeValue("style", $"width:{cellWidth};padding:0 12px 14px 0;{childStyle}");
                    td.InnerHtml = i + c < children.Count ? children[i + c].InnerHtml : "&nbsp;";
                    tr.AppendChild(td);
                }
                tbody.AppendChild(tr);
            }

            table.AppendChild(tbody);
            container.ParentNode.ReplaceChild(table, container);
        }
    }

    /// <summary>
    /// Outlook 은 img 의 width 속성을 CSS 보다 신뢰하는데, 퍼센트 값을 주면 원본 픽셀 폭(여기서는 1240px)으로
    /// 튀어 레이아웃이 무너진다. 픽셀 값으로 고정한다.
    /// PreMailer 가 CSS 의 width:100% 를 width 속성으로 옮기므로, 반드시 인라인화 '이후'에 실행해야 한다.
    /// </summary>
    private static void FixImageWidths(HtmlNode root, int displayWidth)
    {
        var images = root.SelectNodes(".//img");
        if (images is null) return;

        foreach (var img in images)
        {
            var width = img.GetAttributeValue("width", "");
            if (width.Length == 0 || width.Contains('%'))
                img.SetAttributeValue("width", displayWidth.ToString());

            img.Attributes.Remove("height");   // 높이를 고정하면 리사이즈 후 비율이 깨진다
        }
    }

    /// <summary>inline-flex 배지는 Outlook 에서 블록이 되어 폭 전체를 먹는다. inline-block 으로 낮춘다.</summary>
    private static void NormalizeInlineFlexBadges(HtmlNode root)
    {
        var nodes = root.SelectNodes(".//*[contains(@style,'inline-flex')]");
        if (nodes is null) return;

        foreach (var node in nodes)
        {
            var declarations = HtmlDomHelpers.ParseStyle(node.GetAttributeValue("style", ""));
            for (var i = 0; i < declarations.Count; i++)
                if (declarations[i].Key == "display" && declarations[i].Value.Contains("inline-flex"))
                    declarations[i] = new("display", "inline-block");
            node.SetAttributeValue("style", HtmlDomHelpers.BuildStyle(declarations));
        }
    }
}
