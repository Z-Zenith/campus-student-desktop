using StudentDesktop.Models;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class ClubListItemViewModelTests
{
    private static ClubListItemViewModel CreateItem(bool isMember, int memberCount = 3) =>
        new(new ApiClient("http://localhost:0"),
            new ClubDto(Guid.NewGuid(), "Chess Club", "Chess enthusiasts", null, null, null, null, "<html></html>", memberCount),
            isMember);

    [Fact]
    public void Construction_SeedsIsMemberAndMemberCountFromArguments()
    {
        var item = CreateItem(isMember: true, memberCount: 7);

        Assert.True(item.IsMember);
        Assert.Equal(7, item.MemberCount);
    }

    [Fact]
    public async Task Join_WithNoReachableServer_SurfacesErrorAndLeavesStateUnchanged()
    {
        var item = CreateItem(isMember: false, memberCount: 3);

        await item.JoinCommand.ExecuteAsync(null);

        Assert.False(item.IsMember);
        Assert.Equal(3, item.MemberCount);
        Assert.Equal("Could not reach the server. Check your connection and try again.", item.ErrorMessage);
    }

    [Fact]
    public async Task Leave_WithNoReachableServer_SurfacesErrorAndLeavesStateUnchanged()
    {
        var item = CreateItem(isMember: true, memberCount: 3);

        await item.LeaveCommand.ExecuteAsync(null);

        Assert.True(item.IsMember);
        Assert.Equal(3, item.MemberCount);
        Assert.Equal("Could not reach the server. Check your connection and try again.", item.ErrorMessage);
    }

    [Fact]
    public void JoinCommand_CanExecute_IsFalseWhenAlreadyAMember()
    {
        var item = CreateItem(isMember: true);

        Assert.False(item.JoinCommand.CanExecute(null));
    }

    [Fact]
    public void LeaveCommand_CanExecute_IsFalseWhenNotAMember()
    {
        var item = CreateItem(isMember: false);

        Assert.False(item.LeaveCommand.CanExecute(null));
    }
}
