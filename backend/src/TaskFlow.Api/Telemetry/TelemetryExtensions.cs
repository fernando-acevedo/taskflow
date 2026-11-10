using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog.Context;
using TaskFlow.Infrastructure.Diagnostics;

namespace TaskFlow.Api.Telemetry;

/// <summary>OpenTelemetry setup (ADR 0021): always instrumented, exported only when the configuration names a destination.</summary>
internal static class TelemetryExtensions
{
    public static IServiceCollection AddTaskFlowTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var azureConnection = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        // Head sampling: keep SamplingRatio of the new traces and follow the parent's decision for the rest (ADR 0021).
        var samplingRatio = Math.Clamp(configuration.GetValue("Telemetry:SamplingRatio", 0.25), 0d, 1d);

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("taskflow-api"))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                    // The hub's access_token travels in the query string (ADR 0023): it must never reach a trace.
                    options.EnrichWithHttpRequest = (activity, request) => TelemetryRedaction.RedactHubQuery(activity, request.Path, request.QueryString.HasValue);
                })
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation(options => options.RecordException = true)
                .AddSource(TaskFlowTelemetry.SourceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddRuntimeMeters());

        if (!string.IsNullOrWhiteSpace(azureConnection))
        {
            otel.WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = azureConnection));
            otel.WithMetrics(metrics => metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = azureConnection));
        }

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            // Reads OTEL_EXPORTER_OTLP_* (endpoint, protocol, headers) from the configuration itself.
            otel.UseOtlpExporter();
        }

        return services;
    }

    private static MeterProviderBuilder AddRuntimeMeters(this MeterProviderBuilder metrics) =>
        metrics.AddMeter("System.Net.Http", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.RateLimiting");

    /// <summary>The trace id of the current request: the one in the logs and in <c>traceparent</c>.</summary>
    public static string CurrentTraceId(HttpContext context) =>
        Activity.Current is { } activity && activity.TraceId != default ? activity.TraceId.ToString() : context.TraceIdentifier;
}

/// <summary>Adds <c>extensions.traceId</c> to every ProblemDetails MVC writes (the problem-details service does it through its own hook).</summary>
internal sealed class TraceIdResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem })
        {
            problem.Extensions["traceId"] = TelemetryExtensions.CurrentTraceId(context.HttpContext);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}

/// <summary>Puts TraceId and SpanId on every log event of the request (Serilog).</summary>
internal sealed class TraceLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using (LogContext.PushProperty("TraceId", TelemetryExtensions.CurrentTraceId(context)))
        using (LogContext.PushProperty("SpanId", Activity.Current?.SpanId.ToString()))
        {
            await next(context);
        }
    }
}

/// <summary>Keeps the <c>access_token</c> of a hub connection (a bearer token in the query string, ADR 0023) out of the traces.</summary>
internal static class TelemetryRedaction
{
    public const string Redacted = "REDACTED";

    private static readonly string[] QueryTags = ["url.query", "http.target", "url.full", "http.url"];

    /// <summary>On <c>/hubs/</c> requests replaces the whole query (and the query inside the URL tags) with a placeholder.</summary>
    public static void RedactHubQuery(Activity activity, PathString path, bool hasQuery)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!hasQuery || !path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var tag in QueryTags)
        {
            if (activity.GetTagItem(tag) is not string value)
            {
                continue;
            }

            var query = value.IndexOf('?', StringComparison.Ordinal);
            activity.SetTag(tag, tag == "url.query" ? Redacted : query < 0 ? value : $"{value[..query]}?{Redacted}");
        }
    }
}
