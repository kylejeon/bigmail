using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JlkMailer.Application;
using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Classification;
using JlkMailer.Infrastructure.Excel;
using JlkMailer.Presentation.Services;

namespace JlkMailer.Presentation.ViewModels;

/// <summary>설계 §11 화면1 — 불러오기.</summary>
public sealed class ImportViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IDialogService _dialogs;
    private readonly ClosedXmlRecipientReader _reader = new();

    public ImportViewModel(AppState state, IDialogService dialogs)
    {
        _state = state;
        _dialogs = dialogs;

        BrowseExcelCommand = new RelayCommand(BrowseExcel);
        BrowseHtmlCommand = new RelayCommand(BrowseHtml);
        LoadCommand = new RelayCommand(Load, () => ExcelPath.Length > 0 && HtmlPath.Length > 0);
    }

    public IRelayCommand BrowseExcelCommand { get; }
    public IRelayCommand BrowseHtmlCommand { get; }
    public IRelayCommand LoadCommand { get; }

    private string _excelPath = "";
    public string ExcelPath
    {
        get => _excelPath;
        set { if (SetProperty(ref _excelPath, value)) { LoadCommand.NotifyCanExecuteChanged(); RefreshSheets(); } }
    }

    private string _htmlPath = "";
    public string HtmlPath
    {
        get => _htmlPath;
        set { if (SetProperty(ref _htmlPath, value)) LoadCommand.NotifyCanExecuteChanged(); }
    }

    public ObservableCollection<string> Sheets { get; } = [];

    private string _selectedSheet = "";
    public string SelectedSheet
    {
        get => _selectedSheet;
        set { if (SetProperty(ref _selectedSheet, value)) GuessColumns(); }
    }

    private int _headerRow = 1;
    public int HeaderRow
    {
        get => _headerRow;
        set { if (SetProperty(ref _headerRow, Math.Max(1, value))) GuessColumns(); }
    }

    // 컬럼 매핑 — 자동 인식 후 사용자가 수정할 수 있다.
    private string _hospitalColumn = "B";
    private string _nameColumn = "C";
    private string _deptColumn = "D";
    private string _phoneColumn = "E";
    private string _emailColumn = "F";

    public string HospitalColumn { get => _hospitalColumn; set => SetProperty(ref _hospitalColumn, value); }
    public string NameColumn { get => _nameColumn; set => SetProperty(ref _nameColumn, value); }
    public string DeptColumn { get => _deptColumn; set => SetProperty(ref _deptColumn, value); }
    public string PhoneColumn { get => _phoneColumn; set => SetProperty(ref _phoneColumn, value); }
    public string EmailColumn { get => _emailColumn; set => SetProperty(ref _emailColumn, value); }

    private string _status = "엑셀 파일과 HTML 템플릿을 선택하세요.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _loaded;
    public bool Loaded { get => _loaded; private set => SetProperty(ref _loaded, value); }

    public event EventHandler? Completed;

    private void BrowseExcel()
    {
        var path = _dialogs.OpenFile("연락처 엑셀 선택", "Excel 파일 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|모든 파일 (*.*)|*.*");
        if (path is not null) ExcelPath = path;
    }

    private void BrowseHtml()
    {
        var path = _dialogs.OpenFile("메일 HTML 템플릿 선택", "HTML 파일 (*.html;*.htm)|*.html;*.htm|모든 파일 (*.*)|*.*");
        if (path is not null) HtmlPath = path;
    }

    private void RefreshSheets()
    {
        Sheets.Clear();
        if (!File.Exists(ExcelPath)) return;

        try
        {
            foreach (var sheet in _reader.ListSheets(ExcelPath)) Sheets.Add(sheet);
            if (Sheets.Count > 0) SelectedSheet = Sheets[0];
        }
        catch (Exception ex)
        {
            Status = $"엑셀을 열지 못했습니다: {ex.Message}";
        }
    }

    private void GuessColumns()
    {
        if (!File.Exists(ExcelPath) || SelectedSheet.Length == 0) return;

        try
        {
            var map = _reader.GuessColumns(ExcelPath, SelectedSheet, HeaderRow);
            HospitalColumn = map.Hospital;
            NameColumn = map.Name;
            DeptColumn = map.Dept;
            PhoneColumn = map.Phone;
            EmailColumn = map.Email;
            Status = $"컬럼을 자동 인식했습니다: 병원 {map.Hospital} / 성함 {map.Name} / 진료과 {map.Dept} / 이메일 {map.Email}";
        }
        catch (Exception ex)
        {
            Status = $"컬럼 인식 실패: {ex.Message}";
        }
    }

    private void Load()
    {
        try
        {
            var map = new ColumnMap(HospitalColumn, NameColumn, DeptColumn, PhoneColumn, EmailColumn);
            var rows = _reader.Read(ExcelPath, SelectedSheet, HeaderRow, map);

            var rules = _state.Store.GetRules();
            var classifier = new SegmentClassifier(rules.Count > 0 ? rules : SegmentCatalog.DefaultRules);

            var (recipients, summary) = new ImportService(classifier)
                .Build(rows, _state.Store.GetSuppressions());

            _state.ExcelPath = ExcelPath;
            _state.HtmlPath = HtmlPath;
            _state.Campaign.HtmlPath = HtmlPath;
            _state.SetRecipients(recipients, summary);

            Status = $"{summary.TotalRows:N0}행 인식 · {summary.Hospitals}개 기관 · 진료과 {summary.DistinctDeptRaw}종 " +
                     $"— 발송 가능 {summary.Sendable:N0} / 확인 필요 {summary.NeedsReview + summary.NeedsFix:N0}";
            Loaded = true;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Loaded = false;
            Status = $"불러오기 실패: {ex.Message}";
            _dialogs.ShowMessage("불러오기 실패", ex.Message);
        }
    }
}
