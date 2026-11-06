using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.ApiKeys;
using TaskFlow.Domain.ApiKeys;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// The current user's API keys for the current tenant (ADR 0016). Only a signed-in person can use these endpoints, not
/// a key; they are disabled in the public demo.
/// </summary>
[Authorize]
[Route("api/api-keys")]
public sealed class ApiKeysController : ApiControllerBase
{
    /// <summary>Your keys, newest first. The secret is never returned here.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApiKeyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(
        [FromServices] ListApiKeysHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Creates a key. The response carries the <c>secret</c> (<c>tf_...</c>) once; store it, it can't be read again. Send it
    /// as <c>Authorization: Bearer tf_...</c>. Without <c>expiresAt</c> the key lasts 90 days; the maximum is one year.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedApiKeyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedApiKeyDto>> Create(
        CreateApiKeyRequest request,
        [FromServices] CreateApiKeyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateApiKeyCommand(request.Name, request.Scope, request.ExpiresAt), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ProblemFrom(result.Error);
    }

    /// <summary>Revokes the key; it stops working immediately. It stays in the list as revoked.</summary>
    [HttpDelete("{keyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Revoke(
        Guid keyId,
        [FromServices] RevokeApiKeyHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new RevokeApiKeyCommand(keyId), cancellationToken));
}

/// <param name="Name">A label to recognize the key, up to 100 characters.</param>
/// <param name="Scope"><c>Read</c> (default) or <c>Write</c>; a key never manages projects.</param>
/// <param name="ExpiresAt">Optional; defaults to 90 days from now, at most one year.</param>
public sealed record CreateApiKeyRequest(string Name, ApiKeyScope Scope = ApiKeyScope.Read, DateTimeOffset? ExpiresAt = null);
