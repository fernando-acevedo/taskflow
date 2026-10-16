using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager, AppDbContext db) : IIdentityService
{
    public async Task<UserDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await ProjectRows(db.Users.Where(user => user.Id == userId), await AdminRoleIdAsync(cancellationToken))
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDto();
    }

    public async Task<PagedResult<UserDto>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            users = users.Where(user => user.Email!.Contains(search) || (user.DisplayName != null && user.DisplayName.Contains(search)));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var rows = await ProjectRows(users.OrderBy(user => user.Email).Skip((page - 1) * pageSize).Take(pageSize), await AdminRoleIdAsync(cancellationToken))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>([.. rows.Select(row => row.ToDto())], page, pageSize, totalCount);
    }

    public async Task UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId);
        user.DisplayName = displayName;
        EnsureSucceeded(await userManager.UpdateAsync(user));
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId);
        user.IsActive = isActive;
        user.LockoutEnabled = true;
        user.LockoutEnd = isActive ? null : DateTimeOffset.MaxValue;
        EnsureSucceeded(await userManager.UpdateAsync(user));

        if (isActive)
        {
            EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user));
        }
        else
        {
            EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
        }
    }

    public async Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userId);
        var isInRole = await userManager.IsInRoleAsync(user, Roles.Admin);

        if (isAdmin && !isInRole)
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin));
        }
        else if (!isAdmin && isInRole)
        {
            EnsureSucceeded(await userManager.RemoveFromRoleAsync(user, Roles.Admin));
        }

        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user));
    }

    private Task<Guid> AdminRoleIdAsync(CancellationToken cancellationToken) =>
        db.Roles.Where(role => role.Name == Roles.Admin).Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<UserRow> ProjectRows(IQueryable<ApplicationUser> users, Guid adminRoleId) =>
        users.Select(user => new UserRow(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.IsActive,
            user.CreatedAt,
            db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId)));

    private async Task<ApplicationUser> GetUserAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString())
        ?? throw new InvalidOperationException($"User '{userId}' does not exist.");

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity operation failed: " + string.Join("; ", result.Errors.Select(error => error.Code)));
        }
    }

    private sealed record UserRow(Guid Id, string Email, string? DisplayName, bool IsActive, DateTimeOffset CreatedAt, bool IsAdmin)
    {
        public UserDto ToDto() => new(
            Id,
            Email,
            string.IsNullOrWhiteSpace(DisplayName) ? Email.Split('@')[0] : DisplayName,
            IsActive,
            IsAdmin ? [Roles.Admin] : [],
            CreatedAt);
    }
}
