using System.Windows;
using System.Windows.Threading;

namespace JlkMailer.App;

/// <summary>
/// 기반 클래스를 반드시 System.Windows.Application 으로 정규화해야 한다.
///
/// 이 파일은 namespace JlkMailer.App 안에 있고, C# 은 단순명 'Application' 을
/// 바깥 네임스페이스(JlkMailer)부터 찾는다. 거기에 Application 계층 프로젝트의
/// 네임스페이스 JlkMailer.Application 이 있어서 System.Windows.Application 을 가린다.
/// 네임스페이스 중첩 탐색이 using 지시문보다 우선하므로 using System.Windows; 로는 해결되지 않는다.
///   → error CS0118: 'Application' is a namespace but is used like a type
/// </summary>
public partial class App : System.Windows.Application
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
