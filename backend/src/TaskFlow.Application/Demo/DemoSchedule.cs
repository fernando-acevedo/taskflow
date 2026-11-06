using Cronos;

namespace TaskFlow.Application.Demo;

/// <summary>
/// When the public demo resets (ADR 0005). Pure calculations over a cron expression (five fields) in a time zone,
/// so "is a reset due" can be tested without a clock or a database.
/// </summary>
public static class DemoSchedule
{
    /// <summary>How far back to look for the latest occurrence, widening until one is found.</summary>
    private static readonly TimeSpan[] LookBack = [TimeSpan.FromDays(2), TimeSpan.FromDays(35), TimeSpan.FromDays(400), TimeSpan.FromDays(3000)];

    /// <summary>Null when <paramref name="cron"/> and <paramref name="timeZone"/> are valid, otherwise what is wrong.</summary>
    public static string? Validate(string? cron, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(cron) || !CronExpression.TryParse(cron, CronFormat.Standard, out _))
        {
            return $"'{cron}' is not a valid five-field cron expression.";
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"'{timeZone}' is not a known time zone.";
        }

        return null;
    }

    /// <summary>The latest scheduled reset at or before <paramref name="now"/>, or null if the schedule never fired.</summary>
    public static DateTimeOffset? MostRecentOccurrence(string cron, string timeZone, DateTimeOffset now)
    {
        var expression = CronExpression.Parse(cron, CronFormat.Standard);
        var zone = ResolveZone(timeZone);
        foreach (var span in LookBack)
        {
            var last = expression.GetOccurrences(now - span, now, zone, fromInclusive: true, toInclusive: true)
                .Select(occurrence => (DateTimeOffset?)occurrence)
                .LastOrDefault();
            if (last is not null)
            {
                return last;
            }
        }

        return null;
    }

    /// <summary>The first scheduled reset after <paramref name="now"/>.</summary>
    public static DateTimeOffset? NextOccurrence(string cron, string timeZone, DateTimeOffset now) =>
        CronExpression.Parse(cron, CronFormat.Standard).GetNextOccurrence(now, ResolveZone(timeZone), inclusive: false);

    /// <summary>
    /// A reset is due when a scheduled time has passed since the last successful reset (or there was none). A reset
    /// missed while the app slept is therefore due on the next start.
    /// </summary>
    public static bool IsDue(string cron, string timeZone, DateTimeOffset? lastResetAt, DateTimeOffset now)
    {
        var scheduled = MostRecentOccurrence(cron, timeZone, now);
        return scheduled is not null && (lastResetAt is null || lastResetAt < scheduled);
    }

    private static TimeZoneInfo ResolveZone(string timeZone) =>
        TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone);
}
