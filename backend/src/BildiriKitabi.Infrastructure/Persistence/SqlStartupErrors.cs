using System.Globalization;
using Microsoft.Data.SqlClient;

namespace BildiriKitabi.Infrastructure.Persistence;

/// <summary>Tells SQL Server errors worth waiting out at startup from permanent ones.</summary>
public static class SqlStartupErrors
{
    private const int LoginFailed = 18456;

    // Not covered by EF Core's transient list, but expected while SQL Server starts or restarts:
    // 904 database cannot be autostarted during startup, 922 database is being recovered,
    // 4060 cannot open the requested database (arrives together with 18456), 6005 shutdown in progress,
    // 18401 server in script upgrade mode.
    private static readonly HashSet<int> StartupErrorNumbers = [904, 922, 4060, 6005, 18401];

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
                var transient = numbers.Any(StartupErrorNumbers.Contains)
                    || (!numbers.Contains(LoginFailed) && (IsConnectionFailure(sql) || IsEfCoreTransient(sql)));
                return transient
                    ? "SQL error " + string.Join(", ", numbers.Select(n => n.ToString(CultureInfo.InvariantCulture)))
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Severity 20 and above: the connection could not be made or was broken (server down, unreachable).</summary>
    private static bool IsConnectionFailure(SqlException exception) => exception.Class >= 20;

#pragma warning disable EF1001 // Internal EF Core API: the provider's own list of transient SQL Server errors.
    private static bool IsEfCoreTransient(SqlException exception) =>
        Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal.SqlServerTransientExceptionDetector.ShouldRetryOn(exception);
#pragma warning restore EF1001
}
