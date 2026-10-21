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

/// <param name="InvitationSent">False when the email couldn't be sent; the account exists and the admin can resend it.</param>
public sealed record CreatedUserDto(UserDto User, bool InvitationSent);

/// <summary>
/// An admin creates an account without a password and the user is emailed a link to choose one. The account is
/// created either way: a failed email is reported in the result rather than as an error, since retrying the
/// creation would only find the email taken.
/// </summary>
public sealed class CreateUserHandler(
    IIdentityService identity,
    IInvitationSender invitations,
    IValidator<CreateUserCommand> validator)
{
    public async Task<Result<CreatedUserDto>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
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
        var sent = await invitations.SendAsync(created.Value, token, cancellationToken);
        return new CreatedUserDto(created.Value, sent);
    }
}
