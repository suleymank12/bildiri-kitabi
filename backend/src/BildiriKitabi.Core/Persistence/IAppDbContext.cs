using BildiriKitabi.Core.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BildiriKitabi.Core.Persistence;

/// <summary>The application's view of the database; implemented by the EF Core context in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Book> Books { get; }

    DbSet<Paper> Papers { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
