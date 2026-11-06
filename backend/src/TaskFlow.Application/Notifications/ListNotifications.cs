using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Notifications;

public sealed record ListNotificationsQuery(bool UnreadOnly = false, int Page = 1, int PageSize = 20);

public sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>The current user's notifications, newest first.</summary>
public sealed class ListNotificationsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<ListNotificationsQuery> validator)
{
    public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
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

        var page = await notifications.AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);

        return new PagedResult<NotificationDto>([.. page.Items.Select(NotificationDto.From)], page.Page, page.PageSize, page.TotalCount);
    }
}
