using TaskFlow.Domain;

namespace TaskFlow.Domain.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainDoesNotDependOnOtherTaskFlowLayers()
    {
        var references = typeof(AssemblyReference).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("TaskFlow.", StringComparison.Ordinal));

        references.ShouldBeEmpty();
    }
}
