using Microsoft.Data.Sqlite;
using ClaudeMem.Core.Data.Migrations;

namespace ClaudeMem.Core.Data;

public class ClaudeMemDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public ClaudeMemDatabase(string connectionString = "")
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            var dataDir = GetDataDirectory();
            Directory.CreateDirectory(dataDir);
            connectionString = $"Data Source={Path.Combine(dataDir, "claude-mem-csharp.db")}";
        }
        else if (connectionString == ":memory:")
        {
            connectionString = "Data Source=:memory:";
        }

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        ConfigurePragmas();
        RunMigrations();
    }

    public SqliteConnection Connection => _connection;

    private void ConfigurePragmas()
    {
        ExecuteNonQuery("PRAGMA journal_mode = WAL");
        ExecuteNonQuery("PRAGMA synchronous = NORMAL");
        ExecuteNonQuery("PRAGMA foreign_keys = ON");
        ExecuteNonQuery("PRAGMA temp_store = memory");
        ExecuteNonQuery("PRAGMA mmap_size = 268435456");
        ExecuteNonQuery("PRAGMA cache_size = 10000");
    }

    private void RunMigrations()
    {
        var runner = new MigrationRunner(_connection);
        runner.RunAll();
    }

    public List<string> GetTableNames()
    {
        var tables = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    public string GetPragma(string pragmaName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {pragmaName}";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private void ExecuteNonQuery(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string GetDataDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude-mem-csharp");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
