using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Presentation.Services;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>필터 탭 하나. 설계 §11 화면2 상단 배지.</summary>
public sealed class ReviewFilter(string key, string label, Func<RecipientRow, bool> predicate) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public Func<RecipientRow, bool> Predicate { get; } = predicate;

    private int _count;
    public int Count { get => _count; set => SetProperty(ref _count, value); }

    public string Display => $"{Label} {Count:N0}";
}

/// <summary>
/// 설계 §11 화면2 — 검토·정제. 가장 중요한 화면.
/// 분류를 모르는 채 나가는 메일이 없도록, 문제 있는 행은 사람이 손대야만 발송 큐에 들어간다.
/// </summary>
public sealed class ReviewViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IDialogService _dialogs;

    public ReviewViewModel(AppState state, IDialogService dialogs)
    {
        _state = state;
        _dialogs = dialogs;

        AcceptAllFixesCommand = new RelayCommand(AcceptAllFixes);
        ExcludeFilteredCommand = new RelayCommand(ExcludeFiltered);
        IncludeFilteredCommand = new RelayCommand(IncludeFiltered);
        ReclassifyCommand = new RelayCommand(Reclassify);

        Segments = new ObservableCollection<string>(SegmentCatalog.All.Select(s => s.Code));
    }

    public IRelayCommand AcceptAllFixesCommand { get; }
    public IRelayCommand ExcludeFilteredCommand { get; }
    public IRelayCommand IncludeFilteredCommand { get; }
    public IRelayCommand ReclassifyCommand { get; }

    /// <summary>세그먼트 콤보박스 항목.</summary>
    public ObservableCollection<string> Segments { get; }

    public ObservableCollection<RecipientRow> AllRows { get; } = [];
    public ObservableCollection<RecipientRow> VisibleRows { get; } = [];
    public ObservableCollection<ReviewFilter> Filters { get; } = [];

    private ReviewFilter? _selectedFilter;
    public ReviewFilter? SelectedFilter
    {
        get => _selectedFilter;
        set { if (SetProperty(ref _selectedFilter, value)) ApplyFilter(); }
    }

    private string _summaryText = "";
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    private int _sendableCount;
    public int SendableCount { get => _sendableCount; private set => SetProperty(ref _sendableCount, value); }

    /// <summary>발송 가능 건이 하나도 없으면 다음 단계로 못 간다.</summary>
    public bool CanProceed => SendableCount > 0;

    /// <summary>불러오기 직후 호출. 모델을 화면 행으로 감싼다.</summary>
    public void Reload()
    {
        AllRows.Clear();
        foreach (var recipient in _state.Recipients)
        {
            var row = new RecipientRow(recipient);
            row.PropertyChanged += OnRowChanged;
            AllRows.Add(row);
        }

        BuildFilters();
        SelectedFilter = Filters.FirstOrDefault();
        Recount();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 상태를 바꾸는 프로퍼티만 재집계한다. 텍스트 표시 변경까지 세면 그리드가 느려진다.
        if (e.PropertyName is nameof(RecipientRow.Included)
                           or nameof(RecipientRow.Segment)
                           or nameof(RecipientRow.FixAccepted)
                           or "")
            Recount();
    }

    private void BuildFilters()
    {
        Filters.Clear();
        Filters.Add(new ReviewFilter("all", "전체", _ => true));

        foreach (var def in SegmentCatalog.All)
        {
            var code = def.Code;
            var label = code == SegmentCatalog.S7 ? $"⚠ {code}" : code;
            Filters.Add(new ReviewFilter(code, label, r => r.Model.Segment == code));
        }

        Filters.Add(new ReviewFilter("issues", "⚠ 문제",
            r => r.Model.Status is not (RecipientStatus.Ready or RecipientStatus.Excluded)));
    }

    private void ApplyFilter()
    {
        VisibleRows.Clear();
        if (SelectedFilter is null) return;

        foreach (var row in AllRows.Where(SelectedFilter.Predicate))
            VisibleRows.Add(row);
    }

    private void Recount()
    {
        foreach (var filter in Filters)
            filter.Count = AllRows.Count(filter.Predicate);

        SendableCount = AllRows.Count(r => r.Model.IsSendable);

        int Count(RecipientStatus s) => AllRows.Count(r => r.Model.Status == s);

        SummaryText =
            $"발송가능 {SendableCount:N0}   " +
            $"중복 {Count(RecipientStatus.Duplicate):N0}   " +
            $"누락 {Count(RecipientStatus.NoEmail):N0}   " +
            $"형식오류 {Count(RecipientStatus.NeedsFix) + Count(RecipientStatus.Invalid):N0}   " +
            $"미분류 {Count(RecipientStatus.NeedsReview):N0}   " +
            $"제외 {Count(RecipientStatus.Excluded):N0}";

        OnPropertyChanged(nameof(CanProceed));
    }

    /// <summary>설계 §03: 교정은 일괄 '적용'이 아니라 사용자 승인이다. 승인 전에 무엇이 바뀌는지 보여준다.</summary>
    private void AcceptAllFixes()
    {
        var pending = AllRows.Where(r => r.HasSuggestion && !r.FixAccepted).ToList();
        if (pending.Count == 0)
        {
            _dialogs.ShowMessage("교정 제안", "승인할 교정 제안이 없습니다.");
            return;
        }

        var preview = string.Join("\n", pending.Take(10).Select(r => $"  {r.RowNo}행  {r.Model.EmailNorm} → {r.SuggestedEmail}"));
        var more = pending.Count > 10 ? $"\n  … 외 {pending.Count - 10}건" : "";

        if (!_dialogs.Confirm("교정 제안 일괄 승인", $"{pending.Count}건의 이메일을 교정합니다.\n\n{preview}{more}"))
            return;

        foreach (var row in pending) row.FixAccepted = true;
        Recount();
    }

    private void ExcludeFiltered()
    {
        foreach (var row in VisibleRows.Where(r => r.Included)) row.Included = false;
        Recount();
    }

    private void IncludeFiltered()
    {
        foreach (var row in VisibleRows.Where(r => !r.Included && r.Model.Status == RecipientStatus.Excluded))
            row.Included = true;
        Recount();
    }

    /// <summary>
    /// 설계 §07: 규칙 순서를 바꾸면 분류 결과 미리보기가 즉시 갱신되어야 한다.
    /// 규칙을 저장한 뒤 이 버튼으로 전체를 다시 분류한다.
    /// </summary>
    private void Reclassify()
    {
        var rules = _state.Store.GetRules();
        var classifier = new SegmentClassifier(rules.Count > 0 ? rules : SegmentCatalog.DefaultRules);

        foreach (var row in AllRows)
        {
            // 사람이 직접 지정한 행은 건드리지 않는다.
            if (row.Model.Status == RecipientStatus.Excluded) continue;
            classifier.Apply(row.Model);
        }

        DedupeService.Apply(AllRows.Select(r => r.Model).ToList());
        foreach (var row in AllRows) row.Refresh();

        ApplyFilter();
        Recount();
    }

    /// <summary>다음 단계로 넘어가기 전에 모델 상태를 DB 에 반영한다.</summary>
    public void Persist()
    {
        _state.Store.ReplaceRecipients(AllRows.Select(r => r.Model));
    }
}
