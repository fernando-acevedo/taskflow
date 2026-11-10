using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Email.Outbox;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Email;

/// <summary>
/// Writes the invitation in the language of the request that triggered it (the admin's), and links to the
/// frontend's accept-invitation page with the user's email and token. The message is saved to the outbox at once
/// (ADR 0018) and sent, with retries, by the background worker; "not sent" now means "could not be queued".
/// </summary>
internal sealed partial class InvitationSender(
    IEmailOutbox outbox,
    IOptions<EmailOptions> emailOptions,
    IOptions<InvitationTokenProviderOptions> tokenOptions,
    ILogger<InvitationSender> logger) : IInvitationSender
{
    public async Task<bool> SendAsync(UserDto user, string token, CancellationToken cancellationToken)
    {
        try
        {
            await outbox.EnqueueAndSaveAsync("Invitation", Compose(user, BuildLink(user.Email, token)), cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // Any delivery failure (settings, network, SMTP) is reported to the admin as "not sent".
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, exception, user.Id);
            return false;
        }
    }

    public async Task<bool> SendTenantAddedAsync(UserDto user, string tenantName, CancellationToken cancellationToken)
    {
        try
        {
            var link = LinkEmail.FrontendLink(emailOptions.Value, "login").TrimEnd('#');
            var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? SpanishAdded : EnglishAdded;
            await outbox.EnqueueAndSaveAsync(
                "TenantAdded",
                LinkEmail.Compose(
                    user.Email,
                    string.Format(CultureInfo.InvariantCulture, text.Subject, tenantName),
                    string.Format(CultureInfo.InvariantCulture, text.Greeting, user.DisplayName),
                    string.Format(CultureInfo.InvariantCulture, text.Intro, tenantName),
                    text.Button,
                    link,
                    text.Footer),
                cancellationToken);
            return true;
        }
#pragma warning disable CA1031 // Any delivery failure (settings, network, SMTP) is reported to the admin as "not sent".
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, exception, user.Id);
            return false;
        }
    }

    private string BuildLink(string email, string token) =>
        LinkEmail.FrontendLink(emailOptions.Value, "accept-invitation", ("email", email), ("token", token));

    private EmailMessage Compose(UserDto user, string link)
    {
        var days = (int)tokenOptions.Value.TokenLifespan.TotalDays;
        var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? Spanish : English;
        return LinkEmail.Compose(
            user.Email,
            text.Subject,
            string.Format(CultureInfo.InvariantCulture, text.Greeting, user.DisplayName),
            string.Format(CultureInfo.InvariantCulture, text.Intro, user.Email),
            text.Button,
            link,
            string.Format(CultureInfo.InvariantCulture, text.Expiry, days));
    }

    private sealed record InvitationText(string Subject, string Greeting, string Intro, string Button, string Expiry);

    private static readonly InvitationText English = new(
        "You're invited to TaskFlow",
        "Hi {0},",
        "An administrator created a TaskFlow account for you ({0}). Choose your password to sign in:",
        "Choose my password",
        "The link works once and expires in {0} days. If it expires, ask an administrator to send a new one.");

    private static readonly InvitationText Spanish = new(
        "Te invitaron a TaskFlow",
        "Hola, {0}:",
        "Un administrador te creó una cuenta en TaskFlow ({0}). Elige tu contraseña para iniciar sesión:",
        "Elegir mi contraseña",
        "El enlace sirve una sola vez y vence en {0} días. Si vence, pide a un administrador que te envíe uno nuevo.");

    private sealed record AddedText(string Subject, string Greeting, string Intro, string Button, string Footer);

    private static readonly AddedText EnglishAdded = new(
        "You were added to {0} on TaskFlow",
        "Hi {0},",
        "An administrator added you to {0}. Sign in with your existing account to switch to it:",
        "Open TaskFlow",
        "Use the organization selector in the top bar to switch between your organizations.");

    private static readonly AddedText SpanishAdded = new(
        "Te agregaron a {0} en TaskFlow",
        "Hola, {0}:",
        "Un administrador te agregó a {0}. Inicia sesión con tu cuenta actual para cambiar a ella:",
        "Abrir TaskFlow",
        "Usa el selector de organización de la barra superior para cambiar entre tus organizaciones.");

    // The user id rather than the email: logs are kept longer and read by more people than the user list.
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the invitation email to user {UserId}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid userId);
}
