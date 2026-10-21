using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
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

    public async Task<UserDto?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(email);
        var row = await ProjectRows(db.Users.Where(user => user.NormalizedEmail == normalizedEmail), await AdminRoleIdAsync(cancellationToken))
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDto();
    }

    public async Task<IReadOnlyList<UserDto>> FindByIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await ProjectRows(db.Users.Where(user => userIds.Contains(user.Id)), await AdminRoleIdAsync(cancellationToken))
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => row.ToDto())];
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

    public async Task<Result<UserDto>> CreateAsync(NewUser newUser, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = newUser.Email,
            Email = newUser.Email,
            EmailConfirmed = true,
            DisplayName = newUser.DisplayName,
        };

        var created = await userManager.CreateAsync(user);
        if (created.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return UserErrors.EmailTaken;
        }

        EnsureSucceeded(created);

        if (newUser.IsAdmin)
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.Admin));
        }

        return (await FindByIdAsync(user.Id, cancellationToken))!;
    }

    public async Task<string> GenerateInvitationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.GenerateUserTokenAsync(await GetUserAsync(userId), InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);

    public async Task<Result> AcceptInvitationAsync(string email, string token, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null
            || user.PasswordHash is not null
            || !await userManager.VerifyUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, token))
        {
            return UserErrors.InvitationInvalid;
        }

        // Adding the password also renews the security stamp, which makes the token unusable a second time.
        var added = await userManager.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            return Error.Validation(added.Errors
                .GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }

        return Result.Success();
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
            user.PasswordHash == null,
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

    private sealed record UserRow(
        Guid Id,
        string Email,
        string? DisplayName,
        bool IsActive,
        DateTimeOffset CreatedAt,
        bool HasNoPassword,
        bool IsAdmin)
    {
        public UserDto ToDto() => new(
            Id,
            Email,
            string.IsNullOrWhiteSpace(DisplayName) ? Email.Split('@')[0] : DisplayName,
            IsActive,
            IsAdmin ? [Roles.Admin] : [],
            CreatedAt,
            IsInvitationPending: HasNoPassword);
    }
}
