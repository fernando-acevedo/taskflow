using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Tasks;

public sealed class TaskCommentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private readonly Project _project =
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;

    public TaskCommentTests() => _project.AddMember(MemberId, ProjectRole.Member, Now);

    [Fact]
    public void CreateTrimsTheBodyAndDeduplicatesMentions()
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "  Hi  ", [MemberId, MemberId], Now).Value;

        comment.Body.ShouldBe("Hi");
        comment.AuthorId.ShouldBe(OwnerId);
        comment.EditedAt.ShouldBeNull();
        comment.Mentions.Select(mention => mention.UserId).ShouldBe([MemberId]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRequiresABody(string body) =>
        TaskComment.Create(_project, Guid.NewGuid(), OwnerId, body, null, Now).Error.ShouldBe(TaskCommentErrors.BodyRequired);

    [Fact]
    public void CreateRejectsATooLongBody() =>
        TaskComment.Create(_project, Guid.NewGuid(), OwnerId, new string('x', TaskComment.BodyMaxLength + 1), null, Now)
            .Error.ShouldBe(TaskCommentErrors.BodyTooLong);

    [Fact]
    public void CreateRejectsMentioningANonMember() =>
        TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", [Guid.NewGuid()], Now)
            .Error.ShouldBe(TaskCommentErrors.MentionNotMember);

    [Fact]
    public void CreateIsRejectedInAnArchivedProject()
    {
        _project.Archive(Now);

        TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", null, Now).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public void AuthorEditsBodyAndReplacesMentions()
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", [MemberId], Now).Value;
        var later = Now.AddMinutes(5);

        var edited = comment.Edit(_project, OwnerId, "Bye", null, later);

        edited.IsSuccess.ShouldBeTrue();
        comment.Body.ShouldBe("Bye");
        comment.EditedAt.ShouldBe(later);
        comment.Mentions.ShouldBeEmpty();
    }

    [Fact]
    public void NonAuthorGetsForbiddenEvenWithAnInvalidBody()
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", null, Now).Value;

        var edited = comment.Edit(_project, MemberId, " ", [Guid.NewGuid()], Now);

        edited.Error.ShouldBe(TaskCommentErrors.NotAuthor);
        comment.Body.ShouldBe("Hi");
    }

    [Fact]
    public void EditIsRejectedInAnArchivedProject()
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", null, Now).Value;
        _project.Archive(Now);

        comment.Edit(_project, OwnerId, "Bye", null, Now).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void AuthorOrManagerCanDelete(bool isAuthor, bool canManage, bool allowed)
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", null, Now).Value;

        var result = comment.CanDelete(_project, isAuthor ? OwnerId : MemberId, canManage);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.ShouldBe(TaskCommentErrors.NotAuthorOrManager);
        }
    }

    [Fact]
    public void DeleteIsRejectedInAnArchivedProject()
    {
        var comment = TaskComment.Create(_project, Guid.NewGuid(), OwnerId, "Hi", null, Now).Value;
        _project.Archive(Now);

        comment.CanDelete(_project, OwnerId, true).Error.ShouldBe(ProjectErrors.Archived);
    }
}
