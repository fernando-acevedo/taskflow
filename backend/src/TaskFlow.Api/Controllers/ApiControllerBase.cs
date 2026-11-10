using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.Controllers;

/// <summary>Maps use-case results to HTTP responses so every controller answers errors the same way.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult NoContentOrProblem(Result result) =>
        result.IsSuccess ? NoContent() : ProblemFrom(result.Error);

    protected ActionResult<T> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : ProblemFrom(result.Error);

    /// <summary>
    /// 200 with a strong <c>ETag</c> (the rowversion, base64, quoted). 304 when <c>If-None-Match</c> already names it.
    /// </summary>
    protected ActionResult<T> OkOrProblem<T>(Result<T> result, Func<T, byte[]> rowVersion) =>
        result.IsSuccess
            ? WithETag(result.Value, $"\"{Convert.ToBase64String(rowVersion(result.Value))}\"")
            : ProblemFrom(result.Error);

    /// <summary>
    /// For lists: a weak ETag hashed (SHA-256, truncated) from the response as serialized and the query string, with
    /// 304 on a match. The list is still computed and serialized to build the tag, so this saves bandwidth, not work.
    /// </summary>
    protected ActionResult<T> OkOrProblemWithWeakETag<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            return ProblemFrom(result.Error);
        }

        // The same options the response is written with (enums as strings), so the tag follows what the client sees.
        var jsonOptions = HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var body = JsonSerializer.SerializeToUtf8Bytes(result.Value, jsonOptions);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(body);
        hash.AppendData(Encoding.UTF8.GetBytes(Request.QueryString.Value ?? string.Empty));
        return WithETag(result.Value, $"W/\"{Convert.ToHexString(hash.GetHashAndReset(), 0, 16)}\"");
    }

    private ActionResult<T> WithETag<T>(T value, string etag)
    {
        Response.Headers.ETag = etag;

        // The answer depends on who asks and in which tenant, so a shared cache must not reuse it.
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.Append(HeaderNames.Vary, "Authorization, X-Tenant");
        var seen = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(seen)
            && seen.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(candidate => candidate == "*" || candidate.TrimStart('W', '/') == etag.TrimStart('W', '/')))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(value);
    }

    private string IfMatchHeader => Request.Headers.IfMatch.ToString().Trim();

    /// <summary><c>If-Match: *</c>: the write applies to any existing version.</summary>
    private bool IfMatchIsAny => IfMatchHeader == "*";

    private bool HasIfMatch => IfMatchHeader.Length > 0 && !IfMatchIsAny;

    /// <summary>
    /// True when the request names the version it acts on, in <c>If-Match</c> (any form) or in the body. Writes that
    /// require one answer 428 through <see cref="CommonErrors.PreconditionRequired"/> when this is false.
    /// </summary>
    protected bool HasPrecondition(byte[]? body) => IfMatchHeader.Length > 0 || body is { Length: > 0 };

    /// <summary>
    /// The version a write acts on: the one in <c>If-Match</c> when the header is sent, otherwise the one in the body.
    /// <c>If-Match: *</c> is <see cref="RowVersions.Any"/>. A list (<c>"a", "b"</c>) matches when any strong entry
    /// does; weak entries and values that aren't a quoted base64 version never match, so they end in 412. Empty when
    /// neither header nor body came.
    /// </summary>
    protected byte[] RowVersionFrom(byte[]? body)
    {
        if (IfMatchIsAny)
        {
            return RowVersions.Any;
        }

        if (!HasIfMatch)
        {
            return body ?? [];
        }

        var versions = new List<byte>();
        foreach (var entry in IfMatchHeader.Split(',', StringSplitOptions.TrimEntries))
        {
            if (entry.Length > 2 && entry[0] == '"' && entry[^1] == '"')
            {
                try
                {
                    versions.AddRange(Convert.FromBase64String(entry[1..^1]));
                }
                catch (FormatException)
                {
                    // Skipped: it can't match any version.
                }
            }
        }

        return versions.Count > 0 ? [.. versions] : [0];
    }

    /// <summary>The <c>If-Match</c> version for an action with no body version (deletes, archive); null when not sent or <c>*</c>.</summary>
    protected byte[]? OptionalRowVersion() => HasIfMatch ? RowVersionFrom(null) : null;

    /// <summary>201 with a Location header built from the new resource's route values.</summary>
    protected ActionResult<T> CreatedOrProblem<T>(Result<T> result, string actionName, Func<T, object> routeValues) =>
        result.IsSuccess ? CreatedAtAction(actionName, routeValues(result.Value), result.Value) : ProblemFrom(result.Error);

    /// <summary>
    /// Validation errors become a ValidationProblemDetails keyed by field. Other errors become a ProblemDetails
    /// whose title is localized from the error code (falling back to the English description) and which
    /// carries the code as an extension, so the frontend can pick its own translation.
    /// </summary>
    protected ActionResult ProblemFrom(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // With If-Match: * there is nothing to compare, so a missing item is the failed precondition.
        if (IfMatchIsAny && error.Type == ErrorType.NotFound && Request.Method != HttpMethods.Get)
        {
            error = CommonErrors.PreconditionFailed;
        }

        // A stale version that came in If-Match is a failed precondition (412); one in the body stays a 409.
        if (error.Code == CommonErrors.Stale.Code && HasIfMatch)
        {
            error = CommonErrors.PreconditionFailed;
        }

        if (error.Type == ErrorType.Validation)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in error.ValidationErrors ?? new Dictionary<string, string[]>())
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            return ValidationProblem(modelState);
        }

        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            ErrorType.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            _ => StatusCodes.Status400BadRequest,
        };

        var localizer = HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var localized = localizer[error.Code];
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            statusCode,
            title: localized.ResourceNotFound ? error.Description : localized.Value);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
