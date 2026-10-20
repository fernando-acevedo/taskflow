using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.UnitTests.Projects;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void CreatorBecomesTheOnlyOwner()
    {
        var project = NewProject();

        project.Members.ShouldHaveSingleItem().UserId.ShouldBe(OwnerId);
        project.RoleOf(OwnerId).ShouldBe(ProjectRole.Owner);
    }

    [Fact]
    public void CannotCreateForArchivedClient()
    {
        var client = Client.Create("Acme", null, null, Now);
        client.Archive(activeProjectCount: 0);

        Project.Create(client, "Website", null, OwnerId, Now).Error.ShouldBe(ClientErrors.Archived);
    }

    [Fact]
    public void LastOwnerCannotLeaveOrStepDown()
    {
        var project = NewProject();

        project.RemoveMember(OwnerId, Now).Error.ShouldBe(ProjectErrors.LastOwner);
        project.ChangeMemberRole(OwnerId, ProjectRole.Member, Now).Error.ShouldBe(ProjectErrors.LastOwner);
    }

    [Fact]
    public void OwnershipTransfersByPromotingThenSteppingDown()
    {
        var project = NewProject();
        var memberId = Guid.NewGuid();
        project.AddMember(memberId, ProjectRole.Member, Now).IsSuccess.ShouldBeTrue();

        project.ChangeMemberRole(memberId, ProjectRole.Owner, Now).IsSuccess.ShouldBeTrue();
        project.ChangeMemberRole(OwnerId, ProjectRole.Member, Now).IsSuccess.ShouldBeTrue();

        project.RoleOf(memberId).ShouldBe(ProjectRole.Owner);
        project.RoleOf(OwnerId).ShouldBe(ProjectRole.Member);
    }

    [Fact]
    public void AddingAnExistingMemberFails()
    {
        var project = NewProject();

        project.AddMember(OwnerId, ProjectRole.Member, Now).Error.ShouldBe(ProjectErrors.AlreadyMember);
    }

    [Fact]
    public void RemovingSomeoneWhoIsNotAMemberFails()
    {
        var project = NewProject();

        project.RemoveMember(Guid.NewGuid(), Now).Error.ShouldBe(ProjectErrors.MemberNotFound);
    }

    [Fact]
    public void ArchivedProjectRejectsChanges()
    {
        var client = Client.Create("Acme", null, null, Now);
        var project = Project.Create(client, "Website", null, OwnerId, Now).Value;

        project.Archive(Now);

        project.Update(client, "Renamed", null, Now).Error.ShouldBe(ProjectErrors.Archived);
        project.AddMember(Guid.NewGuid(), ProjectRole.Member, Now).Error.ShouldBe(ProjectErrors.Archived);
        project.CanChangeTask(assigneeId: null).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public void CannotRestoreWhileTheClientIsArchived()
    {
        var client = Client.Create("Acme", null, null, Now);
        var project = Project.Create(client, "Website", null, OwnerId, Now).Value;
        project.Archive(Now);
        client.Archive(activeProjectCount: 0);

        project.Restore(client, Now).Error.ShouldBe(ClientErrors.Archived);
        project.IsArchived.ShouldBeTrue();
    }

    [Fact]
    public void AssigneeMustBeAMember()
    {
        var project = NewProject();

        project.CanChangeTask(Guid.NewGuid()).Error.ShouldBe(ProjectErrors.AssigneeNotMember);
        project.CanChangeTask(OwnerId).IsSuccess.ShouldBeTrue();
    }

    private static Project NewProject() =>
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
}
