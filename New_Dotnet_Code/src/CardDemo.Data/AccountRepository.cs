using CardDemo.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the ACCOUNT table (copybook CVACT01Y). Primary key <c>acct_id</c>.
/// Exposes the VSAM-semantics operations over <see cref="Account"/> using plain parameterized SQL.
/// </summary>
public sealed class AccountRepository : RepositoryBase
{
    private const string Table = "ACCOUNT";
    private const string Cols =
        "acct_id, active_status, curr_bal, credit_limit, cash_credit_limit, open_date, " +
        "expiration_date, reissue_date, curr_cyc_credit, curr_cyc_debit, addr_zip, group_id";
    private static readonly string[] KeyCols = { "acct_id" };

    public AccountRepository(RelationalDb db) : base(db) { }
    public AccountRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by primary key. Returns '00' (found) or '23' (not found).</summary>
    public string ReadByKey(long acctId, out Account? account)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE acct_id = @id");
        c.Parameters.AddWithValue("@id", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { account = Map(rd); return FileStatus.Ok; }
        account = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Inserts a new row. Returns '00' or '22' (duplicate primary key).</summary>
    public string Insert(Account a)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES " +
            "(@acct_id, @active_status, @curr_bal, @credit_limit, @cash_credit_limit, @open_date, " +
            "@expiration_date, @reissue_date, @curr_cyc_credit, @curr_cyc_debit, @addr_zip, @group_id)");
        Bind(c, a);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row (matched by primary key). Returns '00' or '23' (not found).</summary>
    public string Update(Account a)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET active_status=@active_status, curr_bal=@curr_bal, credit_limit=@credit_limit, " +
            "cash_credit_limit=@cash_credit_limit, open_date=@open_date, expiration_date=@expiration_date, " +
            "reissue_date=@reissue_date, curr_cyc_credit=@curr_cyc_credit, curr_cyc_debit=@curr_cyc_debit, " +
            "addr_zip=@addr_zip, group_id=@group_id WHERE acct_id=@acct_id");
        Bind(c, a);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by primary key. Returns '00' or '23' (not found).</summary>
    public string Delete(long acctId)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE acct_id = @id");
        c.Parameters.AddWithValue("@id", acctId);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a forward/reverse browse at-or-after the given key (null/none = first row).</summary>
    public void StartBrowse(long? acctId = null)
    {
        if (acctId is null) StartBrowseAt();
        else StartBrowseAt(acctId.Value);
    }

    /// <summary>Positions a browse strictly after the given key.</summary>
    public void StartBrowseAfterKey(long acctId) => StartBrowseAfter(acctId);

    /// <summary>Reads the next row in ascending primary-key order. Returns '00' or '10' (end of file).</summary>
    public string ReadNext(out Account? account)
        => Advance(true, Cols, Table, KeyCols, Map, out account);

    /// <summary>Reads the previous row in descending primary-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out Account? account)
        => Advance(false, Cols, Table, KeyCols, Map, out account);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending primary-key order.</summary>
    public IEnumerable<Account> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY acct_id ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to an <see cref="Account"/>.</summary>
    private static Account Map(SqliteDataReader rd) => new()
    {
        AcctId = rd.GetInt64("acct_id"),
        ActiveStatus = rd.GetText("active_status"),
        CurrBal = rd.GetMoney("curr_bal"),
        CreditLimit = rd.GetMoney("credit_limit"),
        CashCreditLimit = rd.GetMoney("cash_credit_limit"),
        OpenDate = rd.GetText("open_date"),
        ExpirationDate = rd.GetText("expiration_date"),
        ReissueDate = rd.GetText("reissue_date"),
        CurrCycCredit = rd.GetMoney("curr_cyc_credit"),
        CurrCycDebit = rd.GetMoney("curr_cyc_debit"),
        AddrZip = rd.GetText("addr_zip"),
        GroupId = rd.GetText("group_id"),
    };

    /// <summary>Binds an <see cref="Account"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, Account a)
    {
        c.Parameters.AddWithValue("@acct_id", a.AcctId);
        c.Parameters.AddWithValue("@active_status", a.ActiveStatus);
        c.Parameters.AddWithValue("@curr_bal", a.CurrBal);
        c.Parameters.AddWithValue("@credit_limit", a.CreditLimit);
        c.Parameters.AddWithValue("@cash_credit_limit", a.CashCreditLimit);
        c.Parameters.AddWithValue("@open_date", a.OpenDate);
        c.Parameters.AddWithValue("@expiration_date", a.ExpirationDate);
        c.Parameters.AddWithValue("@reissue_date", a.ReissueDate);
        c.Parameters.AddWithValue("@curr_cyc_credit", a.CurrCycCredit);
        c.Parameters.AddWithValue("@curr_cyc_debit", a.CurrCycDebit);
        c.Parameters.AddWithValue("@addr_zip", a.AddrZip);
        c.Parameters.AddWithValue("@group_id", a.GroupId);
    }
}
