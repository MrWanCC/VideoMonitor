using Microsoft.Data.Sqlite;
using VideoMonitor.Infrastructure.Paths;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(IAppPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 5
        };

        connectionString = builder.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(connectionString);
    }
}
