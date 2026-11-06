using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.ApiKeys;
using TaskFlow.Application.Demo;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.UnitTests.ApiKeys;

public sealed class ApiKeyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IDemoMode _demo = Substitute.For<IDemoMode>();
    private readonly ITransactionRunner _transactions = Substitute.For<ITransactionRunner>();
    private readonly List<ApiKey> _added = [];
    private readonly TimeProvider _time = new FixedTime(Now);

    public ApiKeyHandlerTests()
    {
        _user.Id.Returns(UserId);
        _user.TenantId.Returns(Guid.NewGuid());
        _transactions
            .RunSerializableAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result>>>()(call.Arg<CancellationToken>()));
    }

    private CreateApiKeyHandler Create(params ApiKey[] existing)
    {
        var set = FakeDbSet.Of(existing);
        set.When(s => s.Add(Arg.Any<ApiKey>())).Do(call => _added.Add(call.Arg<ApiKey>()));
        _db.ApiKeys.Returns(set);
        return new CreateApiKeyHandler(_db, _user, _demo, new CreateApiKeyValidator(), _transactions, _time);
    }

    private static ApiKey KeyOf(Guid userId) => ApiKey.Create(userId, "k", ApiKeyScope.Read, null, Now).Value.Key;

    [Fact]
    public async Task CreatesAKeyAndReturnsTheSecretOnce()
    {
        var result = await Create().HandleAsync(new CreateApiKeyCommand(" Gantt ", ApiKeyScope.Write, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Secret.ShouldStartWith("tf_");
        result.Value.Name.ShouldBe("Gantt");
        result.Value.ExpiresAt.ShouldBe(Now.AddDays(90));
        _added.ShouldHaveSingleItem().KeyHash.ShouldBe(ApiKey.HashSecret(result.Value.Secret));
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKeyCannotCreateKeys()
    {
        _user.ApiKeyId.Returns(Guid.NewGuid());

        var result = await Create().HandleAsync(new CreateApiKeyCommand("x", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiKeyErrors.NotAllowedWithKey);
        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task IsDisabledInTheDemo()
    {
        _demo.IsEnabled.Returns(true);

        var result = await Create().HandleAsync(new CreateApiKeyCommand("x", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(DemoErrors.ActionDisabled);
    }

    [Fact]
    public async Task CountsAndInsertsInsideTheSerializableTransaction()
    {
        await Create().HandleAsync(new CreateApiKeyCommand("x", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        await _transactions.Received(1).RunSerializableAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAnEmptyName()
    {
        var result = await Create().HandleAsync(new CreateApiKeyCommand(" ", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task LimitsTheActiveKeysPerUser()
    {
        var existing = Enumerable.Range(0, ApiKey.MaxActivePerUser).Select(_ => KeyOf(UserId)).ToArray();

        var result = await Create(existing).HandleAsync(new CreateApiKeyCommand("x", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(ApiKeyErrors.TooMany);
    }

    [Fact]
    public async Task RevokedKeysDoNotCountTowardsTheLimit()
    {
        var existing = Enumerable.Range(0, ApiKey.MaxActivePerUser).Select(_ => KeyOf(UserId)).ToArray();
        existing[0].Revoke(Now);

        var result = await Create(existing).HandleAsync(new CreateApiKeyCommand("x", ApiKeyScope.Read, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ListsOnlyTheCurrentUsersKeysWithoutSecrets()
    {
        var mine = KeyOf(UserId);
        var set = FakeDbSet.Of(mine, KeyOf(Guid.NewGuid()));
        _db.ApiKeys.Returns(set);

        var result = await new ListApiKeysHandler(_db, _user, _demo, _time).HandleAsync(TestContext.Current.CancellationToken);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Id.ShouldBe(mine.Id);
        dto.IsActive.ShouldBeTrue();
        dto.Prefix.ShouldBe(mine.Prefix);
    }

    [Fact]
    public async Task RevokesOwnKeyAndHidesOthers()
    {
        var mine = KeyOf(UserId);
        var theirs = KeyOf(Guid.NewGuid());
        var set = FakeDbSet.Of(mine, theirs);
        _db.ApiKeys.Returns(set);
        var handler = new RevokeApiKeyHandler(_db, _user, _demo, _time);

        (await handler.HandleAsync(new RevokeApiKeyCommand(mine.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        mine.RevokedAt.ShouldBe(Now);

        (await handler.HandleAsync(new RevokeApiKeyCommand(theirs.Id), TestContext.Current.CancellationToken)).Error.ShouldBe(ApiKeyErrors.NotFound);
        theirs.RevokedAt.ShouldBeNull();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
