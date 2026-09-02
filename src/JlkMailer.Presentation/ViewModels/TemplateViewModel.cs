using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;
using JlkMailer.Presentation.Services;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>세그먼트 목록 항목. 건수를 함께 보여줘 어느 문안이 몇 명에게 가는지 알 수 있게 한다.</summary>
public sealed class SegmentItem(SegmentDef def, int count) : ObservableObject
{
    public SegmentDef Definition { get; } = def;
    public string Code => Definition.Code;
    public string Display => $"{Definition.Code} {Definition.Name}   {Count:N0}";

    private int _count = count;
    public int Count { get => _count; set { if (SetProperty(ref _count, value)) OnPropertyChanged(nameof(Display)); } }
}

/// <summary>설계 §11 화면3 — 템플릿 편집·미리보기.</summary>
public sealed class TemplateViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IDialogService _dialogs;

    /// <summary>설계 §08: 모바일에서 35~40자, Gmail 웹에서 약 70자에 잘린다. 60자를 권장 상한으로 둔다.</summary>
    public const int SubjectSoftLimit = 60;
    public const int MobilePreviewLength = 38;

    public TemplateViewModel(AppState state, IDialogService dialogs)
    {
        _state = state;
        _dialogs = dialogs;

        RebuildCommand = new RelayCommand(Rebuild);
        ResetSegmentCommand = new RelayCommand(ResetSegment);
        SavePreviewCommand = new RelayCommand(SavePreview, () => _lastHtml.Length > 0);
    }

    public IRelayCommand RebuildCommand { get; }
    public IRelayCommand ResetSegmentCommand { get; }
    public IRelayCommand SavePreviewCommand { get; }

    public ObservableCollection<SegmentItem> SegmentItems { get; } = [];
    public IReadOnlyList<string> Tokens => TokenRenderer.Known;

    private SegmentItem? _selectedSegment;
    public SegmentItem? SelectedSegment
    {
        get => _selectedSegment;
        set { if (SetProperty(ref _selectedSegment, value)) LoadSegment(); }
    }

    private MailTemplate? _current;

    private string _subject = "";
    public string Subject
    {
        get => _subject;
        set
        {
            if (!SetProperty(ref _subject, value)) return;
            if (_current is not null) _current.Subject = value;
            OnPropertyChanged(nameof(SubjectLength));
            OnPropertyChanged(nameof(SubjectLengthText));
            OnPropertyChanged(nameof(SubjectTooLong));
            OnPropertyChanged(nameof(MobilePreview));
            Invalidate();
        }
    }

    private string _greeting = "";
    public string Greeting
    {
        get => _greeting;
        set { if (SetProperty(ref _greeting, value)) { if (_current is not null) _current.Greeting = value; Invalidate(); } }
    }

    private string _intro = "";
    public string Intro
    {
        get => _intro;
        set { if (SetProperty(ref _intro, value)) { if (_current is not null) _current.Intro = value; Invalidate(); } }
    }

    private string _benefitLead = "";
    public string BenefitLead
    {
        get => _benefitLead;
        set { if (SetProperty(ref _benefitLead, value)) { if (_current is not null) _current.BenefitLead = value; Invalidate(); } }
    }

    private string _closing = "";
    public string Closing
    {
        get => _closing;
        set { if (SetProperty(ref _closing, value)) { if (_current is not null) _current.Closing = value; Invalidate(); } }
    }

    private string _senderName = "";
    /// <summary>{{발신자명}} — 템플릿의 ○○○ 2곳을 대체한다.</summary>
    public string SenderName
    {
        get => _senderName;
        set { if (SetProperty(ref _senderName, value)) { _state.Campaign.SenderDisplayName = value; Invalidate(); } }
    }

    public int SubjectLength => RenderedSubject.Length;
    public string SubjectLengthText => $"{SubjectLength} / {SubjectSoftLimit}자";
    public bool SubjectTooLong => SubjectLength > SubjectSoftLimit;

    /// <summary>모바일 절단 미리보기. 설계 §08.</summary>
    public string MobilePreview => RenderedSubject.Length <= MobilePreviewLength
        ? RenderedSubject
        : RenderedSubject[..MobilePreviewLength] + "…";

    private string _renderedSubject = "";
    public string RenderedSubject { get => _renderedSubject; private set => SetProperty(ref _renderedSubject, value); }

    private string _lastHtml = "";
    /// <summary>WebView2 등 미리보기 컨트롤에 넣을 HTML.</summary>
    public string PreviewHtml { get => _lastHtml; private set => SetProperty(ref _lastHtml, value); }

    private string _previewPlain = "";
    public string PreviewPlain { get => _previewPlain; private set => SetProperty(ref _previewPlain, value); }

    private string _previewRecipientText = "";
    public string PreviewRecipientText { get => _previewRecipientText; private set => SetProperty(ref _previewRecipientText, value); }

    private string _sizeText = "";
    public string SizeText { get => _sizeText; private set => SetProperty(ref _sizeText, value); }

    private string _sizeSeverity = "muted";
    public string SizeSeverity { get => _sizeSeverity; private set => SetProperty(ref _sizeSeverity, value); }

    private bool _stale = true;
    /// <summary>편집 후 다시 만들어야 하는 상태. 발송 화면은 Stale 이면 시작을 막는다.</summary>
    public bool Stale { get => _stale; private set => SetProperty(ref _stale, value); }

    public ObservableCollection<string> Warnings { get; } = [];

    public void Reload()
    {
        SegmentItems.Clear();
        foreach (var def in SegmentCatalog.All)
        {
            var count = _state.Recipients.Count(r => r.Segment == def.Code && r.IsSendable);
            SegmentItems.Add(new SegmentItem(def, count));
        }

        SenderName = _state.Campaign.SenderDisplayName;
        SelectedSegment = SegmentItems.FirstOrDefault(s => s.Count > 0) ?? SegmentItems.FirstOrDefault();
    }

    private void LoadSegment()
    {
        if (SelectedSegment is null) return;

        _current = _state.Templates.FirstOrDefault(t => t.Segment == SelectedSegment.Code);
        if (_current is null)
        {
            _current = DefaultTemplates.For(SelectedSegment.Code);
            _state.Templates.Add(_current);
        }

        _subject = _current.Subject;
        _greeting = _current.Greeting;
        _intro = _current.Intro;
        _benefitLead = _current.BenefitLead;
        _closing = _current.Closing;

        OnPropertyChanged(nameof(Subject));
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(BenefitLead));
        OnPropertyChanged(nameof(Closing));

        Rebuild();
    }

    private void Invalidate()
    {
        Stale = true;
        UpdateSubjectPreview();
    }

    private Recipient PreviewRecipient()
    {
        var real = _state.Recipients.FirstOrDefault(
            r => r.IsSendable && r.Segment == (SelectedSegment?.Code ?? SegmentCatalog.S2));

        if (real is not null) return real;

        var def = SegmentCatalog.Get(SelectedSegment?.Code ?? SegmentCatalog.S2);
        return new Recipient
        {
            Hospital = "분당서울대학교병원",
            Name = "박○○",
            DeptRaw = def.DeptLabel,
            Segment = def.Code,
            DeptLabel = def.DeptLabel,
            Honorific = def.Honorific,
            EmailNorm = "sample@example.com",
            Status = RecipientStatus.Ready,
        };
    }

    private void UpdateSubjectPreview()
    {
        var values = TokenValues.From(PreviewRecipient(), _state.Campaign);
        RenderedSubject = TokenRenderer.RenderSubject(Subject, values);

        OnPropertyChanged(nameof(SubjectLength));
        OnPropertyChanged(nameof(SubjectLengthText));
        OnPropertyChanged(nameof(SubjectTooLong));
        OnPropertyChanged(nameof(MobilePreview));
    }

    /// <summary>
    /// 설계 §09 파이프라인 실행. 이미지 처리가 들어 있어 몇 초 걸릴 수 있으므로 편집할 때마다가 아니라
    /// 사용자가 명시적으로 누를 때 돈다.
    /// </summary>
    private void Rebuild()
    {
        Warnings.Clear();

        if (!File.Exists(_state.HtmlPath))
        {
            Warnings.Add("HTML 템플릿 파일을 찾을 수 없습니다.");
            return;
        }

        try
        {
            var bundle = new RenderService().BuildFromFile(_state.HtmlPath, _state.Campaign, _state.Templates);
            _state.Bundle = bundle;

            var recipient = PreviewRecipient();
            var mail = bundle.Composer.Compose(recipient, _state.Campaign, DefaultTemplates.For(recipient.Segment));

            PreviewHtml = mail.Html;
            PreviewPlain = mail.PlainText;
            RenderedSubject = mail.Subject;
            PreviewRecipientText = $"{recipient.Hospital} {recipient.Name} · {recipient.EffectiveEmail}";

            var htmlKb = Encoding.UTF8.GetByteCount(mail.Html) / 1024;
            var imageKb = mail.Images.Sum(i => i.Bytes.Length) / 1024;

            SizeText = $"본문 {htmlKb} KB · 이미지 CID {mail.Images.Count}개 {imageKb} KB · text/plain {(mail.PlainText.Length > 0 ? "생성됨" : "없음")}";
            SizeSeverity = htmlKb >= 102 ? "crit" : htmlKb >= 80 ? "warn" : "ok";

            foreach (var w in bundle.AllWarnings.Where(w => !w.StartsWith("[정보]"))) Warnings.Add(w);

            Stale = false;
            SavePreviewCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(SubjectLength));
            OnPropertyChanged(nameof(SubjectLengthText));
            OnPropertyChanged(nameof(SubjectTooLong));
            OnPropertyChanged(nameof(MobilePreview));
        }
        catch (Exception ex)
        {
            Warnings.Add($"미리보기 생성 실패: {ex.Message}");
            Stale = true;
        }
    }

    private void ResetSegment()
    {
        if (SelectedSegment is null) return;
        if (!_dialogs.Confirm("문안 초기화", $"{SelectedSegment.Code} 문안을 기본값으로 되돌립니다.")) return;

        var fresh = DefaultTemplates.For(SelectedSegment.Code);
        var index = _state.Templates.FindIndex(t => t.Segment == SelectedSegment.Code);
        if (index >= 0) _state.Templates[index] = fresh; else _state.Templates.Add(fresh);

        LoadSegment();
    }

    private void SavePreview()
    {
        var path = _dialogs.SaveFile("미리보기 저장", "HTML 파일 (*.html)|*.html",
            $"미리보기_{SelectedSegment?.Code ?? "S"}.html");
        if (path is null) return;

        File.WriteAllText(path, PreviewHtml, new UTF8Encoding(false));
        _dialogs.ShowMessage("저장 완료",
            $"{path}\n\n설계 §09 검증: 이 파일을 Gmail·Outlook·네이버·모바일 4곳에서 실제로 확인하세요.");
    }

    /// <summary>다음 단계로 넘어가기 전 문안을 DB 에 저장한다.</summary>
    public void Persist()
    {
        foreach (var template in _state.Templates) _state.Store.SaveTemplate(template);
    }
}
