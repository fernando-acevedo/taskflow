namespace TaskFlow.Application.Users;

/// <summary>Emails an invited user the link to the page where they choose their password (implemented in Infrastructure).</summary>
public interface IInvitationSender
{
    /// <returns>False when the email couldn't be sent; the failure is logged.</returns>
    Task<bool> SendAsync(UserDto user, string token, CancellationToken cancellationToken);

    /// <summary>Tells a user who already has an account that they were added to a tenant (no password link, ADR 0013).</summary>
    /// <returns>False when the email couldn't be sent; the failure is logged.</returns>
    Task<bool> SendTenantAddedAsync(UserDto user, string tenantName, CancellationToken cancellationToken);
}
