using Cashier.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cashier.UI.ViewModels;

public partial class LoginViewModel(IAuthService authService, SessionManager sessionManager) : ObservableObject
{
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private bool isPasswordChangeRequired;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? message;

    public event EventHandler? LoggedIn;

    [RelayCommand]
    private Task InitializeAsync() => Task.CompletedTask;

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
            if (result.Session.PasswordChangeRequired)
            {
                PendingSession = result.Session;
                IsPasswordChangeRequired = true;
                Message = "يجب تغيير كلمة المرور الافتراضية قبل استخدام النظام.";
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

    private UserSession? PendingSession { get; set; }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        IsBusy = true;
        Message = null;
        try
        {
            if (PendingSession is null)
                throw new InvalidOperationException("سجل الدخول أولاً.");
            if (NewPassword != ConfirmPassword)
                throw new InvalidOperationException("كلمتا المرور الجديدتان غير متطابقتين.");
            await authService.ChangePasswordAsync(PendingSession.UserId, Password, NewPassword);
            CompleteLogin(PendingSession with { PasswordChangeRequired = false });
        }
        catch (Exception exception)
        {
            Message = $"تعذر تغيير كلمة المرور: {exception.Message}";
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
