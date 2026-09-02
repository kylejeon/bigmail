using System.Windows;
using JlkMailer.Presentation.Services;
using Microsoft.Win32;

namespace JlkMailer.App.Services;

public sealed class DialogService : IDialogService
{
    public string? OpenFile(string title, string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(Path.GetDirectoryName(initialPath)))
            dialog.InitialDirectory = Path.GetDirectoryName(initialPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowMessage(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
}
