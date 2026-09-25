using System.Text;
using BildiriKitabi.Core.Storage;
using BildiriKitabi.Infrastructure.Storage;

namespace BildiriKitabi.UnitTests.Storage;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bk-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("books/../../outside.txt")]
    [InlineData("books/x/../../../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:outside.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData(".")]
    public void Keys_that_leave_the_root_are_rejected(string key)
    {
        Should.Throw<ArgumentException>(() => _storage.SaveAsync(key, new MemoryStream([1]), TestContext.Current.CancellationToken));
        Should.Throw<ArgumentException>(() => _storage.OpenReadAsync(key, TestContext.Current.CancellationToken));
        Should.Throw<ArgumentException>(() => _storage.DeleteAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saved_files_can_be_read_back_and_replaced_atomically()
    {
        var key = StorageKeys.Output(Guid.NewGuid());

        await _storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("ilk")), TestContext.Current.CancellationToken);
        await _storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("ikinci")), TestContext.Current.CancellationToken);

        (await ReadAsync(key)).ShouldBe("ikinci");
        Directory.GetFiles(Path.GetDirectoryName(_storage.Resolve(key))!).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failed_write_leaves_the_previous_file_and_no_temporary_file()
    {
        var key = StorageKeys.Output(Guid.NewGuid());
        await _storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("eski")), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<IOException>(() => _storage.SaveAsync(key, new FailingStream(), TestContext.Current.CancellationToken));

        (await ReadAsync(key)).ShouldBe("eski");
        Directory.GetFiles(Path.GetDirectoryName(_storage.Resolve(key))!).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deleting_a_prefix_removes_only_that_book()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _storage.SaveAsync(StorageKeys.Source(first, Guid.NewGuid()), new MemoryStream([1]), TestContext.Current.CancellationToken);
        await _storage.SaveAsync(StorageKeys.Output(first), new MemoryStream([2]), TestContext.Current.CancellationToken);
        var kept = StorageKeys.Output(second);
        await _storage.SaveAsync(kept, new MemoryStream([3]), TestContext.Current.CancellationToken);

        await _storage.DeletePrefixAsync(StorageKeys.BookPrefix(first), TestContext.Current.CancellationToken);

        Directory.Exists(Path.Combine(_root, "books", first.ToString("D"))).ShouldBeFalse();
        (await _storage.ExistsAsync(kept, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Opening_a_missing_key_throws_file_not_found()
    {
        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync(StorageKeys.Output(Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    private async Task<string> ReadAsync(string key)
    {
        await using var stream = await _storage.OpenReadAsync(key, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Delivers a few bytes, then fails like a dropped upload.</summary>
    private sealed class FailingStream : MemoryStream
    {
        private bool _sent;

        public FailingStream()
            : base([1, 2, 3])
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent)
            {
                throw new IOException("Bağlantı koptu.");
            }

            _sent = true;
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
