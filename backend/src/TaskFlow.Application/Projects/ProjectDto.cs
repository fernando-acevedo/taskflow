using FluentValidation;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects;

/// <param name="MyRole">The current user's role, or null when an admin views a project they are not a member of.</param>
public sealed record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    Guid ClientId,
    string ClientName,
    bool IsArchived,
    ProjectRole? MyRole,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    byte[] RowVersion);

/// <summary>The editable fields of a project, shared by create and update.</summary>
public interface IProjectFields
{
    Guid ClientId { get; }

    string Name { get; }

    string? Description { get; }
}

internal static class ProjectQueries
{
    public static void AddProjectFieldRules<T>(this AbstractValidator<T> validator)
        where T : IProjectFields
    {
        validator.RuleFor(fields => fields.ClientId).NotEmpty();
        validator.RuleFor(fields => fields.Name).NotEmpty().Length(Project.NameMinLength, Project.NameMaxLength);
        validator.RuleFor(fields => fields.Description).MaximumLength(Project.DescriptionMaxLength);
    }

    public static IQueryable<ProjectDto> ToDtos(this IQueryable<Project> projects, IQueryable<Client> clients, Guid userId) =>
        projects.Select(project => new ProjectDto(
            project.Id,
            project.Name,
            project.Description,
            project.ClientId,
            clients.Where(client => client.Id == project.ClientId).Select(client => client.Name).First(),
            project.IsArchived,
            project.Members.Where(member => member.UserId == userId).Select(member => (ProjectRole?)member.Role).FirstOrDefault(),
            project.Members.Count,
            project.CreatedAt,
            project.UpdatedAt,
            project.RowVersion));
}
