namespace BildiriKitabi.Tests.Shared;

internal static class TestPaths
{
    public const string MissingPapersMessage =
        "Örnek bildiriler bulunamadı: case ile gönderilen 10 .docx dosyasını testdata/bildiriler/ klasörüne kopyalayın.";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string PapersDirectory => Path.Combine(RepositoryRoot, "testdata", "bildiriler");

    /// <summary>
    /// True when the sample papers sent with the case have been copied into <c>testdata/bildiriler</c>. They belong to
    /// the company and are not in git.
    /// </summary>
    public static bool PapersAvailable =>
        Directory.Exists(PapersDirectory) && Directory.EnumerateFiles(PapersDirectory, "*.docx").Any();

    /// <summary>The ten sample papers; skips the calling test when they are missing.</summary>
    public static IReadOnlyList<string> PaperFiles
    {
        get
        {
            EnsurePapersAvailable();
            return Directory.GetFiles(PapersDirectory, "*.docx").Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>One sample paper; skips the calling test when the papers are missing.</summary>
    public static string PaperPath(string fileName)
    {
        EnsurePapersAvailable();
        return Path.Combine(PapersDirectory, fileName);
    }

    /// <summary>Skips the calling test (or test class, when called from its constructor) without the sample papers.</summary>
    public static void EnsurePapersAvailable()
    {
        if (!PapersAvailable)
        {
            Assert.Skip(MissingPapersMessage);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root with backend/global.json was not found.");
    }
}
