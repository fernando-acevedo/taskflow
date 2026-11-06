using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record UpdatePreferencesCommand(ThemePreference ThemePreference);

public sealed class UpdatePreferencesValidator : AbstractValidator<UpdatePreferencesCommand>
{
    public UpdatePreferencesValidator()
    {
        RuleFor(command => command.ThemePreference).IsInEnum();
    }
}

public sealed class UpdatePreferencesHandler(
    IIdentityService identity,
    ICurrentUser currentUser,
    IValidator<UpdatePreferencesCommand> validator)
{
    public async Task<Result> HandleAsync(UpdatePreferencesCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        await identity.UpdateThemePreferenceAsync(currentUser.Id, command.ThemePreference, cancellationToken);
        return Result.Success();
    }
}
