using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Application.Common;

public static class QueryableExtensions
{
    /// <summary>Counts the query, then reads one page of it; <paramref name="query"/> must already be ordered.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, totalCount);
    }
}
