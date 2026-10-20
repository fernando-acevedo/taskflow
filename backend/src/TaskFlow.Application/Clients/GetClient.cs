using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Clients;

public sealed class GetClientHandler(IAppDbContext db)
{
    public async Task<Result<ClientDto>> HandleAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking()
            .Where(client => client.Id == clientId)
            .ToDtos(db.Projects)
            .FirstOrDefaultAsync(cancellationToken);

        return client is null ? ClientErrors.NotFound : client;
    }
}
