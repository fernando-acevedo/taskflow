using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.Members;

public sealed record ChangeProjectMemberRoleCommand(Guid ProjectId, Guid UserId, ProjectRole Role);

public sealed class ChangeProjectMemberRoleValidator : AbstractValidator<ChangeProjectMemberRoleCommand>
{
    public ChangeProjectMemberRoleValidator() => RuleFor(command => command.Role).IsInEnum();
}

/// <summary>
/// An owner promotes or demotes a member. Transferring ownership is promoting someone, then stepping down;
/// the last owner can't step down.
/// </summary>
public sealed class ChangeProjectMemberRoleHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<ChangeProjectMemberRoleCommand> validator,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(ChangeProjectMemberRoleCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Manage, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var changed = allowed.Value.ChangeMemberRole(command.UserId, command.Role, time.GetUtcNow());
        if (!changed.IsSuccess)
        {
            return changed;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
