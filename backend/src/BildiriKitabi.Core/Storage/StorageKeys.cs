using System.Globalization;

namespace BildiriKitabi.Core.Storage;

/// <summary>
/// The only shapes of storage keys the system produces; user file names never become part of a key. Keys are built from
/// the external uids (<see cref="Books.Book.Uid"/>, <see cref="Books.Paper.Uid"/>), never from the database ids.
/// </summary>
public static class StorageKeys
{
    public static string BookPrefix(Guid bookUid) => string.Create(CultureInfo.InvariantCulture, $"books/{bookUid:D}/");

    public static string Source(Guid bookUid, Guid paperUid) =>
        string.Create(CultureInfo.InvariantCulture, $"books/{bookUid:D}/sources/{paperUid:D}.docx");

    public static string Output(Guid bookUid) => string.Create(CultureInfo.InvariantCulture, $"books/{bookUid:D}/output/book.pdf");
}
