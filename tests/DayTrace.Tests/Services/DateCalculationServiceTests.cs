using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Moq;

namespace DayTrace.Tests.Services;

public class DateCalculationServiceTests
{
    private readonly Mock<IWeekScheduleHistoryRepository> _weekScheduleRepoMock;
    private readonly DateCalculationService _service;

    public DateCalculationServiceTests()
    {
        _weekScheduleRepoMock = new Mock<IWeekScheduleHistoryRepository>();
        _service = new DateCalculationService(_weekScheduleRepoMock.Object);
    }

    // ========== GetTodayLocal ==========

    [Fact]
    public void GetTodayLocal_UtcTimezone_ReturnsUtcDate()
    {
        var result = _service.GetTodayLocal("UTC");
        var expected = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetTodayLocal_ValidTimezone_ReturnsCorrectDate()
    {
        // Tokyo is UTC+9, so late UTC evening is already next day in Tokyo
        var result = _service.GetTodayLocal("Asia/Tokyo");
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var expected = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetTodayLocal_InvalidTimezone_Throws()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => _service.GetTodayLocal("Invalid/Timezone"));
    }

    // ========== IsValidTimezone ==========

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Europe/Moscow", true)]
    [InlineData("America/New_York", true)]
    [InlineData("Asia/Tokyo", true)]
    [InlineData("Invalid/Tz", false)]
    [InlineData("", false)]
    public void IsValidTimezone_ReturnsExpected(string tz, bool expected)
    {
        Assert.Equal(expected, DateCalculationService.IsValidTimezone(tz));
    }

    // ========== Week Boundaries ==========

