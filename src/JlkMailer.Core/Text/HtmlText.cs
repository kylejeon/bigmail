using System.Text;

namespace JlkMailer.Core.Text;

public static class HtmlText
{
    /// <summary>
    /// 설계 §08: 값을 HTML 에 넣기 전 &amp; &lt; &gt; " 를 이스케이프한다.
    /// 병원명에 '&amp;' 가 들어간 기관이 실제로 존재하며, 빠뜨리면 그 행부터 HTML 이 깨진다.
    /// </summary>
    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 제목용. 이스케이프하지 않되(설계 §08) CR/LF 와 제어문자는 제거한다.
    /// 헤더 인젝션 방지 — 성함/병원명에 개행이 섞여 있으면 헤더가 갈라진다.
    /// </summary>
    public static string SanitizeHeaderValue(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsControl(c) ? ' ' : c);
        return CollapseSpaces(sb.ToString());
    }

    /// <summary>
    /// 치환 후 생기는 이중 공백 정리.
    /// 행정 세그먼트는 {{진료과}} 가 빈 값이라 '병원명  성함' 처럼 공백이 겹친다.
    /// 개행은 보존하고 스페이스/탭만 접는다.
    /// </summary>
    public static string CollapseSpaces(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var c in s)
        {
            if (c is ' ' or '\t')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                // 개행 앞뒤와 문장부호 앞의 공백은 버린다
                var afterNewline = sb.Length > 0 && sb[^1] is '\n' or '\r';
                if (!afterNewline && c is not ('\n' or '\r' or ',' or '.' or '!' or '?'))
                    sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
