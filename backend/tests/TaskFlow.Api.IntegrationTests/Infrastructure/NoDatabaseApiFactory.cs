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
        builder.UseSetting("Notifications:DueSoonEnabled", "false");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Chatbot:SupportContact", "support@tests.local");
        builder.UseSetting("Chatbot:HelpUrl", "http://localhost:4200/help");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
    }
}
