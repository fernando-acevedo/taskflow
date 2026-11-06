using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;

namespace TaskFlow.Application.UnitTests.Users;

public sealed class UpdatePreferencesHandlerTests
{
    private static readonly Guid CurrentUserId = Guid.NewGuid();

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly UpdatePreferencesHandler _handler;

    public UpdatePreferencesHandlerTests()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Id.Returns(CurrentUserId);
        _handler = new UpdatePreferencesHandler(_identity, currentUser, new UpdatePreferencesValidator());
    }

    [Fact]
    public async Task SavesTheThemeOfTheCurrentUser()
    {
        var result = await _handler.HandleAsync(new UpdatePreferencesCommand(ThemePreference.Dark), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _identity.Received(1).UpdateThemePreferenceAsync(CurrentUserId, ThemePreference.Dark, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAValueOutsideTheEnum()
    {
        var result = await _handler.HandleAsync(new UpdatePreferencesCommand((ThemePreference)99), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        await _identity.DidNotReceiveWithAnyArgs().UpdateThemePreferenceAsync(default, default, TestContext.Current.CancellationToken);
    }
}
