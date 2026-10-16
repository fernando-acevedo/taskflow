using TaskFlow.Application.Users;

namespace TaskFlow.Application.UnitTests.Users;

public sealed class UpdateProfileValidatorTests
{
    private readonly UpdateProfileValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void RejectsMissingOrTooShortNames(string displayName) =>
        _validator.Validate(new UpdateProfileCommand(displayName)).IsValid.ShouldBeFalse();

    [Fact]
    public void RejectsNamesLongerThanTheLimit() =>
        _validator.Validate(new UpdateProfileCommand(new string('a', UpdateProfileValidator.MaxDisplayNameLength + 1))).IsValid.ShouldBeFalse();

    [Fact]
    public void AcceptsAReasonableName() =>
        _validator.Validate(new UpdateProfileCommand("Ada Lovelace")).IsValid.ShouldBeTrue();
}
