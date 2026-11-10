using System.Text.RegularExpressions;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Api.Realtime;

/// <summary>The <c>X-Client-Id</c> of the current request (ADR 0023), set by <see cref="ClientOriginMiddleware"/>.</summary>
internal sealed class ClientOrigin : IClientOrigin
{
    public string? ClientId { get; set; }
}

/// <summary>Reads the <c>X-Client-Id</c> header (a short token the frontend makes up per tab) and keeps it for the events the request raises.</summary>
internal sealed partial class ClientOriginMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Client-Id";

    public Task InvokeAsync(HttpContext context, ClientOrigin origin)
    {
        var value = context.Request.Headers[HeaderName].ToString();
        origin.ClientId = ValidId().IsMatch(value) ? value : null;
        return next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ValidId();
}
