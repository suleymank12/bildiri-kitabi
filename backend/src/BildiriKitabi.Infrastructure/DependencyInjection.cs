using BildiriKitabi.Core.Application;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Fonts;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Core.Uploads;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.Infrastructure.Persistence;
using BildiriKitabi.Infrastructure.Queue;
using BildiriKitabi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>The PDF pipeline: docx reading, rendering and the leak scan.</summary>
    public static IServiceCollection AddBookGeneration(this IServiceCollection services)
    {
        QuestPdfSetup.EnsureInitialized();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDocxReader, OpenXmlDocxReader>();
        services.AddSingleton<IBookRenderer, QuestPdfBookRenderer>();
        services.AddSingleton<IPdfLeakScanner, PdfPigLeakScanner>();
        services.AddSingleton<IGlyphCoverage>(_ => FontGlyphCoverage.Instance);
        services.AddSingleton<BookGenerator>();
        return services;
    }

    /// <summary>
    /// Database, storage, queue and application services. Options must be bound by the host; the connection string
    /// is <c>ConnectionStrings:Default</c> and relative storage paths are resolved against <paramref name="contentRootPath"/>.
    /// </summary>
    public static IServiceCollection AddBookApplication(this IServiceCollection services, string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        services.AddBookGeneration();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddSingleton<IFileStorage>(provider =>
        {
            var root = provider.GetRequiredService<IOptions<StorageOptions>>().Value.RootPath;
            return new LocalFileStorage(Path.IsPathRooted(root) ? root : Path.Combine(contentRootPath, root));
        });
        services.AddSingleton<IBookGenerationQueue, InMemoryBookGenerationQueue>();

        services.AddScoped<BookUploadValidator>();
        services.AddScoped<CreateBookService>();
        services.AddScoped<BookCommandService>();
        services.AddScoped<BookGenerationHandler>();
        return services;
    }
}
