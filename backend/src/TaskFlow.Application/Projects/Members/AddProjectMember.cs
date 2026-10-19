using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
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
    IValidator<AddProjectMemberCommand> validator,
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
        if (user is null)
        {
            return UserErrors.EmailNotFound;
        }

        var project = allowed.Value;
        var added = project.AddMember(user.Id, command.Role, time.GetUtcNow());
        if (!added.IsSuccess)
        {
            return added.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var member = project.Members.Single(member => member.UserId == user.Id);
        return new ProjectMemberDto(user.Id, user.Email, user.DisplayName, user.IsActive, member.Role, member.JoinedAt);
    }
}
