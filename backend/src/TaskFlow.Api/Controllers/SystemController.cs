using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(IStringLocalizer<SharedResource> localizer, IWebHostEnvironment environment) : ControllerBase
{
    private static readonly string Version =
        typeof(SystemController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Basic information about the running API, with a message in the request's language.</summary>
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo() => new SystemInfoResponse(
        "TaskFlow API",
        Version,
        environment.EnvironmentName,
        CultureInfo.CurrentUICulture.Name,
        localizer["ApiWelcome"]);
}

public sealed record SystemInfoResponse(string Name, string Version, string Environment, string Culture, string Message);
