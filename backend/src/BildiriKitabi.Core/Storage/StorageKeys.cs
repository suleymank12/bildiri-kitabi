using System.Globalization;

namespace BildiriKitabi.Core.Storage;

/// <summary>The only shapes of storage keys the system produces; user file names never become part of a key.</summary>
public static class StorageKeys
{
    public static string BookPrefix(Guid bookId) => string.Create(CultureInfo.InvariantCulture, $"books/{bookId:D}/");

    public static string Source(Guid bookId, Guid paperId) =>
        string.Create(CultureInfo.InvariantCulture, $"books/{bookId:D}/sources/{paperId:D}.docx");

    public static string Output(Guid bookId) => string.Create(CultureInfo.InvariantCulture, $"books/{bookId:D}/output/book.pdf");
}
