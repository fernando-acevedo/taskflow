using System.Buffers.Text;
using System.Text;
using TaskFlow.Application.Common;

namespace TaskFlow.Application.UnitTests.Common;

public sealed class PageCursorTests
{
    private static string Raw(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ACursorRoundTripsItsTimestampAndTiebreaker()
    {
        var at = new DateTimeOffset(2026, 11, 10, 12, 30, 15, TimeSpan.FromHours(2)).AddTicks(1234);
        var id = Guid.NewGuid().ToString();

        PageCursor.TryDecode(PageCursor.Encode(at, id), out var decodedAt, out var decodedKey).ShouldBeTrue();

        decodedAt.ShouldBe(at.ToUniversalTime());
        decodedAt.Offset.ShouldBe(TimeSpan.Zero);
        decodedKey.ShouldBe(id);
    }

    [Fact]
    public void TheLongestRealCursorFitsTheLimit()
    {
        var cursor = PageCursor.Encode(DateTimeOffset.MaxValue, Guid.NewGuid().ToString());

        cursor.Length.ShouldBeLessThanOrEqualTo(PageCursor.MaxLength);
        PageCursor.TryDecode(cursor, out _, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("a")]
    public void GarbageIsRejected(string? cursor)
    {
        PageCursor.TryDecode(cursor, out var at, out var key).ShouldBeFalse();
        at.ShouldBe(default);
        key.ShouldBeEmpty();
    }

    [Fact]
    public void ACursorLongerThanTheLimitIsRejectedEvenIfWellFormed()
    {
        var cursor = PageCursor.Encode(DateTimeOffset.UtcNow, new string('x', 200));

        cursor.Length.ShouldBeGreaterThan(PageCursor.MaxLength);
        PageCursor.TryDecode(cursor, out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1|2|3")]
    [InlineData("abc|key")]
    [InlineData("-5|key")]
    [InlineData("+5|key")]
    [InlineData("|key")]
    [InlineData("99999999999999999999|key")]
    [InlineData("3155378976000000000|key")] // DateTimeOffset.MaxValue.UtcTicks + 1
    public void TicksOutOfRangeOrAMalformedShapeAreRejected(string raw) =>
        PageCursor.TryDecode(Raw(raw), out _, out _).ShouldBeFalse();

    [Fact]
    public void TheTiebreakerMayBeEmptyAndKeepsItsContent()
    {
        PageCursor.TryDecode(Raw("100|"), out var at, out var key).ShouldBeTrue();
        at.UtcTicks.ShouldBe(100);
        key.ShouldBeEmpty();
    }

    [Fact]
    public void ItemsWithTheSameTimestampKeepDistinctCursors()
    {
        var at = DateTimeOffset.UtcNow;
        var first = PageCursor.Encode(at, "00000000-0000-0000-0000-000000000001");
        var second = PageCursor.Encode(at, "00000000-0000-0000-0000-000000000002");

        first.ShouldNotBe(second);
        PageCursor.TryDecode(first, out var firstAt, out var firstKey).ShouldBeTrue();
        PageCursor.TryDecode(second, out var secondAt, out var secondKey).ShouldBeTrue();
        firstAt.ShouldBe(secondAt);
        string.CompareOrdinal(firstKey, secondKey).ShouldBeLessThan(0);
    }

    [Fact]
    public void ValidatorsCheckTheKindOfTiebreaker()
    {
        var at = DateTimeOffset.UtcNow;

        PageCursor.IsValidSequence(null).ShouldBeTrue();
        PageCursor.IsValidSequence(PageCursor.Encode(at, "42")).ShouldBeTrue();
        PageCursor.IsValidSequence(PageCursor.Encode(at, Guid.NewGuid().ToString())).ShouldBeFalse();
        PageCursor.IsValidId(PageCursor.Encode(at, Guid.NewGuid().ToString())).ShouldBeTrue();
        PageCursor.IsValidId(PageCursor.Encode(at, "42")).ShouldBeFalse();
        PageCursor.IsValidId("garbage!").ShouldBeFalse();
    }
}
