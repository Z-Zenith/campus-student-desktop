using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class CommunityViewModelTests
{
    // SDA-16: no reachable server, so the initial clubs/classroom-discussions load must fail
    // closed with an ErrorMessage rather than throwing out of the constructor's fire-and-
    // forget loads.
    [Fact]
    public void Sda16_Construction_WithNoReachableServer_DoesNotThrow()
    {
        var exception = Record.Exception(() => new CommunityViewModel(new ApiClient("http://localhost:0")));

        Assert.Null(exception);
    }

    [Fact]
    public void Sda16_Construction_StartsWithNoSelection()
    {
        var viewModel = new CommunityViewModel(new ApiClient("http://localhost:0"));

        Assert.Null(viewModel.SelectedClub);
        Assert.Null(viewModel.SelectedClassroomDiscussion);
        Assert.Empty(viewModel.MyClubs);
        Assert.Empty(viewModel.MyClassroomDiscussions);
    }

    [Fact]
    public async Task Sda16_Post_WithNoSelection_DoesNothing()
    {
        var viewModel = new CommunityViewModel(new ApiClient("http://localhost:0")) { NewPostContent = "hello" };

        var exception = await Record.ExceptionAsync(() => viewModel.PostCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Empty(viewModel.Posts);
    }

    [Fact]
    public async Task Sda16_Post_WithBlankContent_DoesNothing()
    {
        var viewModel = new CommunityViewModel(new ApiClient("http://localhost:0"));
        viewModel.SelectClubCommand.Execute(new Models.ClubDto(Guid.NewGuid(), "Chess Club", null, null, null, null, null, null, 0));
        viewModel.NewPostContent = "   ";

        await viewModel.PostCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Posts);
    }

    // SDA-16: selecting a club must clear any selected classroom discussion, and vice versa —
    // only one "current" item's posts/materials should ever be showing at once.
    [Fact]
    public void Sda16_SelectClub_ClearsSelectedClassroomDiscussion()
    {
        var viewModel = new CommunityViewModel(new ApiClient("http://localhost:0"));
        var discussion = new Models.ClassroomDiscussionDto(Guid.NewGuid(), Guid.NewGuid(), "Section A", Guid.NewGuid(), "CS101", "Intro to CS");
        viewModel.SelectClassroomDiscussionCommand.Execute(discussion);
        Assert.Equal(discussion, viewModel.SelectedClassroomDiscussion);

        var club = new Models.ClubDto(Guid.NewGuid(), "Chess Club", null, null, null, null, null, null, 0);
        viewModel.SelectClubCommand.Execute(club);

        Assert.Equal(club, viewModel.SelectedClub);
        Assert.Null(viewModel.SelectedClassroomDiscussion);
    }

    [Fact]
    public void Sda16_SelectClassroomDiscussion_ClearsSelectedClub()
    {
        var viewModel = new CommunityViewModel(new ApiClient("http://localhost:0"));
        var club = new Models.ClubDto(Guid.NewGuid(), "Chess Club", null, null, null, null, null, null, 0);
        viewModel.SelectClubCommand.Execute(club);
        Assert.Equal(club, viewModel.SelectedClub);

        var discussion = new Models.ClassroomDiscussionDto(Guid.NewGuid(), Guid.NewGuid(), "Section A", Guid.NewGuid(), "CS101", "Intro to CS");
        viewModel.SelectClassroomDiscussionCommand.Execute(discussion);

        Assert.Equal(discussion, viewModel.SelectedClassroomDiscussion);
        Assert.Null(viewModel.SelectedClub);
    }
}
