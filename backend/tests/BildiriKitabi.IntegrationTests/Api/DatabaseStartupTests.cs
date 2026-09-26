using BildiriKitabi.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>Real SQL Server errors, classified the way the startup migration retry sees them.</summary>
public sealed class DatabaseStartupTests(SqlServerFixture sql)
{
    [Fact]
    public void Timeouts_are_transient_and_other_errors_are_not()
    {
        SqlStartupErrors.TransientReason(new TimeoutException()).ShouldBe("timeout");
        SqlStartupErrors.TransientReason(new InvalidOperationException()).ShouldBeNull();
    }

    [Fact]
    public async Task An_unreachable_server_is_transient()
    {
        // Port 1 on the loopback interface: nothing listens there, the connection is refused.
        var error = await OpenAsync("Server=tcp:127.0.0.1,1;User Id=sa;Password=unused;Connect Timeout=3;ConnectRetryCount=0;Encrypt=False");

        SqlStartupErrors.TransientReason(error).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_wrong_password_is_permanent()
    {
        var error = await OpenAsync(WithWrongPassword(sql.NewDatabase()));

        error.Errors.Cast<SqlError>().Select(e => e.Number).ShouldContain(18456);
        SqlStartupErrors.TransientReason(error).ShouldBeNull();
    }

    [Fact]
    public async Task A_database_that_cannot_be_opened_yet_is_transient()
    {
        // A database that does not exist (yet) gives the same error pair as one that is still being recovered.
        var error = await OpenAsync(sql.NewDatabase());

        SqlStartupErrors.TransientReason(error).ShouldNotBeNull().ShouldContain("4060");
    }

    [Fact]
    public async Task The_startup_migration_creates_and_migrates_a_new_database()
    {
        var connectionString = sql.NewDatabase();
        await using var db = NewContext(connectionString);

        await DatabaseStartup.MigrateAsync(db.Database, new StartupRetry(new FakeTimeProvider(), NullLogger.Instance), TestContext.Current.CancellationToken);

        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_startup_migration_fails_at_once_on_a_wrong_password()
    {
        await using var db = NewContext(WithWrongPassword(sql.NewDatabase()));

        // A fake clock that nobody advances: any retry would wait forever and hit the timeout below.
        var migrate = DatabaseStartup.MigrateAsync(db.Database, new StartupRetry(new FakeTimeProvider(), NullLogger.Instance), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<SqlException>(migrate.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    private static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

    private static string WithWrongPassword(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { Password = "Yanlis-Parola-1" }.ConnectionString;

    private static async Task<SqlException> OpenAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        return await Should.ThrowAsync<SqlException>(connection.OpenAsync(TestContext.Current.CancellationToken));
    }
}
