using Microsoft.EntityFrameworkCore;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.UnitTests;

/// <summary>Builds an <see cref="AppDbContext"/> over SQL Server options that never connect: only the model and the change tracker are used.</summary>
internal static class TenantModelFixture
{
    public static AppDbContext CreateContext(TenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
            .Options;
        return new AppDbContext(options, tenant ?? new TenantContext());
    }
}
