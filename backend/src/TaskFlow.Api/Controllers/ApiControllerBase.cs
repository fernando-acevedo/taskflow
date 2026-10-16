using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using TaskFlow.Application.Common;

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
    /// Validation errors become a ValidationProblemDetails keyed by field. Other errors become a ProblemDetails
    /// whose title is localized from the error code (falling back to the English description) and which
    /// carries the code as an extension, so the frontend can pick its own translation.
    /// </summary>
    protected ActionResult ProblemFrom(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

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
