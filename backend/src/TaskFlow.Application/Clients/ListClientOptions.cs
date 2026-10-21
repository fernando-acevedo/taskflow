using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

/// <summary>A client as offered in pickers: no contact details.</summary>
public sealed record ClientOptionDto(Guid Id, string Name);

/// <summary>
/// Every active client by name, for the project form and filters. Not paged: a picker needs them all, and an
/// agency has clients in the hundreds at most.
/// </summary>
public sealed class ListClientOptionsHandler(IAppDbContext db)
{
    public async Task<Result<IReadOnlyList<ClientOptionDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var options = await db.Clients.AsNoTracking()
            .Where(client => !client.IsArchived)
            .OrderBy(client => client.Name)
            .Select(client => new ClientOptionDto(client.Id, client.Name))
            .ToListAsync(cancellationToken);
        return options;
    }
}
