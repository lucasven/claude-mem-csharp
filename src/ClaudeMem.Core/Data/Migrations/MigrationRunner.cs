using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Data.Migrations;

public class MigrationRunner
{
    private readonly SqliteConnection _connection;
    private readonly List<IMigration> _migrations;

    public MigrationRunner(SqliteConnection connection)
    {
        _connection = connection;
        _migrations =
        [
            new Migration001_InitialSchema(),
            new Migration002_FTS5Search()
        ];
    }

    public void RunAll()
    {
        var appliedVersions = GetAppliedVersions();

        foreach (var migration in _migrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version))
                continue;

            migration.Up(_connection);
            RecordMigration(migration);
        }
    }

    private HashSet<int> GetAppliedVersions()
    {
        var versions = new HashSet<int>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_versions'";
        if (cmd.ExecuteScalar() == null)
            return versions;

        cmd.CommandText = "SELECT version FROM schema_versions";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }
        return versions;
    }

    private void RecordMigration(IMigration migration)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schema_versions (version, name, applied_at)
            VALUES (@version, @name, @appliedAt)
            """;
        cmd.Parameters.AddWithValue("@version", migration.Version);
        cmd.Parameters.AddWithValue("@name", migration.Name);
        cmd.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}
