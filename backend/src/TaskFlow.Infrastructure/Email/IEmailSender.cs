namespace TaskFlow.Infrastructure.Email;

/// <summary>Delivers a composed email. Public so integration tests can capture what would be sent.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);
