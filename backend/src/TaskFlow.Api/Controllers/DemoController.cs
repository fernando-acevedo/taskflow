using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;
using TaskFlow.Infrastructure.Demo;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/demo")]
public sealed class DemoController(IOptions<DemoOptions> options, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Whether this is the public demo, when it resets next, and the shared accounts the login page shows. The
    /// accounts (with passwords, which come from app settings) are listed only while the demo is enabled.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<DemoStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<DemoStatusResponse> GetStatus()
    {
        var demo = options.Value;
        if (!demo.Enabled)
        {
            return new DemoStatusResponse(false, null, []);
        }

        return new DemoStatusResponse(
            true,
            DemoSchedule.NextOccurrence(demo.ResetCron, demo.TimeZone, time.GetUtcNow()),
            [.. demo.Accounts.Select(account => new DemoAccountResponse(account.Email!, account.Password!, account.Role.ToString()))]);
    }
}

public sealed record DemoStatusResponse(bool Enabled, DateTimeOffset? NextResetAt, IReadOnlyList<DemoAccountResponse> Accounts);

public sealed record DemoAccountResponse(string Email, string Password, string Role);
