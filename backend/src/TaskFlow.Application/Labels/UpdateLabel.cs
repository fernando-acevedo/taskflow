using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Labels;

public sealed record UpdateLabelCommand(Guid ProjectId, Guid LabelId, string Name, string Color);

public sealed class UpdateLabelValidator : AbstractValidator<UpdateLabelCommand>
{
    public UpdateLabelValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Label.NameMaxLength);
        // The color is checked by the domain (LabelErrors.InvalidColor), which the API localizes by code.
    }
}

public sealed class UpdateLabelHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<UpdateLabelCommand> validator,
    IUniqueViolationDetector uniqueViolations,
    ActivityLog activity)
{
    public async Task<Result<LabelDto>> HandleAsync(UpdateLabelCommand command, CancellationToken cancellationToken)
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

        var labels = await db.Labels.Where(label => label.ProjectId == command.ProjectId).ToListAsync(cancellationToken);
        var label = labels.Find(label => label.Id == command.LabelId);
        if (label is null)
        {
            return LabelErrors.NotFound;
        }

        var previousName = label.Name;
        var updated = label.Update(allowed.Value, command.Name, command.Color, labels);
        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        if (previousName != label.Name)
        {
            activity.Record(
                command.ProjectId, ActivityType.LabelRenamed, new { labelId = label.Id, from = previousName, to = label.Name });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (uniqueViolations.IsUniqueViolation(exception))
        {
            return LabelErrors.NameTaken;
        }

        return label.ToDto();
    }
}
