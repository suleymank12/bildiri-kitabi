using BildiriKitabi.Core.Books;
using Microsoft.EntityFrameworkCore;

namespace BildiriKitabi.Core.Persistence;

/// <summary>
/// The global query filter that hides deleted books, and the one deliberate way past it. A deleted book cannot be read,
/// generated, reordered, renamed or downloaded because every other query starts from the filtered <c>Books</c> set.
/// </summary>
public static class BookQueryFilters
{
    /// <summary>Name of the filter <c>AktifMi = 1</c> defined on <see cref="Book"/>.</summary>
    public const string Active = "ActiveBooks";

    /// <summary>
    /// Deleted books only, for the deleted-books list and restore; nothing else may ignore <see cref="Active"/>.
    /// </summary>
    public static IQueryable<Book> Deleted(this DbSet<Book> books)
    {
        ArgumentNullException.ThrowIfNull(books);
        return books.IgnoreQueryFilters([Active]).Where(b => !b.IsActive);
    }
}
