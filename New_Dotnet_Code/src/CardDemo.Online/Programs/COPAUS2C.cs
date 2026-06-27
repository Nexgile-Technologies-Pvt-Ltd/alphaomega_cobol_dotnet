using System.Globalization;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// The <c>WS-FRAUD-DATA</c> COMMAREA passed on the <c>EXEC CICS LINK</c> from COPAUS1C to COPAUS2C, and
/// the <c>01 DFHCOMMAREA</c> COPAUS2C reads/writes. Mirrors the COBOL group exactly (acct id, cust id,
/// the 200-byte pending-auth detail image carried as a typed <see cref="PautDetail"/>, the one-byte fraud
/// action, plus the two output fields the callee sets). LINK is by-reference: COPAUS2C mutates this object
/// in place (the update-status / message / the report-date stamped into the detail image flow back to the
/// caller). source: COPAUS1C.cbl:93-104; COPAUS2C.cbl:73-86.
/// </summary>
public sealed class WsFraudData
{
    /// <summary>WS-FRD-ACCT-ID 9(11) — owning account id. source: COPAUS1C.cbl:94.</summary>
    public long FrdAcctId { get; set; }

    /// <summary>WS-FRD-CUST-ID 9(9) — owning customer id. source: COPAUS1C.cbl:95.</summary>
    public long FrdCustId { get; set; }

    /// <summary>
    /// WS-FRAUD-AUTH-RECORD PIC X(200) — the pending-authorization detail image (CIPAUDTY layout). Carried
    /// as a typed copy; COPAUS2C reads the PA-* fields from it and stamps PA-FRAUD-RPT-DATE back into it.
    /// source: COPAUS1C.cbl:96,244; COPAUS2C.cbl:77-78.
    /// </summary>
    public PautDetail AuthRecord { get; set; } = new();

    /// <summary>WS-FRD-ACTION X(01); 88 WS-REPORT-FRAUD='F' / WS-REMOVE-FRAUD='R' — input action. source: COPAUS1C.cbl:98-100.</summary>
    public string FrdAction { get; set; } = " ";

    /// <summary>WS-FRD-UPDATE-STATUS X(01); 88 WS-FRD-UPDT-SUCCESS='S' / WS-FRD-UPDT-FAILED='F' — output. source: COPAUS1C.cbl:101-103.</summary>
    public string FrdUpdateStatus { get; set; } = " ";

    /// <summary>WS-FRD-ACT-MSG X(50) — output result/error text. source: COPAUS1C.cbl:104.</summary>
    public string FrdActMsg { get; set; } = "";

    /// <summary>88 WS-FRD-UPDT-SUCCESS — true when the callee set update-status to 'S'. source: COPAUS1C.cbl:102.</summary>
    public bool IsUpdtSuccess => FrdUpdateStatus == "S";
}

