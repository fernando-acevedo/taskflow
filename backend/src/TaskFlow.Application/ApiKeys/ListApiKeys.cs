using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.ApiKeys;

/// <summary>The current user's keys in the current tenant, newest first. Never includes secrets.</summary>
public sealed class ListApiKeysHandler(IAppDbContext db, ICurrentUser currentUser, IDemoMode demo, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<ApiKeyDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ApiKeyId is not null)
        {
            return ApiKeyErrors.NotAllowedWithKey;
        }

        if (demo.IsEnabled)
        {
            return DemoErrors.ActionDisabled;
        }

        var userId = currentUser.Id;
        var now = time.GetUtcNow();
        var keys = await db.ApiKeys.AsNoTracking()
            .Where(key => key.UserId == userId)
            .OrderByDescending(key => key.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ApiKeyDto> dtos = keys
            .Select(key => new ApiKeyDto(
                key.Id, key.Name, key.Prefix, key.Scope, key.CreatedAt, key.ExpiresAt, key.RevokedAt, key.LastUsedAt, key.IsUsable(now)))
            .ToList();
        return Result<IReadOnlyList<ApiKeyDto>>.Success(dtos);
    }
}
