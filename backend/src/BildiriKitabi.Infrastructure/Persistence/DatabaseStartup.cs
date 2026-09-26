using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Infrastructure.Persistence;

/// <summary>
/// Startup migration that waits, for a bounded time, until SQL Server accepts connections and the application
/// database has finished opening. After a container or machine restart the API can start before SQL Server has
/// recovered its databases. Only this call retries; the DbContext has no execution strategy, so the explicit
/// transactions elsewhere are unaffected.
/// </summary>
public static class DatabaseStartup
{
    public static async Task MigrateAsync(DatabaseFacade database, StartupRetry retry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(retry);

        var connectionString = database.GetConnectionString()
            ?? throw new InvalidOperationException("The database connection string is not configured.");

        await retry.RunAsync(
            "Database migration",
            async token =>
            {
                await WaitUntilOpenAsync(connectionString, token).ConfigureAwait(false);
                await database.MigrateAsync(token).ConfigureAwait(false);
            },
            SqlStartupErrors.TransientReason,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to <c>master</c> and reads the application database if it exists. A database that is still being
    /// recovered fails here with a transient error (904, 922 …); a missing one passes and the migration creates it.
    /// Without this, EF Core would take the "cannot open database" error of a recovering database for a missing
    /// database and try to create it.
    /// </summary>
    private static async Task WaitUntilOpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        var connection = new SqlConnection(builder.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(databaseName))
            {
                return;
            }

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = """
                    IF DB_ID(@database) IS NOT NULL
                    BEGIN
                        DECLARE @probe nvarchar(400) = N'SELECT TOP (0) 1 FROM ' + QUOTENAME(@database) + N'.sys.objects';
                        EXEC sp_executesql @probe;
                    END
                    """;
                command.Parameters.Add(new SqlParameter("@database", System.Data.SqlDbType.NVarChar, 128) { Value = databaseName });
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
