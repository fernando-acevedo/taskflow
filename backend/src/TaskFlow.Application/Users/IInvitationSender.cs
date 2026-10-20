namespace TaskFlow.Application.Users;

/// <summary>Emails an invited user the link to the page where they choose their password (implemented in Infrastructure).</summary>
public interface IInvitationSender
{
    /// <returns>False when the email couldn't be sent; the failure is logged.</returns>
    Task<bool> SendAsync(UserDto user, string token, CancellationToken cancellationToken);
}
