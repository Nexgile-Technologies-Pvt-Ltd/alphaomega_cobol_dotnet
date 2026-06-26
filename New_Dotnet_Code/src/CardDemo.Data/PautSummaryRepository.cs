using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the optional-module PAUT_SUMMARY table — the IMS root segment PAUTSUM0
/// ("Pending Authorization Summary", copybook CIPAUSMY) re-hosted relationally. Primary key
/// <c>ACCT_ID</c> (= IMS root key ACCNTID). Maps the DL/I calls the authorization programs issue
/// (see IMS_SCHEMA.md §3) onto SQL: GU -> <see cref="ReadByKey"/>; GN forward root scan ->
/// <see cref="StartBrowse"/>/<see cref="ReadNext"/>; ISRT -> <see cref="Insert"/>; REPL ->
/// <see cref="Update"/>; DLET -> <see cref="Delete"/> (FK ON DELETE CASCADE removes its details).
/// Every operation returns the exact two-character <see cref="FileStatus"/>.
/// </summary>
public sealed class PautSummaryRepository : RepositoryBase
{
    private const string Table = "PAUT_SUMMARY";
    private const string Cols =
        "ACCT_ID, CUST_ID, AUTH_STATUS, ACCOUNT_STATUS_1, ACCOUNT_STATUS_2, ACCOUNT_STATUS_3, " +
        "ACCOUNT_STATUS_4, ACCOUNT_STATUS_5, CREDIT_LIMIT, CASH_LIMIT, CREDIT_BALANCE, CASH_BALANCE, " +
        "APPROVED_AUTH_CNT, DECLINED_AUTH_CNT, APPROVED_AUTH_AMT, DECLINED_AUTH_AMT";
    private static readonly string[] KeyCols = { "ACCT_ID" };

