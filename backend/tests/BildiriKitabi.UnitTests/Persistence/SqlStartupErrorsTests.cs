using BildiriKitabi.Infrastructure.Persistence;

namespace BildiriKitabi.UnitTests.Persistence;

public sealed class SqlStartupErrorsTests
{
    [Theory]
    [InlineData(new[] { 904 }, 16)]           // database cannot be autostarted during startup
    [InlineData(new[] { 922 }, 14)]           // database is being recovered
    [InlineData(new[] { 4060, 18456 }, 14)]   // database not open yet: the pair wins over the failed login
    [InlineData(new[] { 6005 }, 14)]          // shutdown in progress
    [InlineData(new[] { 18401 }, 14)]         // script upgrade mode
    [InlineData(new[] { 11001 }, 20)]         // host name not resolvable yet
    [InlineData(new[] { 10061 }, 20)]         // connection refused
    [InlineData(new[] { 0 }, 20)]             // any connection-level failure (severity 20+)
    [InlineData(new[] { 233 }, 16)]           // no process on the other end
    [InlineData(new[] { -2 }, 11)]            // client-side timeout
    [InlineData(new[] { 40613 }, 17)]         // Azure SQL database not available
    public void Startup_and_connection_errors_are_transient(int[] numbers, byte severity)
    {
        SqlStartupErrors.IsTransient(numbers, severity).ShouldBeTrue();
    }

    [Theory]
    [InlineData(new[] { 18456 }, 14)]         // wrong password
    [InlineData(new[] { 18456 }, 20)]         // wrong password, whatever the severity
    [InlineData(new[] { 18456, 233 }, 16)]    // failed login with a network error: still the credentials
    [InlineData(new[] { 208 }, 16)]           // invalid object name (a migration error)
    [InlineData(new[] { 1801 }, 16)]          // database already exists
    [InlineData(new[] { 262 }, 14)]           // permission denied
    [InlineData(new[] { 1205 }, 13)]          // deadlock: not a startup error
    public void Failed_logins_and_sql_errors_are_permanent(int[] numbers, byte severity)
    {
        SqlStartupErrors.IsTransient(numbers, severity).ShouldBeFalse();
    }
}
