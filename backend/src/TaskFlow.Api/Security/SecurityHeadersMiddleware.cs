namespace TaskFlow.Api.Security;

/// <summary>
/// Adds the response headers that harden an API (ADR 0022). They are set when the response starts, because the
/// exception handler clears the headers of a failed response before writing its own.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>JSON and error responses are never rendered, so nothing may load or embed.</summary>
    internal const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// The API reference (Scalar) serves its own scripts and styles, with an inline bootstrap script and style, web
    /// fonts from Scalar's font host, and "Test request" calls to the API itself or to Scalar's proxy.
    /// </summary>
    internal const string ReferencePolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; "
        + "font-src 'self' data: https://fonts.scalar.com; img-src 'self' data: https:; "
        + "connect-src 'self' https://proxy.scalar.com; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            var headers = httpContext.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = IsReference(httpContext.Request.Path) ? ReferencePolicy : ApiPolicy;
            return Task.CompletedTask;
        }, context);

        return next(context);
    }

    private static bool IsReference(PathString path) => path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase);
}
