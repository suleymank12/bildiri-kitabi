using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BildiriKitabi.Api.Contracts;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Tests.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>
/// The real API (all middleware, EF Core, the background worker) against a test database and a temporary storage
/// folder. Several hosts can share one database and folder to simulate an application restart.
/// </summary>
public sealed class ApiHost : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly Action<IServiceCollection>? _configureServices;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public ApiHost(
        string connectionString,
        string dataRoot,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ConnectionString = connectionString;
        DataRoot = dataRoot;
        _configureServices = configureServices;
        _settings = settings ?? new Dictionary<string, string>();
    }

    public string ConnectionString { get; }

    public string DataRoot { get; }

    public string StorageRoot => Path.Combine(DataRoot, "storage");

    public string UploadTempRoot => Path.Combine(DataRoot, "uploads");

    public static string NewDataRoot() => Path.Combine(Path.GetTempPath(), "bk-api-tests", Guid.NewGuid().ToString("N"));

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Storage:RootPath", StorageRoot);
        builder.UseSetting("Upload:TempPath", UploadTempRoot);
        builder.UseSetting("HttpsRedirection:Enabled", "false");
        builder.UseSetting("RateLimiting:UploadPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:GeneratePermitsPerMinute", "1000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (_configureServices is not null)
        {
            builder.ConfigureTestServices(_configureServices);
        }
    }

    /// <summary>The ten sample papers as a multipart body, in file name order unless another order is given.</summary>
    public static MultipartFormDataContent SampleUpload(string name, IEnumerable<string>? paths = null)
    {
        var content = new MultipartFormDataContent { { new StringContent(name), "name" } };
        foreach (var path in paths ?? TestPaths.PaperFiles)
        {
            AddFile(content, Path.GetFileName(path), File.ReadAllBytes(path));
        }

        return content;
    }

    public static void AddFile(MultipartFormDataContent content, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(file, "files", fileName);
    }

    public async Task<BookDetailDto> CreateSampleBookAsync(string name = "Örnek Bilim Kongresi 2026")
    {
        using var client = Client();
        using var upload = SampleUpload(name);
        using var response = await client.PostAsync(new Uri("/api/books", UriKind.Relative), upload, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<BookDetailDto>(Json, TestContext.Current.CancellationToken))!;
    }

    public async Task<BookDetailDto> GetBookAsync(Guid id)
    {
        using var client = Client();
        return (await client.GetFromJsonAsync<BookDetailDto>(new Uri($"/api/books/{id}", UriKind.Relative), Json, TestContext.Current.CancellationToken))!;
    }

    public async Task<HttpStatusCode> GenerateAsync(Guid id)
    {
        using var client = Client();
        using var response = await client.PostAsync(new Uri($"/api/books/{id}/generate", UriKind.Relative), null, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>Polls the book until it reaches one of the final states or the time limit passes.</summary>
    public async Task<BookDetailDto> WaitForFinalStatusAsync(Guid id, int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var book = await GetBookAsync(id);
            if (book.Status is BookStatus.Completed or BookStatus.Failed)
            {
                return book;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Kitap {timeoutSeconds} sn içinde bitmedi; son durum {book.Status} {book.Stage} %{book.ProgressPercent}.");
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> read, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public IReadOnlyList<string> StoredFiles(Guid? bookId = null)
    {
        var folder = bookId is { } id ? Path.Combine(StorageRoot, "books", id.ToString("D")) : StorageRoot;
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : [];
    }
}
