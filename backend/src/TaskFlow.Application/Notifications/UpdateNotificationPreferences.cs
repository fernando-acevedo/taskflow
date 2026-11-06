using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Application.Notifications;

public sealed record UpdateNotificationPreferencesCommand(
    bool EmailOnAssigned,
    bool EmailOnMentioned,
    bool EmailOnCommentOnMyTask,
    bool EmailDailyDigest);

public sealed class UpdateNotificationPreferencesHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<NotificationPreferencesDto>> HandleAsync(
        UpdateNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        var preferences = await db.NotificationPreferences.FirstOrDefaultAsync(
            preferences => preferences.UserId == currentUser.Id, cancellationToken);
        if (preferences is null)
        {
            preferences = NotificationPreferences.CreateDefault(currentUser.Id);
            db.NotificationPreferences.Add(preferences);
        }

        preferences.Update(command.EmailOnAssigned, command.EmailOnMentioned, command.EmailOnCommentOnMyTask, command.EmailDailyDigest);
        await db.SaveChangesAsync(cancellationToken);
        return NotificationPreferencesDto.From(preferences);
    }
}
