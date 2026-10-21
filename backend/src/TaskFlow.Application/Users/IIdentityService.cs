using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Users;

/// <summary>User and role operations backed by ASP.NET Core Identity (implemented in Infrastructure).</summary>
public interface IIdentityService
{
    Task<UserDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserDto?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>The users that exist among <paramref name="userIds"/>, in no particular order.</summary>
    Task<IReadOnlyList<UserDto>> FindByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<PagedResult<UserDto>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a confirmed, active account without a password; the user chooses one by accepting the invitation.
    /// Fails with <see cref="UserErrors.EmailTaken"/>.
    /// </summary>
    Task<Result<UserDto>> CreateAsync(NewUser user, CancellationToken cancellationToken);

    /// <summary>A single-use token for <see cref="AcceptInvitationAsync"/>; it stops working once a password is set.</summary>
    Task<string> GenerateInvitationTokenAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the first password of an invited user. Fails with <see cref="UserErrors.InvitationInvalid"/> for an
    /// unknown email, a wrong or expired token or an account that already has a password, and with a validation
    /// error keyed by Identity error code when the password doesn't meet the policy.
    /// </summary>
    Task<Result> AcceptInvitationAsync(string email, string token, string password, CancellationToken cancellationToken);

    Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken);

    /// <summary>Deactivating also locks the account and ends its sessions (refresh tokens stop working).</summary>
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Changing the role ends the user's sessions so the next login carries the new role.</summary>
    Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);
}

public sealed record NewUser(string Email, string DisplayName, bool IsAdmin);
