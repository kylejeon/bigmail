namespace JlkMailer.Presentation.Services;

/// <summary>파일 대화상자 등 플랫폼 의존 기능. WPF 계층이 구현한다.</summary>
public interface IDialogService
{
    string? OpenFile(string title, string filter, string? initialPath = null);
    string? SaveFile(string title, string filter, string defaultFileName);
    void ShowMessage(string title, string message);
    bool Confirm(string title, string message);
}

/// <summary>발송 계정 비밀번호 저장소. Windows 에서는 DPAPI, 그 외에서는 저장을 거부한다.</summary>
public interface ISecretService
{
    bool CanPersist { get; }
    void Save(string account, string secret);
    string? Load(string account);
    void Clear(string account);
}
