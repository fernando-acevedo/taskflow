using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common;
using TaskFlow.Application.Notifications;

namespace TaskFlow.Api.Controllers;

/// <summary>The signed-in user's notifications and email preferences. Nobody reads another user's.</summary>
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    /// <summary>A page of notifications, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<CursorPage<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPage<NotificationDto>>> List(
        [FromServices] ListNotificationsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 20) =>
        OkOrProblem(await handler.HandleAsync(new ListNotificationsQuery(unreadOnly, cursor, pageSize), cancellationToken));

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(
        [FromServices] GetUnreadCountHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MarkRead(
        Guid id, [FromServices] MarkNotificationReadHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new MarkNotificationReadCommand(id), cancellationToken));

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> MarkAllRead(
        [FromServices] MarkAllNotificationsReadHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(cancellationToken));

    [HttpGet("preferences")]
    [ProducesResponseType<NotificationPreferencesDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesDto>> GetPreferences(
        [FromServices] GetNotificationPreferencesHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    [HttpPut("preferences")]
    [ProducesResponseType<NotificationPreferencesDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesDto>> UpdatePreferences(
        UpdateNotificationPreferencesCommand command,
        [FromServices] UpdateNotificationPreferencesHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(command, cancellationToken));
}
