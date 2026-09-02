using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Presentation.ViewModels;
using Xunit;

namespace JlkMailer.Tests;

/// <summary>
/// ViewModel 은 net8.0 이라 WPF 없이 검증된다. 설계 §11 의 화면 규칙이 실제로 지켜지는지 확인한다.
/// </summary>
public sealed class ViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"jlk-vm-{Guid.NewGuid():N}.db");
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeSecretService _secrets = new();
    private readonly ShellViewModel _shell;

    public ViewModelTests()
    {
        _shell = new ShellViewModel(_dialogs, _secrets);
        _shell.State.DatabasePath = _dbPath;
    }

    private void LoadRealData()
    {
        _shell.Import.ExcelPath = TestData.Excel;
        _shell.Import.HtmlPath = TestData.Html;
        _shell.Import.LoadCommand.Execute(null);
    }

    // ---------- 화면1 불러오기 ----------

    [Fact]
    public void 엑셀을_열면_시트와_컬럼이_자동_인식된다()
    {
        _shell.Import.ExcelPath = TestData.Excel;

        Assert.Contains("시트1", _shell.Import.Sheets);
        Assert.Equal("B", _shell.Import.HospitalColumn);
        Assert.Equal("C", _shell.Import.NameColumn);
        Assert.Equal("D", _shell.Import.DeptColumn);
        Assert.Equal("F", _shell.Import.EmailColumn);
    }

    [Fact]
    public void 파일을_고르기_전에는_불러오기가_비활성이다()
    {
        Assert.False(_shell.Import.LoadCommand.CanExecute(null));
        _shell.Import.ExcelPath = TestData.Excel;
        Assert.False(_shell.Import.LoadCommand.CanExecute(null));
        _shell.Import.HtmlPath = TestData.Html;
        Assert.True(_shell.Import.LoadCommand.CanExecute(null));
    }

    [Fact]
    public void 불러오면_실측_수치가_상태줄에_나온다()
    {
        LoadRealData();

        Assert.True(_shell.Import.Loaded);
        Assert.Contains("1,872행", _shell.Import.Status);
        Assert.Contains("187개 기관", _shell.Import.Status);
        Assert.Contains("112종", _shell.Import.Status);
    }

    // ---------- 화면2 검토 ----------

    [Fact]
    public void 검토_화면의_필터_건수가_세그먼트_분포와_일치한다()
    {
        LoadRealData();
        var review = _shell.Review;

        Assert.Equal(1872, review.AllRows.Count);
        Assert.Equal(676, review.Filters.Single(f => f.Key == SegmentCatalog.S1).Count);
        Assert.Equal(363, review.Filters.Single(f => f.Key == SegmentCatalog.S2).Count);
        Assert.Equal(73, review.Filters.Single(f => f.Key == SegmentCatalog.S7).Count);
    }

    [Fact]
    public void 필터를_고르면_해당_행만_보인다()
    {
        LoadRealData();
        var review = _shell.Review;

        review.SelectedFilter = review.Filters.Single(f => f.Key == SegmentCatalog.S4);

        Assert.Equal(344, review.VisibleRows.Count);
        Assert.All(review.VisibleRows, r => Assert.Equal(SegmentCatalog.S4, r.Model.Segment));
    }

    /// <summary>설계 §07: 분류를 모르는 채 나가는 메일이 없어야 한다.</summary>
    [Fact]
    public void S7_행은_기본적으로_발송_대상이_아니다()
    {
        LoadRealData();

        var s7 = _shell.Review.AllRows.Where(r => r.Model.Segment == SegmentCatalog.S7).ToList();
        Assert.NotEmpty(s7);
        Assert.All(s7, r => Assert.False(r.Included));
    }

    [Fact]
    public void S7_행에_세그먼트를_지정하면_발송_대상이_된다()
    {
        LoadRealData();

        var row = _shell.Review.AllRows.First(
            r => r.Model.Segment == SegmentCatalog.S7 && r.Model.Status == RecipientStatus.NeedsReview);
        var before = _shell.Review.SendableCount;

        row.Segment = SegmentCatalog.S2;

        Assert.True(row.Included);
        Assert.Equal("신경과", row.Model.DeptLabel);
        Assert.Equal("선생님", row.Model.Honorific);
        Assert.Equal("", row.Issue);
        Assert.Equal(before + 1, _shell.Review.SendableCount);
    }

    /// <summary>설계 §03: 교정은 자동 적용하지 않고 사용자 승인을 받는다.</summary>
    [Fact]
    public void 교정_제안은_승인하기_전까지_적용되지_않는다()
    {
        LoadRealData();

        var row = _shell.Review.AllRows.First(r => r.HasSuggestion);
        Assert.False(row.FixAccepted);
        Assert.False(row.Included);
        Assert.Contains(" ", row.Email);           // 아직 공백이 든 원본 주소

        row.FixAccepted = true;

        Assert.True(row.Included);
        Assert.DoesNotContain(" ", row.Email);
        Assert.Equal("정상", row.StatusText);
    }

    [Fact]
    public void 교정_일괄_승인은_확인을_거쳐_6건을_반영한다()
    {
        LoadRealData();
        var before = _shell.Review.SendableCount;

        _shell.Review.AcceptAllFixesCommand.Execute(null);

        Assert.Single(_dialogs.Confirms);
        Assert.Contains("6건의 이메일을 교정합니다", _dialogs.Confirms[0].Message);
        Assert.Equal(before + 6, _shell.Review.SendableCount);
    }

    [Fact]
    public void 확인을_거부하면_교정이_적용되지_않는다()
    {
        LoadRealData();
        _dialogs.ConfirmResult = false;
        var before = _shell.Review.SendableCount;

        _shell.Review.AcceptAllFixesCommand.Execute(null);

        Assert.Equal(before, _shell.Review.SendableCount);
    }

    [Fact]
    public void 체크를_풀면_제외되고_다시_켜면_돌아온다()
    {
        LoadRealData();

        var row = _shell.Review.AllRows.First(r => r.Included);
        var before = _shell.Review.SendableCount;

        row.Included = false;
        Assert.Equal("제외", row.StatusText);
        Assert.Equal(before - 1, _shell.Review.SendableCount);

        row.Included = true;
        Assert.Equal("정상", row.StatusText);
        Assert.Equal(before, _shell.Review.SendableCount);
    }

    // ---------- 화면3 템플릿 ----------

    [Fact]
    public void 템플릿_화면은_세그먼트별_건수를_보여준다()
    {
        LoadRealData();
        _shell.Review.Persist();
        _shell.Template.Reload();

        // 템플릿 화면은 세그먼트 총계(676)가 아니라 '이 문안을 실제로 받는 사람 수'를 보여준다.
        // 중복·이메일 누락으로 빠진 행은 제외된다.
        var s1 = _shell.Template.SegmentItems.Single(s => s.Code == SegmentCatalog.S1);
        var sendableS1 = _shell.Review.AllRows.Count(r => r.Model.Segment == SegmentCatalog.S1 && r.Model.IsSendable);

        Assert.Equal(sendableS1, s1.Count);
        Assert.True(s1.Count < 676, "세그먼트 총계가 아니라 발송 가능 건수여야 한다");
        Assert.Contains("영상의학", s1.Display);
    }

    [Fact]
    public void 미리보기는_실제_수신자_값으로_렌더된다()
    {
        LoadRealData();
        _shell.Template.Reload();

        Assert.False(_shell.Template.Stale);
        Assert.NotEmpty(_shell.Template.PreviewHtml);
        Assert.DoesNotContain("{{", _shell.Template.PreviewHtml);
        Assert.DoesNotContain("○○○", _shell.Template.PreviewHtml);
        Assert.Contains("본문", _shell.Template.SizeText);
        Assert.Equal("ok", _shell.Template.SizeSeverity);
    }

    /// <summary>설계 §08: 제목 길이 카운터와 모바일 절단 미리보기.</summary>
    [Fact]
    public void 제목이_길어지면_경고하고_모바일_절단을_보여준다()
    {
        LoadRealData();
        _shell.Template.Reload();

        Assert.False(_shell.Template.SubjectTooLong);

        _shell.Template.Subject = new string('가', 80);

        Assert.True(_shell.Template.SubjectTooLong);
        Assert.Contains("80 / 60자", _shell.Template.SubjectLengthText);
        Assert.EndsWith("…", _shell.Template.MobilePreview);
        Assert.Equal(TemplateViewModel.MobilePreviewLength + 1, _shell.Template.MobilePreview.Length);
    }

    [Fact]
    public void 문안을_고치면_다시_만들기_전까지_stale_이다()
    {
        LoadRealData();
        _shell.Template.Reload();
        Assert.False(_shell.Template.Stale);

        _shell.Template.Intro = "수정된 도입 문단입니다.";
        Assert.True(_shell.Template.Stale);

        _shell.Template.RebuildCommand.Execute(null);
        Assert.False(_shell.Template.Stale);
        Assert.Contains("수정된 도입 문단입니다.", _shell.Template.PreviewHtml);
    }

    // ---------- 화면4 발송 ----------

    [Fact]
    public void 발송_대상은_기본_발송_세그먼트만_선택된다()
    {
        LoadRealData();
        _shell.Template.Reload();
        _shell.Send.Reload();

        var s7 = _shell.Send.Targets.SingleOrDefault(t => t.Code == SegmentCatalog.S7);
        Assert.True(s7 is null || !s7.Selected);   // S7 은 발송 가능 건이 없거나 선택되지 않는다

        Assert.True(_shell.Send.Targets.Single(t => t.Code == SegmentCatalog.S2).Selected);
        Assert.Equal(_shell.Review.SendableCount, _shell.Send.TargetCount);
    }

    [Fact]
    public void 대상_선택을_바꾸면_예상_소요가_갱신된다()
    {
        LoadRealData();
        _shell.Template.Reload();
        _shell.Send.Reload();

        foreach (var t in _shell.Send.Targets) t.Selected = t.Code == SegmentCatalog.S2;

        var sendableS2 = _shell.Review.AllRows.Count(r => r.Model.Segment == SegmentCatalog.S2 && r.Model.IsSendable);

        Assert.Equal(sendableS2, _shell.Send.TargetCount);
        Assert.Contains($"{sendableS2:N0}통", _shell.Send.EstimateText);
        Assert.Contains("일 상한 적용 시", _shell.Send.EstimateText);
    }

    /// <summary>설계 §11 화면4: 연결 테스트와 테스트 발송을 통과해야 발송 버튼이 열린다.</summary>
    [Fact]
    public void 연결_테스트와_테스트_발송_없이는_발송을_시작할_수_없다()
    {
        LoadRealData();
        _shell.Template.Reload();
        _shell.Send.Reload();

        _shell.Send.Account = "sales@jlkgroup.com";
        Assert.False(_shell.Send.CanStart);
        Assert.False(_shell.Send.StartCommand.CanExecute(null));
    }

    [Fact]
    public void DPAPI를_쓸_수_없으면_비밀번호_저장을_거부한다()
    {
        _secrets.CanPersist = false;
        _shell.Send.RememberSecret = true;

        Assert.False(_shell.Send.RememberSecret);
        Assert.Contains(_dialogs.Messages, m => m.Title == "저장 불가");
    }

    // ---------- 셸 진행 게이트 ----------

    [Fact]
    public void 불러오기_전에는_다음_단계로_갈_수_없다()
    {
        Assert.Equal(0, _shell.CurrentIndex);
        Assert.False(_shell.CanGoNext);

        LoadRealData();

        Assert.True(_shell.CanGoNext);
    }

    [Fact]
    public void 단계를_지나면서_화면이_순서대로_열린다()
    {
        LoadRealData();

        Assert.Equal("다음: 검토 →", _shell.NextLabel);
        _shell.NextCommand.Execute(null);
        Assert.Equal(1, _shell.CurrentIndex);

        _shell.NextCommand.Execute(null);
        Assert.Equal(2, _shell.CurrentIndex);
        Assert.True(_shell.Steps[2].IsCurrent);
        Assert.False(_shell.Steps[3].IsEnabled);   // 아직 열리지 않은 단계
    }

    [Fact]
    public void 아직_열리지_않은_단계로는_점프할_수_없다()
    {
        LoadRealData();

        _shell.GoToCommand.Execute(_shell.Steps[4]);

        Assert.Equal(0, _shell.CurrentIndex);
    }

    public void Dispose()
    {
        _shell.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
    }
}
