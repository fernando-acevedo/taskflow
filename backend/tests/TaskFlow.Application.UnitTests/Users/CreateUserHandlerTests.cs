using NSubstitute;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.UnitTests.Users;

public sealed class CreateUserHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IInvitationSender _invitations = Substitute.For<IInvitationSender>();
    private readonly CreateUserHandler _handler;

    public CreateUserHandlerTests() => _handler = new CreateUserHandler(_identity, _invitations, new CreateUserValidator());

    [Theory]
    [InlineData("not-an-email", "Someone", "email")]
    [InlineData("someone@tests.local", "S", "displayName")]
    public async Task RejectsInvalidFields(string email, string displayName, string field)
    {
        var result = await _handler.HandleAsync(new CreateUserCommand(email, displayName, false), TestContext.Current.CancellationToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.ValidationErrors!.ShouldContainKey(field);
        await _identity.DidNotReceiveWithAnyArgs().CreateAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RejectsAnEmailThatIsAlreadyRegistered()
    {
        _identity.FindByEmailAsync("taken@tests.local", Arg.Any<CancellationToken>())
            .Returns(new UserDto(Guid.NewGuid(), "taken@tests.local", "taken", true, [], DateTimeOffset.UtcNow));

        var result = await _handler.HandleAsync(
            new CreateUserCommand("taken@tests.local", "Someone", false), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserErrors.EmailTaken);
        await _identity.DidNotReceiveWithAnyArgs().CreateAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CreatesTheUserAndSendsTheInvitation()
    {
        var created = NewPendingUser();
        _identity.CreateAsync(Arg.Any<NewUser>(), Arg.Any<CancellationToken>()).Returns(created);
        _identity.GenerateInvitationTokenAsync(created.Id, Arg.Any<CancellationToken>()).Returns("token");
        _invitations.SendAsync(created, "token", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(
            new CreateUserCommand(" new@tests.local ", " New Person ", true), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(created);
        await _identity.Received(1).CreateAsync(new NewUser("new@tests.local", "New Person", true), Arg.Any<CancellationToken>());
        await _invitations.Received(1).SendAsync(created, "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsAnInvitationThatCouldNotBeSent()
    {
        var created = NewPendingUser();
        _identity.CreateAsync(Arg.Any<NewUser>(), Arg.Any<CancellationToken>()).Returns(created);
        _identity.GenerateInvitationTokenAsync(created.Id, Arg.Any<CancellationToken>()).Returns("token");
        _invitations.SendAsync(created, "token", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateUserCommand("new@tests.local", "New Person", false), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(UserErrors.InvitationNotSent);
    }

    private static UserDto NewPendingUser() =>
        new(Guid.NewGuid(), "new@tests.local", "New Person", true, [], DateTimeOffset.UtcNow, IsInvitationPending: true);
}
