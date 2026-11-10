using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TaskFlow.Api.Identity;

/// <summary>Documents the 429 that every endpoint can answer when a rate limit is exceeded (ADR 0022), with its <c>Retry-After</c>.</summary>
internal sealed class RateLimitResponseTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        if (!document.Components.Schemas.ContainsKey("ProblemDetails"))
        {
            return Task.CompletedTask;
        }

        foreach (var operation in (document.Paths ?? []).Values.SelectMany(path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
        {
            operation.Responses ??= [];
            if (operation.Responses.ContainsKey("429"))
            {
                continue;
            }

            operation.Responses["429"] = new OpenApiResponse
            {
                Description = "Too many requests. Wait the seconds in Retry-After and try again.",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["Retry-After"] = new OpenApiHeader { Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" } },
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference("ProblemDetails", document) },
                },
            };
        }

        return Task.CompletedTask;
    }
}
