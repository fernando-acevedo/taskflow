using TaskFlow.Domain.Clients;

namespace TaskFlow.Domain.UnitTests.Clients;

public sealed class ClientTests
{
    [Fact]
    public void CannotArchiveWithActiveProjects()
    {
        var client = Client.Create("Acme", null, null, DateTimeOffset.UtcNow);

        client.Archive(activeProjectCount: 2).Error.ShouldBe(ClientErrors.HasActiveProjects);
        client.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void ArchivesAndRestores()
    {
        var client = Client.Create("Acme", null, null, DateTimeOffset.UtcNow);

        client.Archive(activeProjectCount: 0).IsSuccess.ShouldBeTrue();
        client.IsArchived.ShouldBeTrue();

        client.Restore();
        client.IsArchived.ShouldBeFalse();
    }
}
