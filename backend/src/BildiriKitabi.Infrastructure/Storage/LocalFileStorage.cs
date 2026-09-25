using BildiriKitabi.Core.Storage;

namespace BildiriKitabi.Infrastructure.Storage;

/// <summary>
/// Stores files on the local disk under a root folder. Every key is resolved to a full path and must stay inside
/// the root (path traversal defense); writes go to a temporary file in the same folder and are then moved over the
/// target, so readers never see a half-written file.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920;

    private readonly string _root;

    public LocalFileStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Resolve(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await using (target.ConfigureAwait(false))
            {
                await content.CopyToAsync(target, BufferSize, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No file is stored under the key.", key);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (!prefix.EndsWith('/'))
        {
            throw new ArgumentException("A prefix names a folder and ends with '/'.", nameof(prefix));
        }

        var directory = Resolve(prefix.TrimEnd('/'));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(Resolve(key)));

    /// <summary>Maps a key to a full path; throws when the key is not a relative path that stays under the root.</summary>
    internal string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Path.IsPathRooted(key) || key.Contains(':', StringComparison.Ordinal) || key.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A storage key must be a relative path.", nameof(key));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, key));
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || fullPath.Length == _root.Length)
        {
            throw new ArgumentException("The storage key points outside the storage root.", nameof(key));
        }

        return fullPath;
    }
}
