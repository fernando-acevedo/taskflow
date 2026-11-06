namespace TaskFlow.Domain.Tenancy;

/// <summary>
/// A super-admin entered a tenant they do not belong to, by naming it in <c>X-Tenant</c> (ADR 0013). At most one row
/// per (user, tenant) every 30 minutes, stored in the <c>TenantSupportAccesses</c> table (not in the activity log); the
/// tenant's Owners and Admins can read them.
/// </summary>
public sealed class TenantSupportAccess
{
    private TenantSupportAccess(Guid id, Guid tenantId, Guid userId, DateTimeOffset at)
    {
        Id = id;
        TenantId = tenantId;
        UserId = userId;
        At = at;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static TenantSupportAccess Create(Guid tenantId, Guid userId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), tenantId, userId, now);
}
