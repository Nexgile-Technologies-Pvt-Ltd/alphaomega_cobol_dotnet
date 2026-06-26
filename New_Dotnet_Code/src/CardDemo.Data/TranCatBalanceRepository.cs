using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the TRAN_CAT_BAL table (copybook CVTRA01Y). Composite primary key
/// (<c>acct_id</c>, <c>type_cd</c>, <c>cat_cd</c>) — TRAN-CAT-KEY = 17 bytes. Exposes the VSAM-semantics
/// operations over <see cref="TranCatBalance"/>. Browse orders by the full composite key.
/// </summary>
public sealed class TranCatBalanceRepository : RepositoryBase
{
    private const string Table = "TRAN_CAT_BAL";
    private const string Cols = "acct_id, type_cd, cat_cd, tran_cat_bal";
    private static readonly string[] KeyCols = { "acct_id", "type_cd", "cat_cd" };

    public TranCatBalanceRepository(RelationalDb db) : base(db) { }
    public TranCatBalanceRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by the full composite key. Returns '00' or '23'.</summary>
    public string ReadByKey(long acctId, string typeCd, int catCd, out TranCatBalance? balance)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE acct_id=@a AND type_cd=@t AND cat_cd=@c");
        c.Parameters.AddWithValue("@a", acctId);
        c.Parameters.AddWithValue("@t", typeCd);
        c.Parameters.AddWithValue("@c", catCd);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { balance = Map(rd); return FileStatus.Ok; }
        balance = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(TranCatBalance x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@acct_id, @type_cd, @cat_cd, @tran_cat_bal)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by composite key. Returns '00' or '23'.</summary>
    public string Update(TranCatBalance x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET tran_cat_bal=@tran_cat_bal " +
            "WHERE acct_id=@acct_id AND type_cd=@type_cd AND cat_cd=@cat_cd");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by composite key. Returns '00' or '23'.</summary>
    public string Delete(long acctId, string typeCd, int catCd)
    {
        using SqliteCommand c = Cmd(
            $"DELETE FROM {Table} WHERE acct_id=@a AND type_cd=@t AND cat_cd=@c");
        c.Parameters.AddWithValue("@a", acctId);
        c.Parameters.AddWithValue("@t", typeCd);
        c.Parameters.AddWithValue("@c", catCd);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given (partial) composite key (null/none = first row).</summary>
    public void StartBrowse(long? acctId = null, string? typeCd = null, int? catCd = null)
        => StartBrowseAt(BuildKey(acctId, typeCd, catCd));

    /// <summary>Positions a browse strictly after the given composite key.</summary>
    public void StartBrowseAfterKey(long acctId, string typeCd, int catCd)
        => StartBrowseAfter(acctId, typeCd, catCd);

    /// <summary>Reads the next row in ascending composite-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out TranCatBalance? balance)
        => Advance(true, Cols, Table, KeyCols, Map, out balance);

    /// <summary>Reads the previous row in descending composite-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out TranCatBalance? balance)
        => Advance(false, Cols, Table, KeyCols, Map, out balance);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending composite-key order.</summary>
    public IEnumerable<TranCatBalance> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY acct_id ASC, type_cd ASC, cat_cd ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Builds the composite key parameter tuple, dropping trailing unspecified parts.</summary>
    private static object?[] BuildKey(long? acctId, string? typeCd, int? catCd)
    {
        if (acctId is null) return Array.Empty<object?>();
        if (typeCd is null) return new object?[] { acctId.Value };
        if (catCd is null) return new object?[] { acctId.Value, typeCd };
        return new object?[] { acctId.Value, typeCd, catCd.Value };
    }

    /// <summary>Maps the current reader row to a <see cref="TranCatBalance"/>.</summary>
    private static TranCatBalance Map(SqliteDataReader rd) => new()
    {
        AcctId = rd.GetInt64("acct_id"),
        TypeCd = rd.GetText("type_cd"),
        CatCd = rd.GetInt32("cat_cd"),
        TranCatBal = rd.GetMoney("tran_cat_bal"),
    };

    /// <summary>Binds a <see cref="TranCatBalance"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, TranCatBalance x)
    {
        c.Parameters.AddWithValue("@acct_id", x.AcctId);
        c.Parameters.AddWithValue("@type_cd", x.TypeCd);
        c.Parameters.AddWithValue("@cat_cd", x.CatCd);
        c.Parameters.AddWithValue("@tran_cat_bal", x.TranCatBal);
    }
}
