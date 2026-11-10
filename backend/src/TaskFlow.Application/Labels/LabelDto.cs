using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Labels;

public sealed record LabelDto(Guid Id, string Name, string Color, byte[] RowVersion);

internal static class LabelMapping
{
    public static LabelDto ToDto(this Label label) => new(label.Id, label.Name, label.Color, label.RowVersion);
}
