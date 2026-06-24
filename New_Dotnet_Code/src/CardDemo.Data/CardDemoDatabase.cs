using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Owns the single SQLite database that backs all CardDemo data stores. Each VSAM file becomes a
/// table whose rows hold the byte-exact fixed-length record image (BLOB) plus the primary key and any
/// alternate key as separate BLOB columns for indexing.
/// </summary>
public sealed class CardDemoDatabase : IDisposable
{
    /// <summary>The open SQLite connection (kept open so an in-memory database persists).</summary>
    public SqliteConnection Connection { get; }

    /// <summary>Creates/opens the database. Defaults to a private in-memory database.</summary>
    public CardDemoDatabase(string connectionString = "Data Source=:memory:")
    {
        Connection = new SqliteConnection(connectionString);
        Connection.Open();
    }

    /// <summary>Creates the table (and alternate index) for a file definition and returns its accessor.</summary>
    public VsamFile DefineFile(VsamFileDefinition def)
    {
        string name = def.Name;
        Execute($"""
            CREATE TABLE IF NOT EXISTS "{name}" (
                pk    BLOB NOT NULL PRIMARY KEY,
                image BLOB NOT NULL,
                ak    BLOB
            ) WITHOUT ROWID;
            """);

        if (def.AlternateKey is not null)
            Execute($"CREATE INDEX IF NOT EXISTS \"{name}_ak\" ON \"{name}\"(ak, pk);");

        return new VsamFile(Connection, def);
    }

    /// <summary>Creates the table for a QSAM sequential file and returns its accessor.</summary>
    public SequentialFile DefineSequentialFile(string name, int recordLength)
    {
        Execute($"""
            CREATE TABLE IF NOT EXISTS "{name}" (
                seq   INTEGER PRIMARY KEY,
                image BLOB NOT NULL
            );
            """);
        return new SequentialFile(Connection, name, recordLength);
    }

    private void Execute(string sql)
    {
        using SqliteCommand cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
