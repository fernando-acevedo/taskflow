using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.UnitTests;

/// <summary>Keeps the tenant guarantee of ADR 0012 from eroding: a new entity or a raw query needs a decision.</summary>
public sealed partial class TenantArchitectureTests
{
    // Entities that are deliberately not tenant-owned, each with its reason (ADR 0012). Identity tables are matched by namespace.
    private static readonly Dictionary<string, string> ExcludedEntities = new()
    {
        ["Tenant"] = "the tenant itself",
        ["TenantMembership"] = "links global users to tenants; read to resolve the tenant",
        ["TenantSupportAccess"] = "audit of super-admin entries; read per tenant by explicit TenantId, written before the tenant is resolved",
        ["NotificationPreferences"] = "per user, not per tenant",
        ["DemoResetLog"] = "global audit of the demo reset",
        ["OutboxMessage"] = "system queue of emails (ADR 0018); the worker has no tenant and the payload is self-contained",
        ["ApplicationUser"] ="Identity: users are global",
    };

    // Source files allowed to use IgnoreQueryFilters / FromSql* / ExecuteSql*, each with its reason.
    private static readonly Dictionary<string, string> AllowedRawSql = new()
    {
        ["Email/Outbox/OutboxDispatcher.cs"] = "claims outbox rows (not tenant owned) with UPDLOCK, READPAST so several instances don't send the same email (ADR 0018)",
        ["ApiKeys/ApiKeyAuthentication.cs"] = "finds an API key by the hash of its secret before any tenant is known; the key says which tenant the request is in (ADR 0016)",
    };

    [Fact]
    public void EveryEntityIsTenantOwnedOrExcluded()
    {
        using var db = TenantModelFixture.CreateContext();

        var undecided = db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => !typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            .Where(entity => !IsJoinEntity(entity) && !IsIdentity(entity))
            .Where(entity => !ExcludedEntities.ContainsKey(entity.ClrType.Name))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        undecided.ShouldBeEmpty("each entity must implement ITenantOwned or be listed in ExcludedEntities with a reason");
    }

    [Fact]
    public void EveryTenantOwnedEntityHasTheTenantFilterAndShadowProperty()
    {
        using var db = TenantModelFixture.CreateContext();

        var owned = db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            .ToList();

        owned.ShouldNotBeEmpty();
        foreach (var entity in owned)
        {
            var property = entity.FindProperty(AppDbContext.TenantIdProperty);
            property.ShouldNotBeNull($"{entity.ClrType.Name} has no TenantId");
            property.IsShadowProperty().ShouldBeTrue();
            property.IsNullable.ShouldBeFalse();
            entity.GetDeclaredQueryFilters().Select(filter => filter.Key).ShouldContain(
                AppDbContext.TenantFilterKey,
                $"{entity.ClrType.Name} has no tenant query filter");
        }
    }

    [Fact]
    public void ClientNameIsUniquePerTenant()
    {
        using var db = TenantModelFixture.CreateContext();

        var client = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Client))!;

        client.GetIndexes()
            .Where(index => index.IsUnique)
            .Select(index => string.Join(",", index.Properties.Select(p => p.Name)))
            .ShouldBe(["TenantId,Name"]);
    }

    [Fact]
    public void RawSqlAndFilterBypassesAreOnlyInTheAllowedList()
    {
        var src = Path.Combine(FindBackendRoot(), "src").Replace('\\', '/');
        var pattern = BypassPattern();

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal)
                && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path => pattern.IsMatch(File.ReadAllText(path)))
            .Where(path => !AllowedRawSql.Keys.Any(allowed => path.EndsWith(allowed, StringComparison.Ordinal)))
            .Select(path => path[(src.Length + 1)..])
            .ToList();

        offenders.ShouldBeEmpty("IgnoreQueryFilters / FromSql / ExecuteSql bypass the tenant filter; add the file to AllowedRawSql with a reason");
    }

    [GeneratedRegex(@"\b(IgnoreQueryFilters|FromSql\w*|ExecuteSql\w*)\b")]
    private static partial Regex BypassPattern();

    private static bool IsJoinEntity(IEntityType entity) => entity.ClrType == typeof(Dictionary<string, object>);

    private static bool IsIdentity(IEntityType entity) =>
        entity.ClrType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true;

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskFlow.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("TaskFlow.slnx not found above the test output.");
    }
}
