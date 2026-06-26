using System.Reflection;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Owns the single open <see cref="SqliteConnection"/> that backs the relational re-architecture
/// (one table per logical COBOL file, one column per elementary field — see
/// <c>_design/ARCHITECTURE.md</c>). On construction it opens the connection and runs the authoritative
/// DDL (<c>_design/schema.sql</c>, embedded as a resource) so every base and optional-module table
/// exists. The repositories in this assembly issue plain parameterized SQL against
/// <see cref="Connection"/>; there is no EF Core anywhere.
/// </summary>
/// <remarks>
/// For an in-memory database the connection must stay open for the data to live: pass
/// <c>"Data Source=:memory:"</c> for a private DB, or the shared-cache form
/// <c>"Data Source=file:carddemo?mode=memory&amp;cache=shared"</c> if several connections must see the
/// same in-memory tables. A file path (e.g. <c>"Data Source=carddemo.db"</c>) gives a durable DB.
/// </remarks>
public sealed class RelationalDb : IDisposable
{
    /// <summary>The default private in-memory connection string.</summary>
    public const string InMemory = "Data Source=:memory:";

    /// <summary>A shared-cache in-memory connection string (multiple connections see the same tables).</summary>
    public const string InMemoryShared = "Data Source=file:carddemo?mode=memory&cache=shared";

    /// <summary>The open SQLite connection. Kept open for the lifetime of this instance.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>
    /// Opens the connection and creates the schema by executing the embedded <c>schema.sql</c>.
    /// </summary>
    /// <param name="connectionString">
    /// SQLite connection string. Defaults to a private in-memory database (<see cref="InMemory"/>).
    /// </param>
    public RelationalDb(string connectionString = InMemory)
    {
        Connection = new SqliteConnection(connectionString);
        Connection.Open();
        ExecuteScript(LoadEmbeddedSchema());
    }

    /// <summary>
    /// Opens the connection and creates the schema from a caller-supplied DDL script (useful for tests
    /// that want to point at an on-disk <c>schema.sql</c> rather than the embedded copy).
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <param name="schemaSql">The full DDL script to execute after opening.</param>
    public RelationalDb(string connectionString, string schemaSql)
    {
        Connection = new SqliteConnection(connectionString);
        Connection.Open();
        ExecuteScript(schemaSql);
    }

    /// <summary>
    /// Runs an arbitrary SQL script (one or more statements) on the open connection. The whole script
    /// is sent as a single command, matching how <c>schema.sql</c> is applied.
    /// </summary>
    public void ExecuteScript(string sql)
    {
        using SqliteCommand cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes every row from every base and optional-module table, leaving the schema intact. Useful
    /// for resetting state between tests or seeding runs (equivalent to OPEN OUTPUT on each file).
    /// </summary>
    public void Clear()
    {
        // Order respects the single FK (TRANSACTION_TYPE_CATEGORY -> TRANSACTION_TYPE): child first.
        string[] tables =
        {
            "ACCOUNT", "CARD", "CARD_XREF", "CUSTOMER", "\"TRANSACTION\"", "DAILY_TRANSACTION",
            "TRAN_CAT_BAL", "DISCLOSURE_GROUP", "TRAN_TYPE", "TRAN_CATEGORY", "USER_SECURITY",
            "TRANSACTION_TYPE_CATEGORY", "TRANSACTION_TYPE", "AUTHFRDS",
        };
        using SqliteTransaction tx = Connection.BeginTransaction();
        foreach (string t in tables)
        {
            using SqliteCommand cmd = Connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {t};";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Begins a transaction on the connection. Wrap a unit of work and commit/rollback on the returned
    /// object; repositories created over <see cref="Connection"/> will participate automatically once
    /// the transaction is current (their commands are assigned the connection's pending transaction).
    /// </summary>
    public SqliteTransaction BeginTransaction() => Connection.BeginTransaction();

    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction, committing on success and rolling back if it
    /// throws. The transaction is passed in so callers can attach it to repository commands if needed.
    /// </summary>
    public void InTransaction(Action<SqliteTransaction> work)
    {
        using SqliteTransaction tx = Connection.BeginTransaction();
        try
        {
            work(tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Loads the embedded <c>schema.sql</c> resource as text.</summary>
    private static string LoadEmbeddedSchema()
    {
        Assembly asm = typeof(RelationalDb).Assembly;
        const string name = "CardDemo.Data.schema.sql";
        using Stream? stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '{name}' was not found in {asm.FullName}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => Connection.Dispose();
}
