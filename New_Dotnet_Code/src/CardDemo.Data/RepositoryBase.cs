using CardDemo.Runtime;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Shared scaffolding for the per-table relational repositories. Each repository maps a base table to a
/// <see cref="CardDemo.Domain"/> entity and exposes the VSAM-semantics operations CardDemo programs use,
/// every one returning the exact two-character <see cref="FileStatus"/> the COBOL code branches on:
/// <list type="bullet">
///   <item>READ key      -> <see cref="FileStatus.Ok"/> '00' / <see cref="FileStatus.RecordNotFound"/> '23'.</item>
///   <item>WRITE         -> '00' / <see cref="FileStatus.DuplicateKeyError"/> '22' on PK conflict.</item>
///   <item>REWRITE       -> '00' / '23' if the row is absent.</item>
///   <item>DELETE        -> '00' / '23' if the row is absent.</item>
///   <item>STARTBR + READNEXT/READPREV -> ordered forward/reverse cursor; '00' / <see cref="FileStatus.EndOfFile"/> '10'.</item>
/// </list>
/// Browse position is held on the instance, mirroring a COBOL FD. Text keys are ordered with BINARY
/// (ordinal) collation so the SQL browse order matches the EBCDIC/ASCII byte order CardDemo relies on.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>The open connection shared with <see cref="RelationalDb"/>.</summary>
    protected SqliteConnection Connection { get; }

    /// <summary>The SQLite constraint-violation error code (SQLITE_CONSTRAINT) used to detect duplicate keys.</summary>
    protected const int SqliteConstraint = 19;

    // Browse (STARTBR / READNEXT / READPREV) cursor state, keyed by the entity's primary-key value(s).
    private object?[]? _browseStartKey;
    private object?[]? _browseLastKey;
    private bool _browseInclusive;
    private bool _browsePrimed;

    /// <summary>Creates a repository over the given <see cref="RelationalDb"/>'s connection.</summary>
    protected RepositoryBase(RelationalDb db) => Connection = db.Connection;

    /// <summary>Creates a repository directly over an open connection.</summary>
    protected RepositoryBase(SqliteConnection connection) => Connection = connection;

    /// <summary>Creates a command on the shared connection, attaching the connection's current transaction (if any).</summary>
    protected SqliteCommand Cmd(string sql)
    {
        SqliteCommand c = Connection.CreateCommand();
        c.CommandText = sql;
        return c;
    }

    // ---- Browse cursor primitives (used by the concrete repositories) --------------------------------

    /// <summary>Positions a browse at-or-after the given key parts; null/empty starts at the first row.</summary>
    protected void StartBrowseAt(params object?[] keyParts)
    {
        _browseStartKey = keyParts.Length == 0 ? null : keyParts;
        _browseLastKey = null;
        _browseInclusive = true;
        _browsePrimed = false;
    }

    /// <summary>Positions a browse strictly after the given key parts (first READNEXT returns the next row).</summary>
    protected void StartBrowseAfter(params object?[] keyParts)
    {
        _browseStartKey = keyParts.Length == 0 ? null : keyParts;
        _browseLastKey = null;
        _browseInclusive = false;
        _browsePrimed = false;
    }

    /// <summary>Ends the current browse, clearing all cursor state.</summary>
    protected void EndBrowseCore()
    {
        _browseStartKey = null;
        _browseLastKey = null;
        _browsePrimed = false;
    }

    /// <summary>
    /// Advances the browse one row in <paramref name="forward"/> (ASC) or reverse (DESC) order and maps it
    /// via <paramref name="map"/>. Returns '00' with the row, or '10' at end of file. The key columns are
    /// compared as a lexicographic tuple so composite primary keys browse correctly.
    /// </summary>
    protected string Advance<TEntity>(
        bool forward,
        string selectColumns,
        string table,
        string[] keyColumns,
        Func<SqliteDataReader, TEntity> map,
        out TEntity? entity)
        where TEntity : class
    {
        string order = forward ? "ASC" : "DESC";
        string orderBy = string.Join(", ", keyColumns.Select(k => $"{k} {order}"));

        SqliteCommand c = Cmd("");
        try
        {
            if (!_browsePrimed)
            {
                if (_browseStartKey is null)
                {
                    c.CommandText = $"SELECT {selectColumns} FROM {table} ORDER BY {orderBy} LIMIT 1";
                }
                else
                {
                    // First step: at-or-after (>=/<=) for an inclusive start, else strictly after (>/<).
                    // A partial start key (fewer parts than the composite PK) compares only the prefix
                    // columns supplied, so "browse from this account" positions at its first category.
                    string op = forward
                        ? (_browseInclusive ? ">=" : ">")
                        : (_browseInclusive ? "<=" : "<");
                    int n = _browseStartKey.Length;
                    c.CommandText =
                        $"SELECT {selectColumns} FROM {table} WHERE {KeyTuplePredicate(keyColumns, op, n)} " +
                        $"ORDER BY {orderBy} LIMIT 1";
                    BindKey(c, _browseStartKey, n);
                }
            }
            else
            {
                // Continuation always uses the full extracted key (strictly past the last row read).
                string op = forward ? ">" : "<";
                int n = keyColumns.Length;
                c.CommandText =
                    $"SELECT {selectColumns} FROM {table} WHERE {KeyTuplePredicate(keyColumns, op, n)} " +
                    $"ORDER BY {orderBy} LIMIT 1";
                BindKey(c, _browseLastKey!, n);
            }

            using SqliteDataReader rd = c.ExecuteReader();
            if (rd.Read())
            {
                entity = map(rd);
                _browseLastKey = ExtractKey(rd, keyColumns);
                _browsePrimed = true;
                return FileStatus.Ok;
            }
        }
        finally
        {
            c.Dispose();
        }

        entity = null;
        return FileStatus.EndOfFile;
    }

    /// <summary>
    /// Builds a row-value tuple comparison predicate over the first <paramref name="n"/> key columns, e.g.
    /// <c>(a, b, c) &gt; (@k0, @k1, @k2)</c>, so multi-column keys compare lexicographically. SQLite compares
    /// row values left-to-right, which is exactly the COBOL key ordering. A single column collapses to a
    /// plain <c>a &gt; @k0</c>. When <paramref name="n"/> is less than the full key, only that prefix is
    /// compared (partial-key positioning).
    /// </summary>
    private static string KeyTuplePredicate(string[] keyColumns, string op, int n)
    {
        if (n == 1)
            return $"{keyColumns[0]} {op} @k0";
        string cols = string.Join(", ", keyColumns.Take(n));
        string ps = string.Join(", ", Enumerable.Range(0, n).Select(i => $"@k{i}"));
        return $"({cols}) {op} ({ps})";
    }

    private static void BindKey(SqliteCommand c, object?[] key, int n)
    {
        for (int i = 0; i < n; i++)
            c.Parameters.AddWithValue($"@k{i}", key[i] ?? DBNull.Value);
    }

    private static object?[] ExtractKey(SqliteDataReader rd, string[] keyColumns)
    {
        var key = new object?[keyColumns.Length];
        for (int i = 0; i < keyColumns.Length; i++)
            key[i] = rd[keyColumns[i]];
        return key;
    }
}