    public PautSummaryRepository(RelationalDb db) : base(db) { }
    public PautSummaryRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>DL/I GU: random read of the root by ACCNTID. Returns '00' or '23' (DL/I GE).</summary>
    public string ReadByKey(long acctId, out PautSummary? summary)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@k");
        c.Parameters.AddWithValue("@k", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { summary = Map(rd); return FileStatus.Ok; }
        summary = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>DL/I ISRT: insert the root segment. Returns '00' or '22' (DL/I II on duplicate).</summary>
    public string Insert(PautSummary x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (" +
            "@ACCT_ID, @CUST_ID, @AUTH_STATUS, @ACCOUNT_STATUS_1, @ACCOUNT_STATUS_2, @ACCOUNT_STATUS_3, " +
            "@ACCOUNT_STATUS_4, @ACCOUNT_STATUS_5, @CREDIT_LIMIT, @CASH_LIMIT, @CREDIT_BALANCE, " +
            "@CASH_BALANCE, @APPROVED_AUTH_CNT, @DECLINED_AUTH_CNT, @APPROVED_AUTH_AMT, @DECLINED_AUTH_AMT)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>DL/I REPL: update the held root (all non-key columns; key never changes). Returns '00' or '23'.</summary>
    public string Update(PautSummary x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET " +
            "CUST_ID=@CUST_ID, AUTH_STATUS=@AUTH_STATUS, ACCOUNT_STATUS_1=@ACCOUNT_STATUS_1, " +
            "ACCOUNT_STATUS_2=@ACCOUNT_STATUS_2, ACCOUNT_STATUS_3=@ACCOUNT_STATUS_3, " +
            "ACCOUNT_STATUS_4=@ACCOUNT_STATUS_4, ACCOUNT_STATUS_5=@ACCOUNT_STATUS_5, " +
            "CREDIT_LIMIT=@CREDIT_LIMIT, CASH_LIMIT=@CASH_LIMIT, CREDIT_BALANCE=@CREDIT_BALANCE, " +
            "CASH_BALANCE=@CASH_BALANCE, APPROVED_AUTH_CNT=@APPROVED_AUTH_CNT, " +
            "DECLINED_AUTH_CNT=@DECLINED_AUTH_CNT, APPROVED_AUTH_AMT=@APPROVED_AUTH_AMT, " +
            "DECLINED_AUTH_AMT=@DECLINED_AUTH_AMT WHERE ACCT_ID=@ACCT_ID");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>
    /// DL/I DLET: delete the root once its details are gone. The FK ON DELETE CASCADE mirrors IMS
    /// physical deletion of children when a root is deleted. Returns '00' or '23'.
    /// </summary>
    public string Delete(long acctId)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE ACCT_ID=@k");
        c.Parameters.AddWithValue("@k", acctId);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a forward root scan (DL/I GN) at-or-after the given key (null/none = first root).</summary>
    public void StartBrowse(long? acctId = null)
    {
        if (acctId is null) StartBrowseAt();
        else StartBrowseAt(acctId.Value);
    }

    /// <summary>Positions a root scan strictly after the given key.</summary>
    public void StartBrowseAfterKey(long acctId) => StartBrowseAfter(acctId);

    /// <summary>DL/I GN: next root in ascending ACCNTID order. Returns '00' or '10' (DL/I GB at end).</summary>
    public string ReadNext(out PautSummary? summary)
        => Advance(true, Cols, Table, KeyCols, Map, out summary);

    /// <summary>Previous root in descending key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out PautSummary? summary)
        => Advance(false, Cols, Table, KeyCols, Map, out summary);

    /// <summary>Ends the current root scan.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All roots in ascending ACCNTID order (the DL/I GN sequence).</summary>
    public IEnumerable<PautSummary> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY ACCT_ID ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Maps the current reader row to a <see cref="PautSummary"/>.</summary>
    private static PautSummary Map(SqliteDataReader rd) => new()
    {
        AcctId = rd.GetInt64OrZero("ACCT_ID"),
        CustId = rd.GetInt64OrZero("CUST_ID"),
        AuthStatus = rd.GetText("AUTH_STATUS"),
        AccountStatus1 = rd.GetText("ACCOUNT_STATUS_1"),
        AccountStatus2 = rd.GetText("ACCOUNT_STATUS_2"),
        AccountStatus3 = rd.GetText("ACCOUNT_STATUS_3"),
        AccountStatus4 = rd.GetText("ACCOUNT_STATUS_4"),
        AccountStatus5 = rd.GetText("ACCOUNT_STATUS_5"),
        CreditLimit = rd.GetMoneyOrZero("CREDIT_LIMIT"),
        CashLimit = rd.GetMoneyOrZero("CASH_LIMIT"),
        CreditBalance = rd.GetMoneyOrZero("CREDIT_BALANCE"),
        CashBalance = rd.GetMoneyOrZero("CASH_BALANCE"),
        ApprovedAuthCnt = rd.GetInt32OrZero("APPROVED_AUTH_CNT"),
        DeclinedAuthCnt = rd.GetInt32OrZero("DECLINED_AUTH_CNT"),
        ApprovedAuthAmt = rd.GetMoneyOrZero("APPROVED_AUTH_AMT"),
        DeclinedAuthAmt = rd.GetMoneyOrZero("DECLINED_AUTH_AMT"),
    };

    /// <summary>Binds a <see cref="PautSummary"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, PautSummary x)
    {
        c.Parameters.AddWithValue("@ACCT_ID", x.AcctId);
        c.Parameters.AddWithValue("@CUST_ID", x.CustId);
        c.Parameters.AddWithValue("@AUTH_STATUS", x.AuthStatus);
        c.Parameters.AddWithValue("@ACCOUNT_STATUS_1", x.AccountStatus1);
        c.Parameters.AddWithValue("@ACCOUNT_STATUS_2", x.AccountStatus2);
        c.Parameters.AddWithValue("@ACCOUNT_STATUS_3", x.AccountStatus3);
        c.Parameters.AddWithValue("@ACCOUNT_STATUS_4", x.AccountStatus4);
        c.Parameters.AddWithValue("@ACCOUNT_STATUS_5", x.AccountStatus5);
        c.Parameters.AddWithValue("@CREDIT_LIMIT", x.CreditLimit);
        c.Parameters.AddWithValue("@CASH_LIMIT", x.CashLimit);
        c.Parameters.AddWithValue("@CREDIT_BALANCE", x.CreditBalance);
        c.Parameters.AddWithValue("@CASH_BALANCE", x.CashBalance);
        c.Parameters.AddWithValue("@APPROVED_AUTH_CNT", x.ApprovedAuthCnt);
        c.Parameters.AddWithValue("@DECLINED_AUTH_CNT", x.DeclinedAuthCnt);
        c.Parameters.AddWithValue("@APPROVED_AUTH_AMT", x.ApprovedAuthAmt);
        c.Parameters.AddWithValue("@DECLINED_AUTH_AMT", x.DeclinedAuthAmt);
    }
}
