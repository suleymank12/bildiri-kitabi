using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Titles;

namespace BildiriKitabi.UnitTests.Books;

public sealed class BookTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_book_is_uploaded_and_numbers_its_papers_in_order()
    {
        var book = NewBook();

        book.Status.ShouldBe(BookStatus.Uploaded);
        book.Papers.Select(p => p.Order).ShouldBe([1, 2]);
        book.IsEditable.ShouldBeTrue();
    }

    [Fact]
    public void Happy_path_goes_from_uploaded_to_completed()
    {
        var book = NewBook();

        book.MarkQueued(Now);
        book.MarkProcessing(Now);
        book.ReportProgress(GenerationStage.Reading, 10);
        book.ReportProgress(GenerationStage.Rendering, 45);
        book.ReportProgress(GenerationStage.Saving, 97);
        book.MarkCompleted("books/x/output/book.pdf", 1234, 22, Now.AddSeconds(3));

        book.Status.ShouldBe(BookStatus.Completed);
        book.Stage.ShouldBeNull();
        book.ProgressPercent.ShouldBe((byte)100);
        book.PageCount.ShouldBe(22);
        book.ProcessingStartedAt.ShouldBe(Now);
        book.ProcessingFinishedAt.ShouldBe(Now.AddSeconds(3));
    }

    [Fact]
    public void Progress_never_goes_backwards_and_stays_below_100_until_completion()
    {
        var book = Processing();

        book.ReportProgress(GenerationStage.Rendering, 45);
        book.ReportProgress(GenerationStage.Verifying, 30);
        book.ProgressPercent.ShouldBe((byte)45);

        book.ReportProgress(GenerationStage.Saving, 250);
        book.ProgressPercent.ShouldBe((byte)99);
    }

    [Fact]
    public void A_failed_book_keeps_code_and_message_and_can_be_queued_again()
    {
        var book = Processing();

        book.MarkFailed("RENDER_FAILED", "PDF dizgisi oluşturulamadı.", Now);
        book.Status.ShouldBe(BookStatus.Failed);
        book.ErrorCode.ShouldBe("RENDER_FAILED");
        book.ErrorMessage.ShouldBe("PDF dizgisi oluşturulamadı.");
        book.IsEditable.ShouldBeTrue();

        book.MarkQueued(Now);
        book.Status.ShouldBe(BookStatus.Queued);
        book.ErrorCode.ShouldBeNull();
        book.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void Error_message_is_cut_to_the_column_length()
    {
        var book = Processing();

        book.MarkFailed("INTERNAL_ERROR", new string('x', 800), Now);

        book.ErrorMessage!.Length.ShouldBe(Book.ErrorMessageMaxLength);
        book.ErrorMessage.ShouldEndWith("…");
    }

    [Fact]
    public void An_interrupted_book_returns_to_the_queue()
    {
        var book = Processing();
        book.ReportProgress(GenerationStage.Rendering, 45);

        book.ReturnToQueue(Now.AddMinutes(1));

        book.Status.ShouldBe(BookStatus.Queued);
        book.QueuedAt.ShouldBe(Now.AddMinutes(1));
        book.ProgressPercent.ShouldBe((byte)0);
        book.ProcessingStartedAt.ShouldBeNull();
    }

    public static TheoryData<string, Action<Book>> InvalidTransitions => new()
    {
        { "Uploaded → Processing", b => b.MarkProcessing(Now) },
        { "Uploaded → Completed", b => b.MarkCompleted("k", 1, 1, Now) },
        { "Uploaded → progress", b => b.ReportProgress(GenerationStage.Reading, 1) },
        { "Uploaded → Failed", b => b.MarkFailed("X", "y", Now) },
        { "Uploaded → back to queue", b => b.ReturnToQueue(Now.AddMinutes(1)) },
        { "Queued → Queued", b => { b.MarkQueued(Now); b.MarkQueued(Now); } },
        { "Queued → Completed", b => { b.MarkQueued(Now); b.MarkCompleted("k", 1, 1, Now); } },
        { "Processing → Queued by user", b => { b.MarkQueued(Now); b.MarkProcessing(Now); b.MarkQueued(Now); } },
        { "Completed → Queued", b => { Complete(b); b.MarkQueued(Now); } },
        { "Completed → Failed", b => { Complete(b); b.MarkFailed("X", "y", Now); } },
        { "Paper added after queueing", b => { b.MarkQueued(Now); AddPaper(b, 9); } },
    };

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Invalid_transitions_throw(string transition, Action<Book> act)
    {
        ArgumentNullException.ThrowIfNull(act);
        Should.Throw<InvalidOperationException>(() => act(NewBook()), customMessage: transition);
    }

    [Fact]
    public void Storage_keys_need_the_ids_assigned_by_the_database_context()
    {
        Should.Throw<InvalidOperationException>(() => NewBook().AssignStorageKeys());
    }

    private static Book NewBook()
    {
        var book = new Book("Örnek Bilim Kongresi 2026", Now);
        AddPaper(book, 1);
        AddPaper(book, 2);
        return book;
    }

    private static Book Processing()
    {
        var book = NewBook();
        book.MarkQueued(Now);
        book.MarkProcessing(Now);
        return book;
    }

    private static void Complete(Book book)
    {
        book.MarkQueued(Now);
        book.MarkProcessing(Now);
        book.MarkCompleted("k", 1, 1, Now);
    }

    private static void AddPaper(Book book, byte seed) =>
        book.AddPaper($"{seed:D2}_Bildiri.docx", 100, Enumerable.Repeat(seed, 32).ToArray(), "BAŞLIK", TitleSource.TitleStyle, Now);
}
