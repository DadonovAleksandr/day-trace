using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Moq;

namespace DayTrace.Tests.Services;

public class EventLockServiceTests
{
    private readonly Mock<ISummaryRepository> _summaryRepoMock;
    private readonly Mock<IWeekScheduleHistoryRepository> _weekScheduleRepoMock;
    private readonly DateCalculationService _dateCalcService;
    private readonly EventLockService _service;

    private const long UserId = 42;

    public EventLockServiceTests()
    {
        _summaryRepoMock = new Mock<ISummaryRepository>();
        _weekScheduleRepoMock = new Mock<IWeekScheduleHistoryRepository>();
        _dateCalcService = new DateCalculationService(_weekScheduleRepoMock.Object);
        _service = new EventLockService(_summaryRepoMock.Object, _dateCalcService);

        // Default: week schedule returns Sunday as week end
        _weekScheduleRepoMock
            .Setup(r => r.GetEffectiveForDateAsync(It.IsAny<long>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync(new WeekScheduleHistory { WeekEnd = "Sunday" });
    }

    // ========== IsEventLockedAsync ==========

    [Fact]
    public async Task IsEventLockedAsync_NoSummary_ReturnsNotLocked()
    {
        // eventDate = Wed 2026-02-18 => week (Sun weekEnd): 2026-02-16 .. 2026-02-22
        var eventDate = new DateOnly(2026, 2, 18);
        var weekStart = new DateOnly(2026, 2, 16);
        var weekEnd = new DateOnly(2026, 2, 22);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "weekly", weekStart, weekEnd, default))
            .ReturnsAsync((Summary?)null);

        var (locked, lockedBy) = await _service.IsEventLockedAsync(UserId, eventDate);

        Assert.False(locked);
        Assert.Null(lockedBy);
    }

    [Fact]
    public async Task IsEventLockedAsync_SummaryGenerated_ReturnsLocked()
    {
        var eventDate = new DateOnly(2026, 2, 18);
        var weekStart = new DateOnly(2026, 2, 16);
        var weekEnd = new DateOnly(2026, 2, 22);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "weekly", weekStart, weekEnd, default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "weekly",
                PeriodStart = weekStart,
                PeriodEnd = weekEnd,
                Status = "generated",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsEventLockedAsync(UserId, eventDate);

        Assert.True(locked);
        Assert.Equal("weekly", lockedBy);
    }

    [Fact]
    public async Task IsEventLockedAsync_SummaryGenerating_ReturnsNotLocked()
    {
        var eventDate = new DateOnly(2026, 2, 18);
        var weekStart = new DateOnly(2026, 2, 16);
        var weekEnd = new DateOnly(2026, 2, 22);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "weekly", weekStart, weekEnd, default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "weekly",
                PeriodStart = weekStart,
                PeriodEnd = weekEnd,
                Status = "generating",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsEventLockedAsync(UserId, eventDate);

        Assert.False(locked);
        Assert.Null(lockedBy);
    }

    [Fact]
    public async Task IsEventLockedAsync_SummaryFailed_ReturnsNotLocked()
    {
        var eventDate = new DateOnly(2026, 2, 18);
        var weekStart = new DateOnly(2026, 2, 16);
        var weekEnd = new DateOnly(2026, 2, 22);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "weekly", weekStart, weekEnd, default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "weekly",
                PeriodStart = weekStart,
                PeriodEnd = weekEnd,
                Status = "failed",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsEventLockedAsync(UserId, eventDate);

        Assert.False(locked);
        Assert.Null(lockedBy);
    }

    // ========== IsSummaryLockedAsync ==========

