using System;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-23: self-service password change with MFA. The student must supply their current
// password, a fresh TOTP code, and the new password; the server rejects the change if the
// TOTP challenge is missing or fails (see ApiClient.ChangePasswordAsync / AuthController).
public partial class ChangePasswordViewModel(ApiClient apiClient) : ViewModelBase
{
    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string _totpCode = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        if (string.IsNullOrWhiteSpace(TotpCode))
        {
            ErrorMessage = "Enter the authentication code from your app.";
            return;
        }

        if (string.IsNullOrEmpty(NewPassword) || NewPassword != ConfirmPassword)
        {
            ErrorMessage = "New password and confirmation do not match.";
            return;
        }

        IsBusy = true;
        try
        {
            await apiClient.ChangePasswordAsync(CurrentPassword, NewPassword, TotpCode);
            SuccessMessage = "Password updated.";
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            TotpCode = string.Empty;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
