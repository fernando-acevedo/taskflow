using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace TaskFlow.Api.ErrorHandling;

/// <summary>Turns unhandled exceptions into an RFC 9457 ProblemDetails response without leaking internals.</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IStringLocalizer<SharedResource> localizer,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandledException(logger, exception, httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = localizer["UnexpectedError"],
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for request {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string traceId);
}
