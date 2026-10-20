using TaskFlow.Application.Common;

namespace TaskFlow.Application.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

public static class Roles
{
    public const string Admin = "Admin";
}

public static class UserErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("Users.NotFound", $"User '{userId}' was not found.");

    public static readonly Error CannotChangeOwnStatus =
        Error.Conflict("Users.CannotChangeOwnStatus", "You can't deactivate or reactivate your own account.");

    public static readonly Error CannotChangeOwnRole =
        Error.Conflict("Users.CannotChangeOwnRole", "You can't change your own role.");
}
