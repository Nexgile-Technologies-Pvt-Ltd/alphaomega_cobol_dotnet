using System.Globalization;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COBIL00C</c> — the "Bill Payment" screen
/// (TRANSID <c>CB00</c>, BMS map <c>COBIL0A</c> / mapset <c>COBIL00</c>). The operator enters an
/// 11-digit account id; the program displays that account's current balance and, on a <c>Y</c>
/// confirmation, pays the balance <b>in full</b>: it writes one new TRANSACTION record for the full
/// current balance (type <c>02</c>, category <c>2</c>, source <c>POS TERM</c>,
/// description <c>BILL PAYMENT - ONLINE</c>, merchant id <c>999999999</c> "BILL PAYMENT") and then
/// debits the account so the new current balance becomes <c>old balance − amount paid</c> (i.e. zero).
/// </summary>
/// <remarks>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and a <c>// source: COBIL00C.cbl:NNN</c>
/// citation. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-private <c>CDEMO-CB00-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>The SEND-then-RETURN termination idiom.</b> Unlike COTRN02C, <c>SEND-BILLPAY-SCREEN</c> here
/// does NOT itself RETURN — only the single <c>EXEC CICS RETURN</c> at the end of MAIN-PARA does
/// (source: :146-149). The COBOL keeps running after a <c>PERFORM SEND-BILLPAY-SCREEN</c> in an error
/// branch; control then falls out through the <c>IF NOT ERR-FLG-ON</c> guards (every later block is gated
/// on <c>NOT ERR-FLG-ON</c>) and reaches the final RETURN, which re-SENDs nothing — the last SEND already
/// painted the screen. The port therefore makes <see cref="SendBillpayScreen"/> just paint the symbolic
/// map (it does NOT record an outcome); the terminating <c>RETURN TRANSID('CB00')</c> is recorded once at
/// the end of <see cref="Handle"/>. The <c>ERR-FLG-ON</c> gates reproduce the "stop processing after an
/// error" behaviour, and because a later SEND would only overwrite with the same WS-MESSAGE state the
/// last-SEND-wins visual result is identical.</para>
/// <para><b>VSAM → repository mapping.</b> ACCTDAT <c>READ … UPDATE</c> = <see cref="AccountRepository.ReadByKey"/>
/// (the relational lock semantics collapse to a single task; FB-3); ACCTDAT <c>REWRITE</c> =
/// <see cref="AccountRepository.Update"/>; CXACAIX <c>READ</c> (alt index by acct id) =
/// <see cref="CardXrefRepository.ReadByAltKey"/>; the descending TRANSACT browse to find the highest
/// existing tran-id (<c>MOVE HIGH-VALUES TO TRAN-ID</c>, STARTBR = position past the last key, READPREV =
/// read the last record, ENDBR) uses <see cref="TransactionRepository.StartBrowse()"/> +
/// <see cref="TransactionRepository.ReadPrevious"/>; the new record is keyed-<c>WRITE</c>n via
/// <see cref="TransactionRepository.Insert"/>. Repository FileStatus maps to the CICS RESP the COBOL
/// <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13),
/// EndOfFile('10')→ENDFILE(20), DuplicateKey/DuplicateKeyError('02'/'22')→DUPREC(14), else→OTHER.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — Tran-id generation parses the X(16) tran-id as a 16-digit number, +1, re-stores zero-padded
/// to 16 digits (<c>TRAN-ID</c> X(16) → <c>WS-TRAN-ID-NUM</c> 9(16)); non-digit/over-width content
/// de-edits per COBOL alphanumeric→numeric rules. source: COBIL00C.cbl:57,216-219</item>
/// <item>FB-2 — <c>MOVE ACCT-CURR-BAL TO WS-CURR-BAL</c> / <c>MOVE WS-CURR-BAL TO CURBALI</c> run
/// UNCONDITIONALLY after the CONFIRM EVALUATE inside the <c>IF NOT ERR-FLG-ON</c> block, so the balance is
/// re-touched (with whatever <c>ACCT-CURR-BAL</c> currently holds, possibly the uninitialised value of a
/// failed read) even on the invalid-confirm path. source: COBIL00C.cbl:169-195</item>
/// <item>FB-3 — A blank confirm performs the ACCTDAT read (to display the balance) but never REWRITEs and
/// never writes a transaction; payment is gated only on CONF-PAY-YES, so blank confirm shows the balance +
/// the "Confirm to make a bill payment..." prompt. source: COBIL00C.cbl:182-184,208-240</item>
/// <item>FB-4 — No account active-status check and no floor beyond <c>ACCT-CURR-BAL &lt;= ZEROS</c>; a
/// closed/expired account with a positive balance is still paid. source: COBIL00C.cbl:197-206</item>
/// <item>FB-5 — The CXACAIX read's NOTFND message is <c>'Account ID NOT found...'</c> (wrong noun: it is the
/// XREF that was not found, message says Account). Kept verbatim. source: COBIL00C.cbl:423-426</item>
/// <item>FB-6 — <c>WS-TRAN-DATE PIC X(08) VALUE '00/00/00'</c> and <c>WS-TRAN-AMT PIC +99999999.99</c> are
/// declared but never used; the success message reports TRAN-ID, not the amount. Not wired up. source:
/// COBIL00C.cbl:55,58</item>
/// </list>
/// </remarks>
public sealed class BillPaymentProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COBIL00C.cbl:36-61
    // =============================================================================================
    private const string WS_PGMNAME = "COBIL00C";       // 05 WS-PGMNAME PIC X(08) VALUE 'COBIL00C'. source: :37
    private const string WS_TRANID = "CB00";            // 05 WS-TRANID  PIC X(04) VALUE 'CB00'.     source: :38
    private const string WS_TRANSACT_FILE = "TRANSACT"; // 05 WS-TRANSACT-FILE PIC X(08) VALUE 'TRANSACT'. source: :40
    private const string WS_ACCTDAT_FILE = "ACCTDAT ";  // 05 WS-ACCTDAT-FILE  PIC X(08) VALUE 'ACCTDAT '. source: :41
    private const string WS_CXACAIX_FILE = "CXACAIX ";  // 05 WS-CXACAIX-FILE  PIC X(08) VALUE 'CXACAIX '. source: :42

    private string _wsMessage = "";                     // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :39

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :43-45
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :46-47
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-USR-MODIFIED PIC X(01) VALUE 'N'. 88 USR-MODIFIED-YES='Y'/USR-MODIFIED-NO='N'.
    // SET at entry, never read elsewhere — dead working storage, reproduced. source: :48-50
    private char _wsUsrModified = 'N';
    private void SetUsrModifiedNo() => _wsUsrModified = 'N';

    // 05 WS-CONF-PAY-FLG PIC X(01) VALUE 'N'. 88 CONF-PAY-YES='Y'/CONF-PAY-NO='N'. source: :51-53
    private bool _confPayYes;
    private void SetConfPayNo() => _confPayYes = false;   // SET CONF-PAY-NO TO TRUE
    private void SetConfPayYes() => _confPayYes = true;   // SET CONF-PAY-YES TO TRUE

    // 05 WS-TRAN-AMT  PIC +99999999.99 (edited; declared, FB-6: unused). source: :55
    // 05 WS-CURR-BAL  PIC +9999999999.99 (edited; sign + 10 int digits + '.' + 2 dec, NOT zero-suppressed). source: :56
    private string _wsCurrBal = "";
    private decimal _wsTranIdNum;                        // 05 WS-TRAN-ID-NUM PIC 9(16) VALUE ZEROS. source: :57
    // 05 WS-TRAN-DATE PIC X(08) VALUE '00/00/00' (declared, FB-6: unused). source: :58
    // 05 WS-ABS-TIME / WS-CUR-DATE-X10 / WS-CUR-TIME-X08 — timestamp work fields. source: :59-61
    private string _wsCurDateX10 = "";
    private string _wsCurTimeX08 = "";
    private string _wsTimestamp = "";                   // WS-TIMESTAMP (CSDAT01Y) X(26).

    // CCDA-* (COTTL01Y / CSMSG01Y) — shared header titles + the invalid-key message.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";  // COTTL01Y CCDA-TITLE01
    private const string CCDA_TITLE02 = "              CardDemo                  ";  // COTTL01Y CCDA-TITLE02
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...";          // CSMSG01Y

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CB00-INFO (program-private state carried in the COMMAREA). source: :64-72
    // =============================================================================================
    private string _cb00TrnidFirst = "";   // 10 CDEMO-CB00-TRNID-FIRST   PIC X(16). source: :65
    private string _cb00TrnidLast = "";    // 10 CDEMO-CB00-TRNID-LAST    PIC X(16). source: :66
    private int _cb00PageNum;              // 10 CDEMO-CB00-PAGE-NUM      PIC 9(08). source: :67
    private char _cb00NextPageFlg = 'N';  // 10 CDEMO-CB00-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. source: :68-70
    private string _cb00TrnSelFlg = "";   // 10 CDEMO-CB00-TRN-SEL-FLG   PIC X(01). source: :71
    private string _cb00TrnSelected = ""; // 10 CDEMO-CB00-TRN-SELECTED  PIC X(16). source: :72

    // =============================================================================================
    //  Record areas — ACCOUNT-RECORD (CVACT01Y), CARD-XREF-RECORD (CVACT03Y), TRAN-RECORD (CVTRA05Y).
    //  source: :80-82 (COPY CVACT01Y / CVACT03Y / CVTRA05Y)
    // =============================================================================================
    // ACCT-ID 9(11) (RIDFLD) + ACCOUNT-RECORD just read for UPDATE.
    private long _acctId;
    private Account? _accountRecord;
    private decimal AcctCurrBal { get => _accountRecord?.CurrBal ?? 0m; set { if (_accountRecord is not null) _accountRecord.CurrBal = value; } }

    // XREF-ACCT-ID 9(11) (RIDFLD) + XREF-CARD-NUM X(16) consumed into TRAN-CARD-NUM.
    private long _xrefAcctId;
    private string _xrefCardNum = "";

    // TRAN-ID X(16) RIDFLD: "" = LOW-VALUES, HighValues16 = HIGH-VALUES.
    private string _tranId = "";
    private const string HighValues16 = "\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF";
    private Transaction _tranRecord = new();   // TRAN-RECORD built for the bill-pay WRITE.

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :63,91-93,111,146-149
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private AccountRepository _accounts = null!;
    private CardXrefRepository _cardXref = null!;
    private TransactionRepository _transactions = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The ACCOUNT / CARD_XREF / TRANSACTION
    /// repositories are created from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public BillPaymentProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public BillPaymentProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COBIL00C. source: :24

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CB00 -> COBIL00C. source: CSD_TRANSACTIONS.md:73; cbl:38

    // =============================================================================================
    //  MAIN-PARA — source: COBIL00C.cbl:99-149
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COBIL00 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null)
        {
            _accounts = new AccountRepository(_db.Connection);
            _cardXref = new CardXrefRepository(_db.Connection);
            _transactions = new TransactionRepository(_db.Connection);
        }

        _errFlgOn = false;          // SET ERR-FLG-OFF     TO TRUE. source: :101
        SetUsrModifiedNo();         // SET USR-MODIFIED-NO TO TRUE. source: :102

        _wsMessage = "";                                       // MOVE SPACES TO WS-MESSAGE. source: :104
        _map.Field("ERRMSG").SetValue("", setMdt: false);      //               ERRMSGO OF COBIL0AO. source: :105

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :107-109
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
            return; // XCTL terminates this task.
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :111
            _commArea = ctx.CommArea!;
            RestoreCb00Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :112
                _commArea.SetReenter();                 // SET CDEMO-PGM-REENTER TO TRUE. source: :113
                MoveLowValuesToMapOut();                // MOVE LOW-VALUES TO COBIL0AO. source: :114
                _map.Field("ACTIDIN").CursorLength = -1; // MOVE -1 TO ACTIDINL OF COBIL0AI. source: :115

                // IF CDEMO-CB00-TRN-SELECTED NOT = SPACES AND LOW-VALUES. source: :116-117
                if (NotSpacesOrLow(_cb00TrnSelected))
                {
                    // MOVE CDEMO-CB00-TRN-SELECTED TO ACTIDINI OF COBIL0AI. source: :118-119
                    _map.Field("ACTIDIN").SetValue(_cb00TrnSelected, setMdt: false);
                    ProcessEnterKey(ctx);               // PERFORM PROCESS-ENTER-KEY. source: :120
                }
                SendBillpayScreen(ctx);                 // PERFORM SEND-BILLPAY-SCREEN. source: :122
            }
            else
            {
                ReceiveBillpayScreen(ctx);              // PERFORM RECEIVE-BILLPAY-SCREEN. source: :124
                // EVALUATE EIBAID. source: :125-142
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);           // WHEN DFHENTER. source: :126-127
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3. source: :128-135
                        if (IsSpacesOrLowValues(_commArea.FromProgram))
                            _commArea.ToProgram = "COMEN01C"; // MOVE 'COMEN01C' TO CDEMO-TO-PROGRAM. source: :130
                        else
                            _commArea.ToProgram = _commArea.FromProgram; // MOVE CDEMO-FROM-PROGRAM. source: :132-133
                        ReturnToPrevScreen(ctx);        // PERFORM RETURN-TO-PREV-SCREEN. source: :135
                        return;                          // XCTL terminates this task.
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);        // WHEN DFHPF4. source: :136-137
                        break;
                    default:
                        // WHEN OTHER. source: :138-141
                        _errFlgOn = true;                          // MOVE 'Y' TO WS-ERR-FLG. source: :139
                        _wsMessage = CCDA_MSG_INVALID_KEY;         // MOVE CCDA-MSG-INVALID-KEY. source: :140
                        SendBillpayScreen(ctx);                    // PERFORM SEND-BILLPAY-SCREEN. source: :141
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :146-149
        SaveCb00Info();
        ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COBIL00C.cbl:154-244
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        SetConfPayNo();                                  // SET CONF-PAY-NO TO TRUE. source: :156

        // EVALUATE TRUE — empty account-id check. source: :158-167
        string actidin = _map.Field("ACTIDIN").Value;
        if (IsSpacesOrLowValues(actidin))
        {
            // WHEN ACTIDINI = SPACES OR LOW-VALUES. source: :159-164
            _errFlgOn = true;                            // MOVE 'Y' TO WS-ERR-FLG. source: :160
            _wsMessage = "Acct ID can NOT be empty...";  // source: :161-162
            _map.Field("ACTIDIN").CursorLength = -1;     // MOVE -1 TO ACTIDINL. source: :163
            SendBillpayScreen(ctx);                      // PERFORM SEND-BILLPAY-SCREEN. source: :164
        }
        // WHEN OTHER -> CONTINUE. source: :165-166

        // IF NOT ERR-FLG-ON. source: :169-195
        if (!ErrFlgOn)
        {
            // MOVE ACTIDINI OF COBIL0AI TO ACCT-ID  XREF-ACCT-ID. source: :170-171
            // Alphanumeric X(11) -> numeric 9(11) de-edit: keep digits (map has no PICIN/MUSTFILL).
            _acctId = (long)ParseDecimal(actidin);
            _xrefAcctId = _acctId;

            // EVALUATE CONFIRMI OF COBIL0AI. source: :173-191
            string confirm = _map.Field("CONFIRM").Value;
            string c1 = confirm.Length > 0 ? confirm.Substring(0, 1) : "";
            if (c1 == "Y" || c1 == "y")
            {
                // WHEN 'Y' / 'y' -> SET CONF-PAY-YES; PERFORM READ-ACCTDAT-FILE. source: :174-177
                SetConfPayYes();
                ReadAcctdatFile(ctx);
            }
            else if (c1 == "N" || c1 == "n")
            {
                // WHEN 'N' / 'n' -> PERFORM CLEAR-CURRENT-SCREEN; MOVE 'Y' TO WS-ERR-FLG. source: :178-181
                ClearCurrentScreen(ctx);
                _errFlgOn = true;
            }
            else if (IsSpacesOrLowValues(confirm))
            {
                // WHEN SPACES / LOW-VALUES -> PERFORM READ-ACCTDAT-FILE (read but do not pay; FB-3). source: :182-184
                ReadAcctdatFile(ctx);
            }
            else
            {
                // WHEN OTHER -> invalid Y/N. source: :185-190
                _errFlgOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :186
                _wsMessage = "Invalid value. Valid values are (Y/N)..."; // source: :187-188
                _map.Field("CONFIRM").CursorLength = -1;                 // MOVE -1 TO CONFIRML. source: :189
                SendBillpayScreen(ctx);                                  // PERFORM SEND-BILLPAY-SCREEN. source: :190
            }

            // FB-2: these two MOVEs run UNCONDITIONALLY after the CONFIRM EVALUATE, re-touching CURBALI with
            // whatever ACCT-CURR-BAL currently holds (possibly uninitialised on a failed/skipped read). source: :193-194
            _wsCurrBal = EditCurrBal(AcctCurrBal);                       // MOVE ACCT-CURR-BAL TO WS-CURR-BAL.
            _map.Field("CURBAL").SetValue(_wsCurrBal, setMdt: false);    // MOVE WS-CURR-BAL TO CURBALI.
        }

        // IF NOT ERR-FLG-ON. source: :197-206
        if (!ErrFlgOn)
        {
            // IF ACCT-CURR-BAL <= ZEROS AND ACTIDINI NOT = SPACES AND LOW-VALUES (FB-4: only gate). source: :198-205
            if (AcctCurrBal <= 0m && NotSpacesOrLow(_map.Field("ACTIDIN").Value))
            {
                _errFlgOn = true;                            // MOVE 'Y' TO WS-ERR-FLG. source: :200
                _wsMessage = "You have nothing to pay...";   // source: :201-202
                _map.Field("ACTIDIN").CursorLength = -1;     // MOVE -1 TO ACTIDINL. source: :203
                SendBillpayScreen(ctx);                      // PERFORM SEND-BILLPAY-SCREEN. source: :204
            }
        }

        // IF NOT ERR-FLG-ON. source: :208-244
        if (!ErrFlgOn)
        {
            if (_confPayYes)
            {
                // IF CONF-PAY-YES — the actual payment. source: :210-235
                ReadCxacaixFile(ctx);                        // PERFORM READ-CXACAIX-FILE. source: :211

                _tranId = HighValues16;                      // MOVE HIGH-VALUES TO TRAN-ID. source: :212
                StartbrTransactFile(ctx);                    // PERFORM STARTBR-TRANSACT-FILE. source: :213
                ReadprevTransactFile(ctx);                   // PERFORM READPREV-TRANSACT-FILE. source: :214
                EndbrTransactFile();                         // PERFORM ENDBR-TRANSACT-FILE. source: :215

                // MOVE TRAN-ID TO WS-TRAN-ID-NUM; ADD 1 TO WS-TRAN-ID-NUM (FB-1). source: :216-217
                _wsTranIdNum = ParseDecimal(_tranId);
                _wsTranIdNum += 1;

                // INITIALIZE TRAN-RECORD; build it. source: :218-229
                _tranRecord = new Transaction();
                _tranRecord.TranId = Zoned(_wsTranIdNum, 16);            // MOVE WS-TRAN-ID-NUM TO TRAN-ID. source: :219
                _tranRecord.TypeCd = "02";                               // MOVE '02' TO TRAN-TYPE-CD. source: :220
                _tranRecord.CatCd = 2;                                   // MOVE 2 TO TRAN-CAT-CD. source: :221
                _tranRecord.Source = PadX("POS TERM", 10);              // MOVE 'POS TERM' TO TRAN-SOURCE. source: :222
                _tranRecord.Desc = PadX("BILL PAYMENT - ONLINE", 100); // MOVE 'BILL PAYMENT - ONLINE' TO TRAN-DESC. source: :223
                _tranRecord.Amt = Decimals.Store(AcctCurrBal, 9, 2, true); // MOVE ACCT-CURR-BAL (S9(10)V99) TO TRAN-AMT (S9(9)V99) — high-order truncate int part to 9 digits (mod 10^9). source: :224
                _tranRecord.CardNum = PadX(_xrefCardNum, 16);          // MOVE XREF-CARD-NUM TO TRAN-CARD-NUM. source: :225
                _tranRecord.MerchantId = 999999999;                     // MOVE 999999999 TO TRAN-MERCHANT-ID. source: :226
                _tranRecord.MerchantName = PadX("BILL PAYMENT", 50);   // MOVE 'BILL PAYMENT' TO TRAN-MERCHANT-NAME. source: :227
                _tranRecord.MerchantCity = PadX("N/A", 50);            // MOVE 'N/A' TO TRAN-MERCHANT-CITY. source: :228
                _tranRecord.MerchantZip = PadX("N/A", 10);             // MOVE 'N/A' TO TRAN-MERCHANT-ZIP. source: :229

                GetCurrentTimestamp(ctx);                               // PERFORM GET-CURRENT-TIMESTAMP. source: :230
                _tranRecord.OrigTs = PadX(_wsTimestamp, 26);           // MOVE WS-TIMESTAMP TO TRAN-ORIG-TS. source: :231
                _tranRecord.ProcTs = PadX(_wsTimestamp, 26);           //                     TRAN-PROC-TS. source: :232

                WriteTransactFile(ctx);                                 // PERFORM WRITE-TRANSACT-FILE. source: :233

                // COMPUTE ACCT-CURR-BAL = ACCT-CURR-BAL - TRAN-AMT (truncate toward zero, no rounding). source: :234
                AcctCurrBal = TruncateToCents(AcctCurrBal - _tranRecord.Amt);
                UpdateAcctdatFile(ctx);                                 // PERFORM UPDATE-ACCTDAT-FILE. source: :235
            }
            else
            {
                // ELSE (not confirmed yet). source: :236-240
                _wsMessage = "Confirm to make a bill payment..."; // source: :237-238
                _map.Field("CONFIRM").CursorLength = -1;          // MOVE -1 TO CONFIRML. source: :239
            }

            SendBillpayScreen(ctx);                              // PERFORM SEND-BILLPAY-SCREEN. source: :242
        }
    }

    // =============================================================================================
    //  GET-CURRENT-TIMESTAMP — source: COBIL00C.cbl:249-267
    // =============================================================================================
    private void GetCurrentTimestamp(CicsContext ctx)
    {
        // EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME) + FORMATTIME YYYYMMDD/DATESEP('-') TIME/TIMESEP(':'). source: :251-261
        DateTime now = ctx.Clock.Now;
        _wsCurDateX10 = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // WS-CUR-DATE-X10 (CCYY-MM-DD). source: :257-258
        _wsCurTimeX08 = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);   // WS-CUR-TIME-X08 (HH:MM:SS). source: :259-260

        // INITIALIZE WS-TIMESTAMP; (01:10)=date; (12:08)=time; WS-TIMESTAMP-TM-MS6 = ZEROS. source: :263-266
        // Result: 'CCYY-MM-DD HH:MM:SS.000000' (26 chars). Byte 11 is the space joining date and time.
        _wsTimestamp = _wsCurDateX10 + " " + _wsCurTimeX08 + ".000000";
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COBIL00C.cbl:273-284
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :275-277
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;       // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :278
        _commArea.FromProgram = WS_PGMNAME;     // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :279
        _commArea.SetFirstEntry();              // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :280

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :281-284
        SaveCb00Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-BILLPAY-SCREEN — source: COBIL00C.cbl:289-301
    // =============================================================================================
    // NOTE: unlike COTRN02C, SEND-BILLPAY-SCREEN does NOT RETURN; it only paints the map. The single
    // EXEC CICS RETURN at the end of MAIN-PARA ends the task. See the class remarks.
    private void SendBillpayScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                    // PERFORM POPULATE-HEADER-INFO. source: :291

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false);  // MOVE WS-MESSAGE TO ERRMSGO. source: :293

        // EXEC CICS SEND MAP('COBIL0A') MAPSET('COBIL00') FROM(COBIL0AO) ERASE CURSOR. source: :295-301
        ctx.SendMap("COBIL0A", "COBIL00", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,   // DFHMSD CTRL=(ALARM,FREEKB).
            Cursor = -1,     // CURSOR — honour the MOVE -1 TO xxxL set on the error/cursor field.
        });
    }

    // =============================================================================================
    //  RECEIVE-BILLPAY-SCREEN — source: COBIL00C.cbl:306-314
    // =============================================================================================
    private void ReceiveBillpayScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COBIL0A') MAPSET('COBIL00') INTO(COBIL0AI) RESP/RESP2 (RESP not checked). source: :308-314
        ctx.ReceiveMap("COBIL0A", "COBIL00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COBIL00C.cbl:319-338
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        DateTime now = ctx.Clock.Now;                                 // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :321

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false);  // MOVE CCDA-TITLE01 TO TITLE01O. source: :323
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false);  // MOVE CCDA-TITLE02 TO TITLE02O. source: :324
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);     // MOVE WS-TRANID  TO TRNNAMEO. source: :325
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);    // MOVE WS-PGMNAME TO PGMNAMEO. source: :326

        // CURDATEO = mm/dd/yy (year = last two digits, WS-CURDATE-YEAR(3:2)). source: :328-332
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);
        // CURTIMEO = hh:mm:ss. source: :334-338
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  READ-ACCTDAT-FILE — READ ACCTDAT … UPDATE by ACCT-ID. source: COBIL00C.cbl:343-372
    // =============================================================================================
    private void ReadAcctdatFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-ACCTDAT-FILE) INTO(ACCOUNT-RECORD) RIDFLD(ACCT-ID) UPDATE RESP. source: :345-354
        // READ … UPDATE collapses to a keyed SELECT here; the matching REWRITE happens in the same task (FB-3).
        string fileStatus = _accounts.ReadByKey(_acctId, out _accountRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :356-372
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :357-358
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). source: :359-364
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :360
                _wsMessage = "Account ID NOT found...";            // source: :361-362
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :363
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :364
                break;
            default:                                               // WHEN OTHER. source: :365-371
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :366
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :367
                _wsMessage = "Unable to lookup Account...";        // source: :368-369
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :370
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :371
                break;
        }
    }

    // =============================================================================================
    //  UPDATE-ACCTDAT-FILE — REWRITE ACCTDAT. source: COBIL00C.cbl:377-403
    // =============================================================================================
    private void UpdateAcctdatFile(CicsContext ctx)
    {
        // EXEC CICS REWRITE DATASET(WS-ACCTDAT-FILE) FROM(ACCOUNT-RECORD) RESP. source: :379-385
        string fileStatus = _accountRecord is null ? FileStatus.RecordNotFound : _accounts.Update(_accountRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :387-403
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :388-389
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). source: :390-395
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :391
                _wsMessage = "Account ID NOT found...";            // source: :392-393
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :394
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :395
                break;
            default:                                               // WHEN OTHER. source: :396-402
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :397
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :398
                _wsMessage = "Unable to Update Account...";        // source: :399-400
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :401
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :402
                break;
        }
    }

    // =============================================================================================
    //  READ-CXACAIX-FILE — READ CXACAIX (alt index by acct id). source: COBIL00C.cbl:408-436
    // =============================================================================================
    private void ReadCxacaixFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-CXACAIX-FILE) INTO(CARD-XREF-RECORD) RIDFLD(XREF-ACCT-ID) RESP. source: :410-418
        string fileStatus = _cardXref.ReadByAltKey(_xrefAcctId, out CardXref? xref);
        if (fileStatus == FileStatus.Ok && xref is not null)
        {
            _xrefCardNum = xref.XrefCardNum;
            _xrefAcctId = xref.AcctId;
        }
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :420-436
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :421-422
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). FB-5 (wrong noun). source: :423-428
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :424
                _wsMessage = "Account ID NOT found...";            // source: :425-426
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :427
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :428
                break;
            default:                                               // WHEN OTHER. source: :429-435
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :430
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :431
                _wsMessage = "Unable to lookup XREF AIX file...";  // source: :432-433
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :434
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :435
                break;
        }
    }

    // =============================================================================================
    //  STARTBR-TRANSACT-FILE — position the browse at-or-after TRAN-ID. source: COBIL00C.cbl:441-467
    // =============================================================================================
    private void StartbrTransactFile(CicsContext ctx)
    {
        // EXEC CICS STARTBR DATASET(WS-TRANSACT-FILE) RIDFLD(TRAN-ID) RESP. source: :443-449
        // With TRAN-ID = HIGH-VALUES the browse is positioned past the last key; READPREV then returns the
        // highest record. The relational browse: position at start, peek to learn if any record exists.
        _transactions.StartBrowse();
        string fileStatus = PeekForwardExists() ? FileStatus.Ok : FileStatus.RecordNotFound;
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :451-467
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :452-453
                break;
            case Resp.NotFnd:                                       // WHEN DFHRESP(NOTFND). source: :454-459
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :455
                _wsMessage = "Transaction ID NOT found...";        // source: :456-457
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :458
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :459
                break;
            default:                                               // WHEN OTHER. source: :460-466
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :461
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :462
                _wsMessage = "Unable to lookup Transaction...";    // source: :463-464
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :465
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :466
                break;
        }
    }

    // =============================================================================================
    //  READPREV-TRANSACT-FILE — read the prior record (the highest key). source: COBIL00C.cbl:472-496
    // =============================================================================================
    private void ReadprevTransactFile(CicsContext ctx)
    {
        // EXEC CICS READPREV DATASET(WS-TRANSACT-FILE) INTO(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :474-482
        string fileStatus = _transactions.ReadPrevious(out Transaction? prev);
        if (fileStatus == FileStatus.Ok && prev is not null)
        {
            _tranRecord = prev;
            _tranId = prev.TranId;        // RIDFLD updated with the key just read.
        }
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :484-496
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL) -> CONTINUE. source: :485-486
                break;
            case Resp.EndFile:                                     // WHEN DFHRESP(ENDFILE) -> MOVE ZEROS TO TRAN-ID. source: :487-488
                _tranId = Zoned(0, 16);
                break;
            default:                                               // WHEN OTHER. source: :489-495
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :490
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :491
                _wsMessage = "Unable to lookup Transaction...";    // source: :492-493
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :494
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :495
                break;
        }
    }

    // =============================================================================================
    //  ENDBR-TRANSACT-FILE — source: COBIL00C.cbl:501-505
    // =============================================================================================
    private void EndbrTransactFile() => _transactions.EndBrowse(); // EXEC CICS ENDBR DATASET(WS-TRANSACT-FILE). source: :503-505

    // =============================================================================================
    //  WRITE-TRANSACT-FILE — keyed WRITE of the new bill-pay record. source: COBIL00C.cbl:510-547
    // =============================================================================================
    private void WriteTransactFile(CicsContext ctx)
    {
        // EXEC CICS WRITE DATASET(WS-TRANSACT-FILE) FROM(TRAN-RECORD) RIDFLD(TRAN-ID) RESP. source: :512-520
        string fileStatus = _transactions.Insert(_tranRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :522-547
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:                                       // WHEN DFHRESP(NORMAL). source: :523-532
                InitializeAllFields();                             // PERFORM INITIALIZE-ALL-FIELDS. source: :524
                _wsMessage = "";                                   // MOVE SPACES TO WS-MESSAGE. source: :525
                // MOVE DFHGREEN TO ERRMSGC OF COBIL0AO — colour the message line green. source: :526
                _map.Field("ERRMSG").ColorOverride = BmsColor.Green;
                // STRING 'Payment successful. ' ' Your Transaction ID is ' TRAN-ID(DELIM SPACE) '.'. source: :527-531
                // 'Payment successful. ' has a trailing space and the next literal a leading space -> TWO spaces.
                // TRAN-ID DELIMITED BY SPACE -> the digits up to the first space (none, so all 16). source: §9.
                _wsMessage = "Payment successful. " + " Your Transaction ID is "
                             + DelimBySpace(_tranRecord.TranId) + ".";
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :532
                break;
            case Resp.DupKey:                                      // WHEN DFHRESP(DUPKEY). source: :533
            case Resp.DupRec:                                      // WHEN DFHRESP(DUPREC). source: :534-539
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :535
                _wsMessage = "Tran ID already exist...";           // source: :536-537
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :538
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :539
                break;
            default:                                               // WHEN OTHER. source: :540-546
                // DISPLAY 'RESP:' ... 'REAS:' ... (operator console trace). source: :541
                _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :542
                _wsMessage = "Unable to Add Bill pay Transaction..."; // source: :543-544
                _map.Field("ACTIDIN").CursorLength = -1;           // MOVE -1 TO ACTIDINL. source: :545
                SendBillpayScreen(ctx);                            // PERFORM SEND-BILLPAY-SCREEN. source: :546
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COBIL00C.cbl:552-555
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();                   // PERFORM INITIALIZE-ALL-FIELDS. source: :554
        SendBillpayScreen(ctx);                  // PERFORM SEND-BILLPAY-SCREEN. source: :555
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COBIL00C.cbl:560-566
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("ACTIDIN").CursorLength = -1;             // MOVE -1 TO ACTIDINL. source: :562
        // MOVE SPACES TO ACTIDINI CURBALI CONFIRMI WS-MESSAGE. source: :563-566
        _map.Field("ACTIDIN").SetValue("", setMdt: false);
        _map.Field("CURBAL").SetValue("", setMdt: false);
        _map.Field("CONFIRM").SetValue("", setMdt: false);
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
    /// then re-position at the first row for the subsequent (descending) READPREV.
    /// </summary>
    private bool PeekForwardExists()
    {
        string st = _transactions.ReadNext(out _);
        _transactions.StartBrowse();
        return st == FileStatus.Ok;
    }

    /// <summary>MOVE LOW-VALUES TO COBIL0AO — blank every named output field + clear per-turn overrides. source: :114</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  CDEMO-CB00-INFO (de)serialize — carried across turns in the COMMAREA. source: :63-72,111,146-149
    // =============================================================================================
    // COBIL00C never reads/writes CDEMO-CUSTOMER-INFO; the program-private CB00 trailer is packed into the
    // customer name slots (CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes) so it round-trips losslessly
    // each turn (only CDEMO-CB00-TRN-SELECTED is read by this program; the rest are carried but unused):
    //   TRNID-FIRST X(16) | TRNID-LAST X(16) | PAGE-NUM 9(8) | NEXT X(1) | SEL-FLG X(1) | TRN-SELECTED X(16).
    private void SaveCb00Info()
    {
        string packed =
            PadX(_cb00TrnidFirst, 16) +
            PadX(_cb00TrnidLast, 16) +
            Zoned(_cb00PageNum, 8) +
            (_cb00NextPageFlg == '\0' ? 'N' : _cb00NextPageFlg) +
            (_cb00TrnSelFlg.Length > 0 ? _cb00TrnSelFlg.Substring(0, 1) : " ") +
            PadX(_cb00TrnSelected, 16);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCb00Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        packed = PadX(packed, 75);
        _cb00TrnidFirst = packed.Substring(0, 16).TrimEnd();
        _cb00TrnidLast = packed.Substring(16, 16).TrimEnd();
        _cb00PageNum = (int)ParseLong(packed.Substring(32, 8));
        char nx = packed[40];
        _cb00NextPageFlg = nx == 'Y' ? 'Y' : 'N';
        char sf = packed[41];
        _cb00TrnSelFlg = sf == ' ' || sf == '\0' ? "" : sf.ToString();
        _cb00TrnSelected = packed.Substring(42, 16).TrimEnd();
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
    /// COBOL <c>STRING ... DELIMITED BY SPACE</c>: copy a field up to (not including) its first space. The
    /// 16-digit zero-padded tran id has no embedded space, so the whole string is emitted (e.g. 0000000000000123).
    /// </summary>
    private static string DelimBySpace(string? value)
    {
        string v = value ?? "";
        int i = v.IndexOf(' ');
        return i < 0 ? v : v.Substring(0, i);
    }

    /// <summary>
    /// Edit a numeric to PIC +9999999999.99 — leading sign ('+' for non-negative, '-' for negative), 10
    /// integer digits with leading ZEROS (this PIC is NOT zero-suppressed), '.', 2 decimals. Truncates
    /// toward zero to 2 decimals (no rounding) and keeps the low 10 integer digits on overflow. source: :56,193-194
    /// </summary>
    private static string EditCurrBal(decimal bal)
    {
        bool negative = bal < 0m;
        decimal mag = negative ? -bal : bal;
        long cents = (long)decimal.Truncate(mag * 100m); // toward-zero truncation, no rounding
        long intPart = cents / 100;
        long decPart = cents % 100;
        string ip = (intPart % 10000000000L).ToString("D10");
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

    private static string Zoned(int value, int width) => Zoned((decimal)value, width);

    /// <summary>Parses a zoned/display digit string (ignoring non-digits) to a decimal; null/empty -> 0 (FB-1 X→9 de-edit).</summary>
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

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    // =============================================================================================
    //  BMS map builder — COBIL0A in mapset COBIL00 (24x80).
    //  source: app/bms/COBIL00.bms:19-137 / SCREEN_COBIL00.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COBIL00.bms:26.</summary>
    public const string MapName = "COBIL0A";

    /// <summary>The DFHMSD mapset name. source: COBIL00.bms:19.</summary>
    public const string MapsetName = "COBIL00";

    /// <summary>
    /// Constructs the <c>COBIL0A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
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

            // ----- 'Bill Payment' bright heading (bms:75-79) -----
            LitAttr(4, 35, 12, AskipBrt, BmsColor.Neutral, "Bill Payment"),    // bms:75-79

            // ----- Enter Acct ID + input (bms:80-93) -----
            Lit(6, 6, 14, BmsColor.Green, "Enter Acct ID:"),                   // bms:80-84
            // ACTIDIN: ATTRB=(FSET,IC,NORM,UNPROT) GREEN HILIGHT=UNDERLINE LEN 11 — the IC cursor field.
            InField("ACTIDIN", 6, 21, 11, ic: true),                           // bms:85-89
            Stopper(6, 33),                                                     // bms:90-92

            // ----- yellow 70-dash rule line (bms:93-97) -----
            // COLOR=YELLOW only (no ATTRB= operand): defaults to UNPROT,NORM per 3270. Length 70.
            DefField(8, 6, 70, BmsColor.Yellow, new string('-', 70)),          // bms:93-97

            // ----- current balance label + CURBAL output (bms:98-108) -----
            Lit(11, 6, 25, BmsColor.Turquoise, "Your current balance is: "),   // bms:98-102 (trailing space; len 25)
            Out("CURBAL", 11, 32, 14, AskipFset, BmsColor.Blue),               // bms:103-106
            DefStopper(11, 47),                                                 // bms:107-108 (LENGTH=0, no attrs)

            // ----- confirm prompt + CONFIRM input + (Y/N) (bms:109-126) -----
            Lit(15, 6, 53, BmsColor.Turquoise,
                "Do you want to pay your balance now. Please confirm: "),       // bms:109-114 (trailing space; len 53)
            // CONFIRM: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE LEN 1.
            InField("CONFIRM", 15, 60, 1, ic: false),                          // bms:115-119
            DefStopper(15, 62),                                                 // bms:120-121 (LENGTH=0, no attrs)
            Lit(15, 63, 5, BmsColor.Neutral, "(Y/N)"),                         // bms:122-126

            // ----- error line + footer (bms:127-135) -----
            // ERRMSG: ATTRB=(ASKIP,BRT,FSET) RED LEN 78.
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:127-130
            Lit(24, 1, 33, BmsColor.Yellow, "ENTER=Continue  F3=Back  F4=Clear"), // bms:131-135
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) + the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>
    /// Unnamed literal that declares no ATTRB= operand (the 70-dash rule line): 3270 defaults apply
    /// (UNPROT,NORM). Carries its INITIAL text + COLOR. source: COBIL00.bms:93-97
    /// </summary>
    private static ScreenField DefField(int row, int col, int len, BmsColor color, string text) =>
        new()
        {
            Row = row, Col = col, Length = len,
            Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = color, Value = text,
        };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>
    /// Named keyable input field: ATTRB=(FSET,[IC,]NORM,UNPROT) GREEN HILIGHT=UNDERLINE. The COBIL00 inputs
    /// (ACTIDIN, CONFIRM) declare no INITIAL, so the field starts blank.
    /// </summary>
    private static ScreenField InField(string name, int row, int col, int len, bool ic)
    {
        BmsAttribute attr = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected;
        if (ic) attr |= BmsAttribute.Ic;
        return new ScreenField
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = attr, Color = BmsColor.Green, Hilight = BmsHilight.Underline,
        };
    }

    /// <summary>A LENGTH=0 stopper field with ATTRB=(ASKIP,NORM).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };

    /// <summary>
    /// A LENGTH=0 stopper field declaring no ATTRB= operand (the (11,47) and (15,62) stoppers in COBIL00):
    /// 3270 defaults apply. source: COBIL00.bms:107-108,120-121
    /// </summary>
    private static ScreenField DefStopper(int row, int col) =>
        new()
        {
            Row = row, Col = col, Length = 0,
            Attribute = BmsAttribute.Unprotected | BmsAttribute.Normal, Color = BmsColor.Default,
        };
}
