using Cashier.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public partial class LoginViewModel(IAuthService authService, SessionManager sessionManager) : ObservableObject
{
    public string CurrentVersion => App.CurrentVersion;

    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private bool isPasswordVisible;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? message;

    public event EventHandler? LoggedIn;

    [RelayCommand]
    private void CheckForUpdates()
    {
        try
        {
            App.CheckForUpdates(System.Windows.Application.Current.MainWindow);
            Message = "بدأ التحقق من التحديثات؛ سيتم تنزيل أحدث إصدار وتثبيته تلقائياً عند توفره.";
        }
        catch (Exception exception)
        {
            Message = $"تعذر بدء التحديث: {exception.Message}";
        }
    }

    [RelayCommand]
    private Task InitializeAsync() => Task.CompletedTask;

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        Message = null;
        try
        {
            var result = await authService.LoginAsync(Username, Password);
            if (!result.Succeeded || result.Session is null)
            {
                Message = result.Error;
                return;
            }
            CompleteLogin(result.Session);
        }
        catch (Exception exception)
        {
            Message = $"تعذر تسجيل الدخول: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CompleteLogin(UserSession session)
    {
        sessionManager.SignIn(session);
        LoggedIn?.Invoke(this, EventArgs.Empty);
    }
}
