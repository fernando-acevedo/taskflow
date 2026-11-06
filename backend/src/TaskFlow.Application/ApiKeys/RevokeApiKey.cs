using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.ApiKeys;

public sealed record RevokeApiKeyCommand(Guid KeyId);

/// <summary>Revokes one of the current user's keys. It stops working with the next request; revoking twice is harmless.</summary>
public sealed class RevokeApiKeyHandler(IAppDbContext db, ICurrentUser currentUser, IDemoMode demo, TimeProvider time)
{
    public async Task<Result> HandleAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken)
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
        var key = await db.ApiKeys.FirstOrDefaultAsync(key => key.Id == command.KeyId && key.UserId == userId, cancellationToken);
        if (key is null)
        {
            return ApiKeyErrors.NotFound;
        }

        key.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
