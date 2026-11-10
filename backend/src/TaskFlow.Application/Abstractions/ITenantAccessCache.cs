namespace TaskFlow.Application.Abstractions;

/// <summary>Drops the cached memberships of a user, in every tenant (ADR 0013); implemented by Infrastructure.</summary>
public interface ITenantAccessCache
{
    ValueTask ForgetAsync(Guid userId, CancellationToken cancellationToken = default);
}
