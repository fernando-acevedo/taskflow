namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>Only Infrastructure and the trainer tool may use ML.NET; the inner layers and the API stay free of it.</summary>
public class MlNetDependencyTests
{
    [Theory]
    [InlineData(typeof(TaskFlow.Application.AssemblyReference))]
    [InlineData(typeof(TaskFlow.Domain.Common.Error))]
    public void InnerLayersDoNotReferenceMlNet(Type marker)
    {
        marker.Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain(name => name != null && name.StartsWith("Microsoft.ML", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("src/TaskFlow.Domain/TaskFlow.Domain.csproj", false)]
    [InlineData("src/TaskFlow.Application/TaskFlow.Application.csproj", false)]
    [InlineData("src/TaskFlow.Api/TaskFlow.Api.csproj", false)]
    [InlineData("src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj", true)]
    public void OnlyInfrastructureDeclaresTheMlNetPackage(string project, bool expected)
    {
        var backend = FindBackendDirectory();
        var text = File.ReadAllText(Path.Combine(backend, project));

        text.Contains("Microsoft.ML", StringComparison.Ordinal).ShouldBe(expected);
    }

    private static string FindBackendDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskFlow.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("backend/ not found above the test output.");
    }
}
