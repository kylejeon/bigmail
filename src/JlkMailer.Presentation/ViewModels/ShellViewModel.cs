using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JlkMailer.Presentation.Services;

namespace JlkMailer.Presentation.ViewModels;

public sealed class WizardStep(int index, string title, string caption) : ObservableObject
{
    public int Index { get; } = index;
    public string Number => $"{Index + 1}";
    public string Title { get; } = title;
    public string Caption { get; } = caption;

    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }

    private bool _isEnabled;
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}

/// <summary>
/// 설계 §11: 5개 화면, 좌→우 단방향 진행.
/// 발송 버튼은 마지막 화면에만 있고, 앞 화면의 검증이 통과해야 활성화된다.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    public ShellViewModel(IDialogService dialogs, ISecretService secrets)
    {
        State = new AppState();

        Import = new ImportViewModel(State, dialogs);
        Review = new ReviewViewModel(State, dialogs);
        Template = new TemplateViewModel(State, dialogs);
        Send = new SendViewModel(State, dialogs, secrets);

        Import.Completed += (_, _) =>
        {
            Review.Reload();
            RefreshGates();
        };

        Steps =
        [
            new WizardStep(0, "불러오기", "엑셀 · HTML 템플릿") { IsEnabled = true },
            new WizardStep(1, "검토 · 정제", "세그먼트 · 중복 · 교정"),
            new WizardStep(2, "템플릿", "제목 · 문안 · 미리보기"),
            new WizardStep(3, "발송", "계정 · 대상 · 속도"),
            new WizardStep(4, "진행 · 로그", "상태 · 결과 내보내기"),
        ];

        NextCommand = new RelayCommand(Next, () => CanGoNext);
        BackCommand = new RelayCommand(Back, () => CurrentIndex > 0 && !Send.IsRunning);
        GoToCommand = new RelayCommand<WizardStep>(GoTo);

        Send.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SendViewModel.IsRunning)) RefreshGates();
        };

        SetCurrent(0);
    }

    public AppState State { get; }
    public ImportViewModel Import { get; }
    public ReviewViewModel Review { get; }
    public TemplateViewModel Template { get; }
    public SendViewModel Send { get; }

    public ObservableCollection<WizardStep> Steps { get; }

    public IRelayCommand NextCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand<WizardStep> GoToCommand { get; }

    private int _currentIndex;
    public int CurrentIndex { get => _currentIndex; private set => SetProperty(ref _currentIndex, value); }

    public string NextLabel => CurrentIndex switch
    {
        0 => "다음: 검토 →",
        1 => "다음: 템플릿 →",
        2 => "다음: 발송 →",
        3 => "다음: 진행 →",
        _ => "",
    };

    /// <summary>단계별 진행 조건. 설계 §11 — 앞 화면의 검증이 통과해야 다음이 열린다.</summary>
    public bool CanGoNext => CurrentIndex switch
    {
        0 => Import.Loaded,
        1 => Review.CanProceed,
        2 => !Template.Stale,
        3 => Send.CanStart || Send.IsRunning,
        _ => false,
    };

    private void Next()
    {
        switch (CurrentIndex)
        {
            case 1:
                Review.Persist();
                Template.Reload();
                break;
            case 2:
                Template.Persist();
                Send.Reload();
                break;
        }

        SetCurrent(Math.Min(CurrentIndex + 1, Steps.Count - 1));
    }

    private void Back() => SetCurrent(Math.Max(CurrentIndex - 1, 0));

    private void GoTo(WizardStep? step)
    {
        if (step is null || !step.IsEnabled || Send.IsRunning) return;
        SetCurrent(step.Index);
    }

    private void SetCurrent(int index)
    {
        CurrentIndex = index;

        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsCurrent = i == index;
            Steps[i].IsEnabled = i <= index;
        }

        RefreshGates();
    }

    private void RefreshGates()
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextLabel));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>View 가 화면 전환마다 호출해 게이트를 다시 계산한다.</summary>
    public void Refresh() => RefreshGates();

    public void Dispose() => State.Dispose();
}
