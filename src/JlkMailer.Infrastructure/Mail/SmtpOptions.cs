namespace JlkMailer.Infrastructure.Mail;

public enum SmtpAuthMode
{
    /// <summary>Google Workspace 앱 비밀번호. 계정에 2단계 인증이 켜져 있어야 발급된다.</summary>
    AppPassword,
    /// <summary>SASL XOAUTH2. 조직 정책으로 앱 비밀번호가 막혀 있을 때의 경로. 설계 §04.</summary>
    OAuth2,
}

/// <summary>설계 §04 발송 채널: Google Workspace SMTP.</summary>
public sealed class SmtpOptions
{
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;
    public bool UseStartTls { get; init; } = true;

    /// <summary>발송 계정 주소. 인증 사용자명이기도 하다.</summary>
    public string UserName { get; init; } = "";

    /// <summary>앱 비밀번호 또는 OAuth2 액세스 토큰. DPAPI 로 복호화한 평문이 들어온다.</summary>
    public string Secret { get; init; } = "";

    public SmtpAuthMode AuthMode { get; init; } = SmtpAuthMode.AppPassword;

    /// <summary>설계 §10: 커넥션을 유지하되 이 통수마다 재연결한다.</summary>
    public int ReconnectEvery { get; init; } = 100;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 서버 인증서의 폐기 여부(CRL/OCSP)까지 확인할지. 기본은 확인함.
    ///
    /// macOS 의 .NET 은 폐기 조회를 완결하지 못해 정상적인 Gmail 인증서에서도
    /// 'incomplete certificate revocation check' 로 연결이 끊긴다.
    /// OCSP 를 막는 사내망에서도 같은 증상이 난다.
    ///
    /// 이 값을 false 로 두어도 인증서 체인·호스트명·유효기간 검증은 그대로 수행된다.
    /// 건너뛰는 것은 '이 인증서가 폐기되었는가' 하나뿐이다.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;
}

/// <summary>인증 실패는 재시도 대상이 아니다. 즉시 발송을 멈춰야 한다.</summary>
public sealed class MailAuthenticationFailedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>TLS 연결 실패. 원인과 다음 조치를 메시지에 담는다.</summary>
public sealed class SmtpConnectionFailedException(string message, Exception? inner = null)
    : Exception(message, inner);
