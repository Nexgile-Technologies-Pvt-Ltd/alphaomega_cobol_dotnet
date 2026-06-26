using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the optional-module PAUT_DETAIL table — the IMS child segment PAUTDTL1
/// ("Pending Authorization Detail", copybook CIPAUDTY) re-hosted relationally. Composite primary key
/// (<c>ACCT_ID</c>, <c>AUTH_KEY</c>); <c>ACCT_ID</c> is the FK to PAUT_SUMMARY (IMS parentage). Because
/// <c>AUTH_KEY</c> (PAUT9CTS) is a 9s-complement value, ascending key order is newest-first — the IMS
/// twin-chain GN/GNP scan order; every parent-scoped scan here preserves it.
/// <para>
/// Maps the DL/I calls (see IMS_SCHEMA.md §3) onto SQL: GU-by-key -> <see cref="ReadByKey"/>; GNP
/// child scan under the current parent -> <see cref="StartParentScan"/> + <see cref="ReadNextInParent"/>;
/// qualified GNP reposition -> <see cref="StartParentScanAt"/>; ISRT path-insert -> <see cref="Insert"/>;
/// REPL -> <see cref="Update"/>; DLET -> <see cref="Delete"/>. Returns the exact two-character
/// <see cref="FileStatus"/>.
/// </para>
/// </summary>
public sealed class PautDetailRepository : RepositoryBase
{
    private const string Table = "PAUT_DETAIL";
    private const string Cols =
        "ACCT_ID, AUTH_KEY, AUTH_DATE_9C, AUTH_TIME_9C, AUTH_ORIG_DATE, AUTH_ORIG_TIME, CARD_NUM, " +
        "AUTH_TYPE, CARD_EXPIRY_DATE, MESSAGE_TYPE, MESSAGE_SOURCE, AUTH_ID_CODE, AUTH_RESP_CODE, " +
        "AUTH_RESP_REASON, PROCESSING_CODE, TRANSACTION_AMT, APPROVED_AMT, MERCHANT_CATAGORY_CODE, " +
        "ACQR_COUNTRY_CODE, POS_ENTRY_MODE, MERCHANT_ID, MERCHANT_NAME, MERCHANT_CITY, MERCHANT_STATE, " +
        "MERCHANT_ZIP, TRANSACTION_ID, MATCH_STATUS, AUTH_FRAUD, FRAUD_RPT_DATE";
    private static readonly string[] KeyCols = { "ACCT_ID", "AUTH_KEY" };

    // GNP (Get-Next-within-Parent) cursor state. Unlike the generic browse, this stays bounded to a
    // single parent (the IMS twin chain under one PAUTSUM0), exactly as a GNP scan does.
    private long _parentAcctId;
    private string? _gnpLastKey;
    private bool _gnpActive;

