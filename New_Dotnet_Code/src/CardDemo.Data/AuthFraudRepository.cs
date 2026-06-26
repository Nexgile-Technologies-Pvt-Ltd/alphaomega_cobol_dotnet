using CardDemo.Cobol.Runtime;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Data;

/// <summary>
/// Relational repository for the optional-module AUTHFRDS table (DB2 DDL AUTHFRDS.ddl; already relational).
/// Composite primary key (<c>CARD_NUM</c> CHAR(16), <c>AUTH_TS</c> TIMESTAMP). Written by COPAUS2C when an
/// authorization is flagged fraud. Alternate access paths: the unique index XAUTHFRD
/// (CARD_NUM ASC, AUTH_TS DESC) — "latest auth per card first" — and a plain index on <c>ACCT_ID</c>.
/// Exposes the VSAM-semantics operations over <see cref="AuthFraud"/>, each returning the exact
/// two-character <see cref="FileStatus"/>. Browse orders by the full composite key.
/// </summary>
public sealed class AuthFraudRepository : RepositoryBase
{
    private const string Table = "AUTHFRDS";
    private const string Cols =
        "CARD_NUM, AUTH_TS, AUTH_TYPE, CARD_EXPIRY_DATE, MESSAGE_TYPE, MESSAGE_SOURCE, AUTH_ID_CODE, " +
        "AUTH_RESP_CODE, AUTH_RESP_REASON, PROCESSING_CODE, TRANSACTION_AMT, APPROVED_AMT, " +
        "MERCHANT_CATAGORY_CODE, ACQR_COUNTRY_CODE, POS_ENTRY_MODE, MERCHANT_ID, MERCHANT_NAME, " +
        "MERCHANT_CITY, MERCHANT_STATE, MERCHANT_ZIP, TRANSACTION_ID, MATCH_STATUS, AUTH_FRAUD, " +
        "FRAUD_RPT_DATE, ACCT_ID, CUST_ID";
    private static readonly string[] KeyCols = { "CARD_NUM", "AUTH_TS" };

    public AuthFraudRepository(RelationalDb db) : base(db) { }
    public AuthFraudRepository(SqliteConnection connection) : base(connection) { }

