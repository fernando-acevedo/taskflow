namespace TaskFlow.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>A business rule rejected the request (400), as opposed to field validation.</summary>
    Invalid,
}

/// <summary>
/// An expected failure. <see cref="Code"/> is stable and is used by the API to localize the message
/// and by the frontend to pick a translation; <see cref="Description"/> is the English fallback.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Error is the domain term; VB consumers are not a concern.")]
public sealed record Error(
    string Code,
    string Description,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error Invalid(string code, string description) => new(code, description, ErrorType.Invalid);

    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new("Validation", "One or more validation errors occurred.", ErrorType.Validation, errors);
}

public static class CommonErrors
{
    /// <summary>The client sent an outdated row version: someone changed the item since it was read.</summary>
    public static readonly Error Stale =
        Error.Conflict("Concurrency.Stale", "Someone else changed this item. Reload it and try again.");
}
