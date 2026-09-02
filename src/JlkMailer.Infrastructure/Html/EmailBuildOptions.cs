namespace JlkMailer.Infrastructure.Html;

/// <summary>설계 §09 이미지 처리 목표치.</summary>
public sealed class EmailBuildOptions
{
    /// <summary>레티나 대응 2배 폭. 표시폭 620px 기준.</summary>
    public int MaxImageWidth { get; init; } = 1240;

    /// <summary>본문 표시폭. img width 속성에 들어간다(Outlook 은 CSS 대신 속성을 본다).</summary>
    public int DisplayWidth { get; init; } = 620;

    /// <summary>JPEG 품질. 의료영상 스크린샷이라 아티팩트가 보이면 낮추지 말고 PNG 유지로 전환할 것.</summary>
    public int JpegQuality { get; init; } = 82;

    /// <summary>true 면 JPEG 로 재인코딩하지 않고 PNG 를 유지한 채 폭만 줄인다.</summary>
    public bool KeepPng { get; init; }

    /// <summary>Gmail 본문 클리핑 한계(102KB)에 대한 경고 임계값.</summary>
    public int GmailClipWarningBytes { get; init; } = 102 * 1024;

    /// <summary>Outlook(Word 엔진) 폴백 적용 여부. 끄면 원본 CSS 그대로 나간다.</summary>
    public bool ApplyOutlookFallbacks { get; init; } = true;
}
