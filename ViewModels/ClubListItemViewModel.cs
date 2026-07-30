using System;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// New "browse and join clubs" feature (campus-backend PR #52): one row in the college-wide
// club catalog. IsMember/MemberCount start out seeded from the GET /clubs vs GET /clubs/mine
// cross-reference (see ClubsViewModel.BuildClubList) and are mutated locally on a successful
// join/leave, since the underlying ClubDto is an immutable record and re-fetching the whole
// catalog after every join/leave would be wasteful.
public partial class ClubListItemViewModel(ApiClient apiClient, ClubDto club, bool isMember) : ObservableObject
{
    public Guid Id { get; } = club.Id;
    public string Name { get; } = club.Name;
    public string? Description { get; } = club.Description;
    public string? FacultyLeadFullName { get; } = club.FacultyLeadFullName;
    public string? StudentInchargeFullName { get; } = club.StudentInchargeFullName;
    public string? HomeSiteHtml { get; } = club.HomeSiteHtml;

    [ObservableProperty]
    private bool _isMember = isMember;

    [ObservableProperty]
    private int _memberCount = club.MemberCount;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    [NotifyCanExecuteChangedFor(nameof(LeaveCommand))]
    private bool _isBusy;

    private bool CanJoin() => !IsBusy && !IsMember;

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await apiClient.JoinClubAsync(Id);
            IsMember = true;
            MemberCount++;
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

    private bool CanLeave() => !IsBusy && IsMember;

    [RelayCommand(CanExecute = nameof(CanLeave))]
    private async Task LeaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await apiClient.LeaveClubAsync(Id);
            IsMember = false;
            MemberCount = Math.Max(0, MemberCount - 1);
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
