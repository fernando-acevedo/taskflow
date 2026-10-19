using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

public sealed record CreateProjectCommand(Guid ClientId, string Name, string? Description) : IProjectFields;

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectValidator() => this.AddProjectFieldRules();
}

/// <summary>Any user can create a project for an active client and becomes its owner.</summary>
public sealed class CreateProjectHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateProjectCommand> validator,
    GetProjectHandler getProject,
    TimeProvider time)
{
    public async Task<Result<ProjectDto>> HandleAsync(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var client = await db.Clients.FirstOrDefaultAsync(client => client.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return ClientErrors.NotFound;
        }

        var name = command.Name.Trim();
        if (await db.Projects.AnyAsync(project => project.ClientId == client.Id && project.Name == name, cancellationToken))
        {
            return ProjectErrors.NameTaken;
        }

        var created = Project.Create(client, name, command.Description.TrimToNull(), currentUser.Id, time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        db.Projects.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        return await getProject.HandleAsync(created.Value.Id, cancellationToken);
    }
}
