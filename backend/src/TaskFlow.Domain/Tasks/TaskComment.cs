using TaskFlow.Domain.Tenancy;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

/// <summary>
/// A comment on a task. Changes take the owning <see cref="Project"/> so that an archived project stays
/// read-only. Mentions are user ids that must be members of the project; they are stored as rows
/// (<see cref="TaskCommentMention"/>) so that notifications can query them.
/// </summary>
public sealed class TaskComment : ITenantOwned, IHasDomainEvents
{
    public const int BodyMaxLength = 4000;

    private readonly List<TaskCommentMention> _mentions = [];
    private readonly List<IDomainEvent> _events = [];

    private TaskComment(Guid id, Guid taskItemId, Guid authorId, string body, DateTimeOffset createdAt)
    {
        Id = id;
        TaskItemId = taskItemId;
        AuthorId = authorId;
        Body = body;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TaskItemId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the author edits the comment; null while it is unedited.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Concurrency token: an edit made on an older version is rejected.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<TaskCommentMention> Mentions => _mentions;

    public static Result<TaskComment> Create(
        Project project,
        Guid taskItemId,
        Guid authorId,
        string body,
        IEnumerable<Guid>? mentionedUserIds,
        DateTimeOffset now)
    {
        var check = Check(project, body, mentionedUserIds, out var mentions);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var comment = new TaskComment(Guid.CreateVersion7(now), taskItemId, authorId, body.Trim(), now);
        comment.SetMentions(mentions);
        comment._events.Add(new CommentAdded(project.Id, project.Name, taskItemId, comment.Id, mentions));
        return comment;
    }

    public IReadOnlyList<IDomainEvent> TakeDomainEvents()
    {
        var events = _events.ToList();
        _events.Clear();
        return events;
    }

    /// <summary>Raises <see cref="CommentDeleted"/>; the caller removes the comment after <see cref="CanDelete"/>.</summary>
    public void MarkDeleted(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _events.Add(new CommentDeleted(project.Id, TaskItemId, Id));
    }

    /// <summary>Only the author edits a comment, and only while the project is editable.</summary>
    public Result Edit(Project project, Guid editorId, string body, IEnumerable<Guid>? mentionedUserIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        if (editorId != AuthorId)
        {
            return TaskCommentErrors.NotAuthor;
        }

        var check = Check(project, body, mentionedUserIds, out var mentions);
        if (!check.IsSuccess)
        {
            return check;
        }

        var before = _mentions.Select(mention => mention.UserId).ToList();
        Body = body.Trim();
        EditedAt = now;
        SetMentions(mentions);
        _events.Add(new CommentUpdated(project.Id, project.Name, TaskItemId, Id, [.. mentions.Except(before)]));
        return Result.Success();
    }

    /// <summary>The author or a project manager (owner) deletes a comment, only while the project is editable.</summary>
    public Result CanDelete(Project project, Guid userId, bool canManageProject)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        return userId == AuthorId || canManageProject ? Result.Success() : TaskCommentErrors.NotAuthorOrManager;
    }

    private static Result Check(Project project, string body, IEnumerable<Guid>? mentionedUserIds, out List<Guid> mentions)
    {
        ArgumentNullException.ThrowIfNull(project);
        mentions = [.. (mentionedUserIds ?? []).Distinct()];
        if (project.IsArchived)
        {
            return ProjectErrors.Archived;
        }

        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return TaskCommentErrors.BodyRequired;
        }

        if (trimmed.Length > BodyMaxLength)
        {
            return TaskCommentErrors.BodyTooLong;
        }

        return mentions.TrueForAll(project.IsMember) ? Result.Success() : TaskCommentErrors.MentionNotMember;
    }

    private void SetMentions(List<Guid> userIds)
    {
        _mentions.RemoveAll(mention => !userIds.Contains(mention.UserId));
        foreach (var userId in userIds.Where(userId => _mentions.TrueForAll(mention => mention.UserId != userId)))
        {
            _mentions.Add(new TaskCommentMention(Id, userId));
        }
    }
}

/// <summary>A user mentioned in a comment (one row per comment and user).</summary>
public sealed class TaskCommentMention : ITenantOwned
{
    internal TaskCommentMention(Guid taskCommentId, Guid userId)
    {
        TaskCommentId = taskCommentId;
        UserId = userId;
    }

    public Guid TaskCommentId { get; private set; }

    public Guid UserId { get; private set; }
}

public static class TaskCommentErrors
{
    public static readonly Error NotFound =
        Error.NotFound("TaskComments.NotFound", "The comment was not found.");

    public static readonly Error BodyRequired =
        Error.Invalid("TaskComments.BodyRequired", "The comment can't be empty.");

    public static readonly Error BodyTooLong =
        Error.Invalid("TaskComments.BodyTooLong", "The comment is too long.");

    public static readonly Error MentionNotMember =
        Error.Invalid("TaskComments.MentionNotMember", "Only members of the project can be mentioned.");

    public static readonly Error NotAuthor =
        Error.Forbidden("TaskComments.NotAuthor", "Only the author can edit a comment.");

    public static readonly Error NotAuthorOrManager =
        Error.Forbidden("TaskComments.NotAuthorOrManager", "Only the author or a project owner can delete a comment.");
}
