namespace TaskFlow.Domain.Common;

/// <summary>A fact about something that happened to an entity (ADR 0024). Handlers react to it around SaveChanges.</summary>
public interface IDomainEvent;

/// <summary>An entity that accumulates <see cref="IDomainEvent"/>s as its methods run.</summary>
public interface IHasDomainEvents
{
    /// <summary>Returns the events raised since the last call, in order, and forgets them.</summary>
    IReadOnlyList<IDomainEvent> TakeDomainEvents();
}

/// <summary>The old and the new value of a field.</summary>
public sealed record Change<T>(T From, T To);
