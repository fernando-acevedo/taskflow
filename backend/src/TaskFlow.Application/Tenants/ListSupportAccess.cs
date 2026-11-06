using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Tenants;

/// <summary>The latest entries of platform administrators into the current tenant (ADR 0013), newest first.</summary>
public sealed class ListSupportAccessHandler(IAppDbContext db, IIdentityService identity, ICurrentUser currentUser)
{
    public const int Limit = 100;

    public async Task<Result<IReadOnlyList<SupportAccessDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
        }

        var tenantId = currentUser.TenantId!.Value;
        var entries = await db.TenantSupportAccesses
            .Where(access => access.TenantId == tenantId)
            .OrderByDescending(access => access.At)
            .Take(Limit)
            .ToListAsync(cancellationToken);
        var users = (await identity.FindByIdsAsync(entries.Select(entry => entry.UserId).Distinct().ToList(), cancellationToken))
            .ToDictionary(user => user.Id);

        IReadOnlyList<SupportAccessDto> result = entries
            .Select(entry => users.TryGetValue(entry.UserId, out var user)
                ? new SupportAccessDto(entry.Id, entry.UserId, user.Email, user.DisplayName, entry.At)
                : new SupportAccessDto(entry.Id, entry.UserId, string.Empty, string.Empty, entry.At))
            .ToList();
        return Result<IReadOnlyList<SupportAccessDto>>.Success(result);
    }
}
