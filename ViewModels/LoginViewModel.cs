using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// SDA-02: roll number/username + password. TOTP is collected on its own page —
// see TotpViewModel — once identifier/password are entered here.
public partial class LoginViewModel(Action<string, string> onCredentialsEntered) : ViewModelBase
{
    [ObservableProperty]
    private string _identifier = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private void Continue()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter your roll number/username and password.";
            return;
        }
        onCredentialsEntered(Identifier, Password);
    }
}
