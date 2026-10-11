using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace LgController.UI;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _viewModel;

    public SetupWindow(SetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        TokenBox.Password = viewModel.Token;
        viewModel.Saved += () => DialogResult = true;
    }

    private void OnTokenChanged(object sender, RoutedEventArgs e) => _viewModel.Token = TokenBox.Password;

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
