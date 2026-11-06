using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Demo;

/// <summary>Whether the API runs as the public demo, where visitors share accounts (ADR 0005).</summary>
public interface IDemoMode
{
    bool IsEnabled { get; }

    /// <summary>True for the shared demo accounts and the seeded admin, which visitors must not lock out.</summary>
    bool IsProtectedAccount(string? email);
}

public static class DemoErrors
{
    public static readonly Error AccountProtected =
        Error.Forbidden("Demo.AccountProtected", "This account is shared in the public demo and can't be deactivated or demoted.");

    public static readonly Error ActionDisabled =
        Error.Forbidden("Demo.ActionDisabled", "This action is disabled in the public demo.");
}
