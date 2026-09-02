using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Sending;
using JlkMailer.Infrastructure.Excel;
using JlkMailer.Infrastructure.Mail;
using JlkMailer.Presentation.Services;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>발송 대상 세그먼트 체크박스 하나.</summary>
public sealed class SegmentTarget(SegmentDef def, int count) : ObservableObject
{
    public SegmentDef Definition { get; } = def;
    public string Code => Definition.Code;
    public int Count { get; } = count;
    public string Display => $"{Definition.Code} {Definition.Name} {Count:N0}";

    private bool _selected;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public event EventHandler? SelectionChanged;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Selected)) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record LogLine(DateTime At, string State, string Email, string Message, string Severity)
{
    public string Display => $"{At:HH:mm:ss}  {Symbol} {Email,-38} {Message}";

    private string Symbol => Severity switch { "ok" => "✓", "crit" => "✗", "warn" => "↻", _ => "·" };
}

/// <summary>
/// 설계 §11 화면4(발송 설정) + 화면5(진행·로그). 한 ViewModel 을 두 View 가 공유한다.
/// 발송 루프 자체는 Application 계층의 SendOrchestrator 가 돌린다.
/// </summary>
public sealed class SendViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IDialogService _dialogs;
    private readonly ISecretService _secrets;
    private CancellationTokenSource? _cts;

    public SendViewModel(AppState state, IDialogService dialogs, ISecretService secrets)
    {
        _state = state;
        _dialogs = dialogs;
        _secrets = secrets;

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsRunning);
        TestSendCommand = new AsyncRelayCommand(TestSendAsync, () => !IsRunning && TestAddress.Length > 0);
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ExportCommand = new RelayCommand(Export);
    }

    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand TestSendCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ExportCommand { get; }

    // ---------- 계정 ----------

    private string _account = "";
    public string Account
    {
        get => _account;
        set { if (SetProperty(ref _account, value)) { _state.Campaign.FromAddress = value; Revalidate(); } }
    }

    private string _secret = "";
    /// <summary>앱 비밀번호. 메모리에만 두고, 저장은 사용자가 명시적으로 요청할 때 DPAPI 로만 한다.</summary>
    public string Secret
    {
        get => _secret;
        set { if (SetProperty(ref _secret, value)) Revalidate(); }
    }

    private bool _rememberSecret;
    public bool RememberSecret
    {
        get => _rememberSecret;
        set
        {
            if (!SetProperty(ref _rememberSecret, value)) return;
            if (value && !_secrets.CanPersist)
            {
                _rememberSecret = false;
                OnPropertyChanged(nameof(RememberSecret));
                _dialogs.ShowMessage("저장 불가", "이 환경에서는 자격증명을 안전하게 저장할 수 없습니다.");
            }
        }
    }

    private string _fromName = "제이엘케이";
    public string FromName { get => _fromName; set { if (SetProperty(ref _fromName, value)) _state.Campaign.FromName = value; } }

    private string _replyTo = "";
    public string ReplyTo { get => _replyTo; set { if (SetProperty(ref _replyTo, value)) _state.Campaign.ReplyTo = value; } }

    private string _host = "smtp.gmail.com";
    public string Host { get => _host; set => SetProperty(ref _host, value); }

    private int _port = 587;
    public int Port { get => _port; set => SetProperty(ref _port, value); }

    private bool _checkCertificateRevocation = true;
    /// <summary>
    /// 끄면 인증서 폐기 확인(CRL/OCSP)만 건너뛴다. 체인·호스트명·유효기간 검증은 유지된다.
    /// OCSP 를 차단하는 사내망에서 'incomplete certificate revocation check' 로 연결이 끊길 때 사용한다.
    /// </summary>
    public bool CheckCertificateRevocation
    {
        get => _checkCertificateRevocation;
        set { if (SetProperty(ref _checkCertificateRevocation, value)) ConnectionVerified = false; }
    }

    private string _connectionStatus = "미연결";
    public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }

    private string _connectionSeverity = "muted";
    public string ConnectionSeverity { get => _connectionSeverity; private set => SetProperty(ref _connectionSeverity, value); }

    private bool _connectionVerified;
    /// <summary>연결 테스트를 통과해야 발송 버튼이 열린다.</summary>
    public bool ConnectionVerified
    {
        get => _connectionVerified;
        private set { if (SetProperty(ref _connectionVerified, value)) Revalidate(); }
    }

    // ---------- 대상 ----------

    public ObservableCollection<SegmentTarget> Targets { get; } = [];

    private int _targetCount;
    public int TargetCount { get => _targetCount; private set => SetProperty(ref _targetCount, value); }

    // ---------- 속도 ----------

    private int _intervalSeconds = 30;
    public int IntervalSeconds { get => _intervalSeconds; set { if (SetProperty(ref _intervalSeconds, Math.Max(1, value))) UpdateEstimate(); } }

    private int _jitterSeconds = 10;
    public int JitterSeconds { get => _jitterSeconds; set { if (SetProperty(ref _jitterSeconds, Math.Max(0, value))) UpdateEstimate(); } }

    private int _dailyCap = 300;
    public int DailyCap
    {
        get => _dailyCap;
        set { if (SetProperty(ref _dailyCap, Math.Max(1, value))) { _state.Campaign.DailyCap = _dailyCap; UpdateEstimate(); } }
    }

    private bool _morningWindow = true;
    public bool MorningWindow { get => _morningWindow; set { if (SetProperty(ref _morningWindow, value)) UpdateEstimate(); } }

    private bool _afternoonWindow = true;
    public bool AfternoonWindow { get => _afternoonWindow; set { if (SetProperty(ref _afternoonWindow, value)) UpdateEstimate(); } }

    private bool _skipWeekends = true;
    public bool SkipWeekends { get => _skipWeekends; set { if (SetProperty(ref _skipWeekends, value)) UpdateEstimate(); } }

    // ---------- 법규 (설계 §12) ----------

    private bool _adPrefix;
    public bool AdPrefix { get => _adPrefix; set { if (SetProperty(ref _adPrefix, value)) { _state.Campaign.AdPrefix = value; _state.Bundle = null; } } }

    private bool _includeUnsubscribe = true;
    public bool IncludeUnsubscribe
    {
        get => _includeUnsubscribe;
        set { if (SetProperty(ref _includeUnsubscribe, value)) { _state.Campaign.IncludeUnsubscribe = value; _state.Bundle = null; } }
    }

    private string _unsubscribeTarget = "cs@jlkgroup.com";
    public string UnsubscribeTarget
    {
        get => _unsubscribeTarget;
        set { if (SetProperty(ref _unsubscribeTarget, value)) { _state.Campaign.UnsubscribeTarget = value; _state.Bundle = null; } }
    }

    private string _testAddress = "";
    public string TestAddress
    {
        get => _testAddress;
        set { if (SetProperty(ref _testAddress, value)) TestSendCommand.NotifyCanExecuteChanged(); }
    }

    private bool _testSendDone;
    /// <summary>설계 §11 화면4: 테스트 발송 없이는 본 발송을 시작할 수 없다.</summary>
    public bool TestSendDone { get => _testSendDone; private set { if (SetProperty(ref _testSendDone, value)) Revalidate(); } }

    // ---------- 진행 ----------

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            Revalidate();
            StopCommand.NotifyCanExecuteChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            TestSendCommand.NotifyCanExecuteChanged();
        }
    }

    private int _sent, _failed, _remaining;
    public int Sent { get => _sent; private set { if (SetProperty(ref _sent, value)) OnPropertyChanged(nameof(ProgressPercent)); } }
    public int Failed { get => _failed; private set { if (SetProperty(ref _failed, value)) OnPropertyChanged(nameof(ProgressPercent)); } }
    public int Remaining { get => _remaining; private set { if (SetProperty(ref _remaining, value)) OnPropertyChanged(nameof(ProgressPercent)); } }

    public double ProgressPercent
    {
        get
        {
            var total = Sent + Failed + Remaining;
            return total == 0 ? 0 : 100.0 * (Sent + Failed) / total;
        }
    }

    public ObservableCollection<LogLine> Log { get; } = [];

    private string _estimateText = "";
    public string EstimateText { get => _estimateText; private set => SetProperty(ref _estimateText, value); }

    private string _outcomeText = "";
    public string OutcomeText { get => _outcomeText; private set => SetProperty(ref _outcomeText, value); }

    public bool CanStart =>
        !IsRunning && ConnectionVerified && TestSendDone && TargetCount > 0 &&
        _state.Bundle is not null && Account.Length > 0;

    private void Revalidate()
    {
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    public void Reload()
    {
        Targets.Clear();
        foreach (var def in SegmentCatalog.All)
        {
            var count = _state.Recipients.Count(r => r.IsSendable && r.Segment == def.Code);
            if (count == 0) continue;

            var target = new SegmentTarget(def, count) { Selected = def.SendByDefault };
            target.SelectionChanged += (_, _) => UpdateEstimate();
            Targets.Add(target);
        }

        Account = _state.Campaign.FromAddress;
        if (Account.Length > 0) Secret = _secrets.Load(Account) ?? "";

        UpdateEstimate();
    }

    private ThrottlePolicy BuildPolicy()
    {
        var windows = new List<TimeWindow>();
        if (MorningWindow) windows.Add(new TimeWindow(new TimeOnly(9, 0), new TimeOnly(11, 0)));
        if (AfternoonWindow) windows.Add(new TimeWindow(new TimeOnly(14, 0), new TimeOnly(16, 0)));
        if (windows.Count == 0) windows.Add(new TimeWindow(new TimeOnly(9, 0), new TimeOnly(18, 0)));

        return new ThrottlePolicy
        {
            IntervalSeconds = IntervalSeconds,
            JitterSeconds = JitterSeconds,
            DailyCap = DailyCap,
            SkipWeekends = SkipWeekends,
            Windows = windows,
        };
    }

    private List<Recipient> SelectedRecipients()
    {
        var codes = Targets.Where(t => t.Selected).Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
        return _state.Recipients.Where(r => r.IsSendable && codes.Contains(r.Segment)).ToList();
    }

    private void UpdateEstimate()
    {
        TargetCount = SelectedRecipients().Count;

        var policy = BuildPolicy();
        var perDay = Math.Min(DailyCap, EstimateWindowCapacity(policy));
        var days = perDay <= 0 ? 0 : (int)Math.Ceiling((double)TargetCount / perDay);
        var hours = TargetCount * policy.IntervalSeconds / 3600.0;

        EstimateText = TargetCount == 0
            ? "대상 세그먼트를 선택하세요."
            : $"{TargetCount:N0}통 × {policy.IntervalSeconds}초 ≈ {hours:F1}시간 · 일 상한 적용 시 약 {days}일 · " +
              $"다음 발송 가능 {policy.NextOpening(DateTime.Now):MM-dd HH:mm}";

        Revalidate();
    }

    /// <summary>허용 시간대 안에서 하루에 실제로 보낼 수 있는 통수.</summary>
    private static int EstimateWindowCapacity(ThrottlePolicy policy)
    {
        var seconds = policy.Windows.Sum(w => (w.End.ToTimeSpan() - w.Start.ToTimeSpan()).TotalSeconds);
        return (int)(seconds / Math.Max(1, policy.IntervalSeconds));
    }

    private SmtpOptions BuildSmtpOptions() => new()
    {
        Host = Host,
        Port = Port,
        UserName = Account,
        Secret = Secret,
        AuthMode = SmtpAuthMode.AppPassword,
        CheckCertificateRevocation = CheckCertificateRevocation,
    };

    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "연결 중…";
        ConnectionSeverity = "muted";

        try
        {
            await using var sender = new MailKitSender(BuildSmtpOptions());
            await sender.ConnectAsync();

            ConnectionStatus = "연결됨";
            ConnectionSeverity = "ok";
            ConnectionVerified = true;

            if (RememberSecret && _secrets.CanPersist) _secrets.Save(Account, Secret);
        }
        catch (Exception ex) when (ex is MailAuthenticationFailedException or SmtpConnectionFailedException)
        {
            ConnectionStatus = ex.Message;
            ConnectionSeverity = "crit";
            ConnectionVerified = false;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"연결 실패: {ex.Message}";
            ConnectionSeverity = "crit";
            ConnectionVerified = false;
        }
    }

    private async Task TestSendAsync()
    {
        if (_state.Bundle is null)
        {
            _dialogs.ShowMessage("미리보기 필요", "템플릿 화면에서 먼저 미리보기를 생성하세요.");
            return;
        }

        try
        {
            var recipient = SelectedRecipients().FirstOrDefault() ?? _state.Recipients.First(r => r.IsSendable);
            var mail = _state.Bundle.Composer.Compose(recipient, _state.Campaign, DefaultTemplates.For(recipient.Segment));

            await using var sender = new MailKitSender(BuildSmtpOptions());
            await sender.ConnectAsync();
            var result = await sender.SendAsync(TestAddress, "테스트", mail, _state.Campaign);

            if (result.Outcome == SmtpOutcome.Success)
            {
                TestSendDone = true;
                _dialogs.ShowMessage("테스트 발송 완료",
                    $"{TestAddress} 로 보냈습니다.\n\n설계 §09 검증: Gmail·Outlook·네이버·모바일에서 레이아웃을 확인한 뒤 본 발송을 시작하세요.");
            }
            else
            {
                _dialogs.ShowMessage("테스트 발송 실패", $"{result.Code} {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("테스트 발송 실패", ex.Message);
        }
    }

    private async Task StartAsync()
    {
        if (_state.Bundle is null) { _dialogs.ShowMessage("미리보기 필요", "템플릿 화면에서 먼저 미리보기를 생성하세요."); return; }

        var targets = SelectedRecipients();
        var policy = BuildPolicy();

        if (!_dialogs.Confirm("발송 시작",
                $"{targets.Count:N0}명에게 발송을 시작합니다.\n\n" +
                $"간격 {policy.IntervalSeconds}±{policy.JitterSeconds}초 · 일 상한 {policy.DailyCap}통\n" +
                $"시간대 {string.Join(", ", policy.Windows)}\n" +
                (AdPrefix ? "제목에 (광고) 접두어가 붙습니다.\n" : "") +
                "\n한 번 나간 메일은 되돌릴 수 없습니다."))
            return;

        _state.Store.UpsertCampaign(_state.Campaign);
        _state.Store.ReplaceRecipients(_state.Recipients);

        // ReplaceRecipients 가 새 id 를 부여하므로 다시 읽어 매핑한다.
        var stored = _state.Store.GetRecipients();
        var byKey = stored.ToDictionary(r => (r.RowNo, r.EmailNorm));
        foreach (var t in targets)
            if (byKey.TryGetValue((t.RowNo, t.EmailNorm), out var s)) t.Id = s.Id;

        var enqueued = _state.Store.EnqueueMissing(_state.Campaign.Id, targets);
        Append("info", "", $"큐에 {enqueued:N0}건 추가 (이미 처리된 {targets.Count - enqueued:N0}건은 건너뜀)", "muted");

        IsRunning = true;
        OutcomeText = "";
        _cts = new CancellationTokenSource();

        try
        {
            await using var sender = new MailKitSender(BuildSmtpOptions());
            var orchestrator = new SendOrchestrator(_state.Store, sender, _state.Bundle.Composer, policy);

            var progress = new Progress<SendProgress>(p =>
            {
                Sent = p.Sent;
                Failed = p.Failed;
                Remaining = p.Remaining;

                var severity = p.LastState switch
                {
                    SendState.Sent => "ok",
                    SendState.Retrying => "warn",
                    SendState.Failed or SendState.Bounced => "crit",
                    _ => "muted",
                };
                Append(p.LastState.ToString(), p.LastEmail, p.LastMessage, severity);
            });

            var outcome = await orchestrator.RunAsync(
                _state.Campaign,
                stored.ToDictionary(r => r.Id),
                _state.Store.GetTemplates().ToDictionary(t => t.Segment),
                progress,
                _cts.Token);

            OutcomeText = Describe(outcome);
            Append("종료", "", OutcomeText,
                outcome.Reason is StopReason.CircuitBreakerTripped or StopReason.AuthenticationFailed ? "crit" : "muted");

            if (outcome.Reason is StopReason.CircuitBreakerTripped or StopReason.AuthenticationFailed)
                _dialogs.ShowMessage("발송 중단", OutcomeText);
        }
        catch (Exception ex)
        {
            OutcomeText = $"발송 중 오류: {ex.Message}";
            Append("오류", "", ex.Message, "crit");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static string Describe(SendOutcome outcome) => outcome.Reason switch
    {
        StopReason.Completed => $"완료 — 성공 {outcome.Sent:N0} / 실패 {outcome.Failed:N0}",
        StopReason.DailyCapReached => $"오늘 상한에 도달했습니다. {outcome.Detail}",
        StopReason.OutsideWindow => $"발송 시간대가 아닙니다. {outcome.Detail}",
        StopReason.CircuitBreakerTripped => $"연속 실패로 중단했습니다. {outcome.Detail}",
        StopReason.AuthenticationFailed => $"인증 실패로 중단했습니다. {outcome.Detail}",
        StopReason.Cancelled => $"중지했습니다 — 성공 {outcome.Sent:N0} / 실패 {outcome.Failed:N0}",
        _ => outcome.Detail ?? "",
    };

    private void Append(string state, string email, string message, string severity)
    {
        Log.Insert(0, new LogLine(DateTime.Now, state, email, message, severity));
        while (Log.Count > 2000) Log.RemoveAt(Log.Count - 1);   // 메모리 보호
    }

    private void Stop() => _cts?.Cancel();

    private void Export()
    {
        var path = _dialogs.SaveFile("발송 결과 저장", "Excel 파일 (*.xlsx)|*.xlsx",
            $"발송결과_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        if (path is null) return;

        try
        {
            ResultExporter.Export(path, _state.Store.GetRecipients(), _state.Store.GetLog(_state.Campaign.Id));
            _dialogs.ShowMessage("저장 완료", path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("저장 실패", ex.Message);
        }
    }
}
