using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BildiriKitabi.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Book> Books => Set<Book>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // EF Core warns that a paper whose book is filtered out could look orphaned. Papers are only ever reached through
        // Books (IAppDbContext has no paper set), so the book filter always applies before them.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All times are stored as UTC datetime2 and come back marked as UTC.
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2").HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime2").HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
