namespace TaskFlow.Application.Users;

/// <summary>The color theme a user chose; <see cref="System"/> follows the operating system (ADR 0015).</summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}
