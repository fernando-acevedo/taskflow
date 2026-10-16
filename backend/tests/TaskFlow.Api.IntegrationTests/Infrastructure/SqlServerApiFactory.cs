using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API against a real SQL Server in a container. Without Docker the tests that use it are
/// skipped locally, but in CI (where the CI variable is set) a missing Docker fails the run.
/// </summary>
public sealed class SqlServerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private MsSqlContainer? _database;

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _database.StartAsync();
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") is null)
        {
            SkipReason = $"Docker is not available ({ex.GetType().Name}). These tests always run in CI.";
            return;
        }

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TaskFlow", _database?.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}
