using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Makes the generated schemas say what the API really does, so clients generated from them get the right types:
/// <list type="bullet">
/// <item>numbers are accepted as strings (System.Text.Json web defaults) but always written as numbers, so the
/// <c>integer | string</c> schema with a pattern keeps just the number type;</item>
/// <item>enums are written as their names, so they are strings;</item>
/// <item>a request's <c>rowVersion</c> may be left out when <c>If-Match</c> carries the version, so it isn't required.</item>
/// </list>
/// </summary>
internal sealed class ContractSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String)
            && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        if (schema.Enum is { Count: > 0 } && schema.Type is null)
        {
            schema.Type = JsonSchemaType.String;
        }

        if (context.JsonTypeInfo.Type.Name.EndsWith("Request", StringComparison.Ordinal))
        {
            schema.Required?.Remove("rowVersion");
        }

        return Task.CompletedTask;
    }
}
