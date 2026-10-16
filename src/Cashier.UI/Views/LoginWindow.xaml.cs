using Cashier.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cashier.UI.Views;

public partial class LoginWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly LoginViewModel viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.LoggedIn += OnLoggedIn;
        Loaded += async (_, _) => await viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) => viewModel.Password = PasswordBox.Password;
    private void NewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) => viewModel.NewPassword = NewPasswordBox.Password;
    private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) => viewModel.ConfirmPassword = ConfirmPasswordBox.Password;

    private void OnLoggedIn(object? sender, EventArgs e)
    {
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        System.Windows.Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        Close();
    }
}
