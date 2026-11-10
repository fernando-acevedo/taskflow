using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Email;
using TaskFlow.Infrastructure.Email.Outbox;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>The email outbox (ADR 0018): queued with the change, retried with backoff, never sent twice.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class OutboxTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANotificationEmailIsQueuedWithTheChangeAndSentLater()
    {
        var (owner, _) = await factory.CreateUserClientAsync();
        var (member, memberEmail) = await factory.CreateUserClientAsync();
        var project = await factory.CreateProjectAsync(owner);
        (await owner.AddMemberAsync(project.Id, memberEmail)).EnsureSuccessStatusCode();
        using var saved = await member.PutAsJsonAsync(
            "/api/notifications/preferences",
            new { emailOnAssigned = true, emailOnMentioned = false, emailOnCommentOnMyTask = false, emailDailyDigest = false },
            Ct);
        saved.EnsureSuccessStatusCode();
        await factory.DrainOutboxAsync();

        await owner.CreateTaskAsync(project.Id, new { title = "Queued email", assigneeId = await member.GetMyIdAsync() });

        // The request answered, the email is waiting in the outbox and has not gone out yet.
        var queued = await FindAsync(memberEmail, "Notification");
        queued.ProcessedAt.ShouldBeNull();
        queued.Payload.ShouldContain("Queued email");

        factory.Emails.SentTo(memberEmail).ShouldContain(message => message.TextBody.Contains("Queued email", StringComparison.Ordinal));
        (await ReadAsync(queued.Id)).ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnEmailIsNotQueuedWhenTheSaveThatCarriesItFails()
    {
        var to = NewRecipient();
        var duplicate = Guid.CreateVersion7();
        await using (var first = factory.Services.CreateAsyncScope())
        {
            var db = first.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxMessages.Add(Message(duplicate, "existing"));
            await db.SaveChangesAsync(Ct);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Enqueue("Test", new EmailMessage(to, "Subject", "Text", "<p>Html</p>"));
        context.OutboxMessages.Add(Message(duplicate, "conflict"));

        await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(Ct));

        await using var check = factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .AnyAsync(message => message.Payload.Contains(to), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task AFailedSendIsRetriedWithBackoffAndThenDelivered()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var sender = new ScriptedSender(factory.Emails) { FailFor = to };
        var dispatcher = NewDispatcher(sender, new OutboxOptions { MaxAttempts = 5, BaseDelay = TimeSpan.FromSeconds(10) });

        // Assert on this test's own row, not on the batch count: the tests share the database and the frozen clock,
        // so rows left waiting by other tests can fall due here when the clock moves.
        await dispatcher.ProcessPendingAsync(Ct);

        var afterFailure = await ReadAsync(id);
        afterFailure.Attempts.ShouldBe(1);
        afterFailure.ProcessedAt.ShouldBeNull();
        afterFailure.FailedAt.ShouldBeNull();
        afterFailure.LastError.ShouldNotBeNull().ShouldContain("smtp is down");
        afterFailure.NextAttemptAt.ShouldBeGreaterThan(factory.Time.GetUtcNow());
        afterFailure.LockToken.ShouldBeNull();

        // Not due yet: nothing is tried again.
        await dispatcher.ProcessPendingAsync(Ct);
        sender.Attempts(to).ShouldBe(1);

        factory.Time.Advance(TimeSpan.FromSeconds(30));
        sender.FailFor = null;
        await dispatcher.ProcessPendingAsync(Ct);

        var delivered = await ReadAsync(id);
        delivered.ProcessedAt.ShouldNotBeNull();
        delivered.Attempts.ShouldBe(2);
        delivered.LastError.ShouldBeNull();
        sender.Attempts(to).ShouldBe(2);
    }

    [Fact]
    public async Task AMessageIsLeftAsFailedAfterTheMaximumAttempts()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var sender = new ScriptedSender(factory.Emails) { FailFor = to };
        var dispatcher = NewDispatcher(sender, new OutboxOptions { MaxAttempts = 2, BaseDelay = TimeSpan.FromSeconds(10), MaxDelay = TimeSpan.FromMinutes(1) });

        await dispatcher.ProcessPendingAsync(Ct);
        factory.Time.Advance(TimeSpan.FromSeconds(30));
        await dispatcher.ProcessPendingAsync(Ct);

        var failed = await ReadAsync(id);
        failed.FailedAt.ShouldNotBeNull();
        failed.ProcessedAt.ShouldBeNull();
        failed.Attempts.ShouldBe(2);
        failed.LastError.ShouldNotBeNull().ShouldContain("smtp is down");

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await dispatcher.ProcessPendingAsync(Ct);
        sender.Attempts(to).ShouldBe(2);
        (await ReadAsync(id)).Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task TwoWorkersNeverSendTheSameMessage()
    {
        await factory.DrainOutboxAsync();
        var prefix = $"race-{Guid.NewGuid():N}";
        var recipients = Enumerable.Range(0, 30).Select(index => $"{prefix}-{index}@tests.local").ToList();
        foreach (var to in recipients)
        {
            await EnqueueAsync(to);
        }

        var sender = new ScriptedSender(factory.Emails) { Delay = TimeSpan.FromMilliseconds(15) };
        var options = new OutboxOptions { BatchSize = 4 };
        var first = NewDispatcher(sender, options);
        var second = NewDispatcher(sender, options);

        var counts = await Task.WhenAll(first.ProcessPendingAsync(Ct), second.ProcessPendingAsync(Ct));

        // How the batches split between the two workers depends on timing (on a fast runner one can take them all);
        // what must hold is that every message goes out exactly once.
        counts.Sum().ShouldBe(recipients.Count);
        foreach (var to in recipients)
        {
            sender.Attempts(to).ShouldBe(1, $"{to} must be sent once");
        }
    }

    [Fact]
    public async Task ProcessedMessagesAreDeletedAfterTheRetention()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var dispatcher = NewDispatcher(new ScriptedSender(factory.Emails), new OutboxOptions { RetentionDays = 7 });
        await dispatcher.ProcessPendingAsync(Ct);

        await dispatcher.CleanUpAsync(Ct);
        (await ExistsAsync(id)).ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var old = factory.Time.GetUtcNow().AddDays(-8);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.Where(message => message.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.ProcessedAt, old), Ct);
        }

        await dispatcher.CleanUpAsync(Ct);
        (await ExistsAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task TheStoredPayloadIsEmptiedOnceTheMessageIsSent()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var dispatcher = NewDispatcher(new ScriptedSender(factory.Emails), new OutboxOptions());

        await dispatcher.ProcessPendingAsync(Ct);

        var sent = await ReadAsync(id);
        sent.ProcessedAt.ShouldNotBeNull();
        sent.Payload.ShouldBe(OutboxDispatcher.EmptyPayload);
    }

    [Fact]
    public async Task AMessageThatIsGivenUpOnKeepsItsErrorButNotItsPayload()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var sender = new ScriptedSender(factory.Emails) { FailFor = to };
        var dispatcher = NewDispatcher(sender, new OutboxOptions { MaxAttempts = 2, BaseDelay = TimeSpan.FromSeconds(10) });

        await dispatcher.ProcessPendingAsync(Ct);
        (await ReadAsync(id)).Payload.ShouldContain(to); // still needed for the retry

        factory.Time.Advance(TimeSpan.FromSeconds(30));
        await dispatcher.ProcessPendingAsync(Ct);

        var failed = await ReadAsync(id);
        failed.FailedAt.ShouldNotBeNull();
        failed.Payload.ShouldBe(OutboxDispatcher.EmptyPayload);
        failed.LastError.ShouldNotBeNull().ShouldContain("smtp is down");
    }

    [Fact]
    public async Task FailedMessagesAreDeletedAfterTheirRetention()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var sender = new ScriptedSender(factory.Emails) { FailFor = to };
        var dispatcher = NewDispatcher(sender, new OutboxOptions { MaxAttempts = 1, FailedRetentionDays = 14 });
        await dispatcher.ProcessPendingAsync(Ct);
        (await ReadAsync(id)).FailedAt.ShouldNotBeNull();

        await dispatcher.CleanUpAsync(Ct);
        (await ExistsAsync(id)).ShouldBeTrue();

        factory.Time.Advance(TimeSpan.FromDays(15));
        await dispatcher.CleanUpAsync(Ct);
        (await ExistsAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task AMessageWhoseLeaseExpiredIsClaimedAgainButALiveOrProcessedOneIsNot()
    {
        await factory.DrainOutboxAsync();
        var expired = NewRecipient();
        var live = NewRecipient();
        var done = NewRecipient();
        var expiredId = await EnqueueAsync(expired);
        var liveId = await EnqueueAsync(live);
        var doneId = await EnqueueAsync(done);
        var now = factory.Time.GetUtcNow();
        await SetLockAsync(expiredId, now.AddMinutes(-1), processedAt: null);
        await SetLockAsync(liveId, now.AddMinutes(5), processedAt: null);
        await SetLockAsync(doneId, now.AddMinutes(-1), processedAt: now.AddMinutes(-2));
        var sender = new ScriptedSender(factory.Emails);
        var dispatcher = NewDispatcher(sender, new OutboxOptions());

        await dispatcher.ProcessPendingAsync(Ct);

        sender.Attempts(expired).ShouldBe(1);
        sender.Attempts(live).ShouldBe(0);
        sender.Attempts(done).ShouldBe(0);
        (await ReadAsync(expiredId)).ProcessedAt.ShouldNotBeNull();

        // Once the live lease also runs out, that one is taken too; the processed one never is.
        factory.Time.Advance(TimeSpan.FromMinutes(10));
        await dispatcher.ProcessPendingAsync(Ct);
        sender.Attempts(live).ShouldBe(1);
        sender.Attempts(expired).ShouldBe(1);
        sender.Attempts(done).ShouldBe(0);
    }

    [Fact]
    public async Task ASendThatTakesLongerThanTheTimeoutCountsAsAFailedAttempt()
    {
        await factory.DrainOutboxAsync();
        var to = NewRecipient();
        var id = await EnqueueAsync(to);
        var sender = new ScriptedSender(factory.Emails) { Delay = TimeSpan.FromSeconds(30) };
        var dispatcher = NewDispatcher(sender, new OutboxOptions { SendTimeout = TimeSpan.FromMilliseconds(200), MaxAttempts = 5 });

        await dispatcher.ProcessPendingAsync(Ct);

        var timedOut = await ReadAsync(id);
        timedOut.ProcessedAt.ShouldBeNull();
        timedOut.Attempts.ShouldBe(1);
        timedOut.LastError.ShouldNotBeNull().ShouldContain("TimeoutException");
        timedOut.NextAttemptAt.ShouldBeGreaterThan(factory.Time.GetUtcNow());
        timedOut.LockToken.ShouldBeNull();
    }

    [Fact]
    public void TheSendTimeoutMustBeShorterThanTheLease()
    {
        var validator = new OutboxOptionsValidator();

        validator.Validate(null, new OutboxOptions()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new OutboxOptions { SendTimeout = TimeSpan.FromMinutes(2), LeaseDuration = TimeSpan.FromMinutes(2) })
            .Failed.ShouldBeTrue();
        validator.Validate(null, new OutboxOptions { SendTimeout = TimeSpan.Zero }).Failed.ShouldBeTrue();
    }

    private async Task SetLockAsync(Guid id, DateTimeOffset lockedUntil, DateTimeOffset? processedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var token = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.Where(message => message.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.LockToken, token)
                    .SetProperty(message => message.LockedUntil, lockedUntil)
                    .SetProperty(message => message.ProcessedAt, processedAt),
                Ct);
    }

    private static string NewRecipient() => $"outbox-{Guid.NewGuid():N}@tests.local";

    private static OutboxMessage Message(Guid id, string payload) => new()
    {
        Id = id,
        Kind = "Test",
        Payload = payload,
        CreatedAt = DateTimeOffset.UnixEpoch,
        NextAttemptAt = DateTimeOffset.UnixEpoch.AddYears(100),
    };

    private OutboxDispatcher NewDispatcher(IEmailSender sender, OutboxOptions options) => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        sender,
        Options.Create(options),
        factory.Time,
        NullLogger<OutboxDispatcher>.Instance);

    private async Task<Guid> EnqueueAsync(string to)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailOutbox>()
            .EnqueueAndSaveAsync("Test", new EmailMessage(to, "Subject", "Text", "<p>Html</p>"), Ct);
        return (await FindAsync(to)).Id;
    }

    private async Task<OutboxMessage> FindAsync(string to, string kind = "Test")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Kind == kind && message.Payload.Contains(to), Ct);
    }

    private async Task<OutboxMessage> ReadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .SingleAsync(message => message.Id == id, Ct);
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AnyAsync(message => message.Id == id, Ct);
    }

    /// <summary>
    /// Counts the sends to each recipient and can fail or slow them. Messages for anyone else (left over from other tests)
    /// go to the factory's sender so they are not lost.
    /// </summary>
    private sealed class ScriptedSender(TestEmailSender fallback) : IEmailSender
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

        public string? FailFor { get; set; }

        public TimeSpan Delay { get; init; }

        public int Attempts(string to) => _attempts.GetValueOrDefault(to);

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (!message.To.StartsWith("outbox-", StringComparison.Ordinal) && !message.To.StartsWith("race-", StringComparison.Ordinal))
            {
                await fallback.SendAsync(message, cancellationToken);
                return;
            }

            _attempts.AddOrUpdate(message.To, 1, (_, count) => count + 1);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (string.Equals(FailFor, message.To, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("smtp is down");
            }
        }
    }
}
