using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TaskFlow.Infrastructure.Email;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>Keeps the emails the API would send so tests can follow the links in them.</summary>
public sealed partial class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> SentTo(string email) =>
        [.. _sent.Where(message => string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The token from the newest invitation link sent to <paramref name="email"/>.</summary>
    public string InvitationTokenFor(string email)
    {
        var match = InvitationLink().Match(SentTo(email)[^1].TextBody);
        match.Success.ShouldBeTrue($"No invitation link in the email to {email}.");
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    [GeneratedRegex(@"/accept-invitation#email=[^&\s]+&token=(?<token>\S+)")]
    private static partial Regex InvitationLink();
}
