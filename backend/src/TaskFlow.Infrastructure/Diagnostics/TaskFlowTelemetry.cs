using System.Diagnostics;

namespace TaskFlow.Infrastructure.Diagnostics;

/// <summary>The trace source of background work (outbox, jobs); OpenTelemetry subscribes to it by name (ADR 0021).</summary>
public static class TaskFlowTelemetry
{
    public const string SourceName = "TaskFlow";

    public static readonly ActivitySource Source = new(SourceName);
}
