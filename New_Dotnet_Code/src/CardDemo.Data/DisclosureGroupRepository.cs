using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the DISCLOSURE_GROUP table (copybook CVTRA02Y). Composite primary key
/// (<c>acct_group_id</c>, <c>tran_type_cd</c>, <c>tran_cat_cd</c>) — DIS-GROUP-KEY = 16 bytes. Exposes
/// the VSAM-semantics operations over <see cref="DisclosureGroup"/>. Browse orders by the full composite key.
/// </summary>
public sealed class DisclosureGroupRepository : RepositoryBase
{
    private const string Table = "DISCLOSURE_GROUP";
    private const string Cols = "acct_group_id, tran_type_cd, tran_cat_cd, int_rate";
    private static readonly string[] KeyCols = { "acct_group_id", "tran_type_cd", "tran_cat_cd" };

    public DisclosureGroupRepository(RelationalDb db) : base(db) { }
    public DisclosureGroupRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by the full composite key. Returns '00' or '23'.</summary>
    public string ReadByKey(string acctGroupId, string tranTypeCd, int tranCatCd, out DisclosureGroup? group)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE acct_group_id=@g AND tran_type_cd=@t AND tran_cat_cd=@c");
        c.Parameters.AddWithValue("@g", acctGroupId);
        c.Parameters.AddWithValue("@t", tranTypeCd);
        c.Parameters.AddWithValue("@c", tranCatCd);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { group = Map(rd); return FileStatus.Ok; }
        group = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(DisclosureGroup x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@acct_group_id, @tran_type_cd, @tran_cat_cd, @int_rate)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by composite key. Returns '00' or '23'.</summary>
    public string Update(DisclosureGroup x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET int_rate=@int_rate " +
            "WHERE acct_group_id=@acct_group_id AND tran_type_cd=@tran_type_cd AND tran_cat_cd=@tran_cat_cd");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by composite key. Returns '00' or '23'.</summary>
    public string Delete(string acctGroupId, string tranTypeCd, int tranCatCd)
    {
        using SqliteCommand c = Cmd(
            $"DELETE FROM {Table} WHERE acct_group_id=@g AND tran_type_cd=@t AND tran_cat_cd=@c");
        c.Parameters.AddWithValue("@g", acctGroupId);
        c.Parameters.AddWithValue("@t", tranTypeCd);
        c.Parameters.AddWithValue("@c", tranCatCd);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given (partial) composite key (null/none = first row).</summary>
    public void StartBrowse(string? acctGroupId = null, string? tranTypeCd = null, int? tranCatCd = null)
        => StartBrowseAt(BuildKey(acctGroupId, tranTypeCd, tranCatCd));

    /// <summary>Positions a browse strictly after the given composite key.</summary>
    public void StartBrowseAfterKey(string acctGroupId, string tranTypeCd, int tranCatCd)
        => StartBrowseAfter(acctGroupId, tranTypeCd, tranCatCd);

    /// <summary>Reads the next row in ascending composite-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out DisclosureGroup? group)
        => Advance(true, Cols, Table, KeyCols, Map, out group);

    /// <summary>Reads the previous row in descending composite-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out DisclosureGroup? group)
        => Advance(false, Cols, Table, KeyCols, Map, out group);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending composite-key order.</summary>
    public IEnumerable<DisclosureGroup> ReadAll()
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} ORDER BY acct_group_id ASC, tran_type_cd ASC, tran_cat_cd ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Builds the composite key parameter tuple, dropping trailing unspecified parts.</summary>
    private static object?[] BuildKey(string? acctGroupId, string? tranTypeCd, int? tranCatCd)
    {
        if (acctGroupId is null) return Array.Empty<object?>();
        if (tranTypeCd is null) return new object?[] { acctGroupId };
        if (tranCatCd is null) return new object?[] { acctGroupId, tranTypeCd };
        return new object?[] { acctGroupId, tranTypeCd, tranCatCd.Value };
    }

    /// <summary>Maps the current reader row to a <see cref="DisclosureGroup"/>.</summary>
    private static DisclosureGroup Map(SqliteDataReader rd) => new()
    {
        AcctGroupId = rd.GetText("acct_group_id"),
        TranTypeCd = rd.GetText("tran_type_cd"),
        TranCatCd = rd.GetInt32("tran_cat_cd"),
        IntRate = rd.GetMoney("int_rate"),
    };

    /// <summary>Binds a <see cref="DisclosureGroup"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, DisclosureGroup x)
    {
        c.Parameters.AddWithValue("@acct_group_id", x.AcctGroupId);
        c.Parameters.AddWithValue("@tran_type_cd", x.TranTypeCd);
        c.Parameters.AddWithValue("@tran_cat_cd", x.TranCatCd);
        c.Parameters.AddWithValue("@int_rate", x.IntRate);
    }
}
