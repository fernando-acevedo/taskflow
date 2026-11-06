namespace TaskFlow.Application.Abstractions;

/// <summary>Drops the cached membership check of a user in a tenant (ADR 0013); implemented by Infrastructure.</summary>
public interface ITenantAccessCache
{
    void Forget(Guid userId, Guid tenantId);
}
