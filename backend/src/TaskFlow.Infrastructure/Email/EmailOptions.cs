using MailKit.Security;

namespace TaskFlow.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Where the Angular app is served; links in emails point there.</summary>
    public string? FrontendUrl { get; set; }

    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "TaskFlow";

    /// <summary>
    /// Without an SMTP host, emails are written to the log instead of sent when this is true (development only:
    /// the log then holds working invitation links); otherwise sending fails.
    /// </summary>
    public bool LogWhenSmtpIsMissing { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public string? Username { get; set; }

    public string? Password { get; set; }
}
