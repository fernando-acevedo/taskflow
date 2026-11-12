namespace TaskFlow.Application.Chatbot.Dialogue;

/// <summary>Picks which text variant of a step to show. A seam so tests can be deterministic.</summary>
public interface IVariantPicker
{
    /// <summary>An index in <c>[0, count)</c>, uniformly distributed.</summary>
    int Pick(int count);
}

/// <summary>Uniform random pick (<see cref="Random.Shared"/>, thread-safe).</summary>
public sealed class RandomVariantPicker : IVariantPicker
{
    public int Pick(int count) => count <= 1 ? 0 : Random.Shared.Next(count);
}
