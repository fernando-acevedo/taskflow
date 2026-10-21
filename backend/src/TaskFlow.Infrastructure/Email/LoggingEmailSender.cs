using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Email;

/// <summary>Used when no SMTP host is configured: logs the email in development, fails everywhere else.</summary>
internal sealed partial class LoggingEmailSender(IOptions<EmailOptions> options, ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!options.Value.LogWhenSmtpIsMissing)
        {
            throw new InvalidOperationException("Email:Smtp:Host is not configured, so emails can't be sent.");
        }

        LogEmail(logger, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP is not configured; this email was not sent.\nTo: {To}\nSubject: {Subject}\n\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
