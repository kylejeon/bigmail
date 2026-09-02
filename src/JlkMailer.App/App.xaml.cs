using System.Windows;
using System.Windows.Threading;

namespace JlkMailer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 처리되지 않은 예외로 앱이 조용히 죽으면, 발송 도중 무슨 일이 있었는지 알 수 없게 된다.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"처리되지 않은 오류가 발생했습니다.\n\n{e.Exception.Message}\n\n" +
            "발송 중이었다면 발송 로그(campaign.db)에 어디까지 나갔는지 기록되어 있습니다. " +
            "앱을 다시 실행하면 이어서 보낼 수 있습니다.",
            "오류", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }
}