/// <summary>
/// Faithful .NET port of the optional CICS/DB2 called subprogram <c>COPAUS2C</c> — "Mark Authorization
/// Message Fraud". It is <b>not</b> a transaction and has no BMS screen: it is invoked synchronously via
/// <c>EXEC CICS LINK</c> from COPAUS1C's MARK-AUTH-FRAUD and persists one fraud-marking action into the
/// (re-hosted DB2) <c>AUTHFRDS</c> table. It derives the <c>AUTH_TS</c> primary-key timestamp from the
/// authorization key fields, INSERTs the row, and on a duplicate-key (DB2 SQLCODE <c>-803</c>, here a
/// SQLite constraint violation surfaced by <see cref="AuthFraudRepository.Insert"/> returning <c>'22'</c>)
/// falls back to an UPDATE of just the fraud flag + report date. It writes the outcome (S/F + message)
/// back into the <see cref="WsFraudData"/> COMMAREA for COPAUS1C to act on. No COMMIT/ROLLBACK here — the
/// caller owns the unit of work (faithful bug #6). source: COPAUS2C.cbl:2-6,88-244; COPAUS2C.md.
/// </summary>
/// <remarks>
/// <para><b>AUTH_TS construction (load-bearing PK derivation).</b> COBOL builds the 23-char timestamp
/// <c>YY-MM-DD HH.MI.SS&lt;sss&gt;000</c> (WS-AUTH-TS, later MOVEd into the 26-char DCLGEN AUTH-TS) where
/// YY/MM/DD are slices of PA-AUTH-ORIG-DATE (YYMMDD) and
/// HH/MI/SS/SSS come from <c>WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C</c> (9s-complement decode) sliced
/// 1-2/3-4/5-6/7-9. The same string is bound on INSERT and on the UPDATE WHERE so the PK matches
/// byte-for-byte. source: COPAUS2C.cbl:35-51,103-114,171-172,224-225.</para>
/// <para><b>Faithful bugs reproduced:</b> #1 9s-complement decode truncates into unsigned 9(09) (silent
/// overflow); #3 PA-FRAUD-RPT-DATE stamped into the COMMAREA detail image but the DB row's FRAUD_RPT_DATE
/// always uses CURRENT DATE; #4 AUTH_FRAUD column sourced from WS-FRD-ACTION (the action), not
/// PA-AUTH-FRAUD; #5 'CATAGORY' misspelling kept; #6 no SYNCPOINT here. source: COPAUS2C.md §7.</para>
/// </remarks>
public sealed class Copaus2c
{
    /// <summary>PROGRAM-ID. COPAUS2C — the LINK target name (WS-PGM-AUTH-FRAUD in COPAUS1C). source: COPAUS1C.cbl:35.</summary>
    public const string ProgramName = "COPAUS2C";

    private readonly AuthFraudRepository _authFrds;
    private readonly IClock _clock;

    // ---- DCLGEN host variables built in MAIN-PARA (the row to write). source: COPAUS2C.cbl:113-139 ----
    // WS-AUTH-TS is the 23-char group 'YY-MM-DD HH.MI.SS<sss>000' built at :38-51; it is later MOVEd into the
    // 26-char DCLGEN host variable AUTH-TS (dcl/AUTHFRDS.dcl:57, PIC X(26)), which space-pads it to 26.
    private string _wsAuthTs = "";  // WS-AUTH-TS (23-char group). source: :38-51.

    /// <summary>
    /// Constructs the fraud worker over the shared relational DB (the AUTHFRDS repository participates in
    /// the caller's pending unit of work, since it shares <see cref="RelationalDb.Connection"/>).
    /// </summary>
    public Copaus2c(RelationalDb db, IClock? clock = null)
    {
        _authFrds = new AuthFraudRepository(db.Connection);
        _clock = clock ?? SystemClock.Instance;
    }

