using HtmlAgilityPack;

namespace JlkMailer.Infrastructure.Html;

internal static class HtmlDomHelpers
{
    /// <summary>class 속성에 해당 클래스가 '단어로' 포함된 노드. contains() 만 쓰면 'body' 가 'bodytext' 에도 걸린다.</summary>
    public static IEnumerable<HtmlNode> ByClass(HtmlNode root, string className)
    {
        var xpath = $".//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
        return root.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();
    }

    public static HtmlNode? FirstByClass(HtmlNode root, string className) =>
        ByClass(root, className).FirstOrDefault();

    public static HtmlNode? BySlot(HtmlNode root, string slot) =>
        root.SelectSingleNode($".//*[@data-slot='{slot}']");

    /// <summary>style 속성을 'prop:value' 사전으로. 순서를 보존한다.</summary>
    public static List<KeyValuePair<string, string>> ParseStyle(string? style)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(style)) return list;

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf(':');
            if (i <= 0) continue;
            list.Add(new(part[..i].Trim().ToLowerInvariant(), part[(i + 1)..].Trim()));
        }
        return list;
    }

    public static string BuildStyle(IEnumerable<KeyValuePair<string, string>> declarations) =>
        string.Join(";", declarations.Select(d => $"{d.Key}:{d.Value}"));

    /// <summary>지정한 속성들을 제거한 style 문자열.</summary>
    public static string StripDeclarations(string? style, params string[] properties)
    {
        var set = new HashSet<string>(properties, StringComparer.OrdinalIgnoreCase);
        return BuildStyle(ParseStyle(style).Where(d => !set.Contains(d.Key)));
    }

    public static string? GetDeclaration(string? style, string property) =>
        ParseStyle(style).FirstOrDefault(d => d.Key.Equals(property, StringComparison.OrdinalIgnoreCase)).Value;
}
