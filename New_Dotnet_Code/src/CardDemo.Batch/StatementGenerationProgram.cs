using System.Text;
using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch program <c>CBSTM03A</c> ("Print Account Statements from
/// Transaction data in two formats: plain text and HTML"). It builds, per account, a plain-text statement
/// (80-column <c>STMTFILE</c>) and an HTML statement (100-column <c>HTMLFILE</c>) from the transaction,
/// cross-reference, customer and account data, doing all file I/O through the subroutine
/// <see cref="StatementFileAccessor"/>.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBSTM03A.cbl</c> (read in full); each
/// PROCEDURE-DIVISION paragraph is a method named after the COBOL paragraph, in original statement order,
/// annotated <c>// source: CBSTM03A.cbl:NNN</c>. The program's distinctive features are preserved faithfully:</para>
/// <list type="number">
/// <item><b>ALTER + GO TO control flow.</b> <c>0000-START</c> EVALUATEs <c>WS-FL-DD</c> and ALTERs the
/// <c>8100-FILE-OPEN</c> jump target before <c>GO TO 8100-FILE-OPEN</c>; the open paragraphs re-dispatch by
/// resetting <c>WS-FL-DD</c> and going back to <c>0000-START</c>. Modeled with a <see cref="FlDd"/> dispatch
/// loop plus a <see cref="_fileOpenTarget"/> enum that records the most recent ALTER (the observable effect
/// of the altered <c>8100-FILE-OPEN</c> GO TO).</item>
/// <item><b>TIOT walking</b> (<c>8000-*</c> mainline) — reproduced as its observable DISPLAY effect (the
/// running JCL/step banner and the per-DD-name lines) rather than a literal control-block storage walk, which
/// has no relational equivalent.</item>
/// <item><b>Per-account statement building</b> into the WS plain-text <c>STATEMENT-LINES</c> and the WS
/// <c>HTML-LINES</c>, reproduced verbatim from the copybook-free WORKING-STORAGE VALUE clauses.</item>
/// <item><b>CALL 'CBSTM03B'</b> for all I/O (here <see cref="StatementFileAccessor.Call"/>), and <b>CALL 'CEE3ABD'</b>
/// mapped to <see cref="AbendException"/>.</item>
/// </list>
/// <para>Money fields are exact <see cref="decimal"/> (truncate-toward-zero); the edited PICs
/// <c>9(9).99-</c> (ST-CURR-BAL) and <c>Z(9).99-</c> (ST-TRANAMT / ST-TOTAL-TRAMT) carry a TRAILING sign,
/// formatted by <see cref="EditTrailingSign"/>.</para>
/// </remarks>
public sealed class StatementGenerationProgram
{
    // ----- Output FD record widths. // source: CBSTM03A.cbl:44-47 ------------------------------------
    private const int StmtRecLen = 80;   // FD-STMTFILE-REC PIC X(80)
    private const int HtmlRecLen = 100;  // FD-HTMLFILE-REC PIC X(100)

    // ----- WS-TRNX-TABLE dimensions. // source: CBSTM03A.cbl:225-233 ---------------------------------
    private const int MaxCards = 51;     // WS-CARD-TBL OCCURS 51 TIMES
    private const int MaxTrans = 10;     // WS-TRAN-TBL OCCURS 10 TIMES
    private const int TrnxRestLen = 318; // TRNX-REST length (COSTM01)

    // COSTM01 TRNX-RECORD money field. // source: cpy/COSTM01.cpy:29
    private const int MoneyDigits = 11;  // TRNX-AMT S9(09)V99 -> 11 zoned digits
    private const int MoneyScale = 2;

    private readonly StatementFileAccessor _io;
    private readonly HostKind _host;
    private readonly List<string> _sysout = [];

    private TextWriter? _stmt;   // STMT-FILE
    private TextWriter? _html;   // HTML-FILE

    // ----- COMP / COMP-3 / MISC working-storage. // source: CBSTM03A.cbl:59-70 ------------------------
    private int _crCnt;                  // CR-CNT  S9(4) COMP VALUE 0
    private int _trCnt;                  // TR-CNT  S9(4) COMP VALUE 0
    private int _crJmp;                  // CR-JMP  S9(4) COMP VALUE 0
    private int _trJmp;                  // TR-JMP  S9(4) COMP VALUE 0
    private decimal _wsTotalAmt;         // WS-TOTAL-AMT COMP-3 S9(9)V99 VALUE 0
    private string _wsFlDd = "TRNXFILE"; // WS-FL-DD X(8) VALUE 'TRNXFILE'
    private decimal _wsTrnAmt;           // WS-TRN-AMT  S9(9)V99 VALUE 0
    private string _wsSaveCard = new(' ', 16); // WS-SAVE-CARD X(16) VALUE SPACES
    private bool _endOfFile;             // END-OF-FILE X(01) VALUE 'N' (true == 'Y')

    // The WS-M03B-AREA the program fills before each CALL 'CBSTM03B'. // source: CBSTM03A.cbl:71-83
    private readonly StatementFileAccessor.M03BArea _m03b = new();

    // ----- TRNX-RECORD (COSTM01) — the record area FLDT is MOVEd into. // source: cpy/COSTM01.cpy:20-36 --
    private string _trnxCardNum = new(' ', 16); // TRNX-CARD-NUM X(16)
    private string _trnxId = new(' ', 16);      // TRNX-ID       X(16)
    private string _trnxRest = new(' ', TrnxRestLen); // TRNX-REST (318)

    // Decoded TRNX-REST sub-fields used by the statement (TRNX-DESC, TRNX-AMT). // source: cpy/COSTM01.cpy:28-29
    private string _trnxDesc = new(' ', 100);   // TRNX-DESC X(100)
    private decimal _trnxAmt;                    // TRNX-AMT  S9(09)V99

    // ----- Record areas MOVEd from FLDT. // source: CBSTM03A.cbl:53-57 -------------------------------
    private CardXref _cardXref = new();   // CARD-XREF-RECORD (CVACT03Y)
    private Customer _customer = new();   // CUSTOMER-RECORD (CUSTREC)
    private Account _account = new();     // ACCOUNT-RECORD (CVACT01Y)

    // ----- WS-TRNX-TABLE — 51 cards x (card-num + 10 trans). // source: CBSTM03A.cbl:225-233 ---------
    private readonly string[] _wsCardNum = new string[MaxCards + 1];                 // WS-CARD-NUM (1..51)
    private readonly string[,] _wsTranNum = new string[MaxCards + 1, MaxTrans + 1];  // WS-TRAN-NUM
    private readonly string[,] _wsTranRest = new string[MaxCards + 1, MaxTrans + 1]; // WS-TRAN-REST
    private readonly int[] _wsTrct = new int[MaxCards + 1];                          // WS-TRCT (1..51)

    // ----- STATEMENT-LINES plain-text fields (the variable parts). // source: CBSTM03A.cbl:85-146 -----
    private string _stName = new(' ', 75);     // ST-NAME      X(75)
    private string _stAdd1 = new(' ', 50);     // ST-ADD1      X(50)
    private string _stAdd2 = new(' ', 50);     // ST-ADD2      X(50)
    private string _stAdd3 = new(' ', 80);     // ST-ADD3      X(80)
    private string _stAcctId = new(' ', 20);   // ST-ACCT-ID   X(20)
    private string _stCurrBal = new(' ', 13);  // ST-CURR-BAL  9(9).99-  (13 chars)
    private string _stFicoScore = new(' ', 20);// ST-FICO-SCORE X(20)
    private string _stTranId = new(' ', 16);   // ST-TRANID    X(16)
    private string _stTranDt = new(' ', 49);   // ST-TRANDT    X(49)
    private string _stTranAmt = new(' ', 13);  // ST-TRANAMT   Z(9).99-  (13 chars)
    private string _stTotalTramt = new(' ', 13);// ST-TOTAL-TRAMT Z(9).99- (13 chars)

    // ----- HTML-LINES variable fields. // source: CBSTM03A.cbl:212-223 ------------------------------
    private string _l11Acct = new(' ', 20);    // L11-ACCT  X(20)
    private string _l23Name = new(' ', 50);    // L23-NAME  X(50)
    private string _htmlAddrLn = new(' ', 100);// HTML-ADDR-LN X(100)
    private string _htmlBsicLn = new(' ', 100);// HTML-BSIC-LN X(100)
    private string _htmlTranLn = new(' ', 100);// HTML-TRAN-LN X(100)

    /// <summary>
    /// Models the ALTERable GO TO target of <c>8100-FILE-OPEN</c>: each ALTER in <c>0000-START</c> records
    /// which open paragraph the (otherwise dead) <c>8100-FILE-OPEN</c> jump now proceeds to. // source: CBSTM03A.cbl:300-310, 726-728
    /// </summary>
    private enum FileOpenTarget { Trnx, Xref, Cust, Acct }
    private FileOpenTarget _fileOpenTarget = FileOpenTarget.Trnx; // 8100-FILE-OPEN initial GO TO. // source: CBSTM03A.cbl:727

    private StatementGenerationProgram(StatementFileAccessor io, HostKind host)
    {
        _io = io;
        _host = host;
    }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>
    /// Runs CBSTM03A over the relational data in <paramref name="support"/>, writing the plain-text statement
    /// file to <paramref name="stmtPath"/> and the HTML statement file to <paramref name="htmlPath"/>.
    /// </summary>
    /// <param name="support">Provides the TRANSACTION/CARD_XREF/CUSTOMER/ACCOUNT repositories for CBSTM03B.</param>
    /// <param name="stmtPath">STMTFILE output path (plain-text, 80-column lines).</param>
    /// <param name="htmlPath">HTMLFILE output path (HTML, 100-column lines).</param>
    /// <param name="host">Host encoding used by CBSTM03B for the record images (defaults to EBCDIC).</param>
    public static IReadOnlyList<string> Run(
        BatchSupport support,
        string stmtPath,
        string htmlPath,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new StatementGenerationProgram(StatementFileAccessor.Create(support, host), host);
        program.Execute(stmtPath, htmlPath);
        return program.Sysout;
    }

    /// <summary>Convenience overload taking a <see cref="RelationalDb"/>.</summary>
    public static IReadOnlyList<string> Run(
        RelationalDb db,
        string stmtPath,
        string htmlPath,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new StatementGenerationProgram(StatementFileAccessor.Create(db, host), host);
        program.Execute(stmtPath, htmlPath);
        return program.Sysout;
    }

    // =================================================================================================
    // PROCEDURE DIVISION mainline (TIOT walk + OPEN OUTPUT + INITIALIZE, then fall into 0000-START).
    // source: CBSTM03A.cbl:262-294
    // =================================================================================================
    private void Execute(string stmtPath, string htmlPath)
    {
        OpenOutput(stmtPath, htmlPath);
        try
        {
            // ---- "Check Unit Control blocks" — TIOT walk, reproduced as its observable DISPLAY effect. ----
            // The literal storage walk (SET ADDRESS OF PSA/TCB/TIOT, COMPUTE BUMP-TIOT, ...) has no relational
            // equivalent; the observable output is the running-JCL banner and the DD-name list. // source: CBSTM03A.cbl:266-291
            Display("Running JCL : " + Alpha("", 8) + " Step " + Alpha("", 8)); // source: CBSTM03A.cbl:270
            Display("DD Names from TIOT: ");                                     // source: CBSTM03A.cbl:275
            // (the per-entry DD lines depend on the JCL TIOT contents, which are not modeled here) // source: CBSTM03A.cbl:276-291

            // OPEN OUTPUT STMT-FILE HTML-FILE (done above) // source: CBSTM03A.cbl:293
            InitializeWsTrnxTable(); // INITIALIZE WS-TRNX-TABLE WS-TRN-TBL-CNTR // source: CBSTM03A.cbl:294

            // Fall through into 0000-START. The ALTER/GO-TO graph is driven by this dispatch loop until a
            // paragraph reaches 1000-MAINLINE or 9999-GOBACK.
            Start0000();
        }
        finally
        {
            _stmt?.Flush();
            _html?.Flush();
            _stmt?.Dispose();
            _html?.Dispose();
        }
    }

    // =================================================================================================
    // 0000-START — EVALUATE WS-FL-DD with ALTER + GO TO. Driven as a dispatch loop. // source: CBSTM03A.cbl:296-314
    // =================================================================================================
    private void Start0000()
    {
        while (true)
        {
            switch (FlDd())
            {
                case "TRNXFILE":
                    // ALTER 8100-FILE-OPEN TO PROCEED TO 8100-TRNXFILE-OPEN; GO TO 8100-FILE-OPEN // source: CBSTM03A.cbl:300-301
                    _fileOpenTarget = FileOpenTarget.Trnx;
                    if (FileOpen8100()) return; // reached 1000-MAINLINE / GOBACK
                    break;
                case "XREFFILE":
                    // ALTER 8100-FILE-OPEN TO PROCEED TO 8200-XREFFILE-OPEN; GO TO 8100-FILE-OPEN // source: CBSTM03A.cbl:302-304
                    _fileOpenTarget = FileOpenTarget.Xref;
                    if (FileOpen8100()) return;
                    break;
                case "CUSTFILE":
                    // ALTER 8100-FILE-OPEN TO PROCEED TO 8300-CUSTFILE-OPEN; GO TO 8100-FILE-OPEN // source: CBSTM03A.cbl:305-307
                    _fileOpenTarget = FileOpenTarget.Cust;
                    if (FileOpen8100()) return;
                    break;
                case "ACCTFILE":
                    // ALTER 8100-FILE-OPEN TO PROCEED TO 8400-ACCTFILE-OPEN; GO TO 8100-FILE-OPEN // source: CBSTM03A.cbl:308-310
                    _fileOpenTarget = FileOpenTarget.Acct;
                    if (FileOpen8100()) return;
                    break;
                case "READTRNX":
                    // GO TO 8500-READTRNX-READ // source: CBSTM03A.cbl:311-312
                    if (Readtrnx8500Read()) return;
                    break;
                default:
                    // WHEN OTHER GO TO 9999-GOBACK // source: CBSTM03A.cbl:313-314
                    Goback9999();
                    return;
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // 8100-FILE-OPEN — GO TO 8100-TRNXFILE-OPEN (ALTERed at run time). // source: CBSTM03A.cbl:726-728
    // Returns true if the chain reached 1000-MAINLINE (terminal), false to loop back to 0000-START.
    // -------------------------------------------------------------------------------------------------
    private bool FileOpen8100()
    {
        switch (_fileOpenTarget) // the (ALTERed) GO TO target
        {
            case FileOpenTarget.Trnx: return TrnxfileOpen8100();
            case FileOpenTarget.Xref: TrnxXrefCustOpen(FileOpenTarget.Xref); return false;
            case FileOpenTarget.Cust: TrnxXrefCustOpen(FileOpenTarget.Cust); return false;
            case FileOpenTarget.Acct: return AcctfileOpen8400();
            default: return false;
        }
    }

    // Dispatch helper for the XREF/CUST open paragraphs (both end with GO TO 0000-START -> loop = false).
    private void TrnxXrefCustOpen(FileOpenTarget which)
    {
        if (which == FileOpenTarget.Xref) XreffileOpen8200();
        else CustfileOpen8300();
    }

    // =================================================================================================
    // 1000-MAINLINE — the per-account statement loop. // source: CBSTM03A.cbl:316-342
    // =================================================================================================
    private void Mainline1000()
    {
        // PERFORM UNTIL END-OF-FILE = 'Y' // source: CBSTM03A.cbl:317
        while (!_endOfFile)
        {
            if (!_endOfFile)                          // IF END-OF-FILE = 'N' // source: CBSTM03A.cbl:318
            {
                XreffileGetNext1000();                // source: CBSTM03A.cbl:319
                if (!_endOfFile)                      // IF END-OF-FILE = 'N' // source: CBSTM03A.cbl:320
                {
                    CustfileGet2000();                // source: CBSTM03A.cbl:321
                    AcctfileGet3000();                // source: CBSTM03A.cbl:322
                    CreateStatement5000();            // source: CBSTM03A.cbl:323
                    _crJmp = 1;                       // MOVE 1 TO CR-JMP // source: CBSTM03A.cbl:324
                    _wsTotalAmt = 0m;                 // MOVE ZERO TO WS-TOTAL-AMT // source: CBSTM03A.cbl:325
                    TrnxfileGet4000();                // source: CBSTM03A.cbl:326
                }
            }
        }

        TrnxfileClose9100();  // source: CBSTM03A.cbl:331
        XreffileClose9200();  // source: CBSTM03A.cbl:333
        CustfileClose9300();  // source: CBSTM03A.cbl:335
        AcctfileClose9400();  // source: CBSTM03A.cbl:337
        CloseStmtHtml();      // CLOSE STMT-FILE HTML-FILE // source: CBSTM03A.cbl:339
        // fall into 9999-GOBACK // source: CBSTM03A.cbl:341
        Goback9999();
    }

    // 9999-GOBACK. GOBACK. // source: CBSTM03A.cbl:341-342
    private static void Goback9999()
    {
        // GOBACK
    }

    // =================================================================================================
    // 1000-XREFFILE-GET-NEXT — sequential read of CARD_XREF via CBSTM03B. // source: CBSTM03A.cbl:345-366
    // =================================================================================================
    private void XreffileGetNext1000()
    {
        _m03b.Dd = "XREFFILE";                        // MOVE 'XREFFILE' TO WS-M03B-DD // source: CBSTM03A.cbl:347
        _m03b.Oper = 'R';                             // SET M03B-READ TO TRUE // source: CBSTM03A.cbl:348
        _m03b.Rc = "00";                              // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:349
        _m03b.Fldt = new string(' ', 1000);           // MOVE SPACES TO WS-M03B-FLDT // source: CBSTM03A.cbl:350
        _io.Call(_m03b);                              // CALL 'CBSTM03B' USING WS-M03B-AREA // source: CBSTM03A.cbl:351

        switch (_m03b.Rc)                             // EVALUATE WS-M03B-RC // source: CBSTM03A.cbl:353
        {
            case "00":                                // source: CBSTM03A.cbl:354-355
                break;                                // CONTINUE
            case "10":                                // source: CBSTM03A.cbl:356-357
                _endOfFile = true;                    // MOVE 'Y' TO END-OF-FILE
                break;
            default:                                   // WHEN OTHER // source: CBSTM03A.cbl:358-361
                Display("ERROR READING XREFFILE");
                Display("RETURN CODE: " + _m03b.Rc);
                AbendProgram9999();
                break;
        }

        // MOVE WS-M03B-FLDT TO CARD-XREF-RECORD. // source: CBSTM03A.cbl:364
        _cardXref = DecodeCardXref(_m03b.Fldt);
        // EXIT // source: CBSTM03A.cbl:366
    }

    // =================================================================================================
    // 2000-CUSTFILE-GET — keyed read of CUSTOMER by XREF-CUST-ID via CBSTM03B. // source: CBSTM03A.cbl:368-390
    // =================================================================================================
    private void CustfileGet2000()
    {
        _m03b.Dd = "CUSTFILE";                                  // source: CBSTM03A.cbl:370
        _m03b.Oper = 'K';                                       // SET M03B-READ-K TO TRUE // source: CBSTM03A.cbl:371
        _m03b.Key = Alpha(ZonedDigits(_cardXref.CustId, 9), 25); // MOVE XREF-CUST-ID TO WS-M03B-KEY // source: CBSTM03A.cbl:372
        _m03b.KeyLn = 0;                                        // MOVE ZERO TO WS-M03B-KEY-LN // source: CBSTM03A.cbl:373
        _m03b.KeyLn = 9;                                        // COMPUTE WS-M03B-KEY-LN = LENGTH OF XREF-CUST-ID (9) // source: CBSTM03A.cbl:374
        _m03b.Rc = "00";                                        // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:375
        _m03b.Fldt = new string(' ', 1000);                     // MOVE SPACES TO WS-M03B-FLDT // source: CBSTM03A.cbl:376
        _io.Call(_m03b);                                        // CALL 'CBSTM03B' USING WS-M03B-AREA // source: CBSTM03A.cbl:377

        switch (_m03b.Rc)                                       // EVALUATE WS-M03B-RC // source: CBSTM03A.cbl:379
        {
            case "00":                                          // source: CBSTM03A.cbl:380-381
                break;                                          // CONTINUE
            default:                                             // WHEN OTHER // source: CBSTM03A.cbl:382-385
                Display("ERROR READING CUSTFILE");
                Display("RETURN CODE: " + _m03b.Rc);
                AbendProgram9999();
                break;
        }

        // MOVE WS-M03B-FLDT TO CUSTOMER-RECORD. // source: CBSTM03A.cbl:388
        _customer = DecodeCustomer(_m03b.Fldt);
        // EXIT // source: CBSTM03A.cbl:390
    }

    // =================================================================================================
    // 3000-ACCTFILE-GET — keyed read of ACCOUNT by XREF-ACCT-ID via CBSTM03B. // source: CBSTM03A.cbl:392-414
    // =================================================================================================
    private void AcctfileGet3000()
    {
        _m03b.Dd = "ACCTFILE";                                   // source: CBSTM03A.cbl:394
        _m03b.Oper = 'K';                                        // SET M03B-READ-K TO TRUE // source: CBSTM03A.cbl:395
        _m03b.Key = Alpha(ZonedDigits(_cardXref.AcctId, 11), 25); // MOVE XREF-ACCT-ID TO WS-M03B-KEY // source: CBSTM03A.cbl:396
        _m03b.KeyLn = 0;                                         // MOVE ZERO TO WS-M03B-KEY-LN // source: CBSTM03A.cbl:397
        _m03b.KeyLn = 11;                                       // COMPUTE WS-M03B-KEY-LN = LENGTH OF XREF-ACCT-ID (11) // source: CBSTM03A.cbl:398
        _m03b.Rc = "00";                                        // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:399
        _m03b.Fldt = new string(' ', 1000);                     // MOVE SPACES TO WS-M03B-FLDT // source: CBSTM03A.cbl:400
        _io.Call(_m03b);                                        // CALL 'CBSTM03B' USING WS-M03B-AREA // source: CBSTM03A.cbl:401

        switch (_m03b.Rc)                                       // EVALUATE WS-M03B-RC // source: CBSTM03A.cbl:403
        {
            case "00":                                          // source: CBSTM03A.cbl:404-405
                break;                                          // CONTINUE
            default:                                             // WHEN OTHER // source: CBSTM03A.cbl:406-409
                Display("ERROR READING ACCTFILE");
                Display("RETURN CODE: " + _m03b.Rc);
                AbendProgram9999();
                break;
        }

        // MOVE WS-M03B-FLDT TO ACCOUNT-RECORD. // source: CBSTM03A.cbl:412
        _account = DecodeAccount(_m03b.Fldt);
        // EXIT // source: CBSTM03A.cbl:414
    }

    // =================================================================================================
    // 4000-TRNXFILE-GET — emit each transaction for this card, then the per-account totals/footers.
    // source: CBSTM03A.cbl:416-456
    // =================================================================================================
    private void TrnxfileGet4000()
    {
        // PERFORM VARYING CR-JMP FROM 1 BY 1 UNTIL CR-JMP > CR-CNT OR (WS-CARD-NUM(CR-JMP) > XREF-CARD-NUM)
        // source: CBSTM03A.cbl:417-419
        for (_crJmp = 1;
             !(_crJmp > _crCnt
               || string.CompareOrdinal(CardNum(_crJmp), Alpha(_cardXref.XrefCardNum, 16)) > 0);
             _crJmp++)
        {
            // IF XREF-CARD-NUM = WS-CARD-NUM (CR-JMP) // source: CBSTM03A.cbl:420
            if (string.Equals(Alpha(_cardXref.XrefCardNum, 16), CardNum(_crJmp), StringComparison.Ordinal))
            {
                _trnxCardNum = CardNum(_crJmp);   // MOVE WS-CARD-NUM (CR-JMP) TO TRNX-CARD-NUM // source: CBSTM03A.cbl:421
                // PERFORM VARYING TR-JMP FROM 1 BY 1 UNTIL TR-JMP > WS-TRCT (CR-JMP) // source: CBSTM03A.cbl:422-423
                for (_trJmp = 1; !(_trJmp > _wsTrct[_crJmp]); _trJmp++)
                {
                    _trnxId = _wsTranNum[_crJmp, _trJmp] ?? new string(' ', 16);   // MOVE WS-TRAN-NUM TO TRNX-ID // source: CBSTM03A.cbl:424-425
                    SetTrnxRest(_wsTranRest[_crJmp, _trJmp] ?? new string(' ', TrnxRestLen)); // MOVE WS-TRAN-REST TO TRNX-REST // source: CBSTM03A.cbl:426-427
                    WriteTrans6000();             // source: CBSTM03A.cbl:428
                    _wsTotalAmt = AddMoney(_wsTotalAmt, _trnxAmt); // ADD TRNX-AMT TO WS-TOTAL-AMT // source: CBSTM03A.cbl:429
                }
            }
        }

        _wsTrnAmt = StoreMoney(_wsTotalAmt);          // MOVE WS-TOTAL-AMT TO WS-TRN-AMT // source: CBSTM03A.cbl:433
        _stTotalTramt = EditZ9V99Trailing(_wsTrnAmt); // MOVE WS-TRN-AMT TO ST-TOTAL-TRAMT // source: CBSTM03A.cbl:434
        WriteStmt(BuildStLine12());                   // WRITE FD-STMTFILE-REC FROM ST-LINE12 // source: CBSTM03A.cbl:435
        WriteStmt(BuildStLine14A());                  // WRITE FD-STMTFILE-REC FROM ST-LINE14A // source: CBSTM03A.cbl:436
        WriteStmt(BuildStLine15());                   // WRITE FD-STMTFILE-REC FROM ST-LINE15 // source: CBSTM03A.cbl:437

        WriteHtml(Html("<tr>"));                       // SET HTML-LTRS; WRITE // source: CBSTM03A.cbl:439-440
        WriteHtml(Html(HtmlL10));                      // SET HTML-L10;  WRITE // source: CBSTM03A.cbl:441-442
        WriteHtml(Html("<h3>End of Statement</h3>"));  // SET HTML-L75;  WRITE // source: CBSTM03A.cbl:443-444
        WriteHtml(Html("</td>"));                      // SET HTML-LTDE; WRITE // source: CBSTM03A.cbl:445-446
        WriteHtml(Html("</tr>"));                      // SET HTML-LTRE; WRITE // source: CBSTM03A.cbl:447-448
        WriteHtml(Html("</table>"));                   // SET HTML-L78;  WRITE // source: CBSTM03A.cbl:449-450
        WriteHtml(Html("</body>"));                    // SET HTML-L79;  WRITE // source: CBSTM03A.cbl:451-452
        WriteHtml(Html("</html>"));                    // SET HTML-L80;  WRITE // source: CBSTM03A.cbl:453-454
        // EXIT // source: CBSTM03A.cbl:456
    }

    // =================================================================================================
    // 5000-CREATE-STATEMENT — build & write the statement header (plain text + HTML). // source: CBSTM03A.cbl:458-504
    // =================================================================================================
    private void CreateStatement5000()
    {
        InitializeStatementLines();                    // INITIALIZE STATEMENT-LINES // source: CBSTM03A.cbl:459
        WriteStmt(BuildStLine0());                     // WRITE FD-STMTFILE-REC FROM ST-LINE0 // source: CBSTM03A.cbl:460
        WriteHtmlHeader5100();                         // PERFORM 5100-WRITE-HTML-HEADER // source: CBSTM03A.cbl:461

        // STRING CUST-FIRST-NAME ' ' CUST-MIDDLE-NAME ' ' CUST-LAST-NAME ' ' INTO ST-NAME // source: CBSTM03A.cbl:462-469
        _stName = Alpha(StringDelimited(
            (Trim(_customer.FirstName), " "),
            (" ", null),
            (Trim(_customer.MiddleName), " "),
            (" ", null),
            (Trim(_customer.LastName), " "),
            (" ", null)), 75);

        _stAdd1 = Alpha(_customer.AddrLine1, 50);      // MOVE CUST-ADDR-LINE-1 TO ST-ADD1 // source: CBSTM03A.cbl:470
        _stAdd2 = Alpha(_customer.AddrLine2, 50);      // MOVE CUST-ADDR-LINE-2 TO ST-ADD2 // source: CBSTM03A.cbl:471

        // STRING CUST-ADDR-LINE-3 ' ' CUST-ADDR-STATE-CD ' ' CUST-ADDR-COUNTRY-CD ' ' CUST-ADDR-ZIP ' '
        // INTO ST-ADD3 // source: CBSTM03A.cbl:472-481
        _stAdd3 = Alpha(StringDelimited(
            (Trim(_customer.AddrLine3), " "),
            (" ", null),
            (Trim(_customer.AddrStateCd), " "),
            (" ", null),
            (Trim(_customer.AddrCountryCd), " "),
            (" ", null),
            (Trim(_customer.AddrZip), " "),
            (" ", null)), 80);

        _stAcctId = Alpha(ZonedDigits(_account.AcctId, 11), 20);     // MOVE ACCT-ID TO ST-ACCT-ID // source: CBSTM03A.cbl:483
        _stCurrBal = Edit9V99Trailing(_account.CurrBal);            // MOVE ACCT-CURR-BAL TO ST-CURR-BAL // source: CBSTM03A.cbl:484
        _stFicoScore = Alpha(ZonedDigits(_customer.FicoCreditScore, 3), 20); // MOVE CUST-FICO-CREDIT-SCORE TO ST-FICO-SCORE // source: CBSTM03A.cbl:485
        WriteHtmlNmadbs5200();                          // PERFORM 5200-WRITE-HTML-NMADBS // source: CBSTM03A.cbl:486

        WriteStmt(BuildStLine1());   // source: CBSTM03A.cbl:488
        WriteStmt(BuildStLine2());   // source: CBSTM03A.cbl:489
        WriteStmt(BuildStLine3());   // source: CBSTM03A.cbl:490
        WriteStmt(BuildStLine4());   // source: CBSTM03A.cbl:491
        WriteStmt(BuildStLine5());   // source: CBSTM03A.cbl:492
        WriteStmt(BuildStLine6());   // source: CBSTM03A.cbl:493
        WriteStmt(BuildStLine5());   // source: CBSTM03A.cbl:494
        WriteStmt(BuildStLine7());   // source: CBSTM03A.cbl:495
        WriteStmt(BuildStLine8());   // source: CBSTM03A.cbl:496
        WriteStmt(BuildStLine9());   // source: CBSTM03A.cbl:497
        WriteStmt(BuildStLine10());  // source: CBSTM03A.cbl:498
        WriteStmt(BuildStLine11());  // source: CBSTM03A.cbl:499
        WriteStmt(BuildStLine12());  // source: CBSTM03A.cbl:500
        WriteStmt(BuildStLine13());  // source: CBSTM03A.cbl:501
        WriteStmt(BuildStLine12());  // source: CBSTM03A.cbl:502
        // EXIT // source: CBSTM03A.cbl:504
    }

    // =================================================================================================
    // 5100-WRITE-HTML-HEADER. // source: CBSTM03A.cbl:506-555
    // =================================================================================================
    private void WriteHtmlHeader5100()
    {
        WriteHtml(Html("<!DOCTYPE html>"));                                  // L01 // source: CBSTM03A.cbl:508-509
        WriteHtml(Html("<html lang=\"en\">"));                              // L02 // source: CBSTM03A.cbl:510-511
        WriteHtml(Html("<head>"));                                          // L03 // source: CBSTM03A.cbl:512-513
        WriteHtml(Html("<meta charset=\"utf-8\">"));                        // L04 // source: CBSTM03A.cbl:514-515
        WriteHtml(Html("<title>HTML Table Layout</title>"));                // L05 // source: CBSTM03A.cbl:516-517
        WriteHtml(Html("</head>"));                                         // L06 // source: CBSTM03A.cbl:518-519
        WriteHtml(Html("<body style=\"margin:0px;\">"));                    // L07 // source: CBSTM03A.cbl:520-521
        WriteHtml(Html(HtmlL08));                                            // L08 // source: CBSTM03A.cbl:522-523
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:524-525
        WriteHtml(Html(HtmlL10));                                            // L10 // source: CBSTM03A.cbl:526-527

        _l11Acct = Alpha(ZonedDigits(_account.AcctId, 11), 20);             // MOVE ACCT-ID TO L11-ACCT // source: CBSTM03A.cbl:529
        WriteHtml(BuildHtmlL11());                                           // WRITE FROM HTML-L11 // source: CBSTM03A.cbl:530
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:531-532
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:533-534
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:535-536
        WriteHtml(Html(HtmlL15));                                            // L15 // source: CBSTM03A.cbl:537-538
        WriteHtml(Html("<p style=\"font-size:16px\">Bank of XYZ</p>"));     // L16 // source: CBSTM03A.cbl:539-540
        WriteHtml(Html("<p>410 Terry Ave N</p>"));                          // L17 // source: CBSTM03A.cbl:541-542
        WriteHtml(Html("<p>Seattle WA 99999</p>"));                         // L18 // source: CBSTM03A.cbl:543-544
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:545-546
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:547-548
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:549-550
        WriteHtml(Html(HtmlL22_35));                                         // L22-35 // source: CBSTM03A.cbl:551-552
        // 5100-EXIT // source: CBSTM03A.cbl:554-555
    }

    // =================================================================================================
    // 5200-WRITE-HTML-NMADBS. // source: CBSTM03A.cbl:558-672
    // =================================================================================================
    private void WriteHtmlNmadbs5200()
    {
        _l23Name = Alpha(_stName, 50);                                       // MOVE ST-NAME TO L23-NAME // source: CBSTM03A.cbl:560
        // MOVE SPACES TO FD-HTMLFILE-REC; STRING '<p ...>' L23-NAME(del '  ') '  ' '</p>' INTO FD-HTMLFILE-REC
        // source: CBSTM03A.cbl:561-567
        WriteHtmlRaw(StringDelimited(
            ("<p style=\"font-size:16px\">", null),
            (_l23Name, "  "),
            ("  ", null),
            ("</p>", null)));                                                // WRITE FD-HTMLFILE-REC // source: CBSTM03A.cbl:568

        // HTML-ADDR-LN for ST-ADD1/2/3 // source: CBSTM03A.cbl:569-592
        _htmlAddrLn = Alpha(StringDelimited(
            ("<p>", null), (_stAdd1, "  "), ("  ", null), ("</p>", null)), 100);
        WriteHtml(_htmlAddrLn);                                              // source: CBSTM03A.cbl:576
        _htmlAddrLn = Alpha(StringDelimited(
            ("<p>", null), (_stAdd2, "  "), ("  ", null), ("</p>", null)), 100);
        WriteHtml(_htmlAddrLn);                                              // source: CBSTM03A.cbl:584
        _htmlAddrLn = Alpha(StringDelimited(
            ("<p>", null), (_stAdd3, "  "), ("  ", null), ("</p>", null)), 100);
        WriteHtml(_htmlAddrLn);                                              // source: CBSTM03A.cbl:592

        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:594-595
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:596-597
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:598-599
        WriteHtml(Html(HtmlL30_42));                                         // L30-42 // source: CBSTM03A.cbl:600-601
        WriteHtml(Html("<p style=\"font-size:16px\">Basic Details</p>"));   // L31 // source: CBSTM03A.cbl:602-603
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:604-605
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:606-607
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:608-609
        WriteHtml(Html(HtmlL22_35));                                         // L22-35 // source: CBSTM03A.cbl:610-611

        // HTML-BSIC-LN for Account ID / Current Balance / FICO Score // source: CBSTM03A.cbl:613-633
        _htmlBsicLn = Alpha(StringDelimited(
            ("<p>Account ID         : ", null), (_stAcctId, null), ("</p>", null)), 100);
        WriteHtml(_htmlBsicLn);                                              // source: CBSTM03A.cbl:619
        _htmlBsicLn = Alpha(StringDelimited(
            ("<p>Current Balance    : ", null), (_stCurrBal, null), ("</p>", null)), 100);
        WriteHtml(_htmlBsicLn);                                              // source: CBSTM03A.cbl:626
        _htmlBsicLn = Alpha(StringDelimited(
            ("<p>FICO Score         : ", null), (_stFicoScore, null), ("</p>", null)), 100);
        WriteHtml(_htmlBsicLn);                                              // source: CBSTM03A.cbl:633

        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:634-635
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:636-637
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:638-639
        WriteHtml(Html(HtmlL30_42));                                         // L30-42 // source: CBSTM03A.cbl:640-641
        WriteHtml(Html("<p style=\"font-size:16px\">Transaction Summary</p>")); // L43 // source: CBSTM03A.cbl:642-643
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:644-645
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:646-647
        WriteHtml(Html("<tr>"));                                            // LTRS // source: CBSTM03A.cbl:648-649
        WriteHtml(Html(HtmlL47));                                            // L47 // source: CBSTM03A.cbl:650-651
        WriteHtml(Html("<p style=\"font-size:16px\">Tran ID</p>"));         // L48 // source: CBSTM03A.cbl:652-653
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:654-655
        WriteHtml(Html(HtmlL50));                                            // L50 // source: CBSTM03A.cbl:656-657
        WriteHtml(Html("<p style=\"font-size:16px\">Tran Details</p>"));    // L51 // source: CBSTM03A.cbl:658-659
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:660-661
        WriteHtml(Html(HtmlL53));                                            // L53 // source: CBSTM03A.cbl:662-663
        WriteHtml(Html("<p style=\"font-size:16px\">Amount</p>"));          // L54 // source: CBSTM03A.cbl:664-665
        WriteHtml(Html("</td>"));                                           // LTDE // source: CBSTM03A.cbl:666-667
        WriteHtml(Html("</tr>"));                                           // LTRE // source: CBSTM03A.cbl:668-669
        // 5200-EXIT // source: CBSTM03A.cbl:671-672
    }

    // =================================================================================================
    // 6000-WRITE-TRANS — one transaction detail (plain text + HTML). // source: CBSTM03A.cbl:675-723
    // =================================================================================================
    private void WriteTrans6000()
    {
        _stTranId = Alpha(_trnxId, 16);             // MOVE TRNX-ID TO ST-TRANID // source: CBSTM03A.cbl:676
        _stTranDt = Alpha(_trnxDesc, 49);           // MOVE TRNX-DESC TO ST-TRANDT // source: CBSTM03A.cbl:677
        _stTranAmt = EditZ9V99Trailing(_trnxAmt);   // MOVE TRNX-AMT TO ST-TRANAMT // source: CBSTM03A.cbl:678
        WriteStmt(BuildStLine14());                 // WRITE FD-STMTFILE-REC FROM ST-LINE14 // source: CBSTM03A.cbl:679

        WriteHtml(Html("<tr>"));                    // LTRS // source: CBSTM03A.cbl:681-682

        WriteHtml(Html(HtmlL58));                   // L58 // source: CBSTM03A.cbl:684-685
        _htmlTranLn = Alpha(StringDelimited(
            ("<p>", null), (_stTranId, null), ("</p>", null)), 100);        // source: CBSTM03A.cbl:686-691
        WriteHtml(_htmlTranLn);                     // source: CBSTM03A.cbl:692
        WriteHtml(Html("</td>"));                   // LTDE // source: CBSTM03A.cbl:693-694

        WriteHtml(Html(HtmlL61));                   // L61 // source: CBSTM03A.cbl:696-697
        _htmlTranLn = Alpha(StringDelimited(
            ("<p>", null), (_stTranDt, null), ("</p>", null)), 100);        // source: CBSTM03A.cbl:698-703
        WriteHtml(_htmlTranLn);                     // source: CBSTM03A.cbl:704
        WriteHtml(Html("</td>"));                   // LTDE // source: CBSTM03A.cbl:705-706

        WriteHtml(Html(HtmlL64));                   // L64 // source: CBSTM03A.cbl:708-709
        _htmlTranLn = Alpha(StringDelimited(
            ("<p>", null), (_stTranAmt, null), ("</p>", null)), 100);       // source: CBSTM03A.cbl:710-715
        WriteHtml(_htmlTranLn);                     // source: CBSTM03A.cbl:716
        WriteHtml(Html("</td>"));                   // LTDE // source: CBSTM03A.cbl:717-718

        WriteHtml(Html("</tr>"));                   // LTRE // source: CBSTM03A.cbl:720-721
        // EXIT // source: CBSTM03A.cbl:723
    }

    // =================================================================================================
    // 8100-TRNXFILE-OPEN. // source: CBSTM03A.cbl:730-762  (returns true -> control transferred to 0000-START)
    // =================================================================================================
    private bool TrnxfileOpen8100()
    {
        _m03b.Dd = "TRNXFILE";              // source: CBSTM03A.cbl:731
        _m03b.Oper = 'O';                   // SET M03B-OPEN TO TRUE // source: CBSTM03A.cbl:732
        _m03b.Rc = "00";                    // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:733
        _io.Call(_m03b);                    // CALL 'CBSTM03B' // source: CBSTM03A.cbl:734

        if (_m03b.Rc is "00" or "04")       // IF WS-M03B-RC = '00' OR '04' // source: CBSTM03A.cbl:736-737
        {
            // CONTINUE
        }
        else                                 // source: CBSTM03A.cbl:738-742
        {
            Display("ERROR OPENING TRNXFILE");
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }

        _m03b.Oper = 'R';                   // SET M03B-READ TO TRUE // source: CBSTM03A.cbl:744
        _m03b.Fldt = new string(' ', 1000); // MOVE SPACES TO WS-M03B-FLDT // source: CBSTM03A.cbl:745
        _io.Call(_m03b);                    // CALL 'CBSTM03B' // source: CBSTM03A.cbl:746

        if (_m03b.Rc is "00" or "04")       // IF WS-M03B-RC = '00' OR '04' // source: CBSTM03A.cbl:748-749
        {
            // CONTINUE
        }
        else                                 // source: CBSTM03A.cbl:750-754
        {
            Display("ERROR READING TRNXFILE");
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }

        SetTrnxRecord(_m03b.Fldt);          // MOVE WS-M03B-FLDT TO TRNX-RECORD // source: CBSTM03A.cbl:756
        _wsSaveCard = _trnxCardNum;         // MOVE TRNX-CARD-NUM TO WS-SAVE-CARD // source: CBSTM03A.cbl:757
        _crCnt = 1;                         // MOVE 1 TO CR-CNT // source: CBSTM03A.cbl:758
        _trCnt = 0;                         // MOVE 0 TO TR-CNT // source: CBSTM03A.cbl:759
        _wsFlDd = "READTRNX";               // MOVE 'READTRNX' TO WS-FL-DD // source: CBSTM03A.cbl:760
        // GO TO 0000-START // source: CBSTM03A.cbl:761
        return false; // re-dispatch via 0000-START
    }

    // =================================================================================================
    // 8200-XREFFILE-OPEN. // source: CBSTM03A.cbl:765-781
    // =================================================================================================
    private void XreffileOpen8200()
    {
        _m03b.Dd = "XREFFILE";              // source: CBSTM03A.cbl:766
        _m03b.Oper = 'O';                   // SET M03B-OPEN TO TRUE // source: CBSTM03A.cbl:767
        _m03b.Rc = "00";                    // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:768
        _io.Call(_m03b);                    // CALL 'CBSTM03B' // source: CBSTM03A.cbl:769

        if (_m03b.Rc is "00" or "04")       // source: CBSTM03A.cbl:771-772
        {
            // CONTINUE
        }
        else                                 // source: CBSTM03A.cbl:773-777
        {
            Display("ERROR OPENING XREFFILE");
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }

        _wsFlDd = "CUSTFILE";               // MOVE 'CUSTFILE' TO WS-FL-DD // source: CBSTM03A.cbl:779
        // GO TO 0000-START // source: CBSTM03A.cbl:780
    }

    // =================================================================================================
    // 8300-CUSTFILE-OPEN. // source: CBSTM03A.cbl:783-799
    // =================================================================================================
    private void CustfileOpen8300()
    {
        _m03b.Dd = "CUSTFILE";              // source: CBSTM03A.cbl:784
        _m03b.Oper = 'O';                   // SET M03B-OPEN TO TRUE // source: CBSTM03A.cbl:785
        _m03b.Rc = "00";                    // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:786
        _io.Call(_m03b);                    // CALL 'CBSTM03B' // source: CBSTM03A.cbl:787

        if (_m03b.Rc is "00" or "04")       // source: CBSTM03A.cbl:789-790
        {
            // CONTINUE
        }
        else                                 // source: CBSTM03A.cbl:791-795
        {
            Display("ERROR OPENING CUSTFILE");
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }

        _wsFlDd = "ACCTFILE";               // MOVE 'ACCTFILE' TO WS-FL-DD // source: CBSTM03A.cbl:797
        // GO TO 0000-START // source: CBSTM03A.cbl:798
    }

    // =================================================================================================
    // 8400-ACCTFILE-OPEN. // source: CBSTM03A.cbl:801-816
    // =================================================================================================
    private bool AcctfileOpen8400()
    {
        _m03b.Dd = "ACCTFILE";              // source: CBSTM03A.cbl:802
        _m03b.Oper = 'O';                   // SET M03B-OPEN TO TRUE // source: CBSTM03A.cbl:803
        _m03b.Rc = "00";                    // MOVE ZERO TO WS-M03B-RC // source: CBSTM03A.cbl:804
        _io.Call(_m03b);                    // CALL 'CBSTM03B' // source: CBSTM03A.cbl:805

        if (_m03b.Rc is "00" or "04")       // source: CBSTM03A.cbl:807-808
        {
            // CONTINUE
        }
        else                                 // source: CBSTM03A.cbl:809-813
        {
            Display("ERROR OPENING ACCTFILE");
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }

        // GO TO 1000-MAINLINE // source: CBSTM03A.cbl:815
        Mainline1000();
        return true; // terminal — does not return to 0000-START
    }

    // =================================================================================================
    // 8500-READTRNX-READ — build the per-card transaction table from the sequential TRNXFILE.
    // source: CBSTM03A.cbl:818-853
    // =================================================================================================
    private bool Readtrnx8500Read()
    {
        while (true)
        {
            // IF WS-SAVE-CARD = TRNX-CARD-NUM ... ELSE ... // source: CBSTM03A.cbl:819-825
            if (string.Equals(_wsSaveCard, _trnxCardNum, StringComparison.Ordinal))
            {
                _trCnt += 1;                                 // ADD 1 TO TR-CNT // source: CBSTM03A.cbl:820
            }
            else
            {
                _wsTrct[_crCnt] = _trCnt;                    // MOVE TR-CNT TO WS-TRCT (CR-CNT) // source: CBSTM03A.cbl:822
                _crCnt += 1;                                 // ADD 1 TO CR-CNT // source: CBSTM03A.cbl:823
                _trCnt = 1;                                  // MOVE 1 TO TR-CNT // source: CBSTM03A.cbl:824
            }

            _wsCardNum[_crCnt] = _trnxCardNum;               // MOVE TRNX-CARD-NUM TO WS-CARD-NUM (CR-CNT) // source: CBSTM03A.cbl:827
            _wsTranNum[_crCnt, _trCnt] = _trnxId;            // MOVE TRNX-ID TO WS-TRAN-NUM (CR-CNT, TR-CNT) // source: CBSTM03A.cbl:828
            _wsTranRest[_crCnt, _trCnt] = _trnxRest;         // MOVE TRNX-REST TO WS-TRAN-REST (CR-CNT, TR-CNT) // source: CBSTM03A.cbl:829
            _wsSaveCard = _trnxCardNum;                      // MOVE TRNX-CARD-NUM TO WS-SAVE-CARD // source: CBSTM03A.cbl:830

            _m03b.Dd = "TRNXFILE";                           // source: CBSTM03A.cbl:832
            _m03b.Oper = 'R';                                // SET M03B-READ TO TRUE // source: CBSTM03A.cbl:833
            _m03b.Fldt = new string(' ', 1000);              // MOVE SPACES TO WS-M03B-FLDT // source: CBSTM03A.cbl:834
            _io.Call(_m03b);                                 // CALL 'CBSTM03B' // source: CBSTM03A.cbl:835

            // EVALUATE WS-M03B-RC // source: CBSTM03A.cbl:837-847
            if (_m03b.Rc == "00")
            {
                SetTrnxRecord(_m03b.Fldt);                   // MOVE WS-M03B-FLDT TO TRNX-RECORD // source: CBSTM03A.cbl:839
                // GO TO 8500-READTRNX-READ // source: CBSTM03A.cbl:840
                continue;
            }
            else if (_m03b.Rc == "10")
            {
                break;                                       // GO TO 8599-EXIT // source: CBSTM03A.cbl:841-842
            }
            else
            {
                Display("ERROR READING TRNXFILE");           // source: CBSTM03A.cbl:844
                Display("RETURN CODE: " + _m03b.Rc);         // source: CBSTM03A.cbl:845
                AbendProgram9999();                          // source: CBSTM03A.cbl:846
                break;
            }
        }

        // 8599-EXIT. // source: CBSTM03A.cbl:849-853
        _wsTrct[_crCnt] = _trCnt;                            // MOVE TR-CNT TO WS-TRCT (CR-CNT) // source: CBSTM03A.cbl:850
        _wsFlDd = "XREFFILE";                                // MOVE 'XREFFILE' TO WS-FL-DD // source: CBSTM03A.cbl:851
        // GO TO 0000-START // source: CBSTM03A.cbl:852
        return false; // re-dispatch via 0000-START
    }

    // =================================================================================================
    // 9100/9200/9300/9400 — CLOSE each file via CBSTM03B. // source: CBSTM03A.cbl:856-919
    // =================================================================================================
    private void TrnxfileClose9100() => CloseFile("TRNXFILE", "ERROR CLOSING TRNXFILE"); // source: CBSTM03A.cbl:856-870
    private void XreffileClose9200() => CloseFile("XREFFILE", "ERROR CLOSING XREFFILE"); // source: CBSTM03A.cbl:873-887
    private void CustfileClose9300() => CloseFile("CUSTFILE", "ERROR CLOSING CUSTFILE"); // source: CBSTM03A.cbl:889-903
    private void AcctfileClose9400() => CloseFile("ACCTFILE", "ERROR CLOSING ACCTFILE"); // source: CBSTM03A.cbl:905-919

    private void CloseFile(string dd, string errorMsg)
    {
        _m03b.Dd = dd;                       // MOVE dd TO WS-M03B-DD
        _m03b.Oper = 'C';                    // SET M03B-CLOSE TO TRUE
        _m03b.Rc = "00";                     // MOVE ZERO TO WS-M03B-RC
        _io.Call(_m03b);                     // CALL 'CBSTM03B'

        if (_m03b.Rc is "00" or "04")        // IF WS-M03B-RC = '00' OR '04'
        {
            // CONTINUE
        }
        else
        {
            Display(errorMsg);
            Display("RETURN CODE: " + _m03b.Rc);
            AbendProgram9999();
        }
        // EXIT
    }

    // =================================================================================================
    // 9999-ABEND-PROGRAM — DISPLAY 'ABENDING PROGRAM'; CALL 'CEE3ABD'. // source: CBSTM03A.cbl:921-923
    // =================================================================================================
    private void AbendProgram9999()
    {
        Display("ABENDING PROGRAM");                              // source: CBSTM03A.cbl:922
        _stmt?.Flush();
        _html?.Flush();
        throw new AbendException("0", "CBSTM03A abend (CEE3ABD)."); // CALL 'CEE3ABD' // source: CBSTM03A.cbl:923
    }

    // =================================================================================================
    // STATEMENT-LINES builders — each returns an 80-char plain-text line (PIC X(80)).
    // The literal FILLER VALUEs are reproduced verbatim from WORKING-STORAGE. // source: CBSTM03A.cbl:85-146
    // =================================================================================================

    // ST-LINE0: 31 '*' + 'START OF STATEMENT' (18) + 31 '*'. // source: CBSTM03A.cbl:86-89
    private static string BuildStLine0()
        => Fixed(new string('*', 31) + Fixed("START OF STATEMENT", 18) + new string('*', 31), StmtRecLen);

    // ST-LINE1: ST-NAME X(75) + FILLER X(5) spaces. // source: CBSTM03A.cbl:90-92
    private string BuildStLine1() => Fixed(Alpha(_stName, 75) + new string(' ', 5), StmtRecLen);

    // ST-LINE2: ST-ADD1 X(50) + FILLER X(30) spaces. // source: CBSTM03A.cbl:93-95
    private string BuildStLine2() => Fixed(Alpha(_stAdd1, 50) + new string(' ', 30), StmtRecLen);

    // ST-LINE3: ST-ADD2 X(50) + FILLER X(30) spaces. // source: CBSTM03A.cbl:96-98
    private string BuildStLine3() => Fixed(Alpha(_stAdd2, 50) + new string(' ', 30), StmtRecLen);

    // ST-LINE4: ST-ADD3 X(80). // source: CBSTM03A.cbl:99-100
    private string BuildStLine4() => Fixed(Alpha(_stAdd3, 80), StmtRecLen);

    // ST-LINE5: FILLER ALL '-' X(80). // source: CBSTM03A.cbl:101-102
    private static string BuildStLine5() => new('-', StmtRecLen);

    // ST-LINE6: spaces X(33) + 'Basic Details' X(14) + spaces X(33). // source: CBSTM03A.cbl:103-106
    private static string BuildStLine6()
        => Fixed(new string(' ', 33) + Fixed("Basic Details", 14) + new string(' ', 33), StmtRecLen);

    // ST-LINE7: 'Account ID         :' X(20) + ST-ACCT-ID X(20) + spaces X(40). // source: CBSTM03A.cbl:107-110
    private string BuildStLine7()
        => Fixed(Fixed("Account ID         :", 20) + Alpha(_stAcctId, 20) + new string(' ', 40), StmtRecLen);

    // ST-LINE8: 'Current Balance    :' X(20) + ST-CURR-BAL 9(9).99- (13) + spaces X(7) + spaces X(40).
    // source: CBSTM03A.cbl:111-115
    private string BuildStLine8()
        => Fixed(Fixed("Current Balance    :", 20) + Alpha(_stCurrBal, 13)
                 + new string(' ', 7) + new string(' ', 40), StmtRecLen);

    // ST-LINE9: 'FICO Score         :' X(20) + ST-FICO-SCORE X(20) + spaces X(40). // source: CBSTM03A.cbl:116-119
    private string BuildStLine9()
        => Fixed(Fixed("FICO Score         :", 20) + Alpha(_stFicoScore, 20) + new string(' ', 40), StmtRecLen);

    // ST-LINE10: FILLER ALL '-' X(80). // source: CBSTM03A.cbl:120-121
    private static string BuildStLine10() => new('-', StmtRecLen);

    // ST-LINE11: spaces X(30) + 'TRANSACTION SUMMARY ' X(20) + spaces X(30). // source: CBSTM03A.cbl:122-125
    private static string BuildStLine11()
        => Fixed(new string(' ', 30) + Fixed("TRANSACTION SUMMARY ", 20) + new string(' ', 30), StmtRecLen);

    // ST-LINE12: FILLER ALL '-' X(80). // source: CBSTM03A.cbl:126-127
    private static string BuildStLine12() => new('-', StmtRecLen);

    // ST-LINE13: 'Tran ID         ' X(16) + 'Tran Details    ' X(51) + '  Tran Amount' X(13).
    // source: CBSTM03A.cbl:128-131
    private static string BuildStLine13()
        => Fixed(Fixed("Tran ID         ", 16) + Fixed("Tran Details    ", 51) + Fixed("  Tran Amount", 13), StmtRecLen);

    // ST-LINE14: ST-TRANID X(16) + ' ' X(01) + ST-TRANDT X(49) + '$' X(01) + ST-TRANAMT Z(9).99- (13).
    // source: CBSTM03A.cbl:132-137
    private string BuildStLine14()
        => Fixed(Alpha(_stTranId, 16) + " " + Alpha(_stTranDt, 49) + "$" + Alpha(_stTranAmt, 13), StmtRecLen);

    // ST-LINE14A: 'Total EXP:' X(10) + spaces X(56) + '$' X(01) + ST-TOTAL-TRAMT Z(9).99- (13).
    // source: CBSTM03A.cbl:138-142
    private string BuildStLine14A()
        => Fixed(Fixed("Total EXP:", 10) + new string(' ', 56) + "$" + Alpha(_stTotalTramt, 13), StmtRecLen);

    // ST-LINE15: 32 '*' + 'END OF STATEMENT' (16) + 32 '*'. // source: CBSTM03A.cbl:143-146
    private static string BuildStLine15()
        => Fixed(new string('*', 32) + Fixed("END OF STATEMENT", 16) + new string('*', 32), StmtRecLen);

    // =================================================================================================
    // HTML-LINES builders / fixed 88-level VALUE literals. // source: CBSTM03A.cbl:148-223
    // =================================================================================================

    // 88-level VALUEs that span continued source lines. // source: CBSTM03A.cbl:157-211
    private const string HtmlL08 =
        "<table  align=\"center\" frame=\"box\" style=\"width:70%; font:12px Segoe UI,sans-serif;\">";
    private const string HtmlL10 =
        "<td colspan=\"3\" style=\"padding:0px 5px;background-color:#1d1d96b3;\">";
    private const string HtmlL15 =
        "<td colspan=\"3\" style=\"padding:0px 5px;background-color:#FFAF33;\">";
    private const string HtmlL22_35 =
        "<td colspan=\"3\" style=\"padding:0px 5px;background-color:#f2f2f2;\">";
    private const string HtmlL30_42 =
        "<td colspan=\"3\" style=\"padding:0px 5px;background-color:#33FFD1; text-align:center;\">";
    private const string HtmlL47 =
        "<td style=\"width:25%; padding:0px 5px; background-color:#33FF5E; text-align:left;\">";
    private const string HtmlL50 =
        "<td style=\"width:55%; padding:0px 5px; background-color:#33FF5E; text-align:left;\">";
    private const string HtmlL53 =
        "<td style=\"width:20%; padding:0px 5px; background-color:#33FF5E; text-align:right;\">";
    private const string HtmlL58 =
        "<td style=\"width:25%; padding:0px 5px; background-color:#f2f2f2; text-align:left;\">";
    private const string HtmlL61 =
        "<td style=\"width:55%; padding:0px 5px; background-color:#f2f2f2; text-align:left;\">";
    private const string HtmlL64 =
        "<td style=\"width:20%; padding:0px 5px; background-color:#f2f2f2; text-align:right;\">";

    // HTML-L11: '<h3>Statement for Account Number: ' X(34) + L11-ACCT X(20) + '</h3>' X(05). // source: CBSTM03A.cbl:212-216
    private string BuildHtmlL11()
        => Fixed(Fixed("<h3>Statement for Account Number: ", 34) + Alpha(_l11Acct, 20) + Fixed("</h3>", 5), HtmlRecLen);

    /// <summary>Renders a fixed HTML literal (the 88-level VALUE) into the 100-byte HTML-FIXED-LN record.</summary>
    private static string Html(string fixedLiteral) => Fixed(fixedLiteral, HtmlRecLen);

    // =================================================================================================
    // INITIALIZE helpers
    // =================================================================================================

    // INITIALIZE WS-TRNX-TABLE WS-TRN-TBL-CNTR. // source: CBSTM03A.cbl:294
    private void InitializeWsTrnxTable()
    {
        for (int c = 0; c <= MaxCards; c++)
        {
            _wsCardNum[c] = new string(' ', 16);
            _wsTrct[c] = 0;
            for (int t = 0; t <= MaxTrans; t++)
            {
                _wsTranNum[c, t] = new string(' ', 16);
                _wsTranRest[c, t] = new string(' ', TrnxRestLen);
            }
        }
    }

    // INITIALIZE STATEMENT-LINES: data items -> spaces (the variable fields). // source: CBSTM03A.cbl:459
    private void InitializeStatementLines()
    {
        _stName = new string(' ', 75);
        _stAdd1 = new string(' ', 50);
        _stAdd2 = new string(' ', 50);
        _stAdd3 = new string(' ', 80);
        _stAcctId = new string(' ', 20);
        _stCurrBal = new string(' ', 13);
        _stFicoScore = new string(' ', 20);
        _stTranId = new string(' ', 16);
        _stTranDt = new string(' ', 49);
        _stTranAmt = new string(' ', 13);
        _stTotalTramt = new string(' ', 13);
    }

    // =================================================================================================
    // TRNX-RECORD (COSTM01) decode/move helpers
    // =================================================================================================

    // MOVE FLDT TO TRNX-RECORD: split the 350-byte image into key (card+id) and rest. // source: cpy/COSTM01.cpy:20-36
    private void SetTrnxRecord(string fldt)
    {
        string rec = fldt.Length >= 350 ? fldt[..350] : fldt.PadRight(350, ' ');
        _trnxCardNum = rec[..16];                // TRNX-CARD-NUM X(16)
        _trnxId = rec.Substring(16, 16);         // TRNX-ID       X(16)
        SetTrnxRest(rec.Substring(32, TrnxRestLen)); // TRNX-REST (318)
    }

    // MOVE ... TO TRNX-REST: keep the 318-byte rest and decode TRNX-DESC / TRNX-AMT. // source: cpy/COSTM01.cpy:24-29
    private void SetTrnxRest(string rest)
    {
        _trnxRest = rest.Length >= TrnxRestLen ? rest[..TrnxRestLen] : rest.PadRight(TrnxRestLen, ' ');
        // TRNX-REST layout: TYPE-CD X(2) CAT-CD 9(4) SOURCE X(10) DESC X(100) AMT S9(9)V99(11) ...
        _trnxDesc = _trnxRest.Substring(16, 100);                       // offset 2+4+10 = 16
        string amt = _trnxRest.Substring(116, MoneyDigits);             // offset 16+100 = 116
        _trnxAmt = ZonedDecimalCodec.Decode(
            HostEncoding.For(_host).GetBytes(amt), MoneyScale, signed: true, _host);
    }

    // =================================================================================================
    // Record-image decoders (FLDT -> typed record). Mirror the CBSTM03B serializers byte-for-byte.
    // =================================================================================================

    private CardXref DecodeCardXref(string fldt)
    {
        string r = Pad(fldt, 50);
        return new CardXref
        {
            XrefCardNum = r[..16],
            CustId = ParseDigits(r.Substring(16, 9)),
            AcctId = ParseDigits(r.Substring(25, 11)),
        };
    }

    private static Customer DecodeCustomer(string fldt)
    {
        string r = Pad(fldt, 500);
        int o = 0;
        string Take(int n) { string s = r.Substring(o, n); o += n; return s; }
        return new Customer
        {
            CustId = ParseDigits(Take(9)),
            FirstName = Take(25),
            MiddleName = Take(25),
            LastName = Take(25),
            AddrLine1 = Take(50),
            AddrLine2 = Take(50),
            AddrLine3 = Take(50),
            AddrStateCd = Take(2),
            AddrCountryCd = Take(3),
            AddrZip = Take(10),
            PhoneNum1 = Take(15),
            PhoneNum2 = Take(15),
            Ssn = ParseDigits(Take(9)),
            GovtIssuedId = Take(20),
            DobYyyyMmDd = Take(10),
            EftAccountId = Take(10),
            PriCardHolderInd = Take(1),
            FicoCreditScore = (int)ParseDigits(Take(3)),
        };
    }

    private Account DecodeAccount(string fldt)
    {
        string r = Pad(fldt, 300);
        int o = 0;
        string Take(int n) { string s = r.Substring(o, n); o += n; return s; }
        decimal Money(int n)
        {
            string s = r.Substring(o, n); o += n;
            return ZonedDecimalCodec.Decode(HostEncoding.For(_host).GetBytes(s), 2, signed: true, _host);
        }
        return new Account
        {
            AcctId = ParseDigits(Take(11)),
            ActiveStatus = Take(1),
            CurrBal = Money(12),
            CreditLimit = Money(12),
            CashCreditLimit = Money(12),
            OpenDate = Take(10),
            ExpirationDate = Take(10),
            ReissueDate = Take(10),
            CurrCycCredit = Money(12),
            CurrCycDebit = Money(12),
            AddrZip = Take(10),
            GroupId = Take(10),
        };
    }

    // =================================================================================================
    // Output / DISPLAY helpers
    // =================================================================================================

    private void OpenOutput(string stmtPath, string htmlPath)
    {
        EnsureDir(stmtPath);
        EnsureDir(htmlPath);
        _stmt = new StreamWriter(new FileStream(stmtPath, FileMode.Create, FileAccess.Write, FileShare.Read));
        _html = new StreamWriter(new FileStream(htmlPath, FileMode.Create, FileAccess.Write, FileShare.Read));
    }

    private void CloseStmtHtml()
    {
        _stmt?.Flush();
        _html?.Flush();
    }

    private static void EnsureDir(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    // WRITE FD-STMTFILE-REC FROM <line>: the 80-byte record, one statement line.
    private void WriteStmt(string line) => _stmt!.WriteLine(Fixed(line, StmtRecLen));

    // WRITE FD-HTMLFILE-REC FROM <line> / WRITE FD-HTMLFILE-REC: the 100-byte record.
    private void WriteHtml(string line) => _html!.WriteLine(Fixed(line, HtmlRecLen));

    // WRITE FD-HTMLFILE-REC (the STRING result already lives in the 100-byte record area).
    private void WriteHtmlRaw(string built) => WriteHtml(Alpha(built, HtmlRecLen));

    // DISPLAY -> SYSOUT.
    private void Display(string line) => _sysout.Add(line);

    // =================================================================================================
    // Edited-numeric (trailing sign) + STRING + MOVE helpers
    // =================================================================================================

    // ST-CURR-BAL PIC 9(9).99- : 9 forced integer digits, '.', 2 frac digits, trailing sign. // source: CBSTM03A.cbl:113
    private static string Edit9V99Trailing(decimal value)
        => EditTrailingSign(value, intDigits: 9, suppress: false);

    // ST-TRANAMT / ST-TOTAL-TRAMT PIC Z(9).99- : 9 zero-suppressed digits, '.', 2 frac, trailing sign.
    // source: CBSTM03A.cbl:137,142
    private static string EditZ9V99Trailing(decimal value)
        => EditTrailingSign(value, intDigits: 9, suppress: true);

    /// <summary>
    /// Formats <paramref name="value"/> for a COBOL edited PIC with a TRAILING sign: integer part
    /// (<paramref name="intDigits"/> digits, optionally zero-suppressed), '.', two fraction digits, then a
    /// trailing sign position ('-' for negative, ' ' otherwise). Truncate-toward-zero, silent overflow.
    /// </summary>
    private static string EditTrailingSign(decimal value, int intDigits, bool suppress)
    {
        const int fracDigits = 2;
        bool negative = value < 0m;
        decimal scaled = decimal.Truncate(Math.Abs(value) * Decimals.Pow10(fracDigits));
        scaled %= Decimals.Pow10(intDigits + fracDigits);  // silent overflow to the field width
        string allDigits = ((long)scaled).ToString().PadLeft(intDigits + fracDigits, '0');
        string intStr = allDigits[..intDigits];
        string fracStr = allDigits[intDigits..];

        var sb = new StringBuilder(intDigits + 1 + fracDigits + 1);
        if (suppress)
        {
            bool significant = false;
            foreach (char d in intStr)
            {
                if (!significant && d == '0') sb.Append(' ');
                else { sb.Append(d); significant = true; }
            }
        }
        else
        {
            sb.Append(intStr);
        }
        sb.Append('.');
        sb.Append(fracStr);
        sb.Append(negative ? '-' : ' '); // trailing sign
        return sb.ToString();
    }

    /// <summary>
    /// COBOL <c>STRING ... DELIMITED BY ...</c> into a receiving field: appends each source item, but when an
    /// item has a non-null delimiter it contributes only the text up to (not including) the first occurrence
    /// of that delimiter (DELIMITED BY 'x'); a null delimiter means DELIMITED BY SIZE (the whole item).
    /// </summary>
    private static string StringDelimited(params (string Source, string? Delimiter)[] items)
    {
        var sb = new StringBuilder();
        foreach ((string source, string? delimiter) in items)
        {
            string s = source ?? "";
            if (delimiter is null || delimiter.Length == 0)
            {
                sb.Append(s); // DELIMITED BY SIZE
            }
            else
            {
                int idx = s.IndexOf(delimiter, StringComparison.Ordinal);
                sb.Append(idx < 0 ? s : s[..idx]);
            }
        }
        return sb.ToString();
    }

    /// <summary>WS-FL-DD trimmed to its EVALUATE comparison value.</summary>
    private string FlDd() => (_wsFlDd ?? "").PadRight(8).TrimEnd();

    // ADD into / store into a COMP-3 / display S9(9)V99 field (truncate-toward-zero, silent overflow).
    private static decimal AddMoney(decimal a, decimal b) => Decimals.Store(a + b, 9, 2, signed: true);
    private static decimal StoreMoney(decimal value) => Decimals.Store(value, 9, 2, signed: true);

    /// <summary>WS-CARD-NUM (n) — the saved card number for table slot n (space-filled when unset).</summary>
    private string CardNum(int n) => _wsCardNum[n] ?? new string(' ', 16);

    /// <summary>COBOL alphanumeric MOVE of a PIC X(width): left-justify, space-pad / right-truncate.</summary>
    private static string Alpha(string? text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }

    /// <summary>Fixed-width record body: pad/truncate to exactly <paramref name="width"/> characters.</summary>
    private static string Fixed(string text, int width)
        => text.Length >= width ? text[..width] : text.PadRight(width, ' ');

    /// <summary>Right-pads a record image string to at least <paramref name="width"/> characters.</summary>
    private static string Pad(string s, int width) => s.Length >= width ? s[..width] : s.PadRight(width, ' ');

    /// <summary>COBOL intra-word trim for STRING DELIMITED BY ' ' sources (leading/trailing handled by delimiter).</summary>
    private static string Trim(string? s) => (s ?? "");

    /// <summary>Unsigned zoned-digit string for a numeric MOVE to an alphanumeric/edited 9(width) display.</summary>
    private static string ZonedDigits(long value, int width)
    {
        decimal modulus = Decimals.Pow10(width);
        decimal v = Math.Abs((decimal)value) % modulus;
        return ((long)v).ToString().PadLeft(width, '0');
    }

    /// <summary>MOVE of an alphanumeric/zoned key region to a numeric FD key — parse the decimal digits.</summary>
    private static long ParseDigits(string text)
    {
        long value = 0;
        foreach (char ch in text)
            if (ch >= '0' && ch <= '9')
                value = value * 10 + (ch - '0');
        return value;
    }
}
