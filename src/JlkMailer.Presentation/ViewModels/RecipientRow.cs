using CommunityToolkit.Mvvm.ComponentModel;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>검토 그리드 한 줄. 설계 §11 화면2.</summary>
public sealed class RecipientRow : ObservableObject
{
    public RecipientRow(Recipient model)
    {
        Model = model;
        _included = model.IsSendable;
    }

    public Recipient Model { get; }

    public int RowNo => Model.RowNo;
    public string Hospital => Model.Hospital;
    public string Name => Model.Name;
    public string DeptRaw => Model.DeptRaw;
    public string Email => Model.EffectiveEmail;
    public string Issue => Model.Issue;

    private bool _included;

    /// <summary>체크박스. 발송 대상에 포함할지.</summary>
    public bool Included
    {
        get => _included;
        set
        {
            if (_included == value) return;
            _included = value;

            // 모델을 먼저 바꾸고 나서 알린다.
            // SetProperty 를 쓰면 알림이 상태 변경보다 먼저 나가고, 구독자(ReviewViewModel)가
            // 아직 바뀌지 않은 Status 로 재집계해 건수가 어긋난다.
            if (!value && Model.Status is RecipientStatus.Ready)
                Model.Status = RecipientStatus.Excluded;
            else if (value && Model.Status is RecipientStatus.Excluded)
                Model.Status = RecipientStatus.Ready;

            OnPropertyChanged(nameof(Included));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Severity));
        }
    }

    /// <summary>세그먼트 직접 지정. S7 행을 사람이 판단해 편입시키는 경로다.</summary>
    public string Segment
    {
        get => Model.Segment;
        set
        {
            if (Model.Segment == value || !SegmentCatalog.Exists(value)) return;

            var def = SegmentCatalog.Get(value);
            Model.Segment = def.Code;
            Model.DeptLabel = def.DeptLabel;
            Model.Honorific = def.Honorific;

            // 사람이 세그먼트를 지정했으면 '확인 필요' 사유는 해소된 것으로 본다.
            if (Model.Status == RecipientStatus.NeedsReview && Model.EmailNorm.Length > 0)
            {
                Model.Status = RecipientStatus.Ready;
                Model.Issue = "";
                _included = true;
                OnPropertyChanged(nameof(Included));
            }

            OnPropertyChanged(nameof(Segment));
            OnPropertyChanged(nameof(SegmentText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Severity));
            OnPropertyChanged(nameof(Issue));
        }
    }

    public string SegmentText => $"{Model.Segment} {SegmentCatalog.Get(Model.Segment).Name}";

    /// <summary>교정 제안이 있는 행에만 표시된다.</summary>
    public string? SuggestedEmail => Model.SuggestedEmail;
    public bool HasSuggestion => Model.SuggestedEmail is { Length: > 0 };

    public bool FixAccepted
    {
        get => Model.FixAccepted;
        set
        {
            if (Model.FixAccepted == value) return;
            Model.FixAccepted = value;

            if (value && Model.Status == RecipientStatus.NeedsFix)
            {
                Model.Status = RecipientStatus.Ready;
                _included = true;
            }
            else if (!value && Model.Status == RecipientStatus.Ready && Model.SuggestedEmail is not null)
            {
                Model.Status = RecipientStatus.NeedsFix;
                _included = false;
            }

            OnPropertyChanged(nameof(FixAccepted));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(Included));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Severity));
        }
    }

    public string StatusText => Model.Status switch
    {
        RecipientStatus.Ready => "정상",
        RecipientStatus.NoEmail => "이메일 없음",
        RecipientStatus.Invalid => "형식 오류",
        RecipientStatus.NeedsFix => "교정 필요",
        RecipientStatus.Duplicate => "중복",
        RecipientStatus.NeedsReview => "확인 필요",
        RecipientStatus.Excluded => "제외",
        RecipientStatus.Suppressed => "수신거부",
        _ => Model.Status.ToString(),
    };

    /// <summary>UI 색상 구분용. 설계 §11 화면2 의 상태 배지.</summary>
    public string Severity => Model.Status switch
    {
        RecipientStatus.Ready => "ok",
        RecipientStatus.NeedsFix or RecipientStatus.NeedsReview => "warn",
        RecipientStatus.NoEmail or RecipientStatus.Invalid or RecipientStatus.Suppressed => "crit",
        _ => "muted",
    };

    public void Refresh()
    {
        _included = Model.IsSendable;
        OnPropertyChanged(string.Empty);
    }
}
