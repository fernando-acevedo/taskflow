using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Notifications;

public sealed record ListNotificationsQuery(bool UnreadOnly = false, string? Cursor = null, int PageSize = 20);

public sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator()
    {
        RuleFor(query => query.Cursor).Must(PageCursor.IsValidId).WithMessage("The cursor is not valid.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>The current user's notifications, newest first.</summary>
public sealed class ListNotificationsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<ListNotificationsQuery> validator)
{
    public async Task<Result<CursorPage<NotificationDto>>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var notifications = db.Notifications.Where(notification => notification.UserId == currentUser.Id
            && db.Projects.Any(project => project.Id == notification.ProjectId
                && project.Members.Any(member => member.UserId == currentUser.Id)));
        if (query.UnreadOnly)
        {
            notifications = notifications.Where(notification => notification.ReadAt == null);
        }

        if (PageCursor.TryDecode(query.Cursor, out var at, out var key))
        {
            var id = Guid.Parse(key);
            notifications = notifications.Where(notification =>
                notification.CreatedAt < at || (notification.CreatedAt == at && notification.Id.CompareTo(id) < 0));
        }

        var rows = await notifications.AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.PageSize;
        var items = hasMore ? rows.Take(query.PageSize).ToList() : rows;
        var last = items.Count > 0 ? items[^1] : null;
        return new CursorPage<NotificationDto>(
            [.. items.Select(NotificationDto.From)],
            hasMore && last is not null ? PageCursor.Encode(last.CreatedAt, last.Id.ToString()) : null);
    }
}
