using FluentValidation.Results;

namespace TaskFlow.Application.Common;

public static class ValidationExtensions
{
    /// <summary>Groups validation failures by camelCase property name, the shape the frontend forms use.</summary>
    public static Error ToError(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var errors = result.Errors
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        return Error.Validation(errors);
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
