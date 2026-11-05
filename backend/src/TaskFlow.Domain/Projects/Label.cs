using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

/// <summary>
/// A colored tag that belongs to one project and can be put on its tasks. Names are unique within the project
/// (case-insensitive); the caller checks that against the project's other labels. Changes take the owning
/// <see cref="Project"/> so that an archived project stays read-only.
/// </summary>
public sealed partial class Label
{
    public const int NameMaxLength = 40;
    public const int ColorLength = 7;

    private Label(Guid id, Guid projectId, string name, string color)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Color = color;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Hex color as <c>#RRGGBB</c>, upper case.</summary>
    public string Color { get; private set; }

    public static Result<Label> Create(Project project, string name, string color, IEnumerable<Label> existing, DateTimeOffset now)
    {
        var checkedFields = Check(project, name, color, existing, null);
        if (!checkedFields.IsSuccess)
        {
            return checkedFields.Error;
        }

        return new Label(Guid.CreateVersion7(now), project.Id, name.Trim(), color.ToUpperInvariant());
    }

    public Result Update(Project project, string name, string color, IEnumerable<Label> existing)
    {
        var checkedFields = Check(project, name, color, existing, this);
        if (!checkedFields.IsSuccess)
        {
            return checkedFields;
        }

        Name = name.Trim();
        Color = color.ToUpperInvariant();
        return Result.Success();
    }

    /// <summary>Checks that the label can be deleted: only while its project is editable.</summary>
    public static Result CanDelete(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.IsArchived ? ProjectErrors.Archived : Result.Success();
    }

    public static bool IsValidColor(string? color) => color is not null && HexColor().IsMatch(color);

    private static Result Check(Project project, string name, string color, IEnumerable<Label> existing, Label? self)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(existing);
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return LabelErrors.NameRequired;
        }

        if (trimmed.Length > NameMaxLength)
        {
            return LabelErrors.NameTooLong;
        }

        if (!IsValidColor(color))
        {
            return LabelErrors.InvalidColor;
        }

        var taken = existing.Any(other =>
            other.Id != self?.Id
            && string.Equals(other.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return taken ? LabelErrors.NameTaken : Result.Success();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}

public static class LabelErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Labels.NotFound", "The label was not found.");

    public static readonly Error NameRequired =
        Error.Invalid("Labels.NameRequired", "The label name is required.");

    public static readonly Error NameTooLong =
        Error.Invalid("Labels.NameTooLong", "The label name is too long.");

    public static readonly Error InvalidColor =
        Error.Invalid("Labels.InvalidColor", "The color must be a hex value like #1A2B3C.");

    public static readonly Error NameTaken =
        Error.Conflict("Labels.NameTaken", "The project already has a label with this name.");

    public static readonly Error NotInProject =
        Error.Invalid("Labels.NotInProject", "One or more labels don't belong to the project.");
}
