using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API against a real SQL Server: a Testcontainers instance when Docker is available, otherwise a
/// throwaway LocalDB database on Windows. With neither, the tests are skipped locally; in CI (where the CI
/// variable is set) a missing Docker fails the run instead.
/// </summary>
public sealed class SqlServerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@tests.local";
    public const string AdminPassword = "Admin12345";

    private MsSqlContainer? _container;
    private string? _connectionString;
    private bool _usesLocalDb;

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
            return;
        }
        catch (Exception) when (Environment.GetEnvironmentVariable("CI") is null)
        {
            _container = null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _usesLocalDb = true;
            _connectionString =
                $@"Server=(localdb)\MSSQLLocalDB;Database=TaskFlowTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";
            return;
        }

        SkipReason = "Neither Docker nor LocalDB is available. These tests always run in CI.";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TaskFlow", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:SeedOnStartup", "true");
        builder.UseSetting("Seed:Admin:Email", AdminEmail);
        builder.UseSetting("Seed:Admin:Password", AdminPassword);
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "10000");
    }

    public override async ValueTask DisposeAsync()
    {
        if (_usesLocalDb && SkipReason is null)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerApiFactory>
{
    public const string Name = "SQL Server";
}