    [Fact]
    public async Task IsSummaryLockedAsync_WeeklyNoMonthly_ReturnsNotLocked()
    {
        // weekly: 2026-02-16 .. 2026-02-22 => month boundaries: 2026-02-01 .. 2026-02-28
        var periodStart = new DateOnly(2026, 2, 16);
        var periodEnd = new DateOnly(2026, 2, 22);
        var monthStart = new DateOnly(2026, 2, 1);
        var monthEnd = new DateOnly(2026, 2, 28);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "monthly", monthStart, monthEnd, default))
            .ReturnsAsync((Summary?)null);

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "weekly", periodStart, periodEnd);

        Assert.False(locked);
        Assert.Null(lockedBy);
    }

    [Fact]
    public async Task IsSummaryLockedAsync_WeeklyMonthlyGenerated_ReturnsLocked()
    {
        var periodStart = new DateOnly(2026, 2, 16);
        var periodEnd = new DateOnly(2026, 2, 22);
        var monthStart = new DateOnly(2026, 2, 1);
        var monthEnd = new DateOnly(2026, 2, 28);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "monthly", monthStart, monthEnd, default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "monthly",
                PeriodStart = monthStart,
                PeriodEnd = monthEnd,
                Status = "generated",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "weekly", periodStart, periodEnd);

        Assert.True(locked);
        Assert.Equal("monthly", lockedBy);
    }

    [Fact]
    public async Task IsSummaryLockedAsync_MonthlyYearlyGenerated_ReturnsLocked()
    {
        // monthly: 2026-02-01 .. 2026-02-28 => year boundaries: 2026-01-01 .. 2026-12-31
        var periodStart = new DateOnly(2026, 2, 1);
        var periodEnd = new DateOnly(2026, 2, 28);
        var yearStart = new DateOnly(2026, 1, 1);
        var yearEnd = new DateOnly(2026, 12, 31);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "yearly", yearStart, yearEnd, default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "yearly",
                PeriodStart = yearStart,
                PeriodEnd = yearEnd,
                Status = "generated",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "monthly", periodStart, periodEnd);

        Assert.True(locked);
        Assert.Equal("yearly", lockedBy);
    }

    [Fact]
    public async Task IsSummaryLockedAsync_MonthlyNoYearly_ReturnsNotLocked()
    {
        var periodStart = new DateOnly(2026, 2, 1);
        var periodEnd = new DateOnly(2026, 2, 28);
        var yearStart = new DateOnly(2026, 1, 1);
        var yearEnd = new DateOnly(2026, 12, 31);

        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "yearly", yearStart, yearEnd, default))
            .ReturnsAsync((Summary?)null);

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "monthly", periodStart, periodEnd);

        Assert.False(locked);
        Assert.Null(lockedBy);
    }

    [Fact]
    public async Task IsSummaryLockedAsync_Yearly_NeverLocked()
    {
        var periodStart = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 12, 31);

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "yearly", periodStart, periodEnd);

        Assert.False(locked);
        Assert.Null(lockedBy);

        // Verify no repository calls were made for yearly
        _summaryRepoMock.Verify(
            r => r.GetAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========== Edge cases ==========

    [Fact]
    public async Task IsSummaryLockedAsync_WeeklyCrossMonthBoundary_ChecksBothMonths()
    {
        // Week crossing month boundary: 2026-02-23 (Mon) .. 2026-03-01 (Sun)
        var periodStart = new DateOnly(2026, 2, 23);
        var periodEnd = new DateOnly(2026, 3, 1);

        // Feb month: 2026-02-01 .. 2026-02-28 — no summary
        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "monthly",
                new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), default))
            .ReturnsAsync((Summary?)null);

        // Mar month: 2026-03-01 .. 2026-03-31 — generated summary
        _summaryRepoMock
            .Setup(r => r.GetAsync(UserId, "monthly",
                new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), default))
            .ReturnsAsync(new Summary
            {
                UserId = UserId,
                PeriodType = "monthly",
                PeriodStart = new DateOnly(2026, 3, 1),
                PeriodEnd = new DateOnly(2026, 3, 31),
                Status = "generated",
                Version = 1
            });

        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "weekly", periodStart, periodEnd);

        Assert.True(locked);
        Assert.Equal("monthly", lockedBy);
    }

    [Fact]
    public async Task IsSummaryLockedAsync_UnknownPeriodType_ReturnsNotLocked()
    {
        var (locked, lockedBy) = await _service.IsSummaryLockedAsync(
            UserId, "daily", new DateOnly(2026, 2, 18), new DateOnly(2026, 2, 18));

        Assert.False(locked);
        Assert.Null(lockedBy);
    }
}
