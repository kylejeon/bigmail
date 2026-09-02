using JlkMailer.Presentation.Services;

namespace JlkMailer.Tests;

public sealed class FakeDialogService : IDialogService
{
    public Queue<string?> OpenFileResults { get; } = new();
    public Queue<string?> SaveFileResults { get; } = new();
    public bool ConfirmResult { get; set; } = true;
    public List<(string Title, string Message)> Messages { get; } = [];
    public List<(string Title, string Message)> Confirms { get; } = [];

    public string? OpenFile(string title, string filter, string? initialPath = null) =>
        OpenFileResults.Count > 0 ? OpenFileResults.Dequeue() : null;

    public string? SaveFile(string title, string filter, string defaultFileName) =>
        SaveFileResults.Count > 0 ? SaveFileResults.Dequeue() : null;

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public bool Confirm(string title, string message)
    {
        Confirms.Add((title, message));
        return ConfirmResult;
    }
}

/// <summary>DPAPI 를 쓸 수 없는 환경을 흉내낸다.</summary>
public sealed class FakeSecretService : ISecretService
{
    private readonly Dictionary<string, string> _store = [];

    public bool CanPersist { get; set; }
    public void Save(string account, string secret) => _store[account] = secret;
    public string? Load(string account) => _store.GetValueOrDefault(account);
    public void Clear(string account) => _store.Remove(account);
}
