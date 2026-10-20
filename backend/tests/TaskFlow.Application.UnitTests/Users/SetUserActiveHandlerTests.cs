using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;

namespace TaskFlow.Application.UnitTests.Users;

public sealed class SetUserActiveHandlerTests
{
    private static readonly Guid CurrentUserId = Guid.NewGuid();

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly SetUserActiveHandler _handler;

    public SetUserActiveHandlerTests()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(CurrentUserId);
        _handler = new SetUserActiveHandler(_identity, currentUser);
    }

    [Fact]
    public async Task RejectsChangingOwnStatus()
    {
        var result = await _handler.HandleAsync(new SetUserActiveCommand(CurrentUserId, false), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserErrors.CannotChangeOwnStatus);
        await _identity.DidNotReceiveWithAnyArgs().SetActiveAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownUser()
    {
        var userId = Guid.NewGuid();

        var result = await _handler.HandleAsync(new SetUserActiveCommand(userId, false), TestContext.Current.CancellationToken);

        result.Error!.Code.ShouldBe("Users.NotFound");
        await _identity.DidNotReceiveWithAnyArgs().SetActiveAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeactivatesAnotherUser()
    {
        var userId = Guid.NewGuid();
        _identity.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDto(userId, "someone@tests.local", "someone", true, [], DateTimeOffset.UtcNow));

        var result = await _handler.HandleAsync(new SetUserActiveCommand(userId, false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _identity.Received(1).SetActiveAsync(userId, false, Arg.Any<CancellationToken>());
    }
}