/// <summary>Reader helpers that read SQLite columns into the CLR types the entities use.</summary>
internal static class ReaderExtensions
{
    /// <summary>Reads a NUMERIC money column as an exact <see cref="decimal"/> (never via floating point).</summary>
    public static decimal GetMoney(this SqliteDataReader rd, string column)
        => rd.GetDecimal(rd.GetOrdinal(column));

    /// <summary>Reads a TEXT column, treating SQL NULL as an empty string (columns are NOT NULL in the schema).</summary>
    public static string GetText(this SqliteDataReader rd, string column)
    {
        int o = rd.GetOrdinal(column);
        return rd.IsDBNull(o) ? string.Empty : rd.GetString(o);
    }

    /// <summary>Reads an INTEGER column as <see cref="long"/>.</summary>
    public static long GetInt64(this SqliteDataReader rd, string column)
        => rd.GetInt64(rd.GetOrdinal(column));

    /// <summary>Reads an INTEGER column as <see cref="int"/>.</summary>
    public static int GetInt32(this SqliteDataReader rd, string column)
        => rd.GetInt32(rd.GetOrdinal(column));

    /// <summary>
    /// Reads a NUMERIC money column as an exact <see cref="decimal"/>, treating SQL NULL as 0. The
    /// optional DB2/IMS tables declare most amount columns nullable, so a NULL must not throw.
    /// </summary>
    public static decimal GetMoneyOrZero(this SqliteDataReader rd, string column)
    {
        int o = rd.GetOrdinal(column);
        return rd.IsDBNull(o) ? 0m : rd.GetDecimal(o);
    }

    /// <summary>Reads an INTEGER column as <see cref="int"/>, treating SQL NULL as 0.</summary>
    public static int GetInt32OrZero(this SqliteDataReader rd, string column)
    {
        int o = rd.GetOrdinal(column);
        return rd.IsDBNull(o) ? 0 : rd.GetInt32(o);
    }

    /// <summary>Reads an INTEGER column as <see cref="long"/>, treating SQL NULL as 0.</summary>
    public static long GetInt64OrZero(this SqliteDataReader rd, string column)
    {
        int o = rd.GetOrdinal(column);
        return rd.IsDBNull(o) ? 0L : rd.GetInt64(o);
    }
}
