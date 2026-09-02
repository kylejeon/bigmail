using System.ComponentModel;
using System.Windows;
using JlkMailer.App.Services;
using JlkMailer.Presentation.ViewModels;

namespace JlkMailer.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow()
    {
        InitializeComponent();

        _shell = new ShellViewModel(new DialogService(), new SecretService());
        DataContext = _shell;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_shell.Send.IsRunning)
        {
            var answer = MessageBox.Show(
                "발송이 진행 중입니다. 지금 닫으면 발송이 중단됩니다.\n" +
                "이미 보낸 메일은 기록에 남아 있으므로 다시 실행하면 이어서 보낼 수 있습니다.\n\n닫을까요?",
                "발송 중", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }

            _shell.Send.StopCommand.Execute(null);
        }

        _shell.Dispose();
        base.OnClosing(e);
    }
}
