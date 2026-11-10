using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using TaskFlow.Api.Identity;
using TaskFlow.Infrastructure.ApiKeys;

namespace TaskFlow.Api.Security;

/// <summary>Rate limits for people and API keys (ADR 0016, ADR 0022). Anonymous requests are limited only by <see cref="RateLimitPolicies.Auth"/>.</summary>
internal static class RateLimiting
{
    public const string ErrorCode = "RateLimit.Exceeded";

    public static IServiceCollection AddTaskFlowRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKeyPermitLimit = configuration.GetValue("RateLimiting:ApiKey:PermitLimit", 120);
        var authPermitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
        var user = Bucket.Read(configuration, "User", tokenLimit: 300, tokensPerPeriod: 50, periodSeconds: 10);
        var report = Bucket.Read(configuration, "Report", tokenLimit: 30, tokensPerPeriod: 5, periodSeconds: 10);
        var export = Bucket.Read(configuration, "Export", tokenLimit: 5, tokensPerPeriod: 1, periodSeconds: 12);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // Each API key has its own budget (ADR 0016); a person's budget is per user. It is not split by the
            // X-Tenant header: the limiter runs before the tenant is resolved, so a raw header value would let a caller
            // dodge the limit and create limiters without end. Anonymous requests are not limited here.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.User.FindFirst(ApiKeyDefaults.KeyIdClaim) is { Value: var keyId }
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        $"api-key:{keyId}",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = apiKeyPermitLimit, Window = TimeSpan.FromMinutes(1) })
                    : UserKey(context) is { } key
                        ? RateLimitPartition.GetTokenBucketLimiter(key, _ => user.ToOptions())
                        : RateLimitPartition.GetNoLimiter("anonymous"));
            options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1) }));
            options.AddPolicy(RateLimitPolicies.Report, context => RateLimitPartition.GetTokenBucketLimiter(
                $"report:{Caller(context)}", _ => report.ToOptions()));
            options.AddPolicy(RateLimitPolicies.Export, context => RateLimitPartition.GetTokenBucketLimiter(
                $"export:{Caller(context)}", _ => export.ToOptions()));
        });
    }

    private static string Caller(HttpContext context) =>
        context.User.FindFirst(ApiKeyDefaults.KeyIdClaim) is { Value: var keyId }
            ? $"api-key:{keyId}"
            : UserKey(context) ?? $"ip:{context.Connection.RemoteIpAddress}";

    private static string? UserKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : null;

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = localizer[ErrorCode] };
        problem.Extensions["code"] = ErrorCode;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }

    private sealed record Bucket(int TokenLimit, int TokensPerPeriod, int PeriodSeconds)
    {
        public static Bucket Read(IConfiguration configuration, string name, int tokenLimit, int tokensPerPeriod, int periodSeconds)
        {
            var section = configuration.GetSection($"RateLimiting:{name}");
            return new Bucket(
                section.GetValue("TokenLimit", tokenLimit),
                section.GetValue("TokensPerPeriod", tokensPerPeriod),
                section.GetValue("ReplenishmentSeconds", periodSeconds));
        }

        public TokenBucketRateLimiterOptions ToOptions() => new()
        {
            TokenLimit = TokenLimit,
            TokensPerPeriod = TokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromSeconds(PeriodSeconds),
            AutoReplenishment = true,
            QueueLimit = 0,
        };
    }
}
