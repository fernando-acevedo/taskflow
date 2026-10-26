using System.Net;

namespace TaskFlow.Infrastructure.Email;

/// <summary>The emails that send the user to a frontend page with a one-time value (invitation, password reset).</summary>
internal static class LinkEmail
{
    /// <summary>
    /// The values (an email or user id and the one-time value) go in the fragment, so they never reach a server (no
    /// access logs, no <c>Referer</c>); the frontend page reads them from there.
    /// </summary>
    public static string FrontendLink(EmailOptions options, string page, params (string Name, string Value)[] values)
    {
        var frontendUrl = options.FrontendUrl
            ?? throw new InvalidOperationException("Email:FrontendUrl is not configured.");
        var fragment = string.Join('&', values.Select(value => $"{value.Name}={Uri.EscapeDataString(value.Value)}"));
        return $"{frontendUrl.TrimEnd('/')}/{page}#{fragment}";
    }

    public static EmailMessage Compose(string to, string subject, string greeting, string intro, string button, string link, string footer)
    {
        var textBody = $"{greeting}\n\n{intro}\n\n{link}\n\n{footer}\n";
        var htmlBody = $"""
            <p>{WebUtility.HtmlEncode(greeting)}</p>
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(button)}</a></p>
            <p>{WebUtility.HtmlEncode(footer)}</p>
            """;

        return new EmailMessage(to, subject, textBody, htmlBody);
    }
}
