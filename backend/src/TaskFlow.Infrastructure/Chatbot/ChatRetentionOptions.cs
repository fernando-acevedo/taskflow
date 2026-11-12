using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>Settings under <c>Chatbot:Retention</c>: how long support conversations are kept (ADR 0026).</summary>
public sealed class ChatRetentionOptions
{
    public const string SectionName = "Chatbot:Retention";

    /// <summary>Turns the background purge off (the integration tests do, and call the purger themselves).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sessions whose last activity is older than this many days are deleted, with their messages and intent logs.</summary>
    public int Days { get; set; } = 90;

    /// <summary>How often the worker purges.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How many sessions one delete removes.</summary>
    public int BatchSize { get; set; } = 500;
}

public sealed class ChatRetentionOptionsValidator : IValidateOptions<ChatRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, ChatRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.Days < 1)
        {
            failures.Add("Chatbot:Retention:Days must be at least 1.");
        }

        if (options.Interval <= TimeSpan.Zero)
        {
            failures.Add("Chatbot:Retention:Interval must be greater than zero.");
        }

        if (options.BatchSize < 1)
        {
            failures.Add("Chatbot:Retention:BatchSize must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
