using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Demo;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Application.Tenants;

public sealed record InviteTenantMemberCommand(string Email, TenantRole Role, string? DisplayName = null);

public sealed class InviteTenantMemberValidator : AbstractValidator<InviteTenantMemberCommand>
{
    public InviteTenantMemberValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(CreateUserValidator.MaxEmailLength);
        RuleFor(command => command.Role).IsInEnum();
        RuleFor(command => command.DisplayName)
            .Length(2, UpdateProfileValidator.MaxDisplayNameLength)
            .When(command => !string.IsNullOrWhiteSpace(command.DisplayName));
    }
}

/// <summary>Adds a member to the current tenant; an Admin can add Admins and Members, only an Owner can add Owners.</summary>
public sealed class InviteTenantMemberHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<InviteTenantMemberCommand> validator,
    TenantMemberInviter inviter,
    IDemoMode demo)
{
    public async Task<Result<InvitedMemberDto>> HandleAsync(InviteTenantMemberCommand command, CancellationToken cancellationToken)
    {
        if (TenantRules.RequireTenantAdmin(currentUser) is { } denied)
        {
            return denied;
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

        if (command.Role == TenantRole.Owner && !TenantRules.IsOwner(currentUser))
        {
            return TenantErrors.OwnerRequired;
        }

        var tenantId = currentUser.TenantId!.Value;
        var tenantName = await db.Tenants
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantName is null)
        {
            return TenantErrors.NotFound;
        }

        return await inviter.InviteAsync(tenantId, tenantName, command.Email, command.DisplayName, command.Role, cancellationToken);
    }
}
