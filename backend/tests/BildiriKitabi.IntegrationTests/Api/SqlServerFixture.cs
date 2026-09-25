using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(BildiriKitabi.IntegrationTests.Api.SqlServerFixture))]

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>
/// One real SQL Server (Testcontainers) for the whole test run; every test gets its own database in it.
/// Without Docker the API tests are skipped with a clear message instead of failing.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>The image can be overridden (for example on ARM machines) with <c>BK_TEST_MSSQL_IMAGE</c>.</summary>
    public static readonly string Image = Environment.GetEnvironmentVariable("BK_TEST_MSSQL_IMAGE") is { Length: > 0 } image ? image : DefaultImage;

    private MsSqlContainer? _container;
    private string? _unavailableReason;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder(Image).Build();
            await _container.StartAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _unavailableReason =
                $"API entegrasyon testleri atlandı: Docker veya SQL Server konteyneri ({Image}) kullanılamıyor. " +
                $"Docker'ı başlatıp testleri yeniden çalıştırın. Ayrıntı: {ex.GetType().Name}: {ex.Message}";
            _container = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public bool IsAvailable => _unavailableReason is null;

    /// <summary>Skips the calling test when no SQL Server container could be started.</summary>
    public void EnsureAvailable()
    {
        if (_unavailableReason is not null)
        {
            Assert.Skip(_unavailableReason);
        }
    }

    /// <summary>A connection string to a new, not yet created database (the API migrates it on startup).</summary>
    public string NewDatabase()
    {
        EnsureAvailable();
        return new SqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            InitialCatalog = $"bk_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
    }
}
