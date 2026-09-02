using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;

namespace JlkMailer.Tests;

/// <summary>SMTP 응답을 대본대로 돌려주는 가짜 발송기. 실제 메일은 한 통도 나가지 않는다.</summary>
public sealed class FakeMailSender : IMailSender
{
    private readonly Queue<SendResult> _script = new();
    private readonly SendResult _fallback;

    public FakeMailSender(SendResult? fallback = null, params SendResult[] script)
    {
        _fallback = fallback ?? SendResult.Ok("<id@test>");
        foreach (var r in script) _script.Enqueue(r);
    }

    public List<string> SentTo { get; } = [];
    public List<ComposedMail> SentMails { get; } = [];
    public int ReconnectCount { get; private set; }
    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }

    public Task ReconnectAsync(CancellationToken ct = default) { ReconnectCount++; IsConnected = true; return Task.CompletedTask; }

    public Task<SendResult> SendAsync(string toAddress, string toName, ComposedMail mail, Campaign campaign,
                                      CancellationToken ct = default)
    {
        var result = _script.Count > 0 ? _script.Dequeue() : _fallback;
        if (result.Outcome == SmtpOutcome.Success)
        {
            SentTo.Add(toAddress);
            SentMails.Add(mail);
        }
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static SendResult Transient(string code = "421") =>
        new(SmtpOutcome.Transient, code, $"{code} rate limited", null);

    public static SendResult Permanent(string code = "550") =>
        new(SmtpOutcome.Permanent, code, $"{code} User unknown", null);
}
