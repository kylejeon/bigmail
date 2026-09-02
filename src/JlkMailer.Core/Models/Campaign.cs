namespace JlkMailer.Core.Models;

/// <summary>설계 §06 campaigns 테이블</summary>
public sealed class Campaign
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string HtmlPath { get; set; } = "";

    /// <summary>발송 도중 템플릿 파일이 바뀌면 경고하기 위한 해시</summary>
    public string HtmlHash { get; set; } = "";

    public string FromName { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string ReplyTo { get; set; } = "";

    /// <summary>{{발신자명}} 토큰 값 — 템플릿의 ○○○ 2곳을 대체</summary>
    public string SenderDisplayName { get; set; } = "";

    /// <summary>제목 맨 앞 '(광고)' 접두어. 설계 §12 — 법무 판단에 따라 토글.</summary>
    public bool AdPrefix { get; set; }

    /// <summary>수신거부 안내 노출 및 List-Unsubscribe 헤더 부착</summary>
    public bool IncludeUnsubscribe { get; set; } = true;

    /// <summary>mailto: 또는 https: 수신거부 주소</summary>
    public string UnsubscribeTarget { get; set; } = "";

    public int DailyCap { get; set; } = 300;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
