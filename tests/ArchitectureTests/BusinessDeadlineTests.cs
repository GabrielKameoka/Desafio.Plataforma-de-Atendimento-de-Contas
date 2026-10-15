using Domain;

namespace ArchitectureTests;

public sealed class BusinessDeadlineTests
{
    [Theory]
    [InlineData(2026, 10, 9, 14, RequestPriority.High, 2026, 10, 13)]
    [InlineData(2026, 10, 9, 14, RequestPriority.Medium, 2026, 10, 15)]
    [InlineData(2026, 10, 9, 14, RequestPriority.Low, 2026, 10, 17)]
    [InlineData(2026, 10, 10, 14, RequestPriority.Medium, 2026, 10, 15)]
    [InlineData(2026, 10, 11, 14, RequestPriority.Medium, 2026, 10, 15)]
    public void Counts_business_days_after_opening_in_sao_paulo(int year, int month, int day,
        int hourUtc, RequestPriority priority, int dueYear, int dueMonth, int dueDayUtc)
    {
        var opening = new DateTimeOffset(year, month, day, hourUtc, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(dueYear, dueMonth, dueDayUtc, 2, 59, 59, 999, TimeSpan.Zero);

        Assert.Equal(expected, BusinessDeadline.Calculate(opening, priority));
    }

    [Fact]
    public void Uses_local_opening_date_when_utc_date_is_already_saturday()
    {
        // Friday 22:30 in Sao Paulo is already Saturday 01:30 UTC.
        var opening = new DateTimeOffset(2026, 10, 10, 1, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 2, 59, 59, 999, TimeSpan.Zero),
            BusinessDeadline.Calculate(opening, RequestPriority.Medium));
    }
}
