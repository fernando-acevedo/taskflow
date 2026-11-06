using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Clients;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.UnitTests;

public sealed class TenantSaveInterceptorTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly TenantContext _tenant = new();

    [Fact]
    public void AddedRowsGetTheCurrentTenant()
    {
        _tenant.Use(TenantA);
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = db.Clients.Add(NewClient()).Entity;

        new TenantSaveInterceptor(_tenant).Apply(db);

        db.Entry(client).Property(AppDbContext.TenantIdProperty).CurrentValue.ShouldBe(TenantA);
    }

    [Fact]
    public void AddingWithoutATenantIsRejected()
    {
        using var db = TenantModelFixture.CreateContext(_tenant);
        db.Clients.Add(NewClient());

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void AddingARowThatNamesAnotherTenantIsRejected()
    {
        _tenant.Use(TenantA);
        using var db = TenantModelFixture.CreateContext(_tenant);
        var entry = db.Clients.Add(NewClient());
        entry.Property(AppDbContext.TenantIdProperty).CurrentValue = TenantB;

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void ModifyingARowOfTheCurrentTenantIsAllowed()
    {
        _tenant.Use(TenantA);
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = Track(db, TenantA);
        client.Update("Renamed", null, null);

        Should.NotThrow(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void ModifyingARowOfAnotherTenantIsRejected()
    {
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = Track(db, TenantB);
        client.Update("Renamed", null, null);
        _tenant.Use(TenantA);

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void DeletingARowOfAnotherTenantIsRejected()
    {
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = Track(db, TenantB);
        db.Clients.Remove(client);
        _tenant.Use(TenantA);

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void MovingARowToAnotherTenantIsRejected()
    {
        _tenant.Use(TenantA);
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = Track(db, TenantA);
        db.Entry(client).Property(AppDbContext.TenantIdProperty).CurrentValue = TenantB;

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    [Fact]
    public void ModifyingWithoutATenantIsRejected()
    {
        using var db = TenantModelFixture.CreateContext(_tenant);
        var client = Track(db, TenantA);
        client.Update("Renamed", null, null);

        Should.Throw<TenantIsolationException>(() => new TenantSaveInterceptor(_tenant).Apply(db));
    }

    private static Client NewClient() => Client.Create("Acme", null, null, Now);

    // Tracks a client as if it had been loaded from the given tenant.
    private static Client Track(AppDbContext db, Guid tenantId)
    {
        var client = NewClient();
        var entry = db.Entry(client);
        entry.Property(AppDbContext.TenantIdProperty).CurrentValue = tenantId;
        entry.State = EntityState.Unchanged;
        return client;
    }
}
