using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Infrastructure.Tenancy;

/// <summary>
/// The two tenants the platform knows by id: the default one that holds the data from before multitenancy (and the
/// development seed), and the public demo (ADR 0012, 0014). Fixed ids let the migration and the code agree on them.
/// </summary>
public static class TenantDefaults
{
    public static readonly Guid DefaultId = new("0192f6a0-0000-7000-8000-000000000001");
    public const string DefaultName = "TaskFlow";
    public const string DefaultSlug = "default";

    public static readonly Guid DemoId = new("0192f6a0-0000-7000-8000-000000000002");
    public const string DemoName = "TaskFlow Demo";
    public const string DemoSlug = "demo";

    public static Tenant CreateDefault(DateTimeOffset now) => Build(DefaultId, DefaultName, DefaultSlug, isDemo: false, now);

    public static Tenant CreateDemo(DateTimeOffset now) => Build(DemoId, DemoName, DemoSlug, isDemo: true, now);

    private static Tenant Build(Guid id, string name, string slug, bool isDemo, DateTimeOffset now) =>
        Tenant.Create(id, name, slug, isDemo, now).Value;
}
