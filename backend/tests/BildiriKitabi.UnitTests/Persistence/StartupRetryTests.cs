using BildiriKitabi.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BildiriKitabi.UnitTests.Persistence;

public sealed class StartupRetryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 10)]
    [InlineData(40, 10)]
    public void Waits_grow_exponentially_up_to_ten_seconds(int failedAttempts, int seconds)
    {
        StartupRetry.DelayAfterFailedAttempt(failedAttempts).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task Retries_transient_errors_with_growing_waits_until_the_operation_succeeds()
    {
        var clock = new FakeTimeProvider(Start);
        var attemptTimes = new List<DateTimeOffset>();

        var run = Retry(clock).RunAsync("Test", _ =>
        {
            attemptTimes.Add(clock.GetUtcNow());
            return attemptTimes.Count < 4 ? Task.FromException(new TransientTestException(attemptTimes.Count)) : Task.CompletedTask;
        }, Transient, TestContext.Current.CancellationToken);
        await DriveAsync(clock, run);

        await run;
        attemptTimes.Count.ShouldBe(4);
        var waits = attemptTimes.Zip(attemptTimes.Skip(1), (earlier, later) => later - earlier).ToList();
        waits[0].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        waits[1].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        waits[2].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Throws_a_permanent_error_at_once_without_waiting()
    {
        var clock = new FakeTimeProvider(Start);
        var attempts = 0;

        var error = await Should.ThrowAsync<InvalidOperationException>(Retry(clock).RunAsync("Test", _ =>
        {
            attempts++;
            return Task.FromException(new InvalidOperationException("permanent"));
        }, Transient, TestContext.Current.CancellationToken));

        error.Message.ShouldBe("permanent");
        attempts.ShouldBe(1);
        clock.GetUtcNow().ShouldBe(Start);
    }

    [Fact]
    public async Task Throws_the_last_transient_error_once_the_budget_is_used_up()
    {
        var clock = new FakeTimeProvider(Start);
        var attempts = 0;

        var run = Retry(clock).RunAsync("Test", _ => Task.FromException(new TransientTestException(++attempts)), Transient, TestContext.Current.CancellationToken);
        await DriveAsync(clock, run);

        var error = await Should.ThrowAsync<TransientTestException>(run);
        error.Attempt.ShouldBe(attempts);
        attempts.ShouldBeGreaterThan(3);
        (clock.GetUtcNow() - Start).ShouldBeLessThan(StartupRetry.DefaultBudget);
    }

    [Fact]
    public async Task Stops_when_startup_is_cancelled()
    {
        var clock = new FakeTimeProvider(Start);
        using var cancellation = new CancellationTokenSource();

        var run = Retry(clock).RunAsync("Test", _ =>
        {
            cancellation.Cancel();
            return Task.FromException(new TransientTestException(1));
        }, Transient, cancellation.Token);

        await Should.ThrowAsync<TransientTestException>(run);
    }

    private static StartupRetry Retry(FakeTimeProvider clock) => new(clock, NullLogger.Instance);

    private static string? Transient(Exception exception) => exception is TransientTestException ? "test" : null;

    /// <summary>Moves the fake clock forward in small steps until the retry loop finishes (or clearly hangs).</summary>
    private static async Task DriveAsync(FakeTimeProvider clock, Task run)
    {
        for (var step = 0; step < 1000 && !run.IsCompleted; step++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);
        }
    }

    private sealed class TransientTestException(int attempt) : Exception($"attempt {attempt}")
    {
        public int Attempt { get; } = attempt;
    }
}
