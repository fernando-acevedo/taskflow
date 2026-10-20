using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

/// <param name="RowVersion">The version the user edited; a newer one in the database means a conflicting edit.</param>
public sealed record UpdateProjectCommand(Guid ProjectId, Guid ClientId, string Name, string? Description, byte[] RowVersion)
    : IProjectFields;

public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectValidator()
    {
        this.AddProjectFieldRules();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public sealed class UpdateProjectHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<UpdateProjectCommand> validator,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(UpdateProjectCommand command, CancellationToken cancellationToken)
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

        var project = allowed.Value;
        if (!project.RowVersion.SequenceEqual(command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var client = await db.Clients.FirstOrDefaultAsync(client => client.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        var name = command.Name.Trim();
        if (await db.Projects.AnyAsync(
                other => other.Id != project.Id && other.ClientId == client.Id && other.Name == name, cancellationToken))
        {
            return ProjectErrors.NameTaken;
        }

        var updated = project.Update(client, name, command.Description.TrimToNull(), time.GetUtcNow());
        if (!updated.IsSuccess)
        {
            return updated;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
