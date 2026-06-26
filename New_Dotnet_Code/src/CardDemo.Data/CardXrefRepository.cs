using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the CARD_XREF table (copybook CVACT03Y). Primary key <c>xref_card_num</c>
/// X(16); alternate index on <c>acct_id</c>. Exposes the VSAM-semantics operations over <see cref="CardXref"/>.
/// </summary>
public sealed class CardXrefRepository : RepositoryBase
{
    private const string Table = "CARD_XREF";
    private const string Cols = "xref_card_num, cust_id, acct_id";
    private static readonly string[] KeyCols = { "xref_card_num" };

    public CardXrefRepository(RelationalDb db) : base(db) { }
    public CardXrefRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string xrefCardNum, out CardXref? xref)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE xref_card_num = @k");
        c.Parameters.AddWithValue("@k", xrefCardNum);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { xref = Map(rd); return FileStatus.Ok; }
        xref = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Random read via the alternate index (acct_id), returning the first match by PK. '00'/'23'.</summary>
    public string ReadByAltKey(long acctId, out CardXref? xref)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE acct_id = @a ORDER BY xref_card_num ASC LIMIT 1");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { xref = Map(rd); return FileStatus.Ok; }
        xref = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>All cross-references for an account, in ascending primary-key order.</summary>
    public IEnumerable<CardXref> ReadAllByAcctId(long acctId)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE acct_id = @a ORDER BY xref_card_num ASC");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(CardXref x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (@xref_card_num, @cust_id, @acct_id)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(CardXref x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET cust_id=@cust_id, acct_id=@acct_id WHERE xref_card_num=@xref_card_num");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string xrefCardNum)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE xref_card_num = @k");
        c.Parameters.AddWithValue("@k", xrefCardNum);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? xrefCardNum = null)
    {
        if (xrefCardNum is null) StartBrowseAt();
        else StartBrowseAt(xrefCardNum);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string xrefCardNum) => StartBrowseAfter(xrefCardNum);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out CardXref? xref)
        => Advance(true, Cols, Table, KeyCols, Map, out xref);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out CardXref? xref)
        => Advance(false, Cols, Table, KeyCols, Map, out xref);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<CardXref> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY xref_card_num ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="CardXref"/>.</summary>
    private static CardXref Map(SqliteDataReader rd) => new()
    {
        XrefCardNum = rd.GetText("xref_card_num"),
        CustId = rd.GetInt64("cust_id"),
        AcctId = rd.GetInt64("acct_id"),
    };

    /// <summary>Binds a <see cref="CardXref"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, CardXref x)
    {
        c.Parameters.AddWithValue("@xref_card_num", x.XrefCardNum);
        c.Parameters.AddWithValue("@cust_id", x.CustId);
        c.Parameters.AddWithValue("@acct_id", x.AcctId);
    }
}
