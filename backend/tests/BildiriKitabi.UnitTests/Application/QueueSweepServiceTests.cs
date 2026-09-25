using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;

namespace BildiriKitabi.UnitTests.Application;

public sealed class QueueSweepServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    public void A_queued_book_is_stale_once_it_waited_long_enough(int secondsWaiting, bool stale)
    {
        QueueSweepService.IsStale(BookStatus.Queued, Now.AddSeconds(-secondsWaiting), Now, StaleAfter).ShouldBe(stale);
    }

    [Fact]
    public void A_queued_book_without_a_queue_time_is_stale()
    {
        QueueSweepService.IsStale(BookStatus.Queued, null, Now, StaleAfter).ShouldBeTrue();
    }

    [Theory]
    [InlineData(BookStatus.Uploaded)]
    [InlineData(BookStatus.Processing)]
    [InlineData(BookStatus.Completed)]
    [InlineData(BookStatus.Failed)]
    public void Only_queued_books_are_ever_stale(BookStatus status)
    {
        QueueSweepService.IsStale(status, Now.AddHours(-1), Now, StaleAfter).ShouldBeFalse();
    }
}
