using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record TaskCommentDto(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string AuthorName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsEdited,
    IReadOnlyList<Guid> MentionedUserIds)
{
    public static TaskCommentDto From(TaskComment comment, string authorName)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return new TaskCommentDto(
            comment.Id,
            comment.TaskItemId,
            comment.AuthorId,
            authorName,
            comment.Body,
            comment.CreatedAt,
            comment.EditedAt,
            comment.EditedAt is not null,
            [.. comment.Mentions.Select(mention => mention.UserId)]);
    }
}
