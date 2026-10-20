using TaskFlow.Application.Common;

namespace TaskFlow.Application.Users;

/// <summary>User and role operations backed by ASP.NET Core Identity (implemented in Infrastructure).</summary>
public interface IIdentityService
{
    Task<UserDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<UserDto>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken);

    /// <summary>Deactivating also locks the account and ends its sessions (refresh tokens stop working).</summary>
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Changing the role ends the user's sessions so the next login carries the new role.</summary>
    Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);
}
