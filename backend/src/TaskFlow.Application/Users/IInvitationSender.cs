namespace TaskFlow.Application.Users;

/// <summary>Queues in the outbox (ADR 0018) the email that gives an invited user the link to the page where they choose their password (implemented in Infrastructure; delivery is retried in the background).</summary>
public interface IInvitationSender
{
    /// <returns>False when the email couldn't be queued; the failure is logged.</returns>
    Task<bool> SendAsync(UserDto user, string token, CancellationToken cancellationToken);

    /// <summary>Tells a user who already has an account that they were added to a tenant (no password link, ADR 0013).</summary>
    /// <returns>False when the email couldn't be queued; the failure is logged.</returns>
    Task<bool> SendTenantAddedAsync(UserDto user, string tenantName, CancellationToken cancellationToken);
}
