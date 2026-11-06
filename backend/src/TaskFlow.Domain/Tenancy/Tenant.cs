using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Tenancy;

/// <summary>An agency sharing the deployment. Its rows are separated from the others' by a <c>TenantId</c> (ADR 0012).</summary>
public sealed partial class Tenant
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 50;

    private Tenant(Guid id, string name, string slug, bool isDemo, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Slug = slug;
        IsDemo = isDemo;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Lowercase letters, digits and hyphens; unique across tenants.</summary>
    public string Slug { get; private set; }

    public bool IsDemo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Tenant> Create(string name, string slug, bool isDemo, DateTimeOffset now)
    {
        if (!IsValidSlug(slug))
        {
            return TenantErrors.InvalidSlug;
        }

        return new Tenant(Guid.CreateVersion7(now), name, slug, isDemo, now);
    }

    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= SlugMaxLength && SlugPattern().IsMatch(slug);

    public void Rename(string name) => Name = name;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public static class TenantErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Tenants.NotFound", "The tenant was not found.");

    public static readonly Error SlugTaken =
        Error.Conflict("Tenants.SlugTaken", "Another tenant already uses this slug.");

    public static readonly Error InvalidSlug =
        Error.Invalid("Tenants.InvalidSlug", "The slug may only contain lowercase letters, digits and single hyphens.");
}
