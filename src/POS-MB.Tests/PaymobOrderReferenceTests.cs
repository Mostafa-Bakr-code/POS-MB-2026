using POS_MB.Business.Payments;

namespace POS_MB.Tests;

// The special_reference sent to/from Paymob - not a DB test, pure round-trip
// logic. See PaymentsController for where the parsed side actually matters.
public class PaymobOrderReferenceTests
{
    [Fact]
    public void Build_ThenTryParse_RoundTripsExactly()
    {
        var date = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        var reference = PaymobOrderReference.Build(date, 8);

        Assert.Equal("20260810-8", reference);
        Assert.True(PaymobOrderReference.TryParse(reference, out var parsedDate, out var parsedSerial));
        Assert.Equal(date.Date, parsedDate);
        Assert.Equal(8, parsedSerial);
    }

    [Theory]
    [InlineData("not-a-date-8")]
    [InlineData("20260810")]
    [InlineData("20260810-notanumber")]
    [InlineData("")]
    public void TryParse_FailsGracefully_OnMalformedInput(string malformed)
    {
        Assert.False(PaymobOrderReference.TryParse(malformed, out _, out _));
    }

    [Fact]
    public void BuildRetry_DiffersFromBuild_ButStillParsesToTheSameOrder()
    {
        var date = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        var original = PaymobOrderReference.Build(date, 8);
        var retry = PaymobOrderReference.BuildRetry(date, 8);

        Assert.NotEqual(original, retry);
        Assert.True(PaymobOrderReference.TryParse(retry, out var parsedDate, out var parsedSerial));
        Assert.Equal(date.Date, parsedDate);
        Assert.Equal(8, parsedSerial);
    }
}
