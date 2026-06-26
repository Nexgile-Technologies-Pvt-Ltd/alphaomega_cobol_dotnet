using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the TRAN_TYPE table (copybook CVTRA03Y). Primary key <c>tran_type</c>.
/// Exposes the VSAM-semantics operations over <see cref="TranType"/>.
/// </summary>
public sealed class TranTypeRepository : RepositoryBase
{
    private const string Table = "TRAN_TYPE";
    private const string Cols = "tran_type, tran_type_desc";
    private static readonly string[] KeyCols = { "tran_type" };

    public TranTypeRepository(RelationalDb db) : base(db) { }
    public TranTypeRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string tranType, out TranType? type)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE tran_type = @k");
        c.Parameters.AddWithValue("@k", tranType);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { type = Map(rd); return FileStatus.Ok; }
        type = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(TranType x)
    {
        using SqliteCommand c = Cmd($"INSERT INTO {Table} ({Cols}) VALUES (@tran_type, @tran_type_desc)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(TranType x)
    {
        using SqliteCommand c = Cmd($"UPDATE {Table} SET tran_type_desc=@tran_type_desc WHERE tran_type=@tran_type");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string tranType)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE tran_type = @k");
        c.Parameters.AddWithValue("@k", tranType);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? tranType = null)
    {
        if (tranType is null) StartBrowseAt();
        else StartBrowseAt(tranType);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string tranType) => StartBrowseAfter(tranType);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out TranType? type)
        => Advance(true, Cols, Table, KeyCols, Map, out type);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out TranType? type)
        => Advance(false, Cols, Table, KeyCols, Map, out type);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<TranType> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY tran_type ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="TranType"/>.</summary>
    private static TranType Map(SqliteDataReader rd) => new()
    {
        TranTypeCode = rd.GetText("tran_type"),
        TranTypeDesc = rd.GetText("tran_type_desc"),
    };

    /// <summary>Binds a <see cref="TranType"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, TranType x)
    {
        c.Parameters.AddWithValue("@tran_type", x.TranTypeCode);
        c.Parameters.AddWithValue("@tran_type_desc", x.TranTypeDesc);
    }
}
