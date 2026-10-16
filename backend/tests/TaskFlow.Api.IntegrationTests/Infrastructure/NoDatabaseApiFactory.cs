using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>Hosts the API for endpoints that never touch the database, so they run without Docker.</summary>
public sealed class NoDatabaseApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TaskFlow", "Server=unused;Database=unused");
    }
}