    // =============================================================================================
    //  MAIN-PARA (LINK entry) — source: COPAUS2C.cbl:89-220
    // =============================================================================================
    /// <summary>The <c>EXEC CICS LINK</c> entry point. Mutates <paramref name="ca"/> in place (LINK COMMAREA semantics).</summary>
    public void Run(WsFraudData ca)
    {
        PautDetail pa = ca.AuthRecord;

        // EXEC CICS ASKTIME/FORMATTIME MMDDYY(WS-CUR-DATE) DATESEP -> WS-CUR-DATE = MM/DD/YY. source: :91-100
        DateTime now = _clock.Now;
        string wsCurDate = $"{now.Month:D2}/{now.Day:D2}/{(now.Year % 100):D2}"; // MM/DD/YY (X(08)).

        // MOVE WS-CUR-DATE TO PA-FRAUD-RPT-DATE — stamps the COMMAREA detail image (FB #3). source: :101
        pa.FraudRptDate = wsCurDate;

        // Build AUTH_TS from the auth key fields. source: :103-114
        string origDate = Fixed(pa.AuthOrigDate, 6);                 // YYMMDD
        string wsAuthYy = origDate.Substring(0, 2);                  // PA-AUTH-ORIG-DATE(1:2). :103
        string wsAuthMm = origDate.Substring(2, 2);                  // (3:2). :104
        string wsAuthDd = origDate.Substring(4, 2);                  // (5:2). :105

        // COMPUTE WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C into unsigned 9(09) (FB #1: drop sign, keep low 9 digits). :107
        long wsAuthTime = 999999999L - pa.AuthTime9c;
        long wsAuthTimeUnsigned = Math.Abs(wsAuthTime) % 1000000000L;
        string wsAuthTimeAn = wsAuthTimeUnsigned.ToString("D9", CultureInfo.InvariantCulture); // WS-AUTH-TIME-AN X(09). :36-37

        string wsAuthHh = wsAuthTimeAn.Substring(0, 2);  // (1:2). :108
        string wsAuthMi = wsAuthTimeAn.Substring(2, 2);  // (3:2). :109
        string wsAuthSs = wsAuthTimeAn.Substring(4, 2);  // (5:2). :110
        string wsAuthSss = wsAuthTimeAn.Substring(6, 3); // (7:3). :111

        // WS-AUTH-TS group: 'YY-MM-DD HH.MI.SS<sss>000' (26 chars). FILLERs supply -,-,space,.,.,'000'. :38-51,114
        _wsAuthTs = $"{wsAuthYy}-{wsAuthMm}-{wsAuthDd} {wsAuthHh}.{wsAuthMi}.{wsAuthSs}{wsAuthSss}000";

        // FRAUD_RPT_DATE column = CURRENT DATE (server date as DB2 DATE 'YYYY-MM-DD'), NOT WS-CUR-DATE. :194
        string currentDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Map every PA-* COMMAREA field onto the DCLGEN host variables (the AUTHFRDS row). source: :113-139
        var row = new AuthFraud
        {
            CardNum = Fixed(pa.CardNum, 16),                         // :113
            AuthTs = _wsAuthTs,                                      // :114
            AuthType = Fixed(pa.AuthType, 4),                       // :115
            CardExpiryDate = Fixed(pa.CardExpiryDate, 4),          // :116
            MessageType = Fixed(pa.MessageType, 6),                // :117
            MessageSource = Fixed(pa.MessageSource, 6),            // :118
            AuthIdCode = Fixed(pa.AuthIdCode, 6),                  // :119
            AuthRespCode = Fixed(pa.AuthRespCode, 2),              // :120
            AuthRespReason = Fixed(pa.AuthRespReason, 4),          // :121
            ProcessingCode = pa.ProcessingCode.ToString("D6", CultureInfo.InvariantCulture), // PA-PROCESSING-CODE 9(06)->X(6). :122
            TransactionAmt = pa.TransactionAmt,                     // :123
            ApprovedAmt = pa.ApprovedAmt,                           // :124
            MerchantCatagoryCode = Fixed(pa.MerchantCatagoryCode, 4), // (sic) :125-126
            AcqrCountryCode = Fixed(pa.AcqrCountryCode, 3),        // :127
            PosEntryMode = pa.PosEntryMode,                         // :128
            MerchantId = Fixed(pa.MerchantId, 15),                 // :129
            MerchantName = Fixed(pa.MerchantName, 22),             // VARCHAR(22), len always 22. :130-131
            MerchantCity = Fixed(pa.MerchantCity, 13),             // :132
            MerchantState = Fixed(pa.MerchantState, 2),            // :133
            MerchantZip = Fixed(pa.MerchantZip, 9),                // :134
            TransactionId = Fixed(pa.TransactionId, 15),           // :135
            MatchStatus = Fixed(pa.MatchStatus, 1),                // :136
            AuthFraudInd = Fixed(ca.FrdAction, 1),                 // FB #4: from WS-FRD-ACTION, not PA-AUTH-FRAUD. :137
            FraudRptDate = currentDate,                             // CURRENT DATE. :194
            AcctId = ca.FrdAcctId,                                  // WS-ACCT-ID. :138
            CustId = ca.FrdCustId,                                  // WS-CUST-ID. :139
        };

        // EXEC SQL INSERT INTO CARDDEMO.AUTHFRDS (...) VALUES (...). source: :141-198
        string insStatus = _authFrds.Insert(row);
        if (insStatus == FileStatus.Ok)
        {
            // SQLCODE = ZERO. source: :199-201
            ca.FrdUpdateStatus = "S";          // SET WS-FRD-UPDT-SUCCESS TO TRUE.
            ca.FrdActMsg = "ADD SUCCESS";      // MOVE 'ADD SUCCESS' TO WS-FRD-ACT-MSG.
        }
        else if (insStatus == FileStatus.DuplicateKeyError)
        {
            // SQLCODE = -803 (duplicate key). source: :203-204
            FraudUpdate(ca, row);
        }
        else
        {
            // WHEN OTHER — a non-duplicate SQL failure. source: :205-216. Over the relational store
            // AuthFraudRepository.Insert returns only OK or DUPLICATE-KEY, so this branch is unreachable;
            // there is no live DB2 SQLCODE/SQLSTATE to render (no DB2 layer), so placeholder edits stand in.
            // The COBOL STRING leaves the final 2 bytes of the X(50) WS-FRD-ACT-MSG unchanged; we space-pad
            // (the residual would be SPACES here on a fresh COMMAREA anyway). Documented emulation boundary.
            ca.FrdUpdateStatus = "F";          // SET WS-FRD-UPDT-FAILED TO TRUE.
            ca.FrdActMsg = Fixed(" SYSTEM ERROR DB2: CODE:" + EditSqlCode(-1) + ", STATE: " + EditSqlState(0), 50);
        }

        // EXEC CICS RETURN (bare — no SYNCPOINT here; caller owns the unit of work, FB #6). source: :218-220
    }

