using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Asp.Versioning;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Scalar.AspNetCore;
using Serilog;
using TaskFlow.Api.ErrorHandling;
using TaskFlow.Api.Health;
using TaskFlow.Api.Identity;
using TaskFlow.Api.Security;
using TaskFlow.Api.Telemetry;
using TaskFlow.Application;
using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} trace={TraceId}{NewLine}{Exception}",
        formatProvider: CultureInfo.InvariantCulture));

// The OpenAPI generator reads the minimal-API options, not MVC's: without this the enums are documented as integers.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

builder.Services.AddTaskFlowTelemetry(builder.Configuration);

builder.Services.AddControllers(options => options.Filters.Add<TraceIdResultFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services
    .AddApiVersioning(options =>
    {
        // Existing routes are v1 and stay unversioned in the URL; a client may ask for a version by header or query (ADR 0022).
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(new HeaderApiVersionReader("api-version"), new QueryStringApiVersionReader("api-version"));
    })
    .AddMvc()
    .AddApiExplorer()
    .AddOpenApi(options =>
    {
        options.Document.AddDocumentTransformer<BearerSecurityTransformer>();
        options.Document.AddDocumentTransformer<RateLimitResponseTransformer>();
        options.Document.AddSchemaTransformer<ContractSchemaTransformer>();
    });

// Only JSON and text are compressed, and the responses never echo a secret from the request, so BREACH has nothing to
// recover (ADR 0022). HTTPS is on because the TLS terminates at the platform, before the app.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["application/json", "application/problem+json", "text/csv", "text/plain", "text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml"];
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = TelemetryExtensions.CurrentTraceId(context.HttpContext));
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

builder.Services.AddTaskFlowRateLimiting(builder.Configuration);

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
    .AllowAnyMethod()
    .WithExposedHeaders("ETag", "api-supported-versions", "Retry-After")));

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();
app.UseMiddleware<TraceLogContextMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseRequestLocalization();
app.UseCors(FrontendCorsPolicy);
// The API reference is public in every environment (ADR 0016): it describes the endpoints and holds no data.
app.MapOpenApi().WithDocumentPerVersion();
app.MapScalarApiReference();
// The documents are named after the API version (1.0); the old v1 addresses keep working.
app.MapGet("/openapi/v1.json", () => Results.Redirect("/openapi/1.0.json")).ExcludeFromDescription();
app.MapGet("/scalar/v1", () => Results.Redirect("/scalar/1.0")).ExcludeFromDescription();

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
    .AddEndpointFilter<ForgetSessionsOnCredentialChange>()
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
