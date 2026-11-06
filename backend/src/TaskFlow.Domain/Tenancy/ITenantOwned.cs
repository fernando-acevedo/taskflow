namespace TaskFlow.Domain.Tenancy;

/// <summary>
/// Marks an entity that belongs to one tenant (ADR 0012). The <c>TenantId</c> is a shadow property of the EF Core
/// model, filled on save and filtered on every query by <c>AppDbContext</c>, so the entity itself never carries it.
/// </summary>
public interface ITenantOwned;
