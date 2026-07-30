using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-16: view and post in the two kinds of "group" a student belongs to — clubs they've
// joined and classroom discussions for sections they're enrolled in (campus-backend PR #52
// split these, plus teacher-only StaffGroups, out of the old flat groups model). Only one of
// SelectedClub/SelectedClassroomDiscussion is ever non-null at a time; material shared in
// either surfaces in that item's Materials list without a separate upload step (reads the
// same rows TWA-06's upload endpoint writes).
public partial class CommunityViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    public ObservableCollection<ClubDto> MyClubs { get; } = [];
    public ObservableCollection<ClassroomDiscussionDto> MyClassroomDiscussions { get; } = [];
    public ObservableCollection<CommunityPostItem> Posts { get; } = [];
    public ObservableCollection<MaterialDto> Materials { get; } = [];

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ClubDto? _selectedClub;

    [ObservableProperty]
    private ClassroomDiscussionDto? _selectedClassroomDiscussion;

    [ObservableProperty]
    private string _newPostContent = "";

    public CommunityViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _ = LoadMyClubsAsync();
        _ = LoadMyClassroomDiscussionsAsync();
    }

    [RelayCommand]
    private async Task LoadMyClubsAsync()
    {
        try
        {
            var clubs = await _apiClient.GetMyClubsAsync();
            MyClubs.Clear();
            foreach (var club in clubs)
            {
                MyClubs.Add(club);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    [RelayCommand]
    private async Task LoadMyClassroomDiscussionsAsync()
    {
        try
        {
            var discussions = await _apiClient.GetMyClassroomDiscussionsAsync();
            MyClassroomDiscussions.Clear();
            foreach (var discussion in discussions)
            {
                MyClassroomDiscussions.Add(discussion);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    // Selection routes through commands (rather than two-way ListBox.SelectedItem bindings)
    // so picking a club always clears any selected classroom discussion and vice versa,
    // without the two backing properties fighting to clear each other back.
    [RelayCommand]
    private void SelectClub(ClubDto club)
    {
        SelectedClassroomDiscussion = null;
        SelectedClub = club;
    }

    [RelayCommand]
    private void SelectClassroomDiscussion(ClassroomDiscussionDto discussion)
    {
        SelectedClub = null;
        SelectedClassroomDiscussion = discussion;
    }

    partial void OnSelectedClubChanged(ClubDto? value) => _ = LoadClubContentAsync(value);

    partial void OnSelectedClassroomDiscussionChanged(ClassroomDiscussionDto? value) => _ = LoadClassroomDiscussionContentAsync(value);

    private async Task LoadClubContentAsync(ClubDto? club)
    {
        Posts.Clear();
        Materials.Clear();
        if (club is null)
        {
            return;
        }
        try
        {
            var posts = await _apiClient.GetClubPostsAsync(club.Id);
            foreach (var post in posts)
            {
                Posts.Add(new CommunityPostItem(post.Id, post.AuthorId, post.Content, post.CreatedAt));
            }
            var materials = await _apiClient.GetClubMaterialsAsync(club.Id);
            foreach (var material in materials)
            {
                Materials.Add(material);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    private async Task LoadClassroomDiscussionContentAsync(ClassroomDiscussionDto? discussion)
    {
        Posts.Clear();
        Materials.Clear();
        if (discussion is null)
        {
            return;
        }
        try
        {
            var posts = await _apiClient.GetClassroomDiscussionPostsAsync(discussion.Id);
            foreach (var post in posts)
            {
                Posts.Add(new CommunityPostItem(post.Id, post.AuthorId, post.Content, post.CreatedAt));
            }
            var materials = await _apiClient.GetClassroomDiscussionMaterialsAsync(discussion.Id);
            foreach (var material in materials)
            {
                Materials.Add(material);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    [RelayCommand]
    private async Task PostAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPostContent) || (SelectedClub is null && SelectedClassroomDiscussion is null))
        {
            return;
        }
        try
        {
            if (SelectedClub is { } club)
            {
                var post = await _apiClient.CreateClubPostAsync(club.Id, NewPostContent.Trim());
                Posts.Insert(0, new CommunityPostItem(post.Id, post.AuthorId, post.Content, post.CreatedAt));
            }
            else if (SelectedClassroomDiscussion is { } discussion)
            {
                var post = await _apiClient.CreateClassroomDiscussionPostAsync(discussion.Id, NewPostContent.Trim());
                Posts.Insert(0, new CommunityPostItem(post.Id, post.AuthorId, post.Content, post.CreatedAt));
            }
            NewPostContent = "";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }
}
