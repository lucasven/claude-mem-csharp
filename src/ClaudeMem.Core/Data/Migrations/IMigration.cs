namespace ClaudeMem.Core.Data.Migrations;

public interface IMigration
{
    int Version { get; }
    string Name { get; }
    void Up(Microsoft.Data.Sqlite.SqliteConnection connection);
}
