using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Email;

/// <summary>
/// The emails the Identity API Endpoints send, in the language of the request, with links to frontend pages:
/// <c>/register</c> sends the email confirmation (needed before Identity sends a password reset), and
/// <c>/forgotPassword</c> the reset link. A failure is logged rather than thrown: the account already exists when
/// these run, and <c>/forgotPassword</c> answers 200 whether or not it does, so a 500 would tell the caller it does.
/// </summary>
internal sealed partial class IdentityEmailSender(
    IEmailSender sender,
    IOptions<EmailOptions> emailOptions,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions,
    ILogger<IdentityEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        // Identity's link points to its own /confirmEmail endpoint (HTML-encoded); the frontend page calls that
        // endpoint with the same user id and code. The app doesn't support changing emails (/manage/info), whose
        // links carry changedEmail: confirming such a link here would confirm the current email instead.
        var query = QueryHelpers.ParseQuery(new Uri(WebUtility.HtmlDecode(confirmationLink)).Query);
        if (query.ContainsKey("changedEmail"))
        {
            LogEmailChangeIgnored(logger, user.Id);
            return Task.CompletedTask;
        }

        return SendAsync(user, email, Confirmation, () => LinkEmail.FrontendLink(
            emailOptions.Value, "confirm-email", ("userId", query["userId"].ToString()), ("code", query["code"].ToString())));
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        // A deactivated user couldn't sign in with the new password anyway.
        if (!user.IsActive)
        {
            return Task.CompletedTask;
        }

        // Identity passes the code base64url-encoded and then HTML-encoded, which leaves base64url as it is.
        return SendAsync(user, email, Reset, () => LinkEmail.FrontendLink(
            emailOptions.Value, "reset-password", ("email", email), ("code", resetCode)));
    }

    // Only used by Identity's Razor UI, which the app doesn't have.
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;

    private async Task SendAsync(ApplicationUser user, string email, LocalizedText texts, Func<string> buildLink)
    {
        try
        {
            var hours = (int)tokenOptions.Value.TokenLifespan.TotalHours;
            var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? texts.Spanish : texts.English;
            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? email.Split('@')[0] : user.DisplayName;
            var message = LinkEmail.Compose(
                email,
                text.Subject,
                string.Format(CultureInfo.InvariantCulture, text.Greeting, name),
                string.Format(CultureInfo.InvariantCulture, text.Intro, email),
                text.Button,
                buildLink(),
                string.Format(CultureInfo.InvariantCulture, text.Expiry, hours));
            await sender.SendAsync(message, CancellationToken.None);
        }
#pragma warning disable CA1031 // Any delivery failure (settings, network, SMTP) must not change the endpoint's answer.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, exception, texts.Kind, user.Id);
        }
    }

    private sealed record EmailText(string Subject, string Greeting, string Intro, string Button, string Expiry);

    private sealed record LocalizedText(string Kind, EmailText English, EmailText Spanish);

    private static readonly LocalizedText Confirmation = new(
        "email confirmation",
        new(
            "Confirm your TaskFlow email",
            "Hi {0},",
            "Thanks for creating a TaskFlow account. Confirm that {0} is your email, so you can reset your password if you ever forget it:",
            "Confirm my email",
            "The link expires in {0} hours. If you didn't create this account, ignore this email."),
        new(
            "Confirma tu email de TaskFlow",
            "Hola, {0}:",
            "Gracias por crear una cuenta en TaskFlow. Confirma que {0} es tu email, así podrás restablecer tu contraseña si alguna vez la olvidas:",
            "Confirmar mi email",
            "El enlace vence en {0} horas. Si no creaste esta cuenta, ignora este email."));

    private static readonly LocalizedText Reset = new(
        "password reset",
        new(
            "Reset your TaskFlow password",
            "Hi {0},",
            "Someone asked to reset the password of your TaskFlow account ({0}). Choose a new one here:",
            "Choose a new password",
            "The link works once and expires in {0} hours. If you didn't ask for it, ignore this email: your password stays the same."),
        new(
            "Restablece tu contraseña de TaskFlow",
            "Hola, {0}:",
            "Alguien pidió restablecer la contraseña de tu cuenta de TaskFlow ({0}). Elige una nueva aquí:",
            "Elegir una contraseña nueva",
            "El enlace sirve una sola vez y vence en {0} horas. Si no lo pediste, ignora este email: tu contraseña no cambia."));

    // The user id rather than the email: logs are kept longer and read by more people than the user list.
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the {Kind} email to user {UserId}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string kind, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored an email change for user {UserId}: the app doesn't support changing emails")]
    private static partial void LogEmailChangeIgnored(ILogger logger, Guid userId);
}
