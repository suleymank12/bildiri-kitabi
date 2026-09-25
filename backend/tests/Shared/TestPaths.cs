namespace BildiriKitabi.Tests.Shared;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string PapersDirectory => Path.Combine(RepositoryRoot, "testdata", "bildiriler");

    public static IReadOnlyList<string> PaperFiles =>
        Directory.GetFiles(PapersDirectory, "*.docx").Order(StringComparer.Ordinal).ToList();

    public static string PaperPath(string fileName) => Path.Combine(PapersDirectory, fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "testdata", "bildiriler")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root with testdata/bildiriler was not found.");
    }
}
