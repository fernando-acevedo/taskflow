using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.TimeTracking;

/// <summary>
/// The entries behind the summary, same filter, as CSV: UTF-8 with a byte order mark, comma separated, one row
/// per entry (oldest first) with English headers. Owners only; the range is required.
/// </summary>
public sealed class ExportTimeSummaryCsvHandler(IAppDbContext db, ProjectAccess access, IIdentityService identity)
{
    public const string Header = "Date,User,Task,Minutes,Note";

    public async Task<Result<byte[]>> HandleAsync(GetTimeSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var range = GetTimeSummaryHandler.ValidateRange(query.From, query.To, required: true);
        if (!range.IsSuccess)
        {
            return range.Error;
        }

        var entries = await TimeSummaryFilter.Entries(db, query)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => new { entry.Date, entry.UserId, entry.TaskItemId, entry.Minutes, entry.Note })
            .ToListAsync(cancellationToken);
        var taskIds = entries.Select(entry => entry.TaskItemId).Distinct().ToList();
        var titles = await db.Tasks.AsNoTracking()
            .Where(task => taskIds.Contains(task.Id))
            .Select(task => new { task.Id, task.Title })
            .ToDictionaryAsync(task => task.Id, task => task.Title, cancellationToken);
        var rows = entries
            .Select(entry => new { entry.Date, entry.UserId, TaskTitle = titles.GetValueOrDefault(entry.TaskItemId, string.Empty), entry.Minutes, entry.Note })
            .ToList();

        var names = (await identity.FindByIdsAsync([.. rows.Select(row => row.UserId).Distinct()], cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);

        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var row in rows)
        {
            csv.Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(names.GetValueOrDefault(row.UserId, string.Empty))).Append(',')
                .Append(Escape(row.TaskTitle)).Append(',')
                .Append(row.Minutes.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.Note)).Append("\r\n");
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    /// <summary>Quotes a field when needed, and defuses values a spreadsheet would run as a formula.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var first = value.AsSpan().IndexOfAnyExcept(' ', '\t');
        if (value[0] is '\t' or '\r' || (first >= 0 && value[first] is '=' or '+' or '-' or '@'))
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
