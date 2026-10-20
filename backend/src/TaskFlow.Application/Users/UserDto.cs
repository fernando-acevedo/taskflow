using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    bool IsInvitationPending = false);

public static class Roles
{
    public const string Admin = "Admin";
}

public static class UserErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("Users.NotFound", $"User '{userId}' was not found.");

    public static readonly Error EmailNotFound =
        Error.NotFound("Users.EmailNotFound", "No user is registered with that email.");

    public static readonly Error EmailTaken =
        Error.Conflict("Users.EmailTaken", "An account with this email already exists.");

    public static readonly Error InvitationNotSent =
        Error.Conflict(
            "Users.InvitationNotSent",
            "The user exists, but the invitation email couldn't be sent. Check the email settings and resend it.");

    public static readonly Error InvitationNotPending =
        Error.Conflict("Users.InvitationNotPending", "This user has already chosen a password.");

    public static readonly Error InvitationInvalid =
        Error.Invalid(
            "Users.InvitationInvalid",
            "This invitation link is invalid or has expired. Ask an administrator to send you a new one.");

    public static readonly Error CannotChangeOwnStatus =
        Error.Conflict("Users.CannotChangeOwnStatus", "You can't deactivate or reactivate your own account.");

    public static readonly Error CannotChangeOwnRole =
        Error.Conflict("Users.CannotChangeOwnRole", "You can't change your own role.");
}