    // =============================================================================================
    //  FRAUD-UPDATE (performed on duplicate key) — source: COPAUS2C.cbl:221-244
    // =============================================================================================
    private void FraudUpdate(WsFraudData ca, AuthFraud row)
    {
        // EXEC SQL UPDATE CARDDEMO.AUTHFRDS SET AUTH_FRAUD=:AUTH-FRAUD, FRAUD_RPT_DATE=CURRENT DATE
        //          WHERE CARD_NUM=:CARD-NUM AND AUTH_TS=TIMESTAMP_FORMAT(:AUTH-TS,...). source: :222-229
        // COBOL sets ONLY the two fraud columns; every other existing DB column value is preserved. Use the
        // targeted repository update (NOT the all-columns Update, which would overwrite the stored row with
        // the COMMAREA image).
        string updStatus = _authFrds.UpdateFraudFlag(row.CardNum, row.AuthTs, row.AuthFraudInd, row.FraudRptDate);
        if (updStatus == FileStatus.Ok)
        {
            // SQLCODE = ZERO. source: :230-232
            ca.FrdUpdateStatus = "S";          // SET WS-FRD-UPDT-SUCCESS TO TRUE.
            ca.FrdActMsg = "UPDT SUCCESS";     // MOVE 'UPDT SUCCESS' TO WS-FRD-ACT-MSG.
        }
        else
        {
            // WHEN OTHER. source: :233-243. As with the INSERT failure branch, UpdateFraudFlag returns only
            // OK or NOT-FOUND over the relational store, so a true DB2 SQL failure cannot arise — there is no
            // live SQLCODE/SQLSTATE to render and the COBOL STRING's residual trailing bytes are SPACES on a
            // fresh COMMAREA, so the space-pad matches. Documented emulation boundary (no DB2 layer).
            ca.FrdUpdateStatus = "F";          // SET WS-FRD-UPDT-FAILED TO TRUE.
            ca.FrdActMsg = Fixed(" UPDT ERROR DB2: CODE:" + EditSqlCode(-1) + ", STATE: " + EditSqlState(0), 50);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Right-pads/truncates to a fixed COBOL X(width) field with spaces.</summary>
    private static string Fixed(string? v, int width)
    {
        v ??= "";
        return v.Length >= width ? v.Substring(0, width) : v.PadRight(width, ' ');
    }

    /// <summary>
    /// WS-SQLCODE PIC +9(06) rendering of an SQLCODE (FB #2 territory): leading sign + 6 zero-filled digits.
    /// Exact DB2 SQLCODE/SQLSTATE values are unverifiable against SQLite; characterization only.
    /// </summary>
    private static string EditSqlCode(int sqlcode) => EditedNumeric.Format(sqlcode, "+999999");

    /// <summary>WS-SQLSTATE PIC +9(09) rendering (FB #2: SQLSTATE forced into a numeric-edited field).</summary>
    private static string EditSqlState(long sqlstate) => EditedNumeric.Format(sqlstate, "+999999999");
}
