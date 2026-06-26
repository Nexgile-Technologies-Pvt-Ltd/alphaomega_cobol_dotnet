using CardDemo.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the TRAN_CATEGORY table (copybook CVTRA04Y). Composite primary key
/// (<c>tran_type_cd</c>, <c>tran_cat_cd</c>). Exposes the VSAM-semantics operations over
/// <see cref="TranCategory"/>. Browse orders by the full composite key.
/// </summary>
public sealed class TranCategoryRepository : RepositoryBase
{
    private const string Table = "TRAN_CATEGORY";
    private const string Cols = "tran_type_cd, tran_cat_cd, tran_cat_type_desc";
    private static readonly string[] KeyCols = { "tran_type_cd", "tran_cat_cd" };

    public TranCategoryRepository(RelationalDb db) : base(db) { }
    public TranCategoryRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by the full composite key. Returns '00' or '23'.</summary>
    public string ReadByKey(string tranTypeCd, int tranCatCd, out TranCategory? category)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE tran_type_cd=@t AND tran_cat_cd=@c");
        c.Parameters.AddWithValue("@t", tranTypeCd);
        c.Parameters.AddWithValue("@c", tranCatCd);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { category = Map(rd); return FileStatus.Ok; }
        category = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(TranCategory x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@tran_type_cd, @tran_cat_cd, @tran_cat_type_desc)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by composite key. Returns '00' or '23'.</summary>
    public string Update(TranCategory x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET tran_cat_type_desc=@tran_cat_type_desc " +
            "WHERE tran_type_cd=@tran_type_cd AND tran_cat_cd=@tran_cat_cd");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by composite key. Returns '00' or '23'.</summary>
    public string Delete(string tranTypeCd, int tranCatCd)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE tran_type_cd=@t AND tran_cat_cd=@c");
        c.Parameters.AddWithValue("@t", tranTypeCd);
        c.Parameters.AddWithValue("@c", tranCatCd);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given (partial) composite key (null/none = first row).</summary>
    public void StartBrowse(string? tranTypeCd = null, int? tranCatCd = null)
        => StartBrowseAt(BuildKey(tranTypeCd, tranCatCd));

    /// <summary>Positions a browse strictly after the given composite key.</summary>
    public void StartBrowseAfterKey(string tranTypeCd, int tranCatCd)
        => StartBrowseAfter(tranTypeCd, tranCatCd);

    /// <summary>Reads the next row in ascending composite-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out TranCategory? category)
        => Advance(true, Cols, Table, KeyCols, Map, out category);

    /// <summary>Reads the previous row in descending composite-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out TranCategory? category)
        => Advance(false, Cols, Table, KeyCols, Map, out category);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending composite-key order.</summary>
    public IEnumerable<TranCategory> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY tran_type_cd ASC, tran_cat_cd ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Builds the composite key parameter tuple, dropping trailing unspecified parts.</summary>
    private static object?[] BuildKey(string? tranTypeCd, int? tranCatCd)
    {
        if (tranTypeCd is null) return Array.Empty<object?>();
        if (tranCatCd is null) return new object?[] { tranTypeCd };
        return new object?[] { tranTypeCd, tranCatCd.Value };
    }

    /// <summary>Maps the current reader row to a <see cref="TranCategory"/>.</summary>
    private static TranCategory Map(SqliteDataReader rd) => new()
    {
        TranTypeCd = rd.GetText("tran_type_cd"),
        TranCatCd = rd.GetInt32("tran_cat_cd"),
        TranCatTypeDesc = rd.GetText("tran_cat_type_desc"),
    };

    /// <summary>Binds a <see cref="TranCategory"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, TranCategory x)
    {
        c.Parameters.AddWithValue("@tran_type_cd", x.TranTypeCd);
        c.Parameters.AddWithValue("@tran_cat_cd", x.TranCatCd);
        c.Parameters.AddWithValue("@tran_cat_type_desc", x.TranCatTypeDesc);
    }
}
