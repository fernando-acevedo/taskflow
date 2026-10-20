using FluentValidation;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record AcceptInvitationCommand(string Email, string Token, string Password);

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(command => command.Email).NotEmpty();
        RuleFor(command => command.Token).NotEmpty();

        // The password policy itself is Identity's; its failures come back from IIdentityService.
        RuleFor(command => command.Password).NotEmpty();
    }
}

/// <summary>An invited user follows the emailed link and chooses their password. Anonymous.</summary>
public sealed class AcceptInvitationHandler(IIdentityService identity, IValidator<AcceptInvitationCommand> validator)
{
    public async Task<Result> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        return await identity.AcceptInvitationAsync(command.Email.Trim(), command.Token, command.Password, cancellationToken);
    }
}
