using StudentDesktop.Models;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class ClubsViewModelTests
{
    // Browse/join clubs (campus-backend PR #52): no reachable server, so the initial catalog
    // load must fail closed with an ErrorMessage rather than throwing out of the
    // constructor's fire-and-forget load.
    [Fact]
    public void Construction_WithNoReachableServer_DoesNotThrow()
    {
        var exception = Record.Exception(() => new ClubsViewModel(new ApiClient("http://localhost:0")));

        Assert.Null(exception);
    }

    [Fact]
    public void Construction_StartsWithNoSelectedClub()
    {
        var viewModel = new ClubsViewModel(new ApiClient("http://localhost:0"));

        Assert.Null(viewModel.SelectedClub);
        Assert.Empty(viewModel.Clubs);
    }

    // The all-clubs-vs-my-clubs cross-reference is the core piece of new logic this feature
    // adds — pulled out as a static, HTTP-free method specifically so it's testable here.
    [Fact]
    public void BuildClubList_MarksClubsThatAppearInMineAsMember()
    {
        var apiClient = new ApiClient("http://localhost:0");
        var joinedId = Guid.NewGuid();
        var notJoinedId = Guid.NewGuid();
        var all = new[]
        {
            new ClubDto(joinedId, "Chess Club", "Chess enthusiasts", null, null, null, null, null, 12),
            new ClubDto(notJoinedId, "Robotics Club", "Build robots", null, null, null, null, null, 5),
        };
        var mine = new[] { all[0] };

        var result = ClubsViewModel.BuildClubList(apiClient, all, mine);

        Assert.Equal(2, result.Count);
        Assert.True(result.Single(c => c.Id == joinedId).IsMember);
        Assert.False(result.Single(c => c.Id == notJoinedId).IsMember);
    }

    [Fact]
    public void BuildClubList_WithNoMemberships_MarksEveryClubAsNotJoined()
    {
        var apiClient = new ApiClient("http://localhost:0");
        var all = new[] { new ClubDto(Guid.NewGuid(), "Chess Club", null, null, null, null, null, null, 0) };

        var result = ClubsViewModel.BuildClubList(apiClient, all, []);

        Assert.False(Assert.Single(result).IsMember);
    }

    [Fact]
    public void SelectClub_SetsSelectedClub()
    {
        var viewModel = new ClubsViewModel(new ApiClient("http://localhost:0"));
        var item = new ClubListItemViewModel(new ApiClient("http://localhost:0"),
            new ClubDto(Guid.NewGuid(), "Chess Club", null, null, null, null, null, null, 0), isMember: false);

        viewModel.SelectClubCommand.Execute(item);

        Assert.Equal(item, viewModel.SelectedClub);
    }
}