    /// <summary>Random read by the full composite key. Returns '00' or '23'.</summary>
    public string ReadByKey(string cardNum, string authTs, out AuthFraud? row)
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} WHERE CARD_NUM=@n AND AUTH_TS=@t");
        c.Parameters.AddWithValue("@n", cardNum);
        c.Parameters.AddWithValue("@t", authTs);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { row = Map(rd); return FileStatus.Ok; }
        row = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>
    /// Read via the XAUTHFRD alternate access path: the most-recent authorization for a card
    /// (CARD_NUM ASC, AUTH_TS DESC → first row = newest). Returns '00' or '23'.
    /// </summary>
    public string ReadLatestByCard(string cardNum, out AuthFraud? row)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE CARD_NUM=@n ORDER BY AUTH_TS DESC LIMIT 1");
        c.Parameters.AddWithValue("@n", cardNum);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { row = Map(rd); return FileStatus.Ok; }
        row = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>Read via the ACCT_ID alternate index, returning the first matching row by PK. '00'/'23'.</summary>
    public string ReadByAltKey(long acctId, out AuthFraud? row)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a ORDER BY CARD_NUM ASC, AUTH_TS ASC LIMIT 1");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        if (rd.Read()) { row = Map(rd); return FileStatus.Ok; }
        row = null;
        return FileStatus.RecordNotFound;
    }

    /// <summary>All fraud rows for an account, via the ACCT_ID alternate index, in (CARD_NUM, AUTH_TS) order.</summary>
    public IEnumerable<AuthFraud> ReadAllByAcctId(long acctId)
    {
        using SqliteCommand c = Cmd(
            $"SELECT {Cols} FROM {Table} WHERE ACCT_ID=@a ORDER BY CARD_NUM ASC, AUTH_TS ASC");
        c.Parameters.AddWithValue("@a", acctId);
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Inserts a new row. Returns '00' or '22'.</summary>
    public string Insert(AuthFraud x)
    {
        using SqliteCommand c = Cmd(
            $"INSERT INTO {Table} ({Cols}) VALUES (" +
            "@CARD_NUM, @AUTH_TS, @AUTH_TYPE, @CARD_EXPIRY_DATE, @MESSAGE_TYPE, @MESSAGE_SOURCE, " +
            "@AUTH_ID_CODE, @AUTH_RESP_CODE, @AUTH_RESP_REASON, @PROCESSING_CODE, @TRANSACTION_AMT, " +
            "@APPROVED_AMT, @MERCHANT_CATAGORY_CODE, @ACQR_COUNTRY_CODE, @POS_ENTRY_MODE, @MERCHANT_ID, " +
            "@MERCHANT_NAME, @MERCHANT_CITY, @MERCHANT_STATE, @MERCHANT_ZIP, @TRANSACTION_ID, " +
            "@MATCH_STATUS, @AUTH_FRAUD, @FRAUD_RPT_DATE, @ACCT_ID, @CUST_ID)");
        Bind(c, x);
        try { c.ExecuteNonQuery(); return FileStatus.Ok; }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint) { return FileStatus.DuplicateKeyError; }
    }

    /// <summary>Updates an existing row by composite key (all non-key columns). Returns '00' or '23'.</summary>
    public string Update(AuthFraud x)
    {
        using SqliteCommand c = Cmd(
            $"UPDATE {Table} SET " +
            "AUTH_TYPE=@AUTH_TYPE, CARD_EXPIRY_DATE=@CARD_EXPIRY_DATE, MESSAGE_TYPE=@MESSAGE_TYPE, " +
            "MESSAGE_SOURCE=@MESSAGE_SOURCE, AUTH_ID_CODE=@AUTH_ID_CODE, AUTH_RESP_CODE=@AUTH_RESP_CODE, " +
            "AUTH_RESP_REASON=@AUTH_RESP_REASON, PROCESSING_CODE=@PROCESSING_CODE, " +
            "TRANSACTION_AMT=@TRANSACTION_AMT, APPROVED_AMT=@APPROVED_AMT, " +
            "MERCHANT_CATAGORY_CODE=@MERCHANT_CATAGORY_CODE, ACQR_COUNTRY_CODE=@ACQR_COUNTRY_CODE, " +
            "POS_ENTRY_MODE=@POS_ENTRY_MODE, MERCHANT_ID=@MERCHANT_ID, MERCHANT_NAME=@MERCHANT_NAME, " +
            "MERCHANT_CITY=@MERCHANT_CITY, MERCHANT_STATE=@MERCHANT_STATE, MERCHANT_ZIP=@MERCHANT_ZIP, " +
            "TRANSACTION_ID=@TRANSACTION_ID, MATCH_STATUS=@MATCH_STATUS, AUTH_FRAUD=@AUTH_FRAUD, " +
            "FRAUD_RPT_DATE=@FRAUD_RPT_DATE, ACCT_ID=@ACCT_ID, CUST_ID=@CUST_ID " +
            "WHERE CARD_NUM=@CARD_NUM AND AUTH_TS=@AUTH_TS");
        Bind(c, x);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Deletes by composite key. Returns '00' or '23'.</summary>
    public string Delete(string cardNum, string authTs)
    {
        using SqliteCommand c = Cmd($"DELETE FROM {Table} WHERE CARD_NUM=@n AND AUTH_TS=@t");
        c.Parameters.AddWithValue("@n", cardNum);
        c.Parameters.AddWithValue("@t", authTs);
        return c.ExecuteNonQuery() > 0 ? FileStatus.Ok : FileStatus.RecordNotFound;
    }

    /// <summary>Positions a browse at-or-after the given (partial) composite key (null/none = first row).</summary>
    public void StartBrowse(string? cardNum = null, string? authTs = null)
        => StartBrowseAt(BuildKey(cardNum, authTs));

    /// <summary>Positions a browse strictly after the given composite key.</summary>
    public void StartBrowseAfterKey(string cardNum, string authTs)
        => StartBrowseAfter(cardNum, authTs);

    /// <summary>Reads the next row in ascending composite-key order. Returns '00' or '10'.</summary>
    public string ReadNext(out AuthFraud? row)
        => Advance(true, Cols, Table, KeyCols, Map, out row);

    /// <summary>Reads the previous row in descending composite-key order. Returns '00' or '10'.</summary>
    public string ReadPrevious(out AuthFraud? row)
        => Advance(false, Cols, Table, KeyCols, Map, out row);

    /// <summary>Ends the current browse.</summary>
    public void EndBrowse() => EndBrowseCore();

    /// <summary>All rows in ascending composite-key order.</summary>
    public IEnumerable<AuthFraud> ReadAll()
    {
        using SqliteCommand c = Cmd($"SELECT {Cols} FROM {Table} ORDER BY CARD_NUM ASC, AUTH_TS ASC");
        using SqliteDataReader rd = c.ExecuteReader();
        while (rd.Read()) yield return Map(rd);
    }

    /// <summary>Builds the composite key parameter tuple, dropping trailing unspecified parts.</summary>
    private static object?[] BuildKey(string? cardNum, string? authTs)
    {
        if (cardNum is null) return Array.Empty<object?>();
        if (authTs is null) return new object?[] { cardNum };
        return new object?[] { cardNum, authTs };
    }

    /// <summary>Maps the current reader row to an <see cref="AuthFraud"/>.</summary>
    private static AuthFraud Map(SqliteDataReader rd) => new()
    {
        CardNum = rd.GetText("CARD_NUM"),
        AuthTs = rd.GetText("AUTH_TS"),
        AuthType = rd.GetText("AUTH_TYPE"),
        CardExpiryDate = rd.GetText("CARD_EXPIRY_DATE"),
        MessageType = rd.GetText("MESSAGE_TYPE"),
        MessageSource = rd.GetText("MESSAGE_SOURCE"),
        AuthIdCode = rd.GetText("AUTH_ID_CODE"),
        AuthRespCode = rd.GetText("AUTH_RESP_CODE"),
        AuthRespReason = rd.GetText("AUTH_RESP_REASON"),
        ProcessingCode = rd.GetText("PROCESSING_CODE"),
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
        AuthFraudInd = rd.GetText("AUTH_FRAUD"),
        FraudRptDate = rd.GetText("FRAUD_RPT_DATE"),
        AcctId = rd.GetInt64OrZero("ACCT_ID"),
        CustId = rd.GetInt64OrZero("CUST_ID"),
    };

    /// <summary>Binds an <see cref="AuthFraud"/> onto the named parameters of an insert/update command.</summary>
    private static void Bind(SqliteCommand c, AuthFraud x)
    {
        c.Parameters.AddWithValue("@CARD_NUM", x.CardNum);
        c.Parameters.AddWithValue("@AUTH_TS", x.AuthTs);
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
        c.Parameters.AddWithValue("@AUTH_FRAUD", x.AuthFraudInd);
        c.Parameters.AddWithValue("@FRAUD_RPT_DATE", x.FraudRptDate);
        c.Parameters.AddWithValue("@ACCT_ID", x.AcctId);
        c.Parameters.AddWithValue("@CUST_ID", x.CustId);
    }
}
