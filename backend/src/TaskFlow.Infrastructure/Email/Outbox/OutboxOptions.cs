namespace TaskFlow.Infrastructure.Email.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Turns the background sender off (the integration tests do, and drain the outbox themselves).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker looks for due messages.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages one worker claims at a time.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Attempts before a message is left as failed.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>The first retry waits about this long; each next one doubles it (with jitter) up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a claim lasts; a worker that crashes releases its messages this way.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The longest one send may take; then it is cancelled and counts as a failed attempt. Must be shorter than
    /// <see cref="LeaseDuration"/>, or another worker could claim the message while this one is still sending it.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Processed messages older than this are deleted.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Messages left as failed are deleted this many days after they failed (their payload is already emptied).</summary>
    public int FailedRetentionDays { get; set; } = 14;
}

/// <summary>Fails startup when the send timeout does not fit inside the lease.</summary>
public sealed class OutboxOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<OutboxOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.SendTimeout <= TimeSpan.Zero)
        {
            failures.Add("Outbox:SendTimeout must be greater than zero.");
        }

        if (options.SendTimeout >= options.LeaseDuration)
        {
            failures.Add("Outbox:SendTimeout must be shorter than Outbox:LeaseDuration, or a slow send could be repeated by another worker.");
        }

        if (options.RetentionDays < 0 || options.FailedRetentionDays < 0)
        {
            failures.Add("Outbox retention days cannot be negative.");
        }

        return failures.Count == 0
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
            : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(failures);
    }
}
