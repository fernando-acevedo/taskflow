namespace TaskFlow.Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>A business rule rejected the request (400), as opposed to field validation.</summary>
    Invalid,

    /// <summary>An <c>If-Match</c> precondition failed (412).</summary>
    PreconditionFailed,

    /// <summary>The request needed a version (<c>If-Match</c> or the body's rowVersion) and carried none (428).</summary>
    PreconditionRequired,
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

    /// <summary>The <c>If-Match</c> header named a version that is no longer current.</summary>
    public static readonly Error PreconditionFailed =
        new("Concurrency.PreconditionFailed", "The item changed since you read it. Reload it and try again.", ErrorType.PreconditionFailed);

    /// <summary>A write that needs a version came with neither <c>If-Match</c> nor a <c>rowVersion</c> in the body.</summary>
    public static readonly Error PreconditionRequired =
        new("Concurrency.PreconditionRequired", "Send the version you read: an If-Match header or the rowVersion in the body.", ErrorType.PreconditionRequired);
}
