using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Projects;

public sealed class LabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void CreatesWithTrimmedNameAndUpperCaseColor()
    {
        var label = Label.Create(NewProject(), "  Bug ", "#ff00aa", [], Now).Value;

        label.Name.ShouldBe("Bug");
        label.Color.ShouldBe("#FF00AA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NameIsRequired(string name) =>
        Label.Create(NewProject(), name, "#FF00AA", [], Now).Error.ShouldBe(LabelErrors.NameRequired);

    [Fact]
    public void NameHasAMaximumLength()
    {
        Label.Create(NewProject(), new string('a', Label.NameMaxLength + 1), "#FF00AA", [], Now).Error.ShouldBe(LabelErrors.NameTooLong);
        Label.Create(NewProject(), new string('a', Label.NameMaxLength), "#FF00AA", [], Now).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FF00AA")]
    [InlineData("#FF00A")]
    [InlineData("#GG00AA")]
    [InlineData("red")]
    public void ColorMustBeHex(string color) =>
        Label.Create(NewProject(), "Bug", color, [], Now).Error.ShouldBe(LabelErrors.InvalidColor);

    [Fact]
    public void NameIsUniqueIgnoringCase()
    {
        var project = NewProject();
        var bug = Label.Create(project, "Bug", "#FF0000", [], Now).Value;

        Label.Create(project, "bUG", "#00FF00", [bug], Now).Error.ShouldBe(LabelErrors.NameTaken);
    }

    [Fact]
    public void UpdateMayKeepItsOwnNameButNotTakeAnothers()
    {
        var project = NewProject();
        var bug = Label.Create(project, "Bug", "#FF0000", [], Now).Value;
        var chore = Label.Create(project, "Chore", "#00FF00", [bug], Now).Value;
        var all = new[] { bug, chore };

        bug.Update(project, "BUG", "#0000FF", all).IsSuccess.ShouldBeTrue();
        bug.Color.ShouldBe("#0000FF");
        bug.Update(project, "chore", "#0000FF", all).Error.ShouldBe(LabelErrors.NameTaken);
    }

    [Fact]
    public void ArchivedProjectIsReadOnly()
    {
        var project = NewProject();
        var bug = Label.Create(project, "Bug", "#FF0000", [], Now).Value;
        project.Archive(Now);

        Label.Create(project, "Other", "#FF0000", [], Now).Error.ShouldBe(ProjectErrors.Archived);
        bug.Update(project, "Bug 2", "#FF0000", [bug]).Error.ShouldBe(ProjectErrors.Archived);
        Label.CanDelete(project).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public void TaskLabelsAreReplacedAsASet()
    {
        var project = NewProject();
        var bug = Label.Create(project, "Bug", "#FF0000", [], Now).Value;
        var chore = Label.Create(project, "Chore", "#00FF00", [bug], Now).Value;
        var task = TaskItem.Create(project, "Fix", null, TaskPriority.Medium, null, null, OwnerId, 0, Now).Value;

        task.SetLabels(project, [bug, chore]).IsSuccess.ShouldBeTrue();
        task.SetLabels(project, [chore, chore]).IsSuccess.ShouldBeTrue();

        task.Labels.ShouldHaveSingleItem().ShouldBe(chore);
    }

    [Fact]
    public void TaskRejectsLabelsOfAnotherProject()
    {
        var project = NewProject();
        var other = NewProject();
        var foreign = Label.Create(other, "Bug", "#FF0000", [], Now).Value;
        var task = TaskItem.Create(project, "Fix", null, TaskPriority.Medium, null, null, OwnerId, 0, Now).Value;

        task.SetLabels(project, [foreign]).Error.ShouldBe(LabelErrors.NotInProject);
    }

    private static Project NewProject() =>
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;
}
