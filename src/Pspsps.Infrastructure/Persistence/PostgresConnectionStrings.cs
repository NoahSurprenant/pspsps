using System.Data.Common;

namespace Pspsps.Infrastructure.Persistence;

/// <summary>The defaults the app's PostgreSQL connection strings get.</summary>
public static class PostgresConnectionStrings
{
    /// <summary>
    /// How long a connect waits, unless the connection string sets Timeout: Npgsql's own default is 15 s. On a
    /// network that drops packets every connect waits this long, so a short one keeps the startup migration's retries
    /// inside the server's startup probe.
    /// </summary>
    public const int ConnectTimeoutSeconds = 5;

    private static readonly string[] TimeoutKeywords = ["Timeout", "Connect Timeout", "Connection Timeout"];

    /// <summary>
    /// <paramref name="connectionString"/> with <see cref="ConnectTimeoutSeconds"/>, unless it sets its own timeout.
    /// </summary>
    public static string WithConnectDefaults(string connectionString)
    {
        // The plain builder holds only the keywords given (Npgsql's reports every keyword it knows as present).
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (!TimeoutKeywords.Any(builder.ContainsKey))
        {
            builder["Timeout"] = ConnectTimeoutSeconds;
        }

        return builder.ConnectionString;
    }
}
