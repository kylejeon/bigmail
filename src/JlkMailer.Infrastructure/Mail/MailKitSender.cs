using System.Net.Sockets;
using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace JlkMailer.Infrastructure.Mail;

/// <summary>
/// MailKit 기반 SMTP 발송. 설계 §04 · §09 · §10.
/// multipart/alternative(HTML + text) 안에 multipart/related(CID 이미지)를 넣는 구조를 만든다.
/// </summary>
public sealed class MailKitSender(SmtpOptions options) : IMailSender
{
    private readonly SmtpClient _client = new() { Timeout = (int)options.Timeout.TotalMilliseconds };
    private int _sentOnConnection;

    public bool IsConnected => _client is { IsConnected: true, IsAuthenticated: true };

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_client.IsConnected) await _client.DisconnectAsync(true, ct);

        _client.CheckCertificateRevocation = options.CheckCertificateRevocation;

        var security = options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;

        try
        {
            await _client.ConnectAsync(options.Host, options.Port, security, ct);
        }
        catch (SslHandshakeException ex) when (options.CheckCertificateRevocation)
        {
            // macOS 의 .NET 은 폐기 조회를 완결하지 못해 정상 인증서에서도 여기서 끊긴다.
            // 사용자가 무엇을 해야 하는지 알려주지 않으면 막다른 길이 된다.
            throw new SmtpConnectionFailedException(
                $"{options.Host}:{options.Port} 와 TLS 연결에 실패했습니다.\n{ex.Message}\n\n" +
                "인증서 폐기 확인(CRL/OCSP)이 원인이라면 폐기 확인만 끄고 재시도할 수 있습니다. " +
                "체인·호스트명·유효기간 검증은 그대로 유지됩니다. " +
                "CLI 는 --no-crl-check, 앱은 '인증서 폐기 확인' 체크 해제입니다.",
                ex);
        }

        try
        {
            if (options.AuthMode == SmtpAuthMode.OAuth2)
                await _client.AuthenticateAsync(new SaslMechanismOAuth2(options.UserName, options.Secret), ct);
            else
                await _client.AuthenticateAsync(options.UserName, options.Secret, ct);
        }
        catch (AuthenticationException ex)
        {
            throw new MailAuthenticationFailedException(
                options.AuthMode == SmtpAuthMode.AppPassword
                    ? "SMTP 인증에 실패했습니다. 앱 비밀번호가 맞는지, 조직 정책으로 앱 비밀번호가 차단되지 않았는지 확인하세요."
                    : "SMTP OAuth2 인증에 실패했습니다. 액세스 토큰이 만료되었을 수 있습니다.",
                ex);
        }

        _sentOnConnection = 0;
    }

    public async Task ReconnectAsync(CancellationToken ct = default) => await ConnectAsync(ct);

    public async Task<SendResult> SendAsync(string toAddress, string toName, ComposedMail mail, Campaign campaign,
                                            CancellationToken ct = default)
    {
        if (!IsConnected) await ConnectAsync(ct);
        else if (options.ReconnectEvery > 0 && _sentOnConnection >= options.ReconnectEvery) await ReconnectAsync(ct);

        var message = BuildMessage(toAddress, toName, mail, campaign);

        try
        {
            var response = await _client.SendAsync(message, ct);
            _sentOnConnection++;
            return SendResult.Ok(message.MessageId, string.IsNullOrWhiteSpace(response) ? "250 OK" : response);
        }
        catch (SmtpCommandException ex)
        {
            var code = (int)ex.StatusCode;
            // 4xx 는 일시 오류이므로 재시도, 5xx 는 영구 오류이므로 즉시 중단. 설계 §10.
            var outcome = code is >= 400 and < 500 ? SmtpOutcome.Transient : SmtpOutcome.Permanent;
            return new SendResult(outcome, code.ToString(), $"{code} {ex.Message}".Trim(), null);
        }
        catch (SmtpProtocolException ex)
        {
            return new SendResult(SmtpOutcome.Transient, "PROTO", $"SMTP 프로토콜 오류: {ex.Message}", null);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new SendResult(SmtpOutcome.Transient, "CONN", $"연결 오류: {ex.Message}", null);
        }
    }

    private static MimeMessage BuildMessage(string toAddress, string toName, ComposedMail mail, Campaign campaign)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(campaign.FromName, campaign.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));

        if (!string.IsNullOrWhiteSpace(campaign.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(campaign.ReplyTo));

        // RFC 2047 인코딩은 MimeKit 이 처리한다. 제목은 원문 그대로 넣는다. 설계 §08.
        message.Subject = mail.Subject;

        if (!string.IsNullOrWhiteSpace(mail.ListUnsubscribe))
        {
            message.Headers.Add("List-Unsubscribe", mail.ListUnsubscribe);
            // One-Click 은 https 엔드포인트일 때만 의미가 있다.
            if (mail.ListUnsubscribe.Contains("http", StringComparison.OrdinalIgnoreCase))
                message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        var builder = new BodyBuilder
        {
            HtmlBody = mail.Html,
            TextBody = mail.PlainText,
        };

        foreach (var image in mail.Images)
        {
            var resource = builder.LinkedResources.Add(image.FileName, image.Bytes,
                ContentType.Parse(image.MediaType));
            resource.ContentId = image.ContentId;
            // 첨부 목록에 파일로 노출되지 않게 한다. 본문 안에서만 참조된다.
            resource.ContentDisposition = new MimeKit.ContentDisposition(MimeKit.ContentDisposition.Inline)
            {
                FileName = image.FileName,
            };
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            try { await _client.DisconnectAsync(true); }
            catch { /* 종료 중 오류는 무시한다 */ }
        }
        _client.Dispose();
    }
}
