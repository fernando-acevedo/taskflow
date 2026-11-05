using System.Net;
using System.Net.Http.Json;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class TaskCommentTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MembersCreateAndListCommentsInOrderWithAuthorAndMentions()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();
        var ownerId = await owner.GetMyIdAsync();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        var first = await PostAsync(owner, project.Id, task.Id, "First", [memberId, memberId]);
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await PostAsync(member, project.Id, task.Id, "Second", null);

        first.AuthorId.ShouldBe(ownerId);
        first.IsEdited.ShouldBeFalse();
        first.EditedAt.ShouldBeNull();
        first.MentionedUserIds.ShouldBe([memberId]);
        var list = await member.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id));
        list.Select(comment => comment.Id).ShouldBe([first.Id, second.Id]);
        list[0].AuthorName.ShouldNotBeNullOrWhiteSpace();
        list[1].AuthorId.ShouldBe(memberId);
        list[1].MentionedUserIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task OnlyTheAuthorEditsAComment()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(member, project.Id, task.Id, "Draft", null);

        using var byOwner = await owner.PutAsJsonAsync(CommentUrl(project.Id, task.Id, comment.Id), new { body = "Hijacked" }, Ct);
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        using var byAuthor = await member.PutAsJsonAsync(CommentUrl(project.Id, task.Id, comment.Id), new { body = "Final" }, Ct);

        byOwner.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await byOwner.ReadAsync<ProblemResponse>()).Code.ShouldBe("TaskComments.NotAuthor");
        byAuthor.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await byAuthor.ReadAsync<CommentResponse>();
        (edited.Body, edited.IsEdited).ShouldBe(("Final", true));
        edited.EditedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AuthorAndOwnerDeleteButAnotherMemberCannot()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        var (otherClient, otherEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        using var other = otherClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        (await owner.AddMemberAsync(project.Id, otherEmail)).EnsureSuccessStatusCode();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(member, project.Id, task.Id, "Mine", null);
        var another = await PostAsync(member, project.Id, task.Id, "Also mine", null);

        using var byOther = await other.DeleteAsync(new Uri(CommentUrl(project.Id, task.Id, comment.Id), UriKind.Relative), Ct);
        using var byOwner = await owner.DeleteAsync(new Uri(CommentUrl(project.Id, task.Id, comment.Id), UriKind.Relative), Ct);
        using var byAuthor = await member.DeleteAsync(new Uri(CommentUrl(project.Id, task.Id, another.Id), UriKind.Relative), Ct);

        byOther.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        byOwner.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        byAuthor.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id))).ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchivedProjectCommentsAreReadOnly()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(owner, project.Id, task.Id, "Before", null);
        (await owner.ArchiveProjectAsync(project.Id)).EnsureSuccessStatusCode();

        using var create = await owner.PostAsJsonAsync(CommentsUrl(project.Id, task.Id), new { body = "After" }, Ct);
        using var update = await owner.PutAsJsonAsync(CommentUrl(project.Id, task.Id, comment.Id), new { body = "Edited" }, Ct);
        using var delete = await owner.DeleteAsync(new Uri(CommentUrl(project.Id, task.Id, comment.Id), UriKind.Relative), Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("Projects.Archived");
        update.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await owner.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MentioningANonMemberIsRejected()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var outsiderId = await outsider.GetMyIdAsync();
        var comment = await PostAsync(owner, project.Id, task.Id, "Hello", null);

        using var create = await owner.PostAsJsonAsync(
            CommentsUrl(project.Id, task.Id), new { body = "Hi", mentionedUserIds = new[] { outsiderId } }, Ct);
        using var update = await owner.PutAsJsonAsync(
            CommentUrl(project.Id, task.Id, comment.Id), new { body = "Hi", mentionedUserIds = new[] { outsiderId } }, Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("TaskComments.MentionNotMember");
        update.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GlobalAdminWhoDoesNotOwnTheProjectCannotDeleteSomeoneElsesComment()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(owner, project.Id, task.Id, "Mine", null);

        using var response = await admin.DeleteAsync(new Uri(CommentUrl(project.Id, task.Id, comment.Id), UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EditingWithoutAMentionRemovesIt()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(owner, project.Id, task.Id, "Hi", [memberId]);

        using var update = await owner.PutAsJsonAsync(
            CommentUrl(project.Id, task.Id, comment.Id), new { body = "Hi again", mentionedUserIds = Array.Empty<Guid>() }, Ct);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await update.ReadAsync<CommentResponse>()).MentionedUserIds.ShouldBeEmpty();
        var list = await owner.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id));
        list.ShouldHaveSingleItem().MentionedUserIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonAuthorEditingWithAnInvalidBodyGetsForbidden()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(member, project.Id, task.Id, "Draft", null);

        using var response = await owner.PutAsJsonAsync(
            CommentUrl(project.Id, task.Id, comment.Id), new { body = "x", mentionedUserIds = new[] { Guid.NewGuid() } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("TaskComments.NotAuthor");
    }

    [Fact]
    public async Task MentioningADeactivatedMemberIsRejected()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (memberClient, memberEmail) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var member = memberClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        var memberId = await member.GetMyIdAsync();
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        var comment = await PostAsync(owner, project.Id, task.Id, "Hello", null);
        using var deactivate = await admin.DeactivateUserAsync(memberId);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var create = await owner.PostAsJsonAsync(
            CommentsUrl(project.Id, task.Id), new { body = "Hi", mentionedUserIds = new[] { memberId } }, Ct);
        using var update = await owner.PutAsJsonAsync(
            CommentUrl(project.Id, task.Id, comment.Id), new { body = "Hi", mentionedUserIds = new[] { memberId } }, Ct);

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await create.ReadAsync<ProblemResponse>()).Code.ShouldBe("TaskComments.MentionNotMember");
        update.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReadOnlyViewersAndOutsidersCannotComment()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        var (outsiderClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        using var outsider = outsiderClient;
        using var admin = await factory.CreateAdminClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });
        await PostAsync(owner, project.Id, task.Id, "Visible", null);

        // An admin who is not a member sees the project read-only.
        using var byAdmin = await admin.PostAsJsonAsync(CommentsUrl(project.Id, task.Id), new { body = "Nope" }, Ct);
        using var byOutsider = await outsider.PostAsJsonAsync(CommentsUrl(project.Id, task.Id), new { body = "Nope" }, Ct);
        var adminList = await admin.GetJsonAsync<List<CommentResponse>>(CommentsUrl(project.Id, task.Id));

        byAdmin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        byOutsider.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        adminList.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task BodyIsRequiredAndLimited()
    {
        var (ownerClient, _) = await factory.CreateUserClientAsync();
        using var owner = ownerClient;
        var project = await factory.CreateProjectAsync(owner);
        var task = await owner.CreateTaskAsync(project.Id, new { title = "Write copy" });

        using var empty = await owner.PostAsJsonAsync(CommentsUrl(project.Id, task.Id), new { body = "  " }, Ct);
        using var tooLong = await owner.PostAsJsonAsync(CommentsUrl(project.Id, task.Id), new { body = new string('x', 4001) }, Ct);

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string CommentsUrl(Guid projectId, Guid taskId) => $"/api/projects/{projectId}/tasks/{taskId}/comments";

    private static string CommentUrl(Guid projectId, Guid taskId, Guid commentId) => $"{CommentsUrl(projectId, taskId)}/{commentId}";

    private static async Task<CommentResponse> PostAsync(HttpClient client, Guid projectId, Guid taskId, string body, Guid[]? mentions)
    {
        using var response = await client.PostAsJsonAsync(CommentsUrl(projectId, taskId), new { body, mentionedUserIds = mentions }, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<CommentResponse>();
    }

    private sealed record CommentResponse(
        Guid Id,
        Guid TaskId,
        Guid AuthorId,
        string AuthorName,
        string Body,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EditedAt,
        bool IsEdited,
        IReadOnlyList<Guid> MentionedUserIds);
}