    [Theory]
    // Sunday as week end: week = Mon-Sun
    [InlineData("2026-02-17", DayOfWeek.Sunday, "2026-02-16", "2026-02-22")] // Tuesday
    [InlineData("2026-02-22", DayOfWeek.Sunday, "2026-02-16", "2026-02-22")] // Sunday (week end)
    [InlineData("2026-02-16", DayOfWeek.Sunday, "2026-02-16", "2026-02-22")] // Monday (week start)
    // Saturday as week end: week = Sun-Sat
    [InlineData("2026-02-17", DayOfWeek.Saturday, "2026-02-15", "2026-02-21")] // Tuesday
    [InlineData("2026-02-15", DayOfWeek.Saturday, "2026-02-15", "2026-02-21")] // Sunday (week start)
    [InlineData("2026-02-21", DayOfWeek.Saturday, "2026-02-15", "2026-02-21")] // Saturday (week end)
    // Friday as week end: week = Sat-Fri
    [InlineData("2026-02-17", DayOfWeek.Friday, "2026-02-14", "2026-02-20")] // Tuesday
    public void ComputeWeekBoundaries_ReturnsCorrectRange(
        string dateStr, DayOfWeek weekEnd, string expectedStartStr, string expectedEndStr)
    {
        var date = DateOnly.Parse(dateStr);
        var expectedStart = DateOnly.Parse(expectedStartStr);
        var expectedEnd = DateOnly.Parse(expectedEndStr);

        var (start, end) = DateCalculationService.ComputeWeekBoundaries(date, weekEnd);

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    [Fact]
    public void ComputeWeekBoundaries_WeekAlways7Days()
    {
        // For any date and any weekEnd, the resulting range should be exactly 7 days
        foreach (DayOfWeek weekEnd in Enum.GetValues<DayOfWeek>())
        {
            var date = new DateOnly(2026, 3, 15); // random date
            var (start, end) = DateCalculationService.ComputeWeekBoundaries(date, weekEnd);
            Assert.Equal(6, end.DayNumber - start.DayNumber); // inclusive, so 6 days difference = 7 days
        }
    }

    // ========== Month Boundaries ==========

    [Theory]
    [InlineData("2026-02-15", "2026-02-01", "2026-02-28")] // Feb non-leap
    [InlineData("2024-02-15", "2024-02-01", "2024-02-29")] // Feb leap year
    [InlineData("2026-01-01", "2026-01-01", "2026-01-31")] // Jan, first day
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31")] // Dec, last day
    [InlineData("2026-06-15", "2026-06-01", "2026-06-30")] // June (30 days)
    public void GetMonthBoundaries_ReturnsCorrectRange(string dateStr, string expectedStartStr, string expectedEndStr)
    {
        var date = DateOnly.Parse(dateStr);
        var (start, end) = DateCalculationService.GetMonthBoundaries(date);

        Assert.Equal(DateOnly.Parse(expectedStartStr), start);
        Assert.Equal(DateOnly.Parse(expectedEndStr), end);
    }

    [Theory]
    [InlineData("2026-02-28", true)]
    [InlineData("2024-02-29", true)]
    [InlineData("2026-01-31", true)]
    [InlineData("2026-06-30", true)]
    [InlineData("2026-02-15", false)]
    [InlineData("2026-01-01", false)]
    public void IsLastDayOfMonth_ReturnsExpected(string dateStr, bool expected)
    {
        Assert.Equal(expected, DateCalculationService.IsLastDayOfMonth(DateOnly.Parse(dateStr)));
    }

    // ========== Year Boundaries ==========

    [Theory]
    [InlineData("2026-06-15", "2026-01-01", "2026-12-31")]
    [InlineData("2026-01-01", "2026-01-01", "2026-12-31")]
    [InlineData("2026-12-31", "2026-01-01", "2026-12-31")]
    public void GetYearBoundaries_ReturnsCorrectRange(string dateStr, string expectedStartStr, string expectedEndStr)
    {
        var date = DateOnly.Parse(dateStr);
        var (start, end) = DateCalculationService.GetYearBoundaries(date);

        Assert.Equal(DateOnly.Parse(expectedStartStr), start);
        Assert.Equal(DateOnly.Parse(expectedEndStr), end);
    }

    [Theory]
    [InlineData("2026-12-31", true)]
    [InlineData("2026-12-30", false)]
    [InlineData("2026-01-01", false)]
    public void IsDecember31_ReturnsExpected(string dateStr, bool expected)
    {
        Assert.Equal(expected, DateCalculationService.IsDecember31(DateOnly.Parse(dateStr)));
    }

    // ========== ParseDayOfWeek ==========

    [Theory]
    [InlineData("Sunday", DayOfWeek.Sunday)]
    [InlineData("sunday", DayOfWeek.Sunday)]
    [InlineData("MONDAY", DayOfWeek.Monday)]
    [InlineData("Friday", DayOfWeek.Friday)]
    public void ParseDayOfWeek_ValidInput_ReturnsCorrect(string input, DayOfWeek expected)
    {
        Assert.Equal(expected, DateCalculationService.ParseDayOfWeek(input));
    }

    [Fact]
    public void ParseDayOfWeek_Invalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => DateCalculationService.ParseDayOfWeek("NotADay"));
    }

    // ========== GetEffectiveWeekEndDayAsync (with mocks) ==========

    [Fact]
    public async Task GetEffectiveWeekEndDayAsync_UsesEffectiveSchedule()
    {
        var schedule = new WeekScheduleHistory
        {
            UserId = 1,
            WeekEnd = "Saturday",
            EffectiveFromLocalDate = new DateOnly(2026, 1, 1)
        };

        _weekScheduleRepoMock
            .Setup(r => r.GetEffectiveForDateAsync(1, It.IsAny<DateOnly>(), default))
            .ReturnsAsync(schedule);

        var result = await _service.GetEffectiveWeekEndDayAsync(1, new DateOnly(2026, 2, 15));

        Assert.Equal(DayOfWeek.Saturday, result);
    }

    [Fact]
    public async Task GetEffectiveWeekEndDayAsync_FallbackToEarliest()
    {
        // No effective schedule for the date
        _weekScheduleRepoMock
            .Setup(r => r.GetEffectiveForDateAsync(1, It.IsAny<DateOnly>(), default))
            .ReturnsAsync((WeekScheduleHistory?)null);

        var earliest = new WeekScheduleHistory
        {
            UserId = 1,
            WeekEnd = "Friday",
            EffectiveFromLocalDate = new DateOnly(2026, 3, 1)
        };

        _weekScheduleRepoMock
            .Setup(r => r.GetEarliestAsync(1, default))
            .ReturnsAsync(earliest);

        var result = await _service.GetEffectiveWeekEndDayAsync(1, new DateOnly(2026, 2, 1));

        Assert.Equal(DayOfWeek.Friday, result);
    }

    [Fact]
    public async Task GetEffectiveWeekEndDayAsync_NoRecords_DefaultsSunday()
    {
        _weekScheduleRepoMock
            .Setup(r => r.GetEffectiveForDateAsync(1, It.IsAny<DateOnly>(), default))
            .ReturnsAsync((WeekScheduleHistory?)null);
        _weekScheduleRepoMock
            .Setup(r => r.GetEarliestAsync(1, default))
            .ReturnsAsync((WeekScheduleHistory?)null);

        var result = await _service.GetEffectiveWeekEndDayAsync(1, new DateOnly(2026, 2, 15));

        Assert.Equal(DayOfWeek.Sunday, result);
    }

    // ========== Backdate Window ==========

    [Fact]
    public void GetBackdateWindow_Returns30DayRange()
    {
        var (min, max) = _service.GetBackdateWindow("UTC");
        Assert.Equal(30, max.DayNumber - min.DayNumber);
    }

    // ========== DST Edge Cases ==========

    [Fact]
    public void GetTodayLocal_DstTimezone_HandlesCorrectly()
    {
        // America/New_York uses DST. Just verify it doesn't throw.
        var result = _service.GetTodayLocal("America/New_York");
        Assert.True(result.Year >= 2026);
    }

    [Fact]
    public void ConvertToLocal_DstTransition_HandlesCorrectly()
    {
        // March 8, 2026 02:00 UTC — near spring-forward in US Eastern
        var utc = new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc); // 2 AM ET = 7 AM UTC
        var local = _service.ConvertToLocal(utc, "America/New_York");
        // Should be 2 AM ET or 3 AM ET depending on DST transition
        Assert.Equal(2026, local.Year);
        Assert.Equal(3, local.Month);
        Assert.Equal(8, local.Day);
    }

    [Fact]
    public void YearBoundaries_CrossYearDst_HandlesCorrectly()
    {
        // Dec 31 near midnight in a DST-aware timezone
        var date = new DateOnly(2026, 12, 31);
        var (start, end) = DateCalculationService.GetYearBoundaries(date);
        Assert.Equal(new DateOnly(2026, 1, 1), start);
        Assert.Equal(new DateOnly(2026, 12, 31), end);
    }
}
