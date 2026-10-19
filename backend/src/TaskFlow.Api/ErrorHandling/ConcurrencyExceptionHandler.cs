using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.ErrorHandling;

/// <summary>
/// Handlers compare row versions before changing anything; this covers the race where another write lands
/// between that check and the save. It answers like the handlers do: 409 with the <c>Concurrency.Stale</c> code.
/// </summary>
internal sealed class ConcurrencyExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IStringLocalizer<SharedResource> localizer) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = localizer[CommonErrors.Stale.Code],
        };
        problem.Extensions["code"] = CommonErrors.Stale.Code;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
