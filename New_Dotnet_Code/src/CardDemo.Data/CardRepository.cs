using CardDemo.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the CARD table (copybook CVACT02Y). Primary key <c>card_num</c> X(16);
/// alternate index on <c>acct_id</c>. Exposes the VSAM-semantics operations over <see cref="Card"/>.
/// </summary>
public sealed class CardRepository : RepositoryBase
{
    private const string Table = "CARD";
    private const string Cols =
        "card_num, acct_id, cvv_cd, embossed_name, expiration_date, active_status";
    private static readonly string[] KeyCols = { "card_num" };

    public CardRepository(RelationalDb db) : base(db) { }
    public CardRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' or '23'.</summary>
    public string ReadByKey(string cardNum, out Card? card)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE card_num = @k");
        c.Parameters.AddWithValue("@k", cardNum);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { card = Map(rd); return FileStatus.Ok; }
        card = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Random read via the alternate index (acct_id), returning the first matching card by PK. '00'/'23'.</summary>
    public string ReadByAltKey(long acctId, out Card? card)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE acct_id = @a ORDER BY card_num ASC LIMIT 1");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { card = Map(rd); return FileStatus.Ok; }
        card = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>All cards for an account, in ascending primary-key (card_num) order.</summary>
    public IEnumerable<Card> ReadAllByAcctId(long acctId)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE acct_id = @a ORDER BY card_num ASC");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(Card x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES " +
            "(@card_num, @acct_id, @cvv_cd, @embossed_name, @expiration_date, @active_status)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by primary key. Returns '00' or '23'.</summary>
    public string Update(Card x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET acct_id=@acct_id, cvv_cd=@cvv_cd, embossed_name=@embossed_name, " +
            "expiration_date=@expiration_date, active_status=@active_status WHERE card_num=@card_num");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23'.</summary>
    public string Delete(string cardNum)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE card_num = @k");
        c.Parameters.AddWithValue("@k", cardNum);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given primary key (null/none = first row).</summary>
    public void StartBrowse(string? cardNum = null)
    {
        if (cardNum is null) StartBrowseAt();
        else StartBrowseAt(cardNum);
    }

    /// <summary>Positions a browse strictly after the given primary key.</summary>
    public void StartBrowseAfterKey(string cardNum) => StartBrowseAfter(cardNum);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out Card? card)
        => Advance(true, Cols, Table, KeyCols, Map, out card);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out Card? card)
        => Advance(false, Cols, Table, KeyCols, Map, out card);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<Card> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY card_num ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="Card"/>.</summary>
    private static Card Map(SqliteDataReader rd) => new()
    {
        CardNum = rd.GetText("card_num"),
        AcctId = rd.GetInt64("acct_id"),
        CvvCd = rd.GetInt32("cvv_cd"),
        EmbossedName = rd.GetText("embossed_name"),
        ExpirationDate = rd.GetText("expiration_date"),
        ActiveStatus = rd.GetText("active_status"),
    };

    /// <summary>Binds a <see cref="Card"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, Card x)
    {
        c.Parameters.AddWithValue("@card_num", x.CardNum);
        c.Parameters.AddWithValue("@acct_id", x.AcctId);
        c.Parameters.AddWithValue("@cvv_cd", x.CvvCd);
        c.Parameters.AddWithValue("@embossed_name", x.EmbossedName);
        c.Parameters.AddWithValue("@expiration_date", x.ExpirationDate);
        c.Parameters.AddWithValue("@active_status", x.ActiveStatus);
    }
}
