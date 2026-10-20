using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Abstractions;

/// <summary>
/// The business tables, implemented by the EF Core context in Infrastructure. EF Core already is a unit of
/// work over repositories, so handlers query these sets directly (ADR 0006).
/// </summary>
public interface IAppDbContext
{
    DbSet<Client> Clients { get; }

    DbSet<Project> Projects { get; }

    DbSet<TaskItem> Tasks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
