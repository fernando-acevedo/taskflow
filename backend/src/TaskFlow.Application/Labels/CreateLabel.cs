using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Labels;

public sealed record CreateLabelCommand(Guid ProjectId, string Name, string Color);

public sealed class CreateLabelValidator : AbstractValidator<CreateLabelCommand>
{
    public CreateLabelValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Label.NameMaxLength);
        RuleFor(command => command.Color).Must(Label.IsValidColor).WithMessage("The color must be a hex value like #1A2B3C.");
    }
}

/// <summary>An owner adds a label to the project.</summary>
public sealed class CreateLabelHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<CreateLabelCommand> validator,
    TimeProvider time)
{
    public async Task<Result<LabelDto>> HandleAsync(CreateLabelCommand command, CancellationToken cancellationToken)
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

        var existing = await db.Labels.AsNoTracking()
            .Where(label => label.ProjectId == command.ProjectId)
            .ToListAsync(cancellationToken);
        var created = Label.Create(allowed.Value, command.Name, command.Color, existing, time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        db.Labels.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value.ToDto();
    }
}
