namespace BildiriKitabi.Core.Storage;

/// <summary>
/// Stores files under system-generated keys (see <see cref="StorageKeys"/>). Keys never contain user input.
/// </summary>
public interface IFileStorage
{
    /// <summary>Writes the whole stream atomically: readers see either the previous file or the complete new one.</summary>
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <exception cref="FileNotFoundException">No file is stored under the key.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes every file whose key starts with the prefix (for example <c>books/{uid}/</c>).</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
