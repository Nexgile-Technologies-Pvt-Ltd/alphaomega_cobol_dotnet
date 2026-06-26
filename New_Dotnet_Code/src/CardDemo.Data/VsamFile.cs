using CardDemo.Runtime;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// A VSAM KSDS file backed by a SQLite table, exposing the record operations CardDemo programs use,
/// each returning the exact two-character FILE STATUS the COBOL code branches on. Stored images are
/// byte-exact; reads return the original bytes unchanged. Browse position is held on the instance,
/// mirroring a COBOL FD.
/// </summary>
public sealed class VsamFile
{
    private readonly SqliteConnection _conn;
    private readonly VsamFileDefinition _def;
    private readonly string _t;

    // Browse (STARTBR/READNEXT/READPREV) state.
    private byte[]? _browseStartKey;
    private byte[]? _browseLastKey;
    private bool _browsePrimed;

    internal VsamFile(SqliteConnection conn, VsamFileDefinition def)
    {
        _conn = conn;
        _def = def;
        _t = def.Name;
    }

    /// <summary>Random read by primary key. Returns "00" (found) or "23" (not found).</summary>
    public string Read(byte[] key, out byte[]? image)
    {
        using SqliteCommand c = Command($"SELECT image FROM \"{_t}\" WHERE pk = @k");
        c.Parameters.AddWithValue("@k", key);
        if (c.ExecuteScalar() is byte[] b) { image = b; return FileStatus.Ok; }
        image = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Random read via the alternate index, returning the first matching record by primary key.</summary>
    public string ReadByAlternateKey(byte[] alternateKey, out byte[]? image)
    {
        RequireAlternateIndex();
        using SqliteCommand c = Command($"SELECT image FROM \"{_t}\" WHERE ak = @a ORDER BY pk LIMIT 1");
        c.Parameters.AddWithValue("@a", alternateKey);
        if (c.ExecuteScalar() is byte[] b) { image = b; return FileStatus.Ok; }
        image = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new record. Returns "00" or "22" (duplicate key).</summary>
    public string Write(byte[] image)
    {
        EnsureLength(image);
        using SqliteCommand c = Command($"INSERT INTO \"{_t}\"(pk, image, ak) VALUES(@k, @i, @a)");
        c.Parameters.AddWithValue("@k", _def.PrimaryKey.Extract(image));
        c.Parameters.AddWithValue("@i", image);
        c.Parameters.AddWithValue("@a", AltKeyValue(image));
        try
        {
            c.ExecuteNonQuery();
            return FileStatus.Ok;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (duplicate PK)
        {
            return FileStatus.DuplicateKeyError;
        }
    }

    /// <summary>Updates an existing record (matched by primary key). Returns "00" or "23".</summary>
    public string Rewrite(byte[] image)
    {
        EnsureLength(image);
        using SqliteCommand c = Command($"UPDATE \"{_t}\" SET image = @i, ak = @a WHERE pk = @k");
        c.Parameters.AddWithValue("@i", image);
        c.Parameters.AddWithValue("@a", AltKeyValue(image));
        c.Parameters.AddWithValue("@k", _def.PrimaryKey.Extract(image));
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns "00" or "23".</summary>
    public string Delete(byte[] key)
    {
        using SqliteCommand c = Command($"DELETE FROM \"{_t}\" WHERE pk = @k");
        c.Parameters.AddWithValue("@k", key);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse. A null key starts at the first record (sequential read of the file).</summary>
    public void StartBrowse(byte[]? key = null)
    {
        _browseStartKey = key;
        _browseLastKey = null;
        _browsePrimed = false;
    }

    /// <summary>Reads the next record in ascending key order. Returns "00" or "10" (end of file).</summary>
    public string ReadNext(out byte[]? image) => ReadDirectional(forward: true, out image);

    /// <summary>Reads the previous record in descending key order. Returns "00" or "10".</summary>
    public string ReadPrevious(out byte[]? image) => ReadDirectional(forward: false, out image);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse()
    {
        _browseStartKey = null;
        _browseLastKey = null;
        _browsePrimed = false;
    }

    /// <summary>Number of records currently in the file.</summary>
    public long Count()
    {
        using SqliteCommand c = Command($"SELECT COUNT(*) FROM \"{_t}\"");
        return (long)c.ExecuteScalar()!;
    }

    /// <summary>Empties the file, modelling OPEN OUTPUT on an indexed file (DISP=NEW / define-and-load).</summary>
    public void Clear()
    {
        using SqliteCommand c = Command($"DELETE FROM \"{_t}\"");
        c.ExecuteNonQuery();
    }

    private string ReadDirectional(bool forward, out byte[]? image)
    {
        string cmp;
        string order = forward ? "ASC" : "DESC";
        SqliteCommand c;

        if (!_browsePrimed)
        {
            if (_browseStartKey is null)
            {
                c = Command($"SELECT pk, image FROM \"{_t}\" ORDER BY pk {order} LIMIT 1");
            }
            else
            {
                cmp = forward ? ">=" : "<=";
                c = Command($"SELECT pk, image FROM \"{_t}\" WHERE pk {cmp} @k ORDER BY pk {order} LIMIT 1");
                c.Parameters.AddWithValue("@k", _browseStartKey);
            }
        }
        else
        {
            cmp = forward ? ">" : "<";
            c = Command($"SELECT pk, image FROM \"{_t}\" WHERE pk {cmp} @k ORDER BY pk {order} LIMIT 1");
            c.Parameters.AddWithValue("@k", _browseLastKey!);
        }

        using (c)
        using (SqliteDataReader rd = c.ExecuteReader())
        {
            if (rd.Read())
            {
                _browseLastKey = (byte[])rd.GetValue(0);
                _browsePrimed = true;
                image = (byte[])rd.GetValue(1);
                return FileStatus.Ok;
            }
        }

        image = null;
        return FileStatus.EndOfFile;
    }

    private object AltKeyValue(ReadOnlySpan<byte> image) =>
        _def.AlternateKey is { } ak ? ak.Extract(image) : DBNull.Value;

    private void EnsureLength(byte[] image)
    {
        if (image.Length != _def.RecordLength)
            throw new ArgumentException(
                $"{_t}: record length {image.Length} != defined length {_def.RecordLength}.", nameof(image));
    }

    private void RequireAlternateIndex()
    {
        if (_def.AlternateKey is null)
            throw new InvalidOperationException($"{_t} has no alternate index defined.");
    }

    private SqliteCommand Command(string sql)
    {
        SqliteCommand c = _conn.CreateCommand();
        c.CommandText = sql;
        return c;
    }
}
