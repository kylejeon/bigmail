using CommunityToolkit.Mvvm.ComponentModel;
using JlkMailer.Application;
using JlkMailer.Core.Models;
using JlkMailer.Infrastructure.Storage;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>
/// 화면 사이를 넘나드는 세션 상태. 설계 §11 의 5개 화면이 공유한다.
/// 좌→우 단방향 진행이므로 앞 단계 결과가 뒤 단계의 전제가 된다.
/// </summary>
public sealed class AppState : ObservableObject, IDisposable
{
    public Campaign Campaign { get; } = new()
    {
        Name = "JLK-CTP 소개",
        FromName = "제이엘케이",
        IncludeUnsubscribe = true,
        UnsubscribeTarget = "cs@jlkgroup.com",
        DailyCap = 300,
    };

    public List<Recipient> Recipients { get; private set; } = [];
    public List<MailTemplate> Templates { get; } = DefaultTemplates.All.Select(t => t.Clone()).ToList();

    private ImportSummary? _summary;
    public ImportSummary? Summary { get => _summary; set => SetProperty(ref _summary, value); }

    private RenderBundle? _bundle;
    /// <summary>템플릿 화면에서 만들어지고 발송 화면이 소비한다. 템플릿을 고치면 무효화된다.</summary>
    public RenderBundle? Bundle { get => _bundle; set => SetProperty(ref _bundle, value); }

    private SqliteCampaignStore? _store;
    public SqliteCampaignStore Store => _store ??= OpenStore();

    private string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JlkMailer", "campaign.db");

    public string DatabasePath
    {
        get => _databasePath;
        set
        {
            if (!SetProperty(ref _databasePath, value)) return;
            _store?.Dispose();
            _store = null;
        }
    }

    public string ExcelPath { get; set; } = "";
    public string HtmlPath { get; set; } = "";

    public void SetRecipients(List<Recipient> recipients, ImportSummary summary)
    {
        Recipients = recipients;
        Summary = summary;
        Bundle = null;   // 대상이 바뀌면 렌더 결과도 무효
        OnPropertyChanged(nameof(Recipients));
    }

    private SqliteCampaignStore OpenStore()
    {
        var store = new SqliteCampaignStore(DatabasePath);
        store.Initialize();
        return store;
    }

    public void Dispose() => _store?.Dispose();
}
