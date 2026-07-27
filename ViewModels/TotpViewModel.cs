using System;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-02: the TOTP step of sign-in, shown on its own page after LoginViewModel collects
// identifier/password. Submits all three together in one LoginAsync call — the backend
// contract is unchanged, only the client-side screens are split.
public partial class TotpViewModel(
    ApiClient apiClient,
    string identifier,
    string password,
    Action<LoginResponse> onLoggedIn,
    Action onBack) : ViewModelBase
{
    [ObservableProperty]
    private string _totpCode = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var response = await apiClient.LoginAsync(identifier, password, TotpCode);
            onLoggedIn(response);
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

    [RelayCommand]
    private void Back() => onBack();
}
