using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Notifications;

/// <summary>The current user's email preferences; a user who never saved any has everything off.</summary>
public sealed class GetNotificationPreferencesHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<NotificationPreferencesDto>> HandleAsync(CancellationToken cancellationToken) =>
        NotificationPreferencesDto.From(await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(preferences => preferences.UserId == currentUser.Id, cancellationToken));
}
