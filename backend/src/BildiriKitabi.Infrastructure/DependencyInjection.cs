using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using Microsoft.Extensions.DependencyInjection;

namespace BildiriKitabi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBookGeneration(this IServiceCollection services)
    {
        QuestPdfSetup.EnsureInitialized();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDocxReader, OpenXmlDocxReader>();
        services.AddSingleton<IBookRenderer, QuestPdfBookRenderer>();
        services.AddSingleton<IPdfLeakScanner, PdfPigLeakScanner>();
        services.AddSingleton<BookGenerator>();
        return services;
    }
}
