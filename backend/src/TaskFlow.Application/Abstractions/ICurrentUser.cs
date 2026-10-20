namespace TaskFlow.Application.Abstractions;

/// <summary>The authenticated user of the current request.</summary>
public interface ICurrentUser
{
    Guid Id { get; }
}
