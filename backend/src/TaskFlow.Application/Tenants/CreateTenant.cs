using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

/// <param name="OwnerEmail">Optional first Owner: an existing account gets the membership, a new one is invited.</param>
/// <param name="OwnerDisplayName">Name for the new account; defaults to the part of the email before the at sign.</param>
public sealed record CreateTenantCommand(string Name, string Slug, string? OwnerEmail = null, string? OwnerDisplayName = null);

public sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(command => command.Slug).NotEmpty();
        RuleFor(command => command.OwnerEmail)
            .EmailAddress()
            .MaximumLength(CreateUserValidator.MaxEmailLength)
            .When(command => !string.IsNullOrWhiteSpace(command.OwnerEmail));
        RuleFor(command => command.OwnerDisplayName)
            .Length(2, UpdateProfileValidator.MaxDisplayNameLength)
            .When(command => !string.IsNullOrWhiteSpace(command.OwnerDisplayName));
    }
}

public sealed record CreatedTenantDto(TenantDto Tenant, InvitedMemberDto? Owner);

/// <summary>Creates a tenant; only the super-admin may (ADR 0013).</summary>
public sealed class CreateTenantHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateTenantCommand> validator,
    TenantMemberInviter inviter,
    IUniqueViolationDetector uniqueViolations,
    IDemoMode demo,
    TimeProvider time)
{
    public async Task<Result<CreatedTenantDto>> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSuperAdmin)
        {
            return TenantRules.SuperAdminRequired;
        }

        if (demo.IsEnabled)
        {
            return DemoErrors.ActionDisabled;
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var slug = command.Slug.Trim();
        var created = Tenant.Create(command.Name.Trim(), slug, isDemo: false, time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        if (await db.Tenants.AnyAsync(existing => existing.Slug == slug, cancellationToken))
        {
            return TenantErrors.SlugTaken;
        }

        var tenant = created.Value;
        db.Tenants.Add(tenant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (uniqueViolations.IsUniqueViolation(exception))
        {
            return TenantErrors.SlugTaken;
        }

        var dto = new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.IsDemo, tenant.CreatedAt);
        if (string.IsNullOrWhiteSpace(command.OwnerEmail))
        {
            return new CreatedTenantDto(dto, null);
        }

        var owner = await inviter.InviteAsync(
            tenant.Id, tenant.Name, command.OwnerEmail, command.OwnerDisplayName, TenantRole.Owner, cancellationToken);
        return owner.IsSuccess ? new CreatedTenantDto(dto, owner.Value) : owner.Error;
    }
}
