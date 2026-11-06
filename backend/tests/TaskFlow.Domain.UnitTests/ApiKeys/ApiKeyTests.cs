using TaskFlow.Domain.ApiKeys;

namespace TaskFlow.Domain.UnitTests.ApiKeys;

public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateReturnsASecretWhoseHashIsStoredAndNotTheSecret()
    {
        var created = ApiKey.Create(Guid.NewGuid(), "Gantt", ApiKeyScope.Write, null, Now).Value;

        created.Secret.ShouldStartWith(ApiKey.SecretPrefix);
        created.Secret.Length.ShouldBeGreaterThan(40);
        created.Key.KeyHash.ShouldBe(ApiKey.HashSecret(created.Secret));
        created.Key.KeyHash.ShouldNotContain(created.Secret);
        created.Key.Prefix.ShouldBe(created.Secret[..ApiKey.DisplayPrefixLength]);
    }

    [Fact]
    public void TwoKeysHaveDifferentSecrets()
    {
        var first = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, null, Now).Value;
        var second = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, null, Now).Value;

        first.Secret.ShouldNotBe(second.Secret);
        first.Key.KeyHash.ShouldNotBe(second.Key.KeyHash);
    }

    [Fact]
    public void WithoutExpiryItLastsNinetyDays()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, null, Now).Value.Key;

        key.ExpiresAt.ShouldBe(Now.AddDays(90));
    }

    [Fact]
    public void ExpiryMustBeInTheFutureAndWithinAYear()
    {
        ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, Now, Now).Error.ShouldBe(ApiKeyErrors.ExpiryInPast);
        ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, Now.AddDays(366), Now).Error.ShouldBe(ApiKeyErrors.ExpiryTooFar);
        ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, Now.AddDays(365), Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ItIsUsableUntilItExpiresOrIsRevoked()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, Now.AddDays(1), Now).Value.Key;

        key.IsUsable(Now.AddHours(1)).ShouldBeTrue();
        key.IsUsable(Now.AddDays(1)).ShouldBeFalse();

        key.Revoke(Now.AddHours(2));
        key.IsUsable(Now.AddHours(3)).ShouldBeFalse();
        key.RevokedAt.ShouldBe(Now.AddHours(2));
    }

    [Fact]
    public void RevokingTwiceKeepsTheFirstTime()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, null, Now).Value.Key;

        key.Revoke(Now.AddHours(1));
        key.Revoke(Now.AddHours(2));

        key.RevokedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void LastUseIsRecordedAtMostOncePerInterval()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "A", ApiKeyScope.Read, null, Now).Value.Key;
        var interval = TimeSpan.FromMinutes(5);

        key.RecordUse(Now, interval).ShouldBeTrue();
        key.RecordUse(Now.AddMinutes(4), interval).ShouldBeFalse();
        key.LastUsedAt.ShouldBe(Now);
        key.RecordUse(Now.AddMinutes(5), interval).ShouldBeTrue();
        key.LastUsedAt.ShouldBe(Now.AddMinutes(5));
    }
}
