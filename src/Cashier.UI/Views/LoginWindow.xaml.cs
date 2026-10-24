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
        Loaded += OnLoginWindowLoaded;
    }

    private async void OnLoginWindowLoaded(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = viewModel.Password;
        await viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsPasswordVisible)
            return;
        viewModel.Password = PasswordBox.Password;
    }

    private void TogglePasswordVisibility_OnClick(object sender, RoutedEventArgs e)
    {
        if (!viewModel.IsPasswordVisible)
            viewModel.Password = PasswordBox.Password;

        viewModel.TogglePasswordVisibilityCommand.Execute(null);
        PasswordBox.Password = viewModel.Password;
    }

    private void OnLoggedIn(object? sender, EventArgs e)
    {
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        System.Windows.Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        Close();
    }
}
