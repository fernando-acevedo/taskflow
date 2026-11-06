namespace TaskFlow.Domain.Tenancy;

public enum TenantRole
{
    Member,
    Admin,
    Owner,
}

/// <summary>A user's role in a tenant. Users are global; a user can belong to several tenants (ADR 0012).</summary>
public sealed class TenantMembership
{
    private TenantMembership(Guid tenantId, Guid userId, TenantRole role, DateTimeOffset joinedAt)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static TenantMembership Create(Guid tenantId, Guid userId, TenantRole role, DateTimeOffset now) =>
        new(tenantId, userId, role, now);

    public void ChangeRole(TenantRole role) => Role = role;
}
