using BildiriKitabi.Core.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BildiriKitabi.Core.Persistence;

/// <summary>
/// The application's view of the database; implemented by the EF Core context in Infrastructure. Papers have no set of
/// their own: they are reached through <see cref="Books"/>, so the global filter on books covers them too.
/// </summary>
public interface IAppDbContext
{
    DbSet<Book> Books { get; }

    ChangeTracker ChangeTracker { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
