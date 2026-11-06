using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TaskFlow.Api.Identity;

/// <summary>Declares the bearer scheme in the OpenAPI document: an access token from /api/auth/login, or an API key (<c>tf_...</c>).</summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.Description =
            "Send `Authorization: Bearer <token>`. The token is either an access token from `/api/auth/login` or an API key "
            + "(`tf_...`, created with `POST /api/api-keys`). API keys only reach `/api/projects/**`, are limited per minute "
            + "(429 when exceeded) and never manage projects. A person chooses the tenant with `X-Tenant`; a key's tenant is fixed.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Access token or API key (`tf_...`).",
        };
        return Task.CompletedTask;
    }
}
