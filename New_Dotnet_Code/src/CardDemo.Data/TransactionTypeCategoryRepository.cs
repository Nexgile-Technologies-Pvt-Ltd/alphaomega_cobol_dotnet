using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the optional-module TRANSACTION_TYPE_CATEGORY table (DB2 DDL TRNTYCAT.ddl;
/// already relational). Composite primary key (<c>TRC_TYPE_CODE</c> CHAR(2), <c>TRC_TYPE_CATEGORY</c>
/// CHAR(4)); FK <c>TRC_TYPE_CODE</c> -> TRANSACTION_TYPE(TR_TYPE). Exposes the VSAM-semantics operations
/// over <see cref="TransactionTypeCategory"/>. Browse orders by the full composite key.
/// </summary>
public sealed class TransactionTypeCategoryRepository : RepositoryBase
{
    private const string Table = "TRANSACTION_TYPE_CATEGORY";
    private const string Cols = "TRC_TYPE_CODE, TRC_TYPE_CATEGORY, TRC_CAT_DATA";
    private static readonly string[] KeyCols = { "TRC_TYPE_CODE", "TRC_TYPE_CATEGORY" };

    public TransactionTypeCategoryRepository(RelationalDb db) : base(db) { }
    public TransactionTypeCategoryRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by the full composite key. Returns '00' or '23'.</summary>
    public string ReadByKey(string trcTypeCode, string trcTypeCategory, out TransactionTypeCategory? category)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE TRC_TYPE_CODE=@t AND TRC_TYPE_CATEGORY=@c");
        c.Parameters.AddWithValue("@t", trcTypeCode);
        c.Parameters.AddWithValue("@c", trcTypeCategory);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { category = Map(rd); return FileStatus.Ok; }
        category = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22' (PK/unique or FK violation -> '22').</summary>
    public string Insert(TransactionTypeCategory x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@TRC_TYPE_CODE, @TRC_TYPE_CATEGORY, @TRC_CAT_DATA)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by composite key. Returns '00' or '23'.</summary>
    public string Update(TransactionTypeCategory x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET TRC_CAT_DATA=@TRC_CAT_DATA " +
            "WHERE TRC_TYPE_CODE=@TRC_TYPE_CODE AND TRC_TYPE_CATEGORY=@TRC_TYPE_CATEGORY");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by composite key. Returns '00' or '23'.</summary>
    public string Delete(string trcTypeCode, string trcTypeCategory)
    {
        using SqliteCommand c = Cmd(
            $"DELETE FROM {Table} WHERE TRC_TYPE_CODE=@t AND TRC_TYPE_CATEGORY=@c");
        c.Parameters.AddWithValue("@t", trcTypeCode);
        c.Parameters.AddWithValue("@c", trcTypeCategory);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given (partial) composite key (null/none = first row).</summary>
    public void StartBrowse(string? trcTypeCode = null, string? trcTypeCategory = null)
        => StartBrowseAt(BuildKey(trcTypeCode, trcTypeCategory));

    /// <summary>Positions a browse strictly after the given composite key.</summary>
    public void StartBrowseAfterKey(string trcTypeCode, string trcTypeCategory)
        => StartBrowseAfter(trcTypeCode, trcTypeCategory);

    /// <summary>Reads the next row in ascending composite-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out TransactionTypeCategory? category)
        => Advance(true, Cols, Table, KeyCols, Map, out category);

    /// <summary>Reads the previous row in descending composite-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out TransactionTypeCategory? category)
        => Advance(false, Cols, Table, KeyCols, Map, out category);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending composite-key order.</summary>
    public IEnumerable<TransactionTypeCategory> ReadAll()
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} ORDER BY TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Builds the composite key parameter tuple, dropping trailing unspecified parts.</summary>
    private static object?[] BuildKey(string? trcTypeCode, string? trcTypeCategory)
    {
        if (trcTypeCode is null) return Array.Empty<object?>();
        if (trcTypeCategory is null) return new object?[] { trcTypeCode };
        return new object?[] { trcTypeCode, trcTypeCategory };
    }

    /// <summary>Maps the current reader row to a <see cref="TransactionTypeCategory"/>.</summary>
    private static TransactionTypeCategory Map(SqliteDataReader rd) => new()
    {
        TrcTypeCode = rd.GetText("TRC_TYPE_CODE"),
        TrcTypeCategory = rd.GetText("TRC_TYPE_CATEGORY"),
        TrcCatData = rd.GetText("TRC_CAT_DATA"),
    };

    /// <summary>Binds a <see cref="TransactionTypeCategory"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, TransactionTypeCategory x)
    {
        c.Parameters.AddWithValue("@TRC_TYPE_CODE", x.TrcTypeCode);
        c.Parameters.AddWithValue("@TRC_TYPE_CATEGORY", x.TrcTypeCategory);
        c.Parameters.AddWithValue("@TRC_CAT_DATA", x.TrcCatData);
    }
}
