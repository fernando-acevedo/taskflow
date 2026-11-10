# 21. Observability with OpenTelemetry

- Status: Accepted
- Date: 2026-11-10

## Context

- Diagnosing a failure meant reading console logs with no way to tie a user's error to a log line, and nothing
  showed how long a request, a SQL command or the outbox took.
- The free App Service plan must not gain a paid dependency: exporting has to be opt-in.

## Decision

- OpenTelemetry is always registered, so every request has a trace id. Traces: ASP.NET Core (health probes are
  filtered out), `HttpClient` and SqlClient (which is what EF Core runs on), plus the `TaskFlow` `ActivitySource`
  (`TaskFlowTelemetry`) used by the outbox (`outbox.process`, `outbox.cleanup`), the due-soon job and the demo reset
  check. Metrics: ASP.NET Core, `HttpClient`, SqlClient, Kestrel and the rate limiter.
- Exporters depend on configuration only: `APPLICATIONINSIGHTS_CONNECTION_STRING` adds the Azure Monitor exporter,
  `OTEL_EXPORTER_OTLP_ENDPOINT` adds OTLP, and with neither nothing is exported.
- `extensions.traceId` is in every ProblemDetails (an MVC result filter and the problem-details service hook), and
  Serilog events carry `TraceId` and `SpanId` (a middleware pushes them into the log context; the console template
  prints the trace id). A user can quote the id of an error and it finds the logs and the trace.
- The frontend sends `traceparent` so its calls join the same trace. CORS already allows any request header.
- The Azure exporter package is used instead of the all-in-one `Azure.Monitor.OpenTelemetry.AspNetCore`, which would
  register its own instrumentations a second time. The EF Core instrumentation package is still beta; SqlClient
  spans cover the commands.

## Consequences

- Without configuration the overhead is the activity creation only, and nothing leaves the process.
- Enabling Application Insights is an app setting, with a cost decided by whoever enables it.
- Do not add request bodies or secrets to spans.
