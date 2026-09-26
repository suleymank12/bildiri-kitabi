using System.Globalization;
using Microsoft.Data.SqlClient;

namespace BildiriKitabi.Infrastructure.Persistence;

/// <summary>Tells SQL Server errors worth waiting out at startup from permanent ones.</summary>
public static class SqlStartupErrors
{
    private const int LoginFailed = 18456;

    /// <summary>Severity 20 and above: the connection could not be made or was broken (server down, unreachable).</summary>
    private const byte ConnectionFailureSeverity = 20;

    // SqlException.IsTransient is not implemented by Microsoft.Data.SqlClient (it always returns false), so the error
    // numbers that matter at startup are listed here explicitly.

    /// <summary>The server or the database is still opening. Transient even when a failed login (18456) comes with them.</summary>
    private static readonly HashSet<int> OpeningErrorNumbers =
    [
        904,   // Database cannot be autostarted during server shutdown or startup.
        922,   // Database is being recovered; waiting until recovery is finished.
        4060,  // Cannot open the requested database (arrives together with 18456 while the database is not open yet).
        6005,  // SHUTDOWN is in progress.
        18401, // Login failed: server is in script upgrade mode.
    ];

    /// <summary>Network and service errors. Transient unless a failed login (18456) says the credentials are wrong.</summary>
    private static readonly HashSet<int> ConnectionErrorNumbers =
    [
        -2,    // Client-side timeout (connect or command).
        64,    // Connection established, then the network name was no longer available during login.
        121,   // Semaphore timeout on the network connection.
        233,   // Connection established, then no process on the other end (server shutting down or not ready).
        10053, // Transport-level error: connection aborted by the local host.
        10054, // Transport-level error: connection reset by the remote host.
        10060, // Connection attempt timed out.
        10061, // Connection refused: the server is not listening yet.
        11001, // Host name could not be resolved (the container is not on the network yet).
        40197, // Azure SQL: service error while processing the request.
        40501, // Azure SQL: service is busy.
        40613, // Azure SQL: database is not currently available.
        49918, // Azure SQL: not enough resources to process the request.
        49919, // Azure SQL: too many create or update operations in progress.
        49920, // Azure SQL: too many operations in progress.
    ];

    /// <summary>
    /// A short description (error numbers only, no message text) when <paramref name="exception"/> is transient,
    /// otherwise <c>null</c>. A failed login on its own (wrong password) and SQL errors in the migration itself are
    /// permanent.
    /// </summary>
    public static string? TransientReason(Exception exception)
    {
        switch (exception)
        {
            case TimeoutException:
                return "timeout";
            case SqlException sql:
                var numbers = sql.Errors.Cast<SqlError>().Select(error => error.Number).ToList();
                return IsTransient(numbers, sql.Class)
                    ? "SQL error " + string.Join(", ", numbers.Select(n => n.ToString(CultureInfo.InvariantCulture)))
                    : null;
            default:
                return null;
        }
    }

    /// <param name="errorNumbers">The numbers of every error in the exception.</param>
    /// <param name="severity">The highest severity (class) among them.</param>
    public static bool IsTransient(IReadOnlyCollection<int> errorNumbers, byte severity)
    {
        ArgumentNullException.ThrowIfNull(errorNumbers);

        if (errorNumbers.Any(OpeningErrorNumbers.Contains))
        {
            return true;
        }

        if (errorNumbers.Contains(LoginFailed))
        {
            return false;
        }

        return severity >= ConnectionFailureSeverity || errorNumbers.Any(ConnectionErrorNumbers.Contains);
    }
}
