using System.Text.RegularExpressions;

namespace JlkMailer.Core.Text;

/// <summary>
/// 이메일 정규화·검증·자동 교정. 설계 §03.
/// 교정은 '제안'만 만들고 적용하지 않는다 — 오타 교정이 남의 주소로 보내는 사고가 되면 안 된다.
/// </summary>
public static partial class EmailAddressUtil
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>중복 판정 및 send_log UNIQUE 키. lower(trim()).</summary>
    public static string Normalize(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string? email)
    {
        var e = (email ?? "").Trim();
        return e.Length > 0 && e.Length <= 254 && ValidPattern().IsMatch(e);
    }

    /// <summary>
    /// 실측 형식오류 6건에 대응하는 교정 규칙(설계 §03 표).
    ///   1) 주소 내부 공백 제거          — 'light26@han mail.net'   → 'light26@hanmail.net'
    ///   2) 두 번째 '@' 를 '.' 로 치환   — 'juhngsk@wonkwang@ac.kr' → 'juhngsk@wonkwang.ac.kr'
    /// 교정 결과가 유효할 때만 제안을 돌려준다.
    /// </summary>
    public static bool TrySuggestFix(string? raw, out string suggestion)
    {
        suggestion = "";
        var e = (raw ?? "").Trim();
        if (e.Length == 0) return false;

        // 1) 공백 제거
        var fixedValue = Whitespace().Replace(e, "");

        // 2) '@' 가 2개면 두 번째를 '.' 으로
        if (fixedValue.Count(c => c == '@') == 2)
        {
            var second = fixedValue.IndexOf('@', fixedValue.IndexOf('@') + 1);
            fixedValue = string.Concat(fixedValue.AsSpan(0, second), ".", fixedValue.AsSpan(second + 1));
        }

        fixedValue = Normalize(fixedValue);
        if (fixedValue == Normalize(e) || !IsValid(fixedValue)) return false;

        suggestion = fixedValue;
        return true;
    }
}
