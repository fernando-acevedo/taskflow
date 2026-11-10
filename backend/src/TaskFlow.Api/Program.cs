using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Serilog;
using TaskFlow.Api.ErrorHandling;
using TaskFlow.Api.Health;
using TaskFlow.Api.Identity;
using TaskFlow.Application;
using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.ApiKeys;
using TaskFlow.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthPolicies.TenantAdmin, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantAdminRequirement())));
builder.Services.AddScoped<IAuthorizationHandler, TenantAdminHandler>();

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] cultures = ["en", "es"];
    options.SetDefaultCulture(cultures[0])
        .AddSupportedCultures(cultures)
        .AddSupportedUICultures(cultures);
    options.ApplyCurrentCultureToResponseHeaders = true;
});

var apiKeyPermitLimit = builder.Configuration.GetValue("RateLimiting:ApiKey:PermitLimit", 120);
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Each API key has its own budget (ADR 0016); people are limited only on the auth endpoints below.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.User.FindFirst(ApiKeyDefaults.KeyIdClaim) is { Value: var keyId }
            ? RateLimitPartition.GetFixedWindowLimiter(
                $"api-key:{keyId}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = apiKeyPermitLimit, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("people"));
    options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1) }));
});

// Behind a reverse proxy the client's address arrives in X-Forwarded-For, and the auth rate limit partitions by it.
// Only proxies in ForwardedHeaders:KnownNetworks (plus loopback) are believed, so clients can't pick their own IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseRequestLocalization();
app.UseCors(FrontendCorsPolicy);
// The API reference is public in every environment (ADR 0016): it describes the endpoints and holds no data.
app.MapOpenApi();
app.MapScalarApiReference();

// HTTPS is enforced by the hosting platform (App Service "HTTPS only"), not by the app.
app.UseAuthentication();
// After authentication so the limiter can partition by API key.
app.UseRateLimiter();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<ApiKeyScopeMiddleware>();
app.UseAuthorization();

app.MapControllers();
// Self-registration can be turned off (the public demo turns it off: Auth__AllowSelfRegistration=false) (admins still invite people); the rest of the Identity endpoints stay.
var allowSelfRegistration = app.Configuration.GetValue("Auth:AllowSelfRegistration", true);
app.MapGroup("/api/auth")
    .WithTags("Auth")
    .RequireRateLimiting(RateLimitPolicies.Auth)
    .AddEndpointFilter(async (context, next) =>
        allowSelfRegistration || !context.HttpContext.Request.Path.StartsWithSegments("/api/auth/register", StringComparison.OrdinalIgnoreCase)
            ? await next(context)
            : Results.NotFound())
    .AddEndpointFilter<DemoRestrictionsFilter>()
    .AddEndpointFilter<ForgetSessionsOnPasswordReset>()
    .MapIdentityApi<ApplicationUser>();
// Liveness answers without touching dependencies; readiness (and /health, kept as its alias) checks the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync });
var ready = new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync };
app.MapHealthChecks("/health/ready", ready);
app.MapHealthChecks("/health", ready);

await app.Services.InitializeDatabaseAsync(
    migrate: app.Configuration.GetValue<bool>("Database:MigrateOnStartup"),
    seed: app.Configuration.GetValue<bool>("Database:SeedOnStartup"));

await app.RunAsync();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
