using TaskFlow.Application;

namespace TaskFlow.Application.UnitTests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData("TaskFlow.Infrastructure")]
    [InlineData("TaskFlow.Api")]
    public void ApplicationDoesNotDependOnOuterLayers(string outerLayer)
    {
        var references = typeof(AssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        references.ShouldNotContain(outerLayer);
    }
}
