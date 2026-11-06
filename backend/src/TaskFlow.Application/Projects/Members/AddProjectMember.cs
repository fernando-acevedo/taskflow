using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.Members;

public sealed record AddProjectMemberCommand(Guid ProjectId, string Email, ProjectRole Role = ProjectRole.Member);

public sealed class AddProjectMemberValidator : AbstractValidator<AddProjectMemberCommand>
{
    public AddProjectMemberValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Role).IsInEnum();
    }
}

/// <summary>An owner adds an existing user by email.</summary>
public sealed class AddProjectMemberHandler(
    IAppDbContext db,
    ProjectAccess access,
    IIdentityService identity,
    ICurrentUser currentUser,
    IValidator<AddProjectMemberCommand> validator,
    ActivityLog activity,
    TimeProvider time)
{
    public async Task<Result<ProjectMemberDto>> HandleAsync(AddProjectMemberCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var user = await identity.FindByEmailAsync(command.Email.Trim(), cancellationToken);
        if (user is null || !await IsTenantMemberAsync(user.Id, cancellationToken))
        {
            // An account of another tenant answers like a missing one, so emails of other tenants are not revealed.
            return UserErrors.EmailNotFound;
        }

        var project = allowed.Value;
        var added = project.AddMember(user.Id, command.Role, time.GetUtcNow());
        if (!added.IsSuccess)
        {
            return added.Error;
        }

        activity.Record(project.Id, ActivityType.MemberAdded, new { memberId = user.Id, role = command.Role });
        await db.SaveChangesAsync(cancellationToken);

        var member = project.Members.Single(member => member.UserId == user.Id);
        return new ProjectMemberDto(user.Id, user.Email, user.DisplayName, user.IsActive, member.Role, member.JoinedAt);
    }

    private async Task<bool> IsTenantMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId
        && await db.TenantMemberships.AnyAsync(
            membership => membership.TenantId == tenantId && membership.UserId == userId, cancellationToken);
}
