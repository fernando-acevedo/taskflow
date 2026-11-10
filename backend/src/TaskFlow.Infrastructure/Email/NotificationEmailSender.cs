using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Email.Outbox;
using TaskFlow.Domain.Notifications;

namespace TaskFlow.Infrastructure.Email;

/// <summary>
/// Writes the email for one notification, or the list of several (the daily digest), in the language of the
/// current request (English when there is none, as in the background job). Links point to the task on the
/// frontend. The message goes to the outbox (ADR 0018) with the caller's next save; one that can't be composed is
/// logged and skipped: the change that raised the notification must still happen.
/// </summary>
internal sealed partial class NotificationEmailSender(
    IEmailOutbox outbox,
    IOptions<EmailOptions> emailOptions,
    ILogger<NotificationEmailSender> logger) : INotificationEmailSender
{
    public void Enqueue(UserDto recipient, IReadOnlyList<Notification> notifications)
    {
        try
        {
            outbox.Enqueue("Notification", Compose(recipient, notifications));
        }
#pragma warning disable CA1031 // A message that can't be composed (settings) must leave the operation that raised it intact.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, exception, recipient.Id);
        }
    }

    private EmailMessage Compose(UserDto recipient, IReadOnlyList<Notification> notifications)
    {
        var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? Spanish : English;
        var greeting = string.Format(CultureInfo.InvariantCulture, text.Greeting, recipient.DisplayName);
        var lines = notifications.Select(notification => Line(text, notification)).ToList();
        var subject = notifications.Count == 1 && notifications[0].Type != NotificationType.TaskDueSoon
            ? lines[0].Sentence
            : text.DigestSubject;

        var textBody = new StringBuilder().Append(greeting).Append("\n\n");
        var htmlBody = new StringBuilder().Append("<p>").Append(WebUtility.HtmlEncode(greeting)).Append("</p>");
        foreach (var line in lines)
        {
            textBody.Append("- ").Append(line.Sentence).Append("\n  ").Append(line.Link).Append('\n');
            htmlBody.Append("<p>").Append(WebUtility.HtmlEncode(line.Sentence)).Append("<br><a href=\"")
                .Append(WebUtility.HtmlEncode(line.Link)).Append("\">").Append(WebUtility.HtmlEncode(text.Open)).Append("</a></p>");
        }

        return new EmailMessage(recipient.Email, subject, textBody.ToString(), htmlBody.ToString());
    }

    private (string Sentence, string Link) Line(NotificationText text, Notification notification)
    {
        using var data = JsonDocument.Parse(notification.Data);
        string Value(string key) => data.RootElement.TryGetProperty(key, out var value) ? value.ToString() : string.Empty;

        var template = notification.Type switch
        {
            NotificationType.TaskAssigned => text.Assigned,
            NotificationType.Mentioned => text.Mentioned,
            NotificationType.CommentOnYourTask => text.Commented,
            _ => text.DueSoon,
        };
        var sentence = string.Format(
            CultureInfo.InvariantCulture, template, Value("actorName"), Value("taskTitle"), Value("projectName"), Value("dueDate"));
        return (sentence, LinkEmail.TaskLink(emailOptions.Value, notification.ProjectId, notification.TaskItemId));
    }

    private sealed record NotificationText(
        string Greeting, string Assigned, string Mentioned, string Commented, string DueSoon, string DigestSubject, string Open);

    // {0} actor, {1} task, {2} project, {3} due date
    private static readonly NotificationText English = new(
        "Hi {0},",
        "{0} assigned you the task \"{1}\" in {2}.",
        "{0} mentioned you in a comment on \"{1}\" in {2}.",
        "{0} commented on your task \"{1}\" in {2}.",
        "Your task \"{1}\" in {2} is due on {3}.",
        "Tasks due soon",
        "Open the task");

    private static readonly NotificationText Spanish = new(
        "Hola, {0}:",
        "{0} te asignó la tarea \"{1}\" en {2}.",
        "{0} te mencionó en un comentario de \"{1}\" en {2}.",
        "{0} comentó tu tarea \"{1}\" en {2}.",
        "Tu tarea \"{1}\" en {2} vence el {3}.",
        "Tareas por vencer",
        "Abrir la tarea");

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue a notification email to user {UserId}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid userId);
}
