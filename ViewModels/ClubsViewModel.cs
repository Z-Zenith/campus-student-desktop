using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// New "browse and join clubs" feature (campus-backend PR #52): every club at the student's
// college, cross-referenced against the ones they've already joined, with self-service
// join/leave per club. This is deliberately a separate catalog entry from Community — "every
// club that exists" (open to any authenticated student) is a different surface than "the
// clubs/discussions I'm already in" (SDA-16's Community tab).
public partial class ClubsViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    public ObservableCollection<ClubListItemViewModel> Clubs { get; } = [];

    [ObservableProperty]
    private ClubListItemViewModel? _selectedClub;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public ClubsViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var all = await _apiClient.GetClubsAsync();
            var mine = await _apiClient.GetMyClubsAsync();

            var previouslySelectedId = SelectedClub?.Id;
            Clubs.Clear();
            foreach (var item in BuildClubList(_apiClient, all, mine))
            {
                Clubs.Add(item);
            }
            SelectedClub = previouslySelectedId is { } id ? Clubs.FirstOrDefault(c => c.Id == id) : null;
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
    private void SelectClub(ClubListItemViewModel club) => SelectedClub = club;

    // Cross-references the college-wide club catalog against the caller's own memberships —
    // pulled out as a standalone, HTTP-free static method so this logic is unit-testable
    // without a reachable backend.
    public static IReadOnlyList<ClubListItemViewModel> BuildClubList(ApiClient apiClient, IEnumerable<ClubDto> all, IEnumerable<ClubDto> mine)
    {
        var memberClubIds = mine.Select(c => c.Id).ToHashSet();
        return all.Select(club => new ClubListItemViewModel(apiClient, club, memberClubIds.Contains(club.Id))).ToList();
    }
}
