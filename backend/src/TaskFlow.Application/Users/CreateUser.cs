using FluentValidation;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record CreateUserCommand(string Email, string DisplayName, bool IsAdmin);

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public const int MaxEmailLength = 256;

    public CreateUserValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(MaxEmailLength);

        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .Length(2, UpdateProfileValidator.MaxDisplayNameLength);
    }
}

/// <summary>
/// An admin creates an account without a password and the user is emailed a link to choose one. If the email
/// can't be sent the account still exists, and the admin can resend the invitation.
/// </summary>
public sealed class CreateUserHandler(
    IIdentityService identity,
    IInvitationSender invitations,
    IValidator<CreateUserCommand> validator)
{
    public async Task<Result<UserDto>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var email = command.Email.Trim();
        if (await identity.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return UserErrors.EmailTaken;
        }

        var created = await identity.CreateAsync(new NewUser(email, command.DisplayName.Trim(), command.IsAdmin), cancellationToken);
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        var token = await identity.GenerateInvitationTokenAsync(created.Value.Id, cancellationToken);
        if (!await invitations.SendAsync(created.Value, token, cancellationToken))
        {
            return UserErrors.InvitationNotSent;
        }

        return created.Value;
    }
}
