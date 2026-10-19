using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.UnitTests.Projects;

public sealed class ProjectAccessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private readonly Project _project;

    public ProjectAccessTests()
    {
        _project = Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
        _project.AddMember(MemberId, ProjectRole.Member, Now);
    }

    [Theory]
    [InlineData("owner", false, true, true, true)]
    [InlineData("member", false, true, true, false)]
    [InlineData("outsider", false, false, false, false)]
    [InlineData("outsider", true, true, false, false)]
    public void AccessDependsOnTheProjectRoleAndAdminsOnlyRead(string who, bool isAdmin, bool view, bool work, bool manage)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(who switch { "owner" => OwnerId, "member" => MemberId, _ => OutsiderId });
        user.IsAdmin.Returns(isAdmin);

        ProjectAccess.Allows(_project, ProjectAccessLevel.View, user).ShouldBe(view);
        ProjectAccess.Allows(_project, ProjectAccessLevel.Work, user).ShouldBe(work);
        ProjectAccess.Allows(_project, ProjectAccessLevel.Manage, user).ShouldBe(manage);
    }
}
