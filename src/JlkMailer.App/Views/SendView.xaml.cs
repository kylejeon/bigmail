using System.Windows;
using System.Windows.Controls;
using JlkMailer.Presentation.ViewModels;

namespace JlkMailer.App.Views;

public partial class SendView : UserControl
{
    public SendView() => InitializeComponent();

    /// <summary>
    /// PasswordBox.Password 는 보안상 DependencyProperty 가 아니라 바인딩할 수 없다.
    /// 코드비하인드에서 ViewModel 로 옮긴다.
    /// </summary>
    private void OnSecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SendViewModel vm && sender is PasswordBox box)
            vm.Secret = box.Password;
    }
}
