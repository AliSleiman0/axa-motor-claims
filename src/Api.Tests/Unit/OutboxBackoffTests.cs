using Api.Outbox;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §6.3's retry schedule, as a table. One of the four cores CLAUDE.md requires to be
/// written test-first; pure, so it needs no clock and no database.
/// </summary>
public sealed class OutboxBackoffTests
{
    private const int Ceiling = 6;

    [Theory]
    // §6.3 verbatim: "1 min -> 5 min -> 30 min -> 2 h -> 6 h ...".
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    // ... and the ceiling from there on, for every attempt up to MaxAttempts.
    [InlineData(5, 360)]
    [InlineData(6, 360)]
    [InlineData(7, 360)]
    [InlineData(8, 360)]
    public void Delay_FollowsTheScheduleInDesign63(int attempts, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), OutboxBackoff.Delay(attempts, Ceiling));
    }

    [Fact]
    public void Delay_TakesAboutADayAndAHalfToExhaustTheSchedule()
    {
        // Not decoration: this is the number that answers "how long until a push that will never
        // succeed shows up on A2 where someone can retry it". §6.3's schedule with a 6 h ceiling
        // works out at 26 h 36 m — over a day, which is worth knowing rather than assuming, and is
        // the figure to revisit when #33 answers NEXT3's maintenance windows.
        var total = TimeSpan.Zero;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            total += OutboxBackoff.Delay(attempt, Ceiling);
        }

        Assert.Equal(TimeSpan.FromMinutes(1 + 5 + 30 + 120 + (360 * 4)), total);
        Assert.Equal(TimeSpan.FromHours(26) + TimeSpan.FromMinutes(36), total);
    }

    [Fact]
    public void Delay_IsCappedByTheConfiguredCeiling()
    {
        // The ceiling caps the early entries too, not only the tail: #33 may answer that NEXT3 is
        // only ever down briefly, and "retry faster" must actually mean faster from attempt one.
        Assert.Equal(TimeSpan.FromHours(1), OutboxBackoff.Delay(4, ceilingHours: 1));
        Assert.Equal(TimeSpan.FromHours(1), OutboxBackoff.Delay(8, ceilingHours: 1));

        // Below the ceiling, the table still wins.
        Assert.Equal(TimeSpan.FromMinutes(1), OutboxBackoff.Delay(1, ceilingHours: 1));
    }

    [Fact]
    public void Delay_GrowsMonotonically()
    {
        for (var attempt = 2; attempt <= 12; attempt++)
        {
            Assert.True(
                OutboxBackoff.Delay(attempt, Ceiling) >= OutboxBackoff.Delay(attempt - 1, Ceiling),
                $"Attempt {attempt} backed off less than attempt {attempt - 1}.");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Delay_RejectsAnAttemptCountBelowOne(int attempts)
    {
        // The dequeue increments attempts before the worker ever sees the row, so zero here means a
        // caller has lost track of which attempt failed.
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxBackoff.Delay(attempts, Ceiling));
    }

    [Fact]
    public void Delay_RejectsANonPositiveCeiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxBackoff.Delay(1, ceilingHours: 0));
    }
}
