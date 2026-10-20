using NSubstitute;
using TaskFlow.Application.Users;

namespace TaskFlow.Application.UnitTests.Users;

public sealed class ResendInvitationHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IInvitationSender _invitations = Substitute.For<IInvitationSender>();
    private readonly ResendInvitationHandler _handler;

    public ResendInvitationHandlerTests() => _handler = new ResendInvitationHandler(_identity, _invitations);

    [Fact]
    public async Task ReturnsNotFoundForUnknownUser()
    {
        var result = await _handler.HandleAsync(new ResendInvitationCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("Users.NotFound");
    }

    [Fact]
    public async Task RejectsAUserWhoAlreadyHasAPassword()
    {
        var user = GivenUser(isInvitationPending: false);

        var result = await _handler.HandleAsync(new ResendInvitationCommand(user.Id), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserErrors.InvitationNotPending);
        await _invitations.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendsANewTokenToAPendingUser()
    {
        var user = GivenUser(isInvitationPending: true);
        _identity.GenerateInvitationTokenAsync(user.Id, Arg.Any<CancellationToken>()).Returns("fresh-token");
        _invitations.SendAsync(user, "fresh-token", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(new ResendInvitationCommand(user.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _invitations.Received(1).SendAsync(user, "fresh-token", Arg.Any<CancellationToken>());
    }

    private UserDto GivenUser(bool isInvitationPending)
    {
        var user = new UserDto(Guid.NewGuid(), "someone@tests.local", "someone", true, [], DateTimeOffset.UtcNow, isInvitationPending);
        _identity.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }
}
