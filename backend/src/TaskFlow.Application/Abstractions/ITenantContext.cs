namespace TaskFlow.Application.Abstractions;

/// <summary>
/// The tenant the current request works in (ADR 0012). <c>null</c> means none was resolved, and then the data
/// access fails closed: queries return nothing and saves are rejected.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}
