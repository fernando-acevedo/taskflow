using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Users;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Email;

/// <summary>
/// Writes the invitation in the language of the request that triggered it (the admin's), and links to the
/// frontend's accept-invitation page with the user's email and token.
/// </summary>
internal sealed partial class InvitationSender(
    IEmailSender sender,
    IOptions<EmailOptions> emailOptions,
    IOptions<InvitationTokenProviderOptions> tokenOptions,
    ILogger<InvitationSender> logger) : IInvitationSender
{
    public async Task<bool> SendAsync(UserDto user, string token, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(Compose(user, BuildLink(user.Email, token)), cancellationToken);
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

    // The user id rather than the email: logs are kept longer and read by more people than the user list.
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the invitation email to user {UserId}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid userId);
}
