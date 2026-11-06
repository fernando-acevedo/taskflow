using Microsoft.EntityFrameworkCore;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.UnitTests;

public sealed class TenantFilterTests
{
    [Fact]
    public void QueriesAreFilteredByTheCurrentTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.Use(tenantId);
        using var db = TenantModelFixture.CreateContext(tenant);

        var sql = db.Clients.ToQueryString();

        sql.ShouldContain("[TenantId]");
        sql.ShouldContain(tenantId.ToString(), Case.Insensitive);
    }

    [Fact]
    public void WithoutTenantTheQueryMatchesNothing()
    {
        using var db = TenantModelFixture.CreateContext();

        var sql = db.Tasks.ToQueryString();

        sql.ShouldEndWith("WHERE 0 = 1");
    }

    [Fact]
    public void TheFilterFollowsTheContextInstance()
    {
        var first = new TenantContext();
        first.Use(Guid.NewGuid());
        var second = new TenantContext();
        second.Use(Guid.NewGuid());
        using var a = TenantModelFixture.CreateContext(first);
        using var b = TenantModelFixture.CreateContext(second);

        a.Clients.ToQueryString().ShouldContain(first.TenantId!.Value.ToString(), Case.Insensitive);
        b.Clients.ToQueryString().ShouldContain(second.TenantId!.Value.ToString(), Case.Insensitive);
    }
}
