using FusionNet.Core.Utilities;

namespace FusionNet.Tests;

/// <summary>Display formatting helpers used on every page; edge cases must never print garbage.</summary>
public class HumanizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-1, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    [InlineData(double.NaN, "0 B")]
    [InlineData(double.PositiveInfinity, "0 B")]
    public void Bytes_FormatsBinaryUnits(double bytes, string expected)
    {
        Assert.Equal(expected, Humanize.Bytes(bytes));
    }

    [Fact]
    public void Bytes_UnsignedOverloadMatchesDoubleOverload()
    {
        Assert.Equal(Humanize.Bytes(2048d), Humanize.Bytes(2048ul));
        Assert.Equal("2 KB", Humanize.Bytes(2048ul));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(-100, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(1048576, "1 MB/s")]
    public void Rate_AppendsPerSecond(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, Humanize.Rate(bytesPerSecond));
    }

    [Theory]
    [InlineData(0d, 0, "0 ms")]
    [InlineData(12.4, 0, "12 ms")]
    [InlineData(12.6, 0, "13 ms")]
    [InlineData(12.345, 2, "12.35 ms")]
    public void Ms_FormatsWithInvariantCulture(double milliseconds, int decimals, string expected)
    {
        Assert.Equal(expected, Humanize.Ms(milliseconds, decimals));
    }

    [Fact]
    public void Ms_NullableShowsDashForMissingSamples()
    {
        Assert.Equal("—", Humanize.Ms((double?)null));
        Assert.Equal("42 ms", Humanize.Ms((double?)42));
    }

    [Theory]
    [InlineData(0d, 0, "0%")]
    [InlineData(50d, 0, "50%")]
    [InlineData(33.3, 1, "33.3%")]
    [InlineData(99.25, 2, "99.25%")]
    public void Percent_FormatsWithInvariantCulture(double value, int decimals, string expected)
    {
        Assert.Equal(expected, Humanize.Percent(value, decimals));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(5, "00:05")]
    [InlineData(65, "01:05")]
    [InlineData(599, "09:59")]
    public void Duration_ShowsMinutesAndSecondsUnderOneHour(int totalSeconds, string expected)
    {
        Assert.Equal(expected, Humanize.Duration(TimeSpan.FromSeconds(totalSeconds)));
    }

    [Fact]
    public void Duration_ShowsHoursWhenNeededAndClampsNegative()
    {
        Assert.Equal("2:03:04", Humanize.Duration(
            TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4)));
        Assert.Equal("00:00", Humanize.Duration(TimeSpan.FromSeconds(-30)));
    }

    [Theory]
    [InlineData(0L, "—")]
    [InlineData(-5L, "—")]
    [InlineData(100_000_000L, "100 Mbps")]
    [InlineData(1_000_000_000L, "1000 Mbps")]
    [InlineData(1_500_000L, "1.5 Mbps")]
    public void Speed_FormatsLinkSpeed(long bitsPerSecond, string expected)
    {
        Assert.Equal(expected, Humanize.Speed(bitsPerSecond));
    }

    [Theory]
    [InlineData(100d, "excellent")]
    [InlineData(90d, "excellent")]
    [InlineData(89d, "good")]
    [InlineData(75d, "good")]
    [InlineData(74d, "fair")]
    [InlineData(50d, "fair")]
    [InlineData(49d, "poor")]
    [InlineData(1d, "poor")]
    [InlineData(0d, "unknown")]
    [InlineData(-1d, "unknown")]
    public void Quality_MapsScoreToLabel(double score, string expected)
    {
        Assert.Equal(expected, Humanize.Quality(score));
    }

    [Fact]
    public void Clock_UsesLocalTimeHoursMinutesSeconds()
    {
        var text = Humanize.Clock(DateTimeOffset.UtcNow);

        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", text);
    }

    [Fact]
    public void Relative_DescribesAgeAtEachBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("now", Humanize.Relative(now.AddSeconds(-5), now));
        Assert.Equal("10s ago", Humanize.Relative(now.AddSeconds(-10), now));
        Assert.Equal("59s ago", Humanize.Relative(now.AddSeconds(-59), now));
        Assert.Equal("1m ago", Humanize.Relative(now.AddSeconds(-60), now));
        Assert.Equal("59m ago", Humanize.Relative(now.AddMinutes(-59), now));
        Assert.Equal("1h ago", Humanize.Relative(now.AddMinutes(-60), now));
        Assert.Equal("2h ago", Humanize.Relative(now.AddHours(-2), now));
        Assert.Equal("1d ago", Humanize.Relative(now.AddHours(-25), now));
    }
}
