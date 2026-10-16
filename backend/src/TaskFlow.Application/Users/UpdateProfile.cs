using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;

namespace TaskFlow.Application.Users;

public sealed record UpdateProfileCommand(string DisplayName);

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public const int MaxDisplayNameLength = 100;

    public UpdateProfileValidator()
    {
        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .Length(2, MaxDisplayNameLength);
    }
}

public sealed class UpdateProfileHandler(
    IIdentityService identity,
    ICurrentUser currentUser,
    IValidator<UpdateProfileCommand> validator)
{
    public async Task<Result> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        await identity.UpdateDisplayNameAsync(currentUser.Id, command.DisplayName.Trim(), cancellationToken);
        return Result.Success();
    }
}
