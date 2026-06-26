using System.Globalization;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COTRN02C</c> — the "Add Transaction" screen
/// (TRANSID <c>CT02</c>, BMS map <c>COTRN2A</c> / mapset <c>COTRN02</c>). It validates the operator's
/// key + data fields and, on a <c>Y</c> confirmation, writes a brand-new record to the TRANSACTION
/// (TRANSACT) file with a tran-id one greater than the current highest key.
/// </summary>
/// <remarks>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and a <c>// source: COTRN02C.cbl:NNN</c>
/// citation. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-private <c>CDEMO-CT02-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>The SEND-then-RETURN termination idiom.</b> <c>SEND-TRNADD-SCREEN</c> ends with
/// <c>EXEC CICS RETURN TRANSID(WS-TRANID)</c>, which terminates the task. Therefore every
/// <c>PERFORM SEND-TRNADD-SCREEN</c> in a validation/error branch is a hard exit: the statements after it
/// (and in its callers) do NOT execute. The port reproduces this by having <see cref="SendTrnaddScreen"/>
/// record the RETURN outcome on the context and by every caller bailing out as soon as
/// <c>ctx.Outcome is not null</c>.</para>
/// <para><b>VSAM → repository mapping.</b> The TRANSACT master is reached two ways: a descending browse to
/// find the highest existing tran-id (<c>MOVE HIGH-VALUES TO TRAN-ID</c>, <c>STARTBR</c> = position past the
/// last key, <c>READPREV</c> = read the last record, <c>ENDBR</c>) and a keyed <c>WRITE</c> of the new
/// record (= <see cref="TransactionRepository.Insert"/>). The XREF lookups use <c>CXACAIX</c> (read by
/// account-id alternate index = <see cref="CardXrefRepository.ReadByAltKey"/>) and <c>CCXREF</c> (read by
/// card-number primary key = <see cref="CardXrefRepository.ReadByKey"/>). The repository FileStatus is
/// mapped to the CICS RESP the COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0),
/// RecordNotFound('23')→NOTFND(13), EndOfFile('10')→ENDFILE(20), DuplicateKeyError('22')→DUPREC(14),
/// anything else→an OTHER/file-error.</para>
/// <para>Date validation uses <see cref="Csutldtc"/> (the LE <c>CSUTLDTC</c> stand-in) exactly as the COBOL
/// <c>CALL 'CSUTLDTC'</c>; the program tolerates a non-'0000' severity only when the message number is
/// <c>'2513'</c> (the bad-date-value condition), reproduced here.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>B-1 — In <c>VALIDATE-INPUT-KEY-FIELDS</c> the <c>IF ... IS NOT NUMERIC</c> guards <c>PERFORM
/// SEND-TRNADD-SCREEN</c> (which RETURNs), but were the program to continue it would still
/// <c>COMPUTE WS-ACCT-ID-N = FUNCTION NUMVAL(...)</c>. Because SEND terminates the task, the NUMVAL after a
/// non-numeric value is never reached at runtime — preserved by the outcome guard. source: COTRN02C.cbl:197-208</item>
/// <item>B-2 — The STARTBR-TRANSACT-FILE NOTFND / error branches drop the cursor on <c>ACTIDINL</c> and emit
/// "Transaction ID NOT found..." / "Unable to lookup Transaction..." even though the browse is over the new
/// record's predecessor; the messages name the wrong field/intent but are reproduced verbatim. source:
/// COTRN02C.cbl:655-668</item>
/// <item>B-3 — WRITE-TRANSACT-FILE's DUPKEY/DUPREC and OTHER branches drop the cursor on <c>ACTIDINL</c>
/// (the account field), not on any amount/data field. Reproduced. source: COTRN02C.cbl:740,747</item>
/// <item>B-4 — On a successful key validation by ACCT path, <c>MOVE WS-ACCT-ID-N TO ... ACTIDINI</c> (and the
/// card path likewise) rewrites the input field to the zero-suppressed-then-zero-filled numeric image, so a
/// short/blank-padded entry comes back zero-filled. Reproduced via the 9(11)/9(16) numeric MOVE. source:
/// COTRN02C.cbl:206-207,220-221</item>
/// <item>B-5 — <c>COPY-LAST-TRAN-DATA</c> (PF5) performs VALIDATE-INPUT-KEY-FIELDS then a descending browse
/// and, if no error, copies the last record's fields into the screen, then falls straight into
/// PROCESS-ENTER-KEY — re-validating + (on Y) immediately re-adding. Reproduced. source: COTRN02C.cbl:471-495</item>
/// <item>B-6 — <c>WS-USR-MODIFIED</c> is SET to NO at entry and never used anywhere; dead working storage.
/// Reproduced as an unused field. source: COTRN02C.cbl:49-51,110</item>
/// <item>B-7 — The amount re-edit (<c>NUMVAL-C</c> → <c>+99999999.99</c> → back into TRNAMTI) happens in
/// VALIDATE-INPUT-DATA-FIELDS before the confirm check, so even a NOT-confirmed ('N'/blank) attempt mutates
/// the displayed amount to its edited form. Reproduced. source: COTRN02C.cbl:383-386</item>
/// </list>
/// </remarks>
public sealed class Cotrn02c : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COTRN02C.cbl:35-60
    // =============================================================================================
    private const string WS_PGMNAME = "COTRN02C";       // 05 WS-PGMNAME PIC X(08) VALUE 'COTRN02C'. source: :36
    private const string WS_TRANID = "CT02";            // 05 WS-TRANID  PIC X(04) VALUE 'CT02'.     source: :37
    private const string WS_TRANSACT_FILE = "TRANSACT"; // 05 WS-TRANSACT-FILE PIC X(08).            source: :39
    private const string WS_ACCTDAT_FILE = "ACCTDAT ";  // 05 WS-ACCTDAT-FILE  PIC X(08).            source: :40
    private const string WS_CCXREF_FILE = "CCXREF  ";   // 05 WS-CCXREF-FILE   PIC X(08).            source: :41
    private const string WS_CXACAIX_FILE = "CXACAIX ";  // 05 WS-CXACAIX-FILE  PIC X(08).            source: :42

    private string _wsMessage = "";                     // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :44-46
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. source: :47-48
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-USR-MODIFIED PIC X(01) VALUE 'N'. 88 USR-MODIFIED-YES='Y'/USR-MODIFIED-NO='N'.
    // B-6: SET at entry, never read. source: :49-51
    private char _wsUsrModified = 'N';
    private void SetUsrModifiedNo() => _wsUsrModified = 'N';

    // 05 WS-TRAN-AMT  PIC +99999999.99 (edited; declared, unused here). source: :53
    // 05 WS-TRAN-DATE PIC X(08) VALUE '00/00/00' (declared, unused here). source: :54
    private long _wsAcctIdN;                            // 05 WS-ACCT-ID-N  PIC 9(11) VALUE 0.    source: :55
    private long _wsCardNumN;                           // 05 WS-CARD-NUM-N PIC 9(16) VALUE 0.    source: :56
    private decimal _wsTranIdN;                         // 05 WS-TRAN-ID-N  PIC 9(16) VALUE ZEROS. source: :57
    private decimal _wsTranAmtN;                        // 05 WS-TRAN-AMT-N PIC S9(9)V99 VALUE ZERO. source: :58
    private string _wsTranAmtE = "";                    // 05 WS-TRAN-AMT-E PIC +99999999.99.     source: :59
    private const string WS_DATE_FORMAT = "YYYY-MM-DD"; // 05 WS-DATE-FORMAT PIC X(10) VALUE 'YYYY-MM-DD'. source: :60

    // 01 CSUTLDTC-PARM — the date-validator interface. source: :62-69
    private string _csutldtcResult = "";

    // CCDA-* (COTTL01Y / CSMSG01Y) — shared header titles + the invalid-key message.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CT02-INFO (program-private state carried in the COMMAREA). source: :72-80
    // =============================================================================================
    private string _ct02TrnidFirst = "";   // 10 CDEMO-CT02-TRNID-FIRST   PIC X(16). source: :73
    private string _ct02TrnidLast = "";    // 10 CDEMO-CT02-TRNID-LAST    PIC X(16). source: :74
    private int _ct02PageNum;              // 10 CDEMO-CT02-PAGE-NUM      PIC 9(08). source: :75
    private char _ct02NextPageFlg = 'N';  // 10 CDEMO-CT02-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. source: :76-78
    private string _ct02TrnSelFlg = "";   // 10 CDEMO-CT02-TRN-SEL-FLG   PIC X(01). source: :79
    private string _ct02TrnSelected = ""; // 10 CDEMO-CT02-TRN-SELECTED  PIC X(16). source: :80

    // =============================================================================================
    //  TRAN-RECORD (CVTRA05Y) + TRAN-ID RIDFLD + CARD-XREF-RECORD (CVACT03Y). source: :88-90
    // =============================================================================================
    // TRAN-ID X(16): the STARTBR/READPREV/WRITE key. "" = LOW-VALUES, HighValues16 = HIGH-VALUES.
    private string _tranId = "";
    private const string HighValues16 = "\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF";

    private Transaction _tranRecord = new();   // TRAN-RECORD just read/built.
    // CARD-XREF-RECORD fields (XREF-ACCT-ID / XREF-CARD-NUM / XREF-CUST-ID).
    private long _xrefAcctId;
    private string _xrefCardNum = "";

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :71,82-93,99-101
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private TransactionRepository _transactions = null!;
    private CardXrefRepository _cardXref = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The TRANSACTION + CARD_XREF
    /// repositories are created from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public Cotrn02c(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cotrn02c() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COTRN02C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CT02 -> COTRN02C. source: CSD_TRANSACTIONS.md:83; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COTRN02C.cbl:107-159
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COTRN02 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null)
        {
            _transactions = new TransactionRepository(_db.Connection);
            _cardXref = new CardXrefRepository(_db.Connection);
        }

        _errFlgOn = false;          // SET ERR-FLG-OFF     TO TRUE. source: :109
        SetUsrModifiedNo();         // SET USR-MODIFIED-NO TO TRUE. source: :110

        _wsMessage = "";                                       // MOVE SPACES TO WS-MESSAGE. source: :112
        _map.Field("ERRMSG").SetValue("", setMdt: false);      //          ... ERRMSGO OF COTRN2AO. source: :113

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :115-117
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :119
            _commArea = ctx.CommArea!;
            RestoreCt02Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :120
                _commArea.SetReenter();                 // SET CDEMO-PGM-REENTER TO TRUE. source: :121
                MoveLowValuesToMapOut();                // MOVE LOW-VALUES TO COTRN2AO. source: :122
                _map.Field("ACTIDIN").CursorLength = -1; // MOVE -1 TO ACTIDINL OF COTRN2AI. source: :123

                // IF CDEMO-CT02-TRN-SELECTED NOT = SPACES AND LOW-VALUES. source: :124-125
                if (NotSpacesOrLow(_ct02TrnSelected))
                {
                    // MOVE CDEMO-CT02-TRN-SELECTED TO CARDNINI OF COTRN2AI. source: :126-127
                    _map.Field("CARDNIN").SetValue(_ct02TrnSelected, setMdt: false);
                    ProcessEnterKey(ctx);               // PERFORM PROCESS-ENTER-KEY. source: :128
                    if (ctx.Outcome is not null) return; // a SEND/RETURN inside terminated the task.
                }
                SendTrnaddScreen(ctx);                  // PERFORM SEND-TRNADD-SCREEN. source: :130
                if (ctx.Outcome is not null) return;
            }
            else
            {
                ReceiveTrnaddScreen(ctx);               // PERFORM RECEIVE-TRNADD-SCREEN. source: :132
                // EVALUATE EIBAID. source: :133
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);           // WHEN DFHENTER. source: :134-135
                        if (ctx.Outcome is not null) return;
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3. source: :136-143
                        if (IsSpacesOrLowValues(_commArea.FromProgram))
                            _commArea.ToProgram = "COMEN01C"; // MOVE 'COMEN01C' TO CDEMO-TO-PROGRAM. source: :138
                        else
                            _commArea.ToProgram = _commArea.FromProgram; // MOVE CDEMO-FROM-PROGRAM. source: :140-141
                        ReturnToPrevScreen(ctx);        // PERFORM RETURN-TO-PREV-SCREEN. source: :143
                        return;
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);        // WHEN DFHPF4. source: :144-145
                        if (ctx.Outcome is not null) return;
                        break;
                    case AidKey.Pf5:
                        CopyLastTranData(ctx);          // WHEN DFHPF5. source: :146-147
                        if (ctx.Outcome is not null) return;
                        break;
                    default:
                        // WHEN OTHER. source: :148-151
                        _errFlgOn = true;                          // MOVE 'Y' TO WS-ERR-FLG. source: :149
                        _wsMessage = CCDA_MSG_INVALID_KEY;         // MOVE CCDA-MSG-INVALID-KEY. source: :150
                        SendTrnaddScreen(ctx);                     // PERFORM SEND-TRNADD-SCREEN. source: :151
                        if (ctx.Outcome is not null) return;
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :156-159
        if (ctx.Outcome is null)
        {
            SaveCt02Info();
            ctx.ReturnTransId(WS_TRANID, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COTRN02C.cbl:164-188
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        ValidateInputKeyFields(ctx);                 // PERFORM VALIDATE-INPUT-KEY-FIELDS. source: :166
        if (ctx.Outcome is not null) return;
        ValidateInputDataFields(ctx);                // PERFORM VALIDATE-INPUT-DATA-FIELDS. source: :167
        if (ctx.Outcome is not null) return;

        // EVALUATE CONFIRMI OF COTRN2AI. source: :169-188
        string confirm = _map.Field("CONFIRM").Value;
        string c1 = confirm.Length > 0 ? confirm.Substring(0, 1) : "";
        if (c1 == "Y" || c1 == "y")
        {
            // WHEN 'Y' / 'y' -> PERFORM ADD-TRANSACTION. source: :170-172
            AddTransaction(ctx);
        }
        else if (c1 == "N" || c1 == "n" || IsSpacesOrLowValues(confirm))
        {
            // WHEN 'N' / 'n' / SPACES / LOW-VALUES. source: :173-181
            _errFlgOn = true;                                     // MOVE 'Y' TO WS-ERR-FLG. source: :177
            _wsMessage = "Confirm to add this transaction...";   // source: :178-179
            _map.Field("CONFIRM").CursorLength = -1;             // MOVE -1 TO CONFIRML. source: :180
            SendTrnaddScreen(ctx);                               // PERFORM SEND-TRNADD-SCREEN. source: :181
        }
        else
        {
            // WHEN OTHER. source: :182-187
            _errFlgOn = true;                                          // MOVE 'Y' TO WS-ERR-FLG. source: :183
            _wsMessage = "Invalid value. Valid values are (Y/N)...";  // source: :184-185
            _map.Field("CONFIRM").CursorLength = -1;                  // MOVE -1 TO CONFIRML. source: :186
            SendTrnaddScreen(ctx);                                    // PERFORM SEND-TRNADD-SCREEN. source: :187
        }
    }

    // =============================================================================================
    //  VALIDATE-INPUT-KEY-FIELDS — source: COTRN02C.cbl:193-230
    // =============================================================================================
    private void ValidateInputKeyFields(CicsContext ctx)
    {
        string actidin = _map.Field("ACTIDIN").Value;
        string cardnin = _map.Field("CARDNIN").Value;

        // EVALUATE TRUE. source: :195-230
        if (NotSpacesOrLow(actidin))
        {
            // WHEN ACTIDINI NOT = SPACES AND LOW-VALUES. source: :196
            if (!IsNumericX(actidin))
            {
                // IF ACTIDINI IS NOT NUMERIC. source: :197-203
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :198
                _wsMessage = "Account ID must be Numeric...";      // source: :199-200
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :201
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :202
                if (ctx.Outcome is not null) return;               // RETURN inside SEND ends the task.
            }
            // COMPUTE WS-ACCT-ID-N = FUNCTION NUMVAL(ACTIDINI). source: :204-205
            _wsAcctIdN = (long)NumVal(actidin);
            // MOVE WS-ACCT-ID-N TO XREF-ACCT-ID  ACTIDINI OF COTRN2AI. source: :206-207 (B-4: zero-fills the field)
            _xrefAcctId = _wsAcctIdN;
            _map.Field("ACTIDIN").SetValue(Zoned(_wsAcctIdN, 11), setMdt: false);
            ReadCxacaixFile(ctx);                                  // PERFORM READ-CXACAIX-FILE. source: :208
            if (ctx.Outcome is not null) return;
            // MOVE XREF-CARD-NUM TO CARDNINI OF COTRN2AI. source: :209
            _map.Field("CARDNIN").SetValue(_xrefCardNum, setMdt: false);
        }
        else if (NotSpacesOrLow(cardnin))
        {
            // WHEN CARDNINI NOT = SPACES AND LOW-VALUES. source: :210
            if (!IsNumericX(cardnin))
            {
                // IF CARDNINI IS NOT NUMERIC. source: :211-217
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :212
                _wsMessage = "Card Number must be Numeric...";     // source: :213-214
                _map.Field("CARDNIN").CursorLength = -1;           // MOVE -1 TO CARDNINL. source: :215
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :216
                if (ctx.Outcome is not null) return;
            }
            // COMPUTE WS-CARD-NUM-N = FUNCTION NUMVAL(CARDNINI). source: :218-219
            _wsCardNumN = (long)NumVal(cardnin);
            // MOVE WS-CARD-NUM-N TO XREF-CARD-NUM  CARDNINI OF COTRN2AI. source: :220-221 (B-4: zero-fills)
            _xrefCardNum = Zoned(_wsCardNumN, 16);
            _map.Field("CARDNIN").SetValue(_xrefCardNum, setMdt: false);
            ReadCcxrefFile(ctx);                                  // PERFORM READ-CCXREF-FILE. source: :222
            if (ctx.Outcome is not null) return;
            // MOVE XREF-ACCT-ID TO ACTIDINI OF COTRN2AI. source: :223
            _map.Field("ACTIDIN").SetValue(Zoned(_xrefAcctId, 11), setMdt: false);
        }
        else
        {
            // WHEN OTHER. source: :224-229
            _errFlgOn = true;                                            // MOVE 'Y' TO WS-ERR-FLG. source: :225
            _wsMessage = "Account or Card Number must be entered...";    // source: :226-227
            _map.Field("ACTIDIN").CursorLength = -1;                     // MOVE -1 TO ACTIDINL. source: :228
            SendTrnaddScreen(ctx);                                       // PERFORM SEND-TRNADD-SCREEN. source: :229
        }
    }

    // =============================================================================================
    //  VALIDATE-INPUT-DATA-FIELDS — source: COTRN02C.cbl:235-437
    // =============================================================================================
    private void ValidateInputDataFields(CicsContext ctx)
    {
        // IF ERR-FLG-ON -> blank all data fields. (Cannot fire here: ERR-FLG-ON would have RETURNed via SEND,
        // but kept verbatim for fidelity.) source: :237-249
        if (ErrFlgOn)
        {
            foreach (string f in new[]
                     { "TTYPCD", "TCATCD", "TRNSRC", "TRNAMT", "TDESC", "TORIGDT", "TPROCDT", "MID", "MNAME", "MCITY", "MZIP" })
                _map.Field(f).SetValue("", setMdt: false);
        }

        string ttypcd = _map.Field("TTYPCD").Value;
        string tcatcd = _map.Field("TCATCD").Value;
        string trnsrc = _map.Field("TRNSRC").Value;
        string tdesc = _map.Field("TDESC").Value;
        string trnamt = _map.Field("TRNAMT").Value;
        string torigdt = _map.Field("TORIGDT").Value;
        string tprocdt = _map.Field("TPROCDT").Value;
        string mid = _map.Field("MID").Value;
        string mname = _map.Field("MNAME").Value;
        string mcity = _map.Field("MCITY").Value;
        string mzip = _map.Field("MZIP").Value;

        // EVALUATE TRUE — empty checks, first match wins. source: :251-320
        if (IsSpacesOrLowValues(ttypcd))
            DataError(ctx, "Type CD can NOT be empty...", "TTYPCD");        // source: :252-257
        else if (IsSpacesOrLowValues(tcatcd))
            DataError(ctx, "Category CD can NOT be empty...", "TCATCD");    // source: :258-263
        else if (IsSpacesOrLowValues(trnsrc))
            DataError(ctx, "Source can NOT be empty...", "TRNSRC");         // source: :264-269
        else if (IsSpacesOrLowValues(tdesc))
            DataError(ctx, "Description can NOT be empty...", "TDESC");     // source: :270-275
        else if (IsSpacesOrLowValues(trnamt))
            DataError(ctx, "Amount can NOT be empty...", "TRNAMT");         // source: :276-281
        else if (IsSpacesOrLowValues(torigdt))
            DataError(ctx, "Orig Date can NOT be empty...", "TORIGDT");     // source: :282-287
        else if (IsSpacesOrLowValues(tprocdt))
            DataError(ctx, "Proc Date can NOT be empty...", "TPROCDT");     // source: :288-293
        else if (IsSpacesOrLowValues(mid))
            DataError(ctx, "Merchant ID can NOT be empty...", "MID");       // source: :294-299
        else if (IsSpacesOrLowValues(mname))
            DataError(ctx, "Merchant Name can NOT be empty...", "MNAME");   // source: :300-305
        else if (IsSpacesOrLowValues(mcity))
            DataError(ctx, "Merchant City can NOT be empty...", "MCITY");   // source: :306-311
        else if (IsSpacesOrLowValues(mzip))
            DataError(ctx, "Merchant Zip can NOT be empty...", "MZIP");     // source: :312-317
        // WHEN OTHER -> CONTINUE. source: :318-319
        if (ctx.Outcome is not null) return;

        // EVALUATE TRUE — Type CD / Category CD numeric checks. source: :322-337
        if (!IsNumericX(ttypcd))
            DataError(ctx, "Type CD must be Numeric...", "TTYPCD");         // source: :323-328
        else if (!IsNumericX(tcatcd))
            DataError(ctx, "Category CD must be Numeric...", "TCATCD");     // source: :329-334
        // WHEN OTHER -> CONTINUE. source: :335-336
        if (ctx.Outcome is not null) return;

        // EVALUATE TRUE — amount format -99999999.99 (positionally). source: :339-351
        // WHEN TRNAMTI(1:1) NOT '-' AND '+'  OR  (2:8) NOT NUMERIC  OR  (10:1) NOT '.'  OR  (11:2) NOT NUMERIC.
        string amt = PadX(trnamt, 12);
        char a1 = amt[0];
        if ((a1 != '-' && a1 != '+')
            || !IsNumericRun(amt, 1, 8)        // (2:8)
            || amt[9] != '.'                   // (10:1)
            || !IsNumericRun(amt, 10, 2))      // (11:2)
        {
            DataError(ctx, "Amount should be in format -99999999.99", "TRNAMT"); // source: :344-348
        }
        if (ctx.Outcome is not null) return;

        // EVALUATE TRUE — Orig Date format YYYY-MM-DD (positionally). source: :353-366
        string od = PadX(torigdt, 10);
        if (!IsNumericRun(od, 0, 4)            // (1:4)
            || od[4] != '-'                    // (5:1)
            || !IsNumericRun(od, 5, 2)         // (6:2)
            || od[7] != '-'                    // (8:1)
            || !IsNumericRun(od, 8, 2))        // (9:2)
        {
            DataError(ctx, "Orig Date should be in format YYYY-MM-DD", "TORIGDT"); // source: :359-363
        }
        if (ctx.Outcome is not null) return;

        // EVALUATE TRUE — Proc Date format YYYY-MM-DD (positionally). source: :368-381
        string pd = PadX(tprocdt, 10);
        if (!IsNumericRun(pd, 0, 4)            // (1:4)
            || pd[4] != '-'                    // (5:1)
            || !IsNumericRun(pd, 5, 2)         // (6:2)
            || pd[7] != '-'                    // (8:1)
            || !IsNumericRun(pd, 8, 2))        // (9:2)
        {
            DataError(ctx, "Proc Date should be in format YYYY-MM-DD", "TPROCDT"); // source: :374-378
        }
        if (ctx.Outcome is not null) return;

        // COMPUTE WS-TRAN-AMT-N = FUNCTION NUMVAL-C(TRNAMTI); MOVE -> WS-TRAN-AMT-E; -> TRNAMTI.
        // B-7: re-edits the displayed amount even when the add is not confirmed. source: :383-386
        TestNumValC(trnamt, out _wsTranAmtN);
        _wsTranAmtE = EditAmount(_wsTranAmtN);
        _map.Field("TRNAMT").SetValue(_wsTranAmtE, setMdt: false);

        // CALL 'CSUTLDTC' for the Orig Date; tolerate only severity '0000' or message '2513'. source: :389-407
        ValidateDateLe(ctx, _map.Field("TORIGDT").Value, "Orig Date - Not a valid date...", "TORIGDT");
        if (ctx.Outcome is not null) return;

        // CALL 'CSUTLDTC' for the Proc Date; tolerate only severity '0000' or message '2513'. source: :409-427
        ValidateDateLe(ctx, _map.Field("TPROCDT").Value, "Proc Date - Not a valid date...", "TPROCDT");
        if (ctx.Outcome is not null) return;

        // IF MIDI IS NOT NUMERIC -> error. source: :430-436
        if (!IsNumericX(_map.Field("MID").Value))
        {
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :431
            _wsMessage = "Merchant ID must be Numeric...";     // source: :432-433
            _map.Field("MID").CursorLength = -1;               // MOVE -1 TO MIDL. source: :434
            SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :435
        }
    }

    /// <summary>One empty/numeric data-field error: set ERR flag, message, cursor, and SEND (which RETURNs).</summary>
    private void DataError(CicsContext ctx, string message, string field)
    {
        _errFlgOn = true;                            // MOVE 'Y' TO WS-ERR-FLG.
        _wsMessage = message;                        // MOVE '...' TO WS-MESSAGE.
        _map.Field(field).CursorLength = -1;         // MOVE -1 TO xxxL.
        SendTrnaddScreen(ctx);                       // PERFORM SEND-TRNADD-SCREEN.
    }

    /// <summary>
    /// CALL 'CSUTLDTC' USING the date + 'YYYY-MM-DD' mask. Continue when severity '0000'; otherwise error
    /// only when the result message number is NOT '2513' (the bad-date-value condition CardDemo tolerates).
    /// source: COTRN02C.cbl:389-407,409-427
    /// </summary>
    private void ValidateDateLe(CicsContext ctx, string date, string message, string field)
    {
        // MOVE date TO CSUTLDTC-DATE; MOVE WS-DATE-FORMAT TO CSUTLDTC-DATE-FORMAT; MOVE SPACES TO RESULT.
        // CALL 'CSUTLDTC' — build the 80-byte CSUTLDTC-RESULT image (severity bytes 1-4, msg-num bytes 16-19).
        _csutldtcResult = Csutldtc(date, WS_DATE_FORMAT);

        // CSUTLDTC-RESULT-SEV-CD = bytes 1-4; CSUTLDTC-RESULT-MSG-NUM = bytes 16-19 of the 80-byte result.
        string sevCd = SafeSlice(_csutldtcResult, 0, 4);
        string msgNum = SafeSlice(_csutldtcResult, 15, 4);

        if (sevCd == "0000")
            return; // CONTINUE.

        if (msgNum != "2513")
        {
            _wsMessage = message;                    // MOVE '...Not a valid date...' TO WS-MESSAGE.
            _errFlgOn = true;                        // MOVE 'Y' TO WS-ERR-FLG.
            _map.Field(field).CursorLength = -1;     // MOVE -1 TO xxxL.
            SendTrnaddScreen(ctx);                   // PERFORM SEND-TRNADD-SCREEN.
        }
    }

    /// <summary>
    /// CSUTLDTC LE date-validator stand-in (the program's <c>CALL 'CSUTLDTC' USING date, 'YYYY-MM-DD', RESULT</c>).
    /// Returns the 80-byte CSUTLDTC-RESULT image whose only fields the caller reads are CSUTLDTC-RESULT-SEV-CD
    /// (bytes 1-4) and CSUTLDTC-RESULT-MSG-NUM (bytes 16-19): severity <c>'0000'</c> + message <c>'0000'</c>
    /// for a valid date; severity <c>'0012'</c> + message <c>'2513'</c> (LE bad-date-value condition, the one
    /// CardDemo tolerates) for any unparseable/invalid CCYY-MM-DD. The remaining bytes are spaces — the COBOL
    /// branches on nothing else. // source: COTRN02C.cbl:393-407, CSUTLDTC.cbl:128-149
    /// </summary>
    private static string Csutldtc(string date, string mask)
    {
        bool valid = mask.TrimEnd() == "YYYY-MM-DD"
            && DateTime.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        // sev(4) + 'Mesg Code:' filler(11) + msg-no(4) + space + verdict... — only sev/msg-no are read.
        string sev = valid ? "0000" : "0012";
        string msgNo = valid ? "0000" : "2513";
        string image = sev + "Mesg Code: " + msgNo;
        return image.PadRight(80, ' ');
    }

    // =============================================================================================
    //  ADD-TRANSACTION — source: COTRN02C.cbl:442-466
    // =============================================================================================
    private void AddTransaction(CicsContext ctx)
    {
        _tranId = HighValues16;                  // MOVE HIGH-VALUES TO TRAN-ID. source: :444
        StartbrTransactFile(ctx);                // PERFORM STARTBR-TRANSACT-FILE. source: :445
        if (ctx.Outcome is not null) return;
        ReadprevTransactFile(ctx);               // PERFORM READPREV-TRANSACT-FILE. source: :446
        if (ctx.Outcome is not null) return;
        EndbrTransactFile();                     // PERFORM ENDBR-TRANSACT-FILE. source: :447

        // MOVE TRAN-ID TO WS-TRAN-ID-N; ADD 1 TO WS-TRAN-ID-N. source: :448-449
        _wsTranIdN = ParseDecimal(_tranId);
        _wsTranIdN += 1;

        // INITIALIZE TRAN-RECORD; build the new record. source: :450-465
        _tranRecord = new Transaction();
        _tranRecord.TranId = Zoned(_wsTranIdN, 16);                       // MOVE WS-TRAN-ID-N TO TRAN-ID. source: :451
        _tranRecord.TypeCd = PadX(_map.Field("TTYPCD").Value, 2);        // MOVE TTYPCDI TO TRAN-TYPE-CD. source: :452
        _tranRecord.CatCd = (int)ParseDecimal(_map.Field("TCATCD").Value); // MOVE TCATCDI TO TRAN-CAT-CD. source: :453
        _tranRecord.Source = PadX(_map.Field("TRNSRC").Value, 10);       // MOVE TRNSRCI TO TRAN-SOURCE. source: :454
        _tranRecord.Desc = PadX(_map.Field("TDESC").Value, 100);        // MOVE TDESCI  TO TRAN-DESC. source: :455
        // COMPUTE WS-TRAN-AMT-N = FUNCTION NUMVAL-C(TRNAMTI); MOVE WS-TRAN-AMT-N TO TRAN-AMT. source: :456-458
        TestNumValC(_map.Field("TRNAMT").Value, out _wsTranAmtN);
        _tranRecord.Amt = TruncateToCents(_wsTranAmtN);                   // S9(9)V99 — truncate toward zero.
        _tranRecord.CardNum = PadX(_map.Field("CARDNIN").Value, 16);     // MOVE CARDNINI TO TRAN-CARD-NUM. source: :459
        _tranRecord.MerchantId = (long)ParseDecimal(_map.Field("MID").Value); // MOVE MIDI TO TRAN-MERCHANT-ID. source: :460
        _tranRecord.MerchantName = PadX(_map.Field("MNAME").Value, 50);  // MOVE MNAMEI TO TRAN-MERCHANT-NAME. source: :461
        _tranRecord.MerchantCity = PadX(_map.Field("MCITY").Value, 50);  // MOVE MCITYI TO TRAN-MERCHANT-CITY. source: :462
        _tranRecord.MerchantZip = PadX(_map.Field("MZIP").Value, 10);    // MOVE MZIPI  TO TRAN-MERCHANT-ZIP. source: :463
        _tranRecord.OrigTs = PadX(_map.Field("TORIGDT").Value, 26);      // MOVE TORIGDTI TO TRAN-ORIG-TS. source: :464
        _tranRecord.ProcTs = PadX(_map.Field("TPROCDT").Value, 26);      // MOVE TPROCDTI TO TRAN-PROC-TS. source: :465

        WriteTransactFile(ctx);                  // PERFORM WRITE-TRANSACT-FILE. source: :466
    }

    // =============================================================================================
    //  COPY-LAST-TRAN-DATA — source: COTRN02C.cbl:471-495
    // =============================================================================================
    private void CopyLastTranData(CicsContext ctx)
    {
        ValidateInputKeyFields(ctx);             // PERFORM VALIDATE-INPUT-KEY-FIELDS. source: :473
        if (ctx.Outcome is not null) return;

        _tranId = HighValues16;                  // MOVE HIGH-VALUES TO TRAN-ID. source: :475
        StartbrTransactFile(ctx);                // PERFORM STARTBR-TRANSACT-FILE. source: :476
        if (ctx.Outcome is not null) return;
        ReadprevTransactFile(ctx);               // PERFORM READPREV-TRANSACT-FILE. source: :477
        if (ctx.Outcome is not null) return;
        EndbrTransactFile();                     // PERFORM ENDBR-TRANSACT-FILE. source: :478

        // IF NOT ERR-FLG-ON -> copy the last record into the input fields. source: :480-493
        if (!ErrFlgOn)
        {
            _wsTranAmtE = EditAmount(_tranRecord.Amt);                              // MOVE TRAN-AMT TO WS-TRAN-AMT-E. source: :481
            _map.Field("TTYPCD").SetValue(_tranRecord.TypeCd, setMdt: false);       // source: :482
            _map.Field("TCATCD").SetValue(_tranRecord.CatCd.ToString(CultureInfo.InvariantCulture), setMdt: false); // source: :483
            _map.Field("TRNSRC").SetValue(_tranRecord.Source, setMdt: false);       // source: :484
            _map.Field("TRNAMT").SetValue(_wsTranAmtE, setMdt: false);              // MOVE WS-TRAN-AMT-E TO TRNAMTI. source: :485
            _map.Field("TDESC").SetValue(_tranRecord.Desc, setMdt: false);          // source: :486
            _map.Field("TORIGDT").SetValue(_tranRecord.OrigTs, setMdt: false);      // source: :487
            _map.Field("TPROCDT").SetValue(_tranRecord.ProcTs, setMdt: false);      // source: :488
            _map.Field("MID").SetValue(_tranRecord.MerchantId.ToString(CultureInfo.InvariantCulture), setMdt: false); // source: :489
            _map.Field("MNAME").SetValue(_tranRecord.MerchantName, setMdt: false);  // source: :490
            _map.Field("MCITY").SetValue(_tranRecord.MerchantCity, setMdt: false);  // source: :491
            _map.Field("MZIP").SetValue(_tranRecord.MerchantZip, setMdt: false);    // source: :492
        }

        ProcessEnterKey(ctx);                    // PERFORM PROCESS-ENTER-KEY. source: :495 (B-5)
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COTRN02C.cbl:500-511
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :502-504
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;       // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :505
        _commArea.FromProgram = WS_PGMNAME;     // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :506
        _commArea.SetFirstEntry();              // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :507

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :508-511
        SaveCt02Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-TRNADD-SCREEN — source: COTRN02C.cbl:516-534
    // =============================================================================================
    private void SendTrnaddScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                    // PERFORM POPULATE-HEADER-INFO. source: :518

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false);  // MOVE WS-MESSAGE TO ERRMSGO. source: :520

        // EXEC CICS SEND MAP('COTRN2A') MAPSET('COTRN02') FROM(COTRN2AO) ERASE CURSOR. source: :522-528
        ctx.SendMap("COTRN2A", "COTRN02", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,   // DFHMSD CTRL=(ALARM,FREEKB).
            Cursor = -1,     // CURSOR — honour the MOVE -1 TO xxxL set on the error field.
        });

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA) — ends the task. source: :530-534
        SaveCt02Info();
        ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // =============================================================================================
    //  RECEIVE-TRNADD-SCREEN — source: COTRN02C.cbl:539-547
    // =============================================================================================
    private void ReceiveTrnaddScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COTRN2A') MAPSET('COTRN02') INTO(COTRN2AI) RESP. source: :541-547
        ctx.ReceiveMap("COTRN2A", "COTRN02", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COTRN02C.cbl:552-571
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        DateTime now = ctx.Clock.Now;                                 // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :554

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false);  // MOVE CCDA-TITLE01 TO TITLE01O. source: :556
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false);  // MOVE CCDA-TITLE02 TO TITLE02O. source: :557
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);     // MOVE WS-TRANID  TO TRNNAMEO. source: :558
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);    // MOVE WS-PGMNAME TO PGMNAMEO. source: :559

        // CURDATEO = mm/dd/yy. source: :561-565
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);
        // CURTIMEO = hh:mm:ss. source: :567-571
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  READ-CXACAIX-FILE — read CARD-XREF via the acct-id alternate index. source: COTRN02C.cbl:576-604
    // =============================================================================================
    private void ReadCxacaixFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-CXACAIX-FILE) INTO(CARD-XREF-RECORD) RIDFLD(XREF-ACCT-ID) RESP. source: :578-586
        string fileStatus = _cardXref.ReadByAltKey(_xrefAcctId, out CardXref? xref);
        if (fileStatus == FileStatus.Ok && xref is not null)
        {
            _xrefCardNum = xref.XrefCardNum;
            _xrefAcctId = xref.AcctId;
        }
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :588-604
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :589-590
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). source: :591-596
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :592
                _wsMessage = "Account ID NOT found...";            // source: :593-594
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :595
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :596
                break;
            default:                                               // WHEN OTHER. source: :597-603
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :599
                _wsMessage = "Unable to lookup Acct in XREF AIX file..."; // source: :600-601
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :602
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :603
                break;
        }
    }

    // =============================================================================================
    //  READ-CCXREF-FILE — read CARD-XREF by card-number primary key. source: COTRN02C.cbl:609-637
    // =============================================================================================
    private void ReadCcxrefFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-CCXREF-FILE) INTO(CARD-XREF-RECORD) RIDFLD(XREF-CARD-NUM) RESP. source: :611-619
        string fileStatus = _cardXref.ReadByKey(_xrefCardNum, out CardXref? xref);
        if (fileStatus == FileStatus.Ok && xref is not null)
        {
            _xrefCardNum = xref.XrefCardNum;
            _xrefAcctId = xref.AcctId;
        }
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :621-637
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :622-623
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). source: :624-629
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :625
                _wsMessage = "Card Number NOT found...";           // source: :626-627
                _map.Field("CARDNIN").CursorLength = -1;           // MOVE -1 TO CARDNINL. source: :628
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :629
                break;
            default:                                               // WHEN OTHER. source: :630-636
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :632
                _wsMessage = "Unable to lookup Card # in XREF file..."; // source: :633-634
                _map.Field("CARDNIN").CursorLength = -1;           // MOVE -1 TO CARDNINL. source: :635
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :636
                break;
        }
    }

    // =============================================================================================
    //  STARTBR-TRANSACT-FILE — position the browse at-or-after TRAN-ID. source: COTRN02C.cbl:642-668
    // =============================================================================================
    private void StartbrTransactFile(CicsContext ctx)
    {
        // EXEC CICS STARTBR DATASET(WS-TRANSACT-FILE) RIDFLD(TRAN-ID) RESP. source: :644-650
        // GTEQ default: position at-or-after TRAN-ID. With TRAN-ID = HIGH-VALUES no key is >= 0xFF...FF, so
        // CICS returns NOTFND; the subsequent READPREV (after STARTBR positions past the end) returns the
        // last record. The relational browse peeks forward to learn whether a record exists at the position.
        if (_tranId.Length == 0)
            _transactions.StartBrowse();
        else
            _transactions.StartBrowse(_tranId);
        string fileStatus = PeekForwardExists() ? FileStatus.Ok : FileStatus.RecordNotFound;
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :652-668
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :653-654
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). B-2. source: :655-660
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :656
                _wsMessage = "Transaction ID NOT found...";        // source: :657-658
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :659
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :660
                break;
            default:                                               // WHEN OTHER. source: :661-667
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :663
                _wsMessage = "Unable to lookup Transaction...";    // source: :664-665
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :666
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :667
                break;
        }
    }

    // =============================================================================================
    //  READPREV-TRANSACT-FILE — read the prior record (the highest key). source: COTRN02C.cbl:673-697
    // =============================================================================================
    private void ReadprevTransactFile(CicsContext ctx)
    {
        // EXEC CICS READPREV DATASET(WS-TRANSACT-FILE) INTO(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :675-683
        string fileStatus = _transactions.ReadPrevious(out Transaction? prev);
        if (fileStatus == FileStatus.Ok && prev is not null)
        {
            _tranRecord = prev;
            _tranId = prev.TranId;        // RIDFLD updated with the key just read.
        }
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :685-697
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :686-687
                break;
            case Resp.EndFile:                                     // WHEN DFHRESP(ENDFILE) -> MOVE ZEROS TO TRAN-ID. source: :688-689
                _tranId = Zoned(0, 16);
                break;
            default:                                               // WHEN OTHER. source: :690-696
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :692
                _wsMessage = "Unable to lookup Transaction...";    // source: :693-694
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :695
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :696
                break;
        }
    }

    // =============================================================================================
    //  ENDBR-TRANSACT-FILE — source: COTRN02C.cbl:702-706
    // =============================================================================================
    private void EndbrTransactFile() => _transactions.EndBrowse(); // EXEC CICS ENDBR DATASET(WS-TRANSACT-FILE). source: :704

    // =============================================================================================
    //  WRITE-TRANSACT-FILE — keyed WRITE of the new record. source: COTRN02C.cbl:711-749
    // =============================================================================================
    private void WriteTransactFile(CicsContext ctx)
    {
        // EXEC CICS WRITE DATASET(WS-TRANSACT-FILE) FROM(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :713-721
        string fileStatus = _transactions.Insert(_tranRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :723-749
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL). source: :724-734
                InitializeAllFields();                             // PERFORM INITIALIZE-ALL-FIELDS. source: :725
                _wsMessage = "";                                   // MOVE SPACES TO WS-MESSAGE. source: :726
                // MOVE DFHGREEN TO ERRMSGC OF COTRN2AO — colour the message line green. source: :727
                _map.Field("ERRMSG").ColorOverride = BmsColor.Green;
                // STRING 'Transaction added successfully. ' ' Your Tran ID is ' TRAN-ID(DELIM SPACE) '.'. source: :728-733
                _wsMessage = "Transaction added successfully. " + " Your Tran ID is " +
                             _tranRecord.TranId.TrimEnd() + ".";
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :734
                break;
            case Resp.DupKey:                                      // WHEN DFHRESP(DUPKEY). source: :735
            case Resp.DupRec:                                      // WHEN DFHRESP(DUPREC). B-3. source: :736-741
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :737
                _wsMessage = "Tran ID already exist...";           // source: :738-739
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :740
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :741
                break;
            default:                                               // WHEN OTHER. source: :742-748
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :744
                _wsMessage = "Unable to Add Transaction...";       // source: :745-746
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :747
                SendTrnaddScreen(ctx);                             // PERFORM SEND-TRNADD-SCREEN. source: :748
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COTRN02C.cbl:754-757
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();                   // PERFORM INITIALIZE-ALL-FIELDS. source: :756
        SendTrnaddScreen(ctx);                   // PERFORM SEND-TRNADD-SCREEN. source: :757
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COTRN02C.cbl:762-779
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("ACTIDIN").CursorLength = -1;             // MOVE -1 TO ACTIDINL. source: :764
        // MOVE SPACES TO the input fields + WS-MESSAGE. source: :765-779
        foreach (string f in new[]
                 { "ACTIDIN", "CARDNIN", "TTYPCD", "TCATCD", "TRNSRC", "TRNAMT", "TDESC",
                   "TORIGDT", "TPROCDT", "MID", "MNAME", "MCITY", "MZIP", "CONFIRM" })
            _map.Field(f).SetValue("", setMdt: false);
        _wsMessage = "";
    }

    // =============================================================================================
    //  WS-RESP-CD mapper — repository FileStatus -> CICS RESP. source: EVALUATE WS-RESP-CD branches.
    // =============================================================================================
    private void SetResp(string fileStatus)
    {
        _wsRespCd = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,                 // '00' -> DFHRESP(NORMAL)
            FileStatus.EndOfFile => (int)Resp.EndFile,         // '10' -> DFHRESP(ENDFILE)
            FileStatus.RecordNotFound => (int)Resp.NotFnd,     // '23' -> DFHRESP(NOTFND)
            FileStatus.DuplicateKey => (int)Resp.DupRec,       // '02' -> DFHRESP(DUPREC)
            FileStatus.DuplicateKeyError => (int)Resp.DupRec,  // '22' (insert PK conflict) -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                              // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    /// <summary>
    /// CICS STARTBR with GTEQ returns NORMAL when at least one record is at-or-after the RID, NOTFND
    /// otherwise. The relational browse is lazy, so peek one row forward to learn whether any record exists,
    /// then re-position the cursor at the same start key for the subsequent READPREV.
    /// </summary>
    private bool PeekForwardExists()
    {
        string st = _transactions.ReadNext(out _);
        if (_tranId.Length == 0) _transactions.StartBrowse();
        else _transactions.StartBrowse(_tranId);
        return st == FileStatus.Ok;
    }

    /// <summary>MOVE LOW-VALUES TO COTRN2AO — blank every named output field + clear per-turn overrides. source: :122</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  CDEMO-CT02-INFO (de)serialize — carried across turns in the COMMAREA's customer slots.
    //  source: COTRN02C.cbl:72-80,119,156-159
    // =============================================================================================
    // COTRN02C never reads/writes CDEMO-CUSTOMER-INFO; the trailer is packed there so the program-private
    // state round-trips losslessly each turn. Pack into CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   TRNID-FIRST X(16) | TRNID-LAST X(16) | PAGE-NUM 9(8) | NEXT X(1) | SEL-FLG X(1) | TRN-SELECTED X(16).
    private void SaveCt02Info()
    {
        string packed =
            PadX(_ct02TrnidFirst, 16) +
            PadX(_ct02TrnidLast, 16) +
            Zoned(_ct02PageNum, 8) +
            (_ct02NextPageFlg == '\0' ? 'N' : _ct02NextPageFlg) +
            (_ct02TrnSelFlg.Length > 0 ? _ct02TrnSelFlg.Substring(0, 1) : " ") +
            PadX(_ct02TrnSelected, 16);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCt02Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        packed = PadX(packed, 75);
        _ct02TrnidFirst = packed.Substring(0, 16).TrimEnd();
        _ct02TrnidLast = packed.Substring(16, 16).TrimEnd();
        _ct02PageNum = (int)ParseLong(packed.Substring(32, 8));
        char nx = packed[40];
        _ct02NextPageFlg = nx == 'Y' ? 'Y' : 'N';
        char sf = packed[41];
        _ct02TrnSelFlg = sf == ' ' || sf == '\0' ? "" : sf.ToString();
        _ct02TrnSelected = packed.Substring(42, 16).TrimEnd();
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "NOT = SPACES AND LOW-VALUES" guard.</summary>
    private static bool NotSpacesOrLow(string? s) => !IsSpacesOrLowValues(s);

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL).</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// COBOL class test <c>field IS NUMERIC</c> on a fixed-width display field: every character must be a
    /// digit '0'-'9' (trailing spaces / low-values fail). An empty value is treated as non-numeric.
    /// </summary>
    private static bool IsNumericX(string? value)
    {
        string v = (value ?? "").TrimEnd('\0');
        if (v.Length == 0) return false;
        foreach (char c in v)
            if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>True when the <paramref name="len"/> characters of <paramref name="s"/> starting at <paramref name="start"/> are all digits — the COBOL reference-modification numeric class test.</summary>
    private static bool IsNumericRun(string s, int start, int len)
    {
        if (start + len > s.Length) return false;
        for (int i = start; i < start + len; i++)
            if (s[i] is < '0' or > '9') return false;
        return true;
    }

    /// <summary>FUNCTION NUMVAL: parse a (possibly signed/spaced) plain numeric; non-numeric → 0.</summary>
    private static decimal NumVal(string? v)
    {
        string t = (v ?? "").Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n) ? n : 0m;
    }

    /// <summary>
    /// FUNCTION NUMVAL-C(x) — parse currency: optional sign, thousands commas, currency symbol, one decimal
    /// point. Returns false when not parseable; <paramref name="result"/> is then 0.
    /// </summary>
    private static bool TestNumValC(string? value, out decimal result)
    {
        result = 0m;
        string t = (value ?? "").Trim();
        if (t.Length == 0) return false;
        bool neg = t.StartsWith("-") || t.EndsWith("-") || (t.StartsWith("(") && t.EndsWith(")"));
        var sb = new System.Text.StringBuilder();
        bool dot = false;
        foreach (char c in t)
        {
            if (c is >= '0' and <= '9') sb.Append(c);
            else if (c == '.' && !dot) { dot = true; sb.Append('.'); }
            else if (c is ',' or '+' or '-' or '$' or '(' or ')' or ' ') { /* skip */ }
            else return false;
        }
        if (sb.Length == 0 || (sb.Length == 1 && sb[0] == '.')) return false;
        if (!decimal.TryParse(sb.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n))
            return false;
        result = neg ? -n : n;
        return true;
    }

    /// <summary>
    /// Edit a numeric to PIC +99999999.99 (leading sign, 8 integer digits, '.', 2 decimals). Truncates
    /// toward zero to 2 decimals (no rounding) and keeps the low 8 integer digits on overflow.
    /// </summary>
    private static string EditAmount(decimal amt)
    {
        bool negative = amt < 0m;
        decimal mag = negative ? -amt : amt;
        long cents = (long)decimal.Truncate(mag * 100m); // toward-zero truncation, no rounding
        long intPart = cents / 100;
        long decPart = cents % 100;
        string ip = (intPart % 100000000L).ToString("D8");
        string dp = decPart.ToString("D2");
        return (negative ? "-" : "+") + ip + "." + dp;
    }

    /// <summary>MOVE numeric TO S9(9)V99 — truncate toward zero to 2 decimals, no rounding.</summary>
    private static decimal TruncateToCents(decimal v)
        => decimal.Truncate(v * 100m) / 100m;

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>Renders a numeric as a zero-padded zoned-decimal DISPLAY string of width <paramref name="width"/> (low-order truncated on overflow).</summary>
    private static string Zoned(decimal value, int width)
    {
        decimal mag = value < 0 ? -value : value;
        string s = decimal.Truncate(mag).ToString(CultureInfo.InvariantCulture);
        if (s.Length >= width) return s[^width..];
        return s.PadLeft(width, '0');
    }

    private static string Zoned(long value, int width) => Zoned((decimal)value, width);
    private static string Zoned(int value, int width) => Zoned((decimal)value, width);

    /// <summary>Parses a zoned/display digit string (ignoring non-digits) to a decimal; null/empty -> 0.</summary>
    private static decimal ParseDecimal(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0m;
        decimal v = 0m;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    /// <summary>Parses a digit string (ignoring non-digits) to a long; null/empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    /// <summary>Safe fixed-width slice of a string (the COBOL reference-modification read of a result field).</summary>
    private static string SafeSlice(string s, int start, int len)
    {
        if (s.Length < start + len) s = s.PadRight(start + len, ' ');
        return s.Substring(start, len);
    }

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    // =============================================================================================
    //  BMS map builder — COTRN2A in mapset COTRN02 (24x80).
    //  source: app/bms/COTRN02.bms:19-307 / SCREEN_COTRN02.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COTRN02.bms:26.</summary>
    public const string MapName = "COTRN2A";

    /// <summary>The DFHMSD mapset name. source: COTRN02.bms:19.</summary>
    public const string MapsetName = "COTRN02";

    /// <summary>
    /// Constructs the <c>COTRN2A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The <c>IC</c> cursor field is
    /// <c>ACTIDIN</c> (6,21). No PICIN/PICOUT and no JUSTIFY clauses appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                              // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                 // bms:34-37
            Out("TITLE01", 1, 21, 40, AskipFset, BmsColor.Yellow),             // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                             // bms:42-46
            OutInit("CURDATE", 1, 71, 8, AskipFset, BmsColor.Blue, "mm/dd/yy"),// bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                              // bms:52-56
            Out("PGMNAME", 2, 7, 8, AskipFset, BmsColor.Blue),                 // bms:57-60
            Out("TITLE02", 2, 21, 40, AskipFset, BmsColor.Yellow),             // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                             // bms:65-69
            OutInit("CURTIME", 2, 71, 8, AskipFset, BmsColor.Blue, "hh:mm:ss"),// bms:70-74

            // ----- 'Add Transaction' heading (bms:75-79) -----
            LitAttr(4, 30, 15, AskipBrt, BmsColor.Neutral, "Add Transaction"), // bms:75-79

            // ----- Enter Acct # / Card # (bms:80-110) -----
            Lit(6, 6, 13, BmsColor.Turquoise, "Enter Acct #:"),                // bms:80-84
            // ACTIDIN: ATTRB=(FSET,IC,NORM,UNPROT) GREEN UNDERLINE INITIAL ' ' — the IC cursor field.
            InField("ACTIDIN", 6, 21, 11, ic: true, initial: " "),             // bms:85-90
            Stopper(6, 33),                                                     // bms:91-93
            LitAttr(6, 37, 4, Askip, BmsColor.Neutral, "(or)"),                // bms:94-98
            Lit(6, 46, 7, BmsColor.Turquoise, "Card #:"),                      // bms:99-103
            InField("CARDNIN", 6, 55, 16, ic: false, initial: null),           // bms:104-108
            Stopper(6, 72),                                                     // bms:109-110

            // ----- horizontal rule (70 dashes) (bms:111-116) -----
            LitAttr(8, 6, 70, Askip, BmsColor.Neutral, new string('-', 70)),    // bms:111-116

            // ----- Type CD / Category CD / Source (bms:117-155) -----
            Lit(10, 6, 8, BmsColor.Turquoise, "Type CD:"),                     // bms:117-121
            InField("TTYPCD", 10, 15, 2, ic: false, initial: " "),             // bms:122-127
            Stopper(10, 18),                                                    // bms:128-129
            Lit(10, 23, 12, BmsColor.Turquoise, "Category CD:"),               // bms:130-134
            InField("TCATCD", 10, 36, 4, ic: false, initial: " "),             // bms:135-140
            Stopper(10, 41),                                                    // bms:141-142
            Lit(10, 46, 7, BmsColor.Turquoise, "Source:"),                     // bms:143-147
            InField("TRNSRC", 10, 54, 10, ic: false, initial: " "),            // bms:148-153
            Stopper(10, 65),                                                    // bms:154-155

            // ----- Description (bms:156-168) -----
            Lit(12, 6, 12, BmsColor.Turquoise, "Description:"),                // bms:156-160
            InField("TDESC", 12, 19, 60, ic: false, initial: " "),             // bms:161-166
            Stopper(12, 80),                                                    // bms:167-168

            // ----- Amount / Orig Date / Proc Date (bms:169-222) -----
            Lit(14, 6, 7, BmsColor.Turquoise, "Amount:"),                      // bms:169-173
            InField("TRNAMT", 14, 14, 12, ic: false, initial: " "),            // bms:174-179
            Stopper(14, 27),                                                    // bms:180-181
            Lit(14, 31, 10, BmsColor.Turquoise, "Orig Date:"),                 // bms:182-186
            InField("TORIGDT", 14, 42, 10, ic: false, initial: " "),           // bms:187-192
            Stopper(14, 53),                                                    // bms:193-194
            Lit(14, 57, 10, BmsColor.Turquoise, "Proc Date:"),                 // bms:195-199
            InField("TPROCDT", 14, 68, 10, ic: false, initial: " "),           // bms:200-205
            Stopper(14, 79),                                                    // bms:206-207
            Lit(15, 13, 14, BmsColor.Blue, "(-99999999.99)"),                  // bms:208-212
            Lit(15, 41, 12, BmsColor.Blue, "(YYYY-MM-DD)"),                    // bms:213-217
            Lit(15, 67, 12, BmsColor.Blue, "(YYYY-MM-DD)"),                    // bms:218-222

            // ----- Merchant ID / Name (bms:223-248) -----
            Lit(16, 6, 12, BmsColor.Turquoise, "Merchant ID:"),                // bms:223-227
            InField("MID", 16, 19, 9, ic: false, initial: " "),                // bms:228-233
            Stopper(16, 29),                                                    // bms:234-235
            Lit(16, 33, 14, BmsColor.Turquoise, "Merchant Name:"),             // bms:236-240
            InField("MNAME", 16, 48, 30, ic: false, initial: " "),             // bms:241-246
            Stopper(16, 79),                                                    // bms:247-248

            // ----- Merchant City / Zip (bms:249-274) -----
            Lit(18, 6, 14, BmsColor.Turquoise, "Merchant City:"),              // bms:249-253
            InField("MCITY", 18, 21, 25, ic: false, initial: " "),             // bms:254-259
            Stopper(18, 47),                                                    // bms:260-261
            Lit(18, 53, 13, BmsColor.Turquoise, "Merchant Zip:"),              // bms:262-266
            InField("MZIP", 18, 67, 10, ic: false, initial: " "),              // bms:267-272
            Stopper(18, 78),                                                    // bms:273-274

            // ----- confirmation prompt + CONFIRM input + (Y/N) (bms:275-292) -----
            Lit(21, 6, 55, BmsColor.Turquoise,
                "You are about to add this transaction. Please confirm :"),     // bms:275-280
            InField("CONFIRM", 21, 63, 1, ic: false, initial: null),           // bms:281-285
            Stopper(21, 65),                                                    // bms:286-287
            LitAttr(21, 66, 5, Askip, BmsColor.Neutral, "(Y/N)"),              // bms:288-292

            // ----- error line + footer (bms:293-302) -----
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:293-296
            Lit(24, 1, 53, BmsColor.Yellow,
                "ENTER=Continue  F3=Back  F4=Clear  F5=Copy Last Tran."),       // bms:297-302
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM), TURQUOISE-or-given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>
    /// Named keyable input field: ATTRB=(FSET,[IC,]NORM,UNPROT) GREEN HILIGHT=UNDERLINE. When
    /// <paramref name="initial"/> is non-null the BMS INITIAL=' ' single space is painted.
    /// </summary>
    private static ScreenField InField(string name, int row, int col, int len, bool ic, string? initial)
    {
        BmsAttribute attr = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected;
        if (ic) attr |= BmsAttribute.Ic;
        return new ScreenField
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = attr, Color = BmsColor.Green, Hilight = BmsHilight.Underline,
            Value = initial ?? "",
        };
    }

    /// <summary>A LENGTH=0 stopper field (attribute cell only, ATTRB=(ASKIP,NORM)).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };
}