    public PautDetailRepository(RelationalDb db) : base(db) { }
    public PautDetailRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>DL/I GU/qualified-GNP by key: read one detail. Returns '00' or '23' (DL/I GE).</summary>
    public string ReadByKey(long acctId, string authKey, out PautDetail? detail)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a AND AUTH_KEY=@k");
        c.Parameters.AddWithValue("@a", acctId);
        c.Parameters.AddWithValue("@k", authKey);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { detail = Map(rd); return FileStatus.Ok; }
        detail = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>DL/I ISRT (path insert): add a child under its parent. Returns '00' or '22' (DL/I II).</summary>
    public string Insert(PautDetail x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (" +
            "@ACCT_ID, @AUTH_KEY, @AUTH_DATE_9C, @AUTH_TIME_9C, @AUTH_ORIG_DATE, @AUTH_ORIG_TIME, " +
            "@CARD_NUM, @AUTH_TYPE, @CARD_EXPIRY_DATE, @MESSAGE_TYPE, @MESSAGE_SOURCE, @AUTH_ID_CODE, " +
            "@AUTH_RESP_CODE, @AUTH_RESP_REASON, @PROCESSING_CODE, @TRANSACTION_AMT, @APPROVED_AMT, " +
            "@MERCHANT_CATAGORY_CODE, @ACQR_COUNTRY_CODE, @POS_ENTRY_MODE, @MERCHANT_ID, @MERCHANT_NAME, " +
            "@MERCHANT_CITY, @MERCHANT_STATE, @MERCHANT_ZIP, @TRANSACTION_ID, @MATCH_STATUS, @AUTH_FRAUD, " +
            "@FRAUD_RPT_DATE)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>DL/I REPL: update the held child (all non-key columns; key never changes). Returns '00' or '23'.</summary>
    public string Update(PautDetail x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET " +
            "AUTH_DATE_9C=@AUTH_DATE_9C, AUTH_TIME_9C=@AUTH_TIME_9C, AUTH_ORIG_DATE=@AUTH_ORIG_DATE, " +
            "AUTH_ORIG_TIME=@AUTH_ORIG_TIME, CARD_NUM=@CARD_NUM, AUTH_TYPE=@AUTH_TYPE, " +
            "CARD_EXPIRY_DATE=@CARD_EXPIRY_DATE, MESSAGE_TYPE=@MESSAGE_TYPE, MESSAGE_SOURCE=@MESSAGE_SOURCE, " +
            "AUTH_ID_CODE=@AUTH_ID_CODE, AUTH_RESP_CODE=@AUTH_RESP_CODE, AUTH_RESP_REASON=@AUTH_RESP_REASON, " +
            "PROCESSING_CODE=@PROCESSING_CODE, TRANSACTION_AMT=@TRANSACTION_AMT, APPROVED_AMT=@APPROVED_AMT, " +
            "MERCHANT_CATAGORY_CODE=@MERCHANT_CATAGORY_CODE, ACQR_COUNTRY_CODE=@ACQR_COUNTRY_CODE, " +
            "POS_ENTRY_MODE=@POS_ENTRY_MODE, MERCHANT_ID=@MERCHANT_ID, MERCHANT_NAME=@MERCHANT_NAME, " +
            "MERCHANT_CITY=@MERCHANT_CITY, MERCHANT_STATE=@MERCHANT_STATE, MERCHANT_ZIP=@MERCHANT_ZIP, " +
            "TRANSACTION_ID=@TRANSACTION_ID, MATCH_STATUS=@MATCH_STATUS, AUTH_FRAUD=@AUTH_FRAUD, " +
            "FRAUD_RPT_DATE=@FRAUD_RPT_DATE WHERE ACCT_ID=@ACCT_ID AND AUTH_KEY=@AUTH_KEY");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>DL/I DLET: delete the held child. Returns '00' or '23'.</summary>
    public string Delete(long acctId, string authKey)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE ACCT_ID=@a AND AUTH_KEY=@k");
        c.Parameters.AddWithValue("@a", acctId);
        c.Parameters.AddWithValue("@k", authKey);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    // ---- GNP (parent-scoped) cursor ------------------------------------------------------------------

    /// <summary>
    /// Opens a DL/I GNP scan over the children of <paramref name="acctId"/> (the parent established by the
    /// last root GU/GN), positioned before the first child. The scan stays bounded to this parent.
    /// </summary>
    public void StartParentScan(long acctId)
    {
        _parentAcctId = acctId;
        _gnpLastKey = null;          // null = before the first child
        _gnpActive = true;
    }

    /// <summary>
    /// Opens a qualified DL/I GNP scan ("reposition") over the children of <paramref name="acctId"/>,
    /// resuming at-or-after <paramref name="savedAuthKey"/> (screen paging in COPAUS0C/COPAUS1C).
    /// </summary>
    public void StartParentScanAt(long acctId, string savedAuthKey)
    {
        _parentAcctId = acctId;
        // Emulate ">= savedAuthKey": start just BEFORE savedAuthKey so the first ReadNextInParent
        // returns the row at-or-after it (the WHERE uses strict '>', so seed with the predecessor key).
        _gnpLastKey = StringJustBefore(savedAuthKey);
        _gnpActive = true;
    }

    /// <summary>
    /// DL/I GNP: next child under the current parent, in ascending AUTH_KEY order (== newest-first).
    /// Returns '00' with the row, or '10' (DL/I GE — no more children) when the twin chain is exhausted.
    /// </summary>
    public string ReadNextInParent(out PautDetail? detail)
    {
        if (!_gnpActive)
        {
            detail = null;
            return FileStatus.EndOfFile;
        }

        SqliteCommand c;
        if (_gnpLastKey is null)
        {
            c = Cmd($"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a ORDER BY AUTH_KEY ASC LIMIT 1");
            c.Parameters.AddWithValue("@a", _parentAcctId);
        }
        else
        {
            c = Cmd($"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a AND AUTH_KEY > @k " +
                    "ORDER BY AUTH_KEY ASC LIMIT 1");
            c.Parameters.AddWithValue("@a", _parentAcctId);
            c.Parameters.AddWithValue("@k", _gnpLastKey);
        }

        try
        {
            using SqliteDataReader rd = c.ExecuteReader();
            if (rd.Read())
            {
                detail = Map(rd);
                _gnpLastKey = detail.AuthKey;
                return FileStatus.Ok;
            }
        }
        finally { c.Dispose(); }

        detail = null;
        return FileStatus.EndOfFile;
    }

    /// <summary>Ends the current GNP scan.</summary>
    public void EndParentScan()
    {
        _gnpActive = false;
        _gnpLastKey = null;
    }

    /// <summary>All children of a parent in ascending AUTH_KEY order (the DL/I GNP sequence).</summary>
    public IEnumerable<PautDetail> ReadAllByParent(long acctId)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a ORDER BY AUTH_KEY ASC");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>All rows in ascending composite-key order (whole-DB sequential scan).</summary>
    public IEnumerable<PautDetail> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY ACCT_ID ASC, AUTH_KEY ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>
    /// Returns the largest 8-char string strictly less than <paramref name="key"/> so a strict '&gt;'
    /// scan resumes at-or-after <paramref name="key"/>. Decrements the last non-NUL char; if the key is
    /// empty or all-NUL the floor is the empty string (scan from the very first child).
    /// </summary>
    private static string StringJustBefore(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        char[] cs = key.ToCharArray();
        for (int i = cs.Length - 1; i >= 0; i--)
        {
            if (cs[i] > '\0')
            {
                cs[i]--;
                // Pad the remaining positions with the max char so we sit just below the original key.
                for (int j = i + 1; j < cs.Length; j++) cs[j] = '￿';
                return new string(cs);
            }
        }
        return string.Empty;
    }

    /// <summary>Maps the current reader row to a <see cref="PautDetail"/>.</summary>
    private static PautDetail Map(SqliteDataReader rd) => new()
    {
        AcctId = rd.GetInt64OrZero("ACCT_ID"),
        AuthKey = rd.GetText("AUTH_KEY"),
        AuthDate9c = rd.GetInt32OrZero("AUTH_DATE_9C"),
        AuthTime9c = rd.GetInt64OrZero("AUTH_TIME_9C"),
        AuthOrigDate = rd.GetText("AUTH_ORIG_DATE"),
        AuthOrigTime = rd.GetText("AUTH_ORIG_TIME"),
        CardNum = rd.GetText("CARD_NUM"),
        AuthType = rd.GetText("AUTH_TYPE"),
        CardExpiryDate = rd.GetText("CARD_EXPIRY_DATE"),
        MessageType = rd.GetText("MESSAGE_TYPE"),
        MessageSource = rd.GetText("MESSAGE_SOURCE"),
        AuthIdCode = rd.GetText("AUTH_ID_CODE"),
        AuthRespCode = rd.GetText("AUTH_RESP_CODE"),
        AuthRespReason = rd.GetText("AUTH_RESP_REASON"),
        ProcessingCode = rd.GetInt32OrZero("PROCESSING_CODE"),
        TransactionAmt = rd.GetMoneyOrZero("TRANSACTION_AMT"),
        ApprovedAmt = rd.GetMoneyOrZero("APPROVED_AMT"),
        MerchantCatagoryCode = rd.GetText("MERCHANT_CATAGORY_CODE"),
        AcqrCountryCode = rd.GetText("ACQR_COUNTRY_CODE"),
        PosEntryMode = rd.GetInt32OrZero("POS_ENTRY_MODE"),
        MerchantId = rd.GetText("MERCHANT_ID"),
        MerchantName = rd.GetText("MERCHANT_NAME"),
        MerchantCity = rd.GetText("MERCHANT_CITY"),
        MerchantState = rd.GetText("MERCHANT_STATE"),
        MerchantZip = rd.GetText("MERCHANT_ZIP"),
        TransactionId = rd.GetText("TRANSACTION_ID"),
        MatchStatus = rd.GetText("MATCH_STATUS"),
        AuthFraud = rd.GetText("AUTH_FRAUD"),
        FraudRptDate = rd.GetText("FRAUD_RPT_DATE"),
    };

    /// <summary>Binds a <see cref="PautDetail"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, PautDetail x)
    {
        c.Parameters.AddWithValue("@ACCT_ID", x.AcctId);
        c.Parameters.AddWithValue("@AUTH_KEY", x.AuthKey);
        c.Parameters.AddWithValue("@AUTH_DATE_9C", x.AuthDate9c);
        c.Parameters.AddWithValue("@AUTH_TIME_9C", x.AuthTime9c);
        c.Parameters.AddWithValue("@AUTH_ORIG_DATE", x.AuthOrigDate);
        c.Parameters.AddWithValue("@AUTH_ORIG_TIME", x.AuthOrigTime);
        c.Parameters.AddWithValue("@CARD_NUM", x.CardNum);
        c.Parameters.AddWithValue("@AUTH_TYPE", x.AuthType);
        c.Parameters.AddWithValue("@CARD_EXPIRY_DATE", x.CardExpiryDate);
        c.Parameters.AddWithValue("@MESSAGE_TYPE", x.MessageType);
        c.Parameters.AddWithValue("@MESSAGE_SOURCE", x.MessageSource);
        c.Parameters.AddWithValue("@AUTH_ID_CODE", x.AuthIdCode);
        c.Parameters.AddWithValue("@AUTH_RESP_CODE", x.AuthRespCode);
        c.Parameters.AddWithValue("@AUTH_RESP_REASON", x.AuthRespReason);
        c.Parameters.AddWithValue("@PROCESSING_CODE", x.ProcessingCode);
        c.Parameters.AddWithValue("@TRANSACTION_AMT", x.TransactionAmt);
        c.Parameters.AddWithValue("@APPROVED_AMT", x.ApprovedAmt);
        c.Parameters.AddWithValue("@MERCHANT_CATAGORY_CODE", x.MerchantCatagoryCode);
        c.Parameters.AddWithValue("@ACQR_COUNTRY_CODE", x.AcqrCountryCode);
        c.Parameters.AddWithValue("@POS_ENTRY_MODE", x.PosEntryMode);
        c.Parameters.AddWithValue("@MERCHANT_ID", x.MerchantId);
        c.Parameters.AddWithValue("@MERCHANT_NAME", x.MerchantName);
        c.Parameters.AddWithValue("@MERCHANT_CITY", x.MerchantCity);
        c.Parameters.AddWithValue("@MERCHANT_STATE", x.MerchantState);
        c.Parameters.AddWithValue("@MERCHANT_ZIP", x.MerchantZip);
        c.Parameters.AddWithValue("@TRANSACTION_ID", x.TransactionId);
        c.Parameters.AddWithValue("@MATCH_STATUS", x.MatchStatus);
        c.Parameters.AddWithValue("@AUTH_FRAUD", x.AuthFraud);
        c.Parameters.AddWithValue("@FRAUD_RPT_DATE", x.FraudRptDate);
    }
}
