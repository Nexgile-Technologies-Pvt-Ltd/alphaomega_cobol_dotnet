using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COACTVWC</c> — the "Account View" inquiry
/// screen (TRANSID <c>CAVW</c>, BMS map <c>CACTVWA</c> / mapset <c>COACTVW</c>).
/// </summary>
/// <remarks>
/// <para>
/// COACTVWC is a pure read-only inquiry: it accepts an 11-digit account number on a 24x80 BMS screen,
/// validates it, then resolves the account to a customer via the card cross-reference alternate index
/// (by ACCT-ID, file <c>CXACAIX</c> = table CARD_XREF) and reads the account master (<c>ACCTDAT</c> =
/// ACCOUNT) and customer master (<c>CUSTDAT</c> = CUSTOMER) to assemble a detail display. It never
/// updates anything. Each invocation is one RECEIVE + one SEND, ending with
/// <c>EXEC CICS RETURN TRANSID('CAVW') COMMAREA(...)</c> (pseudo-conversational); PF3 XCTLs back to the
/// caller (or the main menu).
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COACTVWC.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c>/<c>GO TO</c> control flow, the
/// COMMAREA field usage (<see cref="CardDemoCommArea"/>), and every faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Every <c>EXEC CICS READ ... RIDFLD</c> is a keyed
/// <c>ReadByKey</c> (no browse, no write). The repository <c>FileStatus</c> is mapped to the CICS RESP
/// code the COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→
/// NOTFND(13), anything else→an OTHER/file-error (1).</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — Duplicate <c>0000-MAIN-EXIT</c> paragraph (declared twice in succession). Reproduced as
/// a no-op duplicate method. source: COACTVWC.cbl:408-413</item>
/// <item>FB-2 — Stray sequence-number artifact <c>00</c> trailing the not-numeric MOVE literal (line
/// 672); the message text itself is kept exactly, including the double space in
/// <c>"must&#160;&#160;be"</c>. source: COACTVWC.cbl:671-673</item>
/// <item>FB-3 — The MOVEd not-numeric literal <c>"Account Filter must  be a non-zero 11 digit number"</c>
/// (double space) differs from the unused 88-level literals (single spaces); the MOVEd one is used.
/// source: COACTVWC.cbl:125-128, 671-673</item>
/// <item>FB-4 — CUSTDAT NOTFND sets <c>FLG-CUSTFILTER-NOT-OK</c>, but <c>9000-READ-ACCT</c> guards on
/// <c>DID-NOT-FIND-CUST-IN-CUSTDAT</c> which is never SET (its SET is commented out). The guard can
/// never be true; INPUT-ERROR carries the error to the screen instead. source: COACTVWC.cbl:839-842,713-715</item>
/// <item>FB-5 — ACCTDAT NOTFND likewise sets only <c>INPUT-ERROR</c>+<c>FLG-ACCTFILTER-NOT-OK</c>; the
/// <c>SET DID-NOT-FIND-ACCT-IN-ACCTDAT</c> is commented out, so the <c>IF DID-NOT-FIND-ACCT-IN-ACCTDAT</c>
/// guard in 9000 is dead and control <b>falls through to 9400-GETCUSTDATA</b> after an account-not-found,
/// reading CUSTDAT with the (zero) CDEMO-CUST-ID from the failed xref/acct path. source: COACTVWC.cbl:789-806,704-706,708-711</item>
/// <item>FB-6 — In 9400, <c>MOVE WS-RESP-CD/REAS-CD TO ERROR-RESP/RESP2</c> is issued <b>before</b> the
/// <c>IF WS-RETURN-MSG-OFF</c> guard (unlike 9200/9300 which do it inside). Ordering kept. source: COACTVWC.cbl:843-845</item>
/// <item>FB-7 — Many declared XCTL-target literals (COCRDLIC/COCRDUPC/COCRDSLC) and several 88-level
/// messages (WS-INFORM-OUTPUT, SEARCHED-ACCT-*, DID-NOT-FIND-*, etc.) are dead — never SET/used. Not
/// wired up. source: COACTVWC.cbl:115-138,151-183</item>
/// </list>
/// </remarks>
public sealed class AccountViewProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-LITERALS — source: COACTVWC.cbl:142-202
    // =============================================================================================
    private const string ProgramId = "COACTVWC";   // WS-THISPGM PIC X(8). source: COACTVWC.cbl:143-144
    private const string TransactionId = "CAVW";    // WS-THISTRANID PIC X(4). source: COACTVWC.cbl:145-146
    private const string MapSetId = "COACTVW"; // WS-THISMAPSET PIC X(8). source: COACTVWC.cbl:147-148 ('COACTVW ' X(8))
    private const string MapId = "CACTVWA";    // WS-THISMAP PIC X(7). source: COACTVWC.cbl:149-150
    private const string MenuProgramId = "COMEN01C";   // WS-MENUPGM PIC X(8). source: COACTVWC.cbl:168-169
    private const string MenuTransactionId = "CM00";    // WS-MENUTRANID PIC X(4). source: COACTVWC.cbl:170-171

    private const string AccountFileName = "ACCTDAT ";          // WS-ACCTFILENAME PIC X(8). source: COACTVWC.cbl:184-185
    private const string CustomerFileName = "CUSTDAT ";          // WS-CUSTFILENAME PIC X(8). source: COACTVWC.cbl:188-189
    private const string CardXrefFileName = "CXACAIX "; // WS-CARDXREFNAME (acct alt-index path) PIC X(8). source: COACTVWC.cbl:192-193

    // CCDA-TITLE01/02 (COTTL01Y) — shared screen header. source: COTTL01Y.cpy.
    private const string Title01 = "      AWS Mainframe Modernization       "; // CCDA-TITLE01 PIC X(40)
    private const string Title02 = "              CardDemo                  "; // CCDA-TITLE02 PIC X(40)

    // =============================================================================================
    //  WS-MISC-STORAGE flags — source: COACTVWC.cbl:35-105
    // =============================================================================================

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. source: COACTVWC.cbl:40-43
    private int _responseCode; // WS-RESP-CD
    private int _reasonCode;   // WS-REAS-CD

    // 05 WS-INPUT-FLAG: 88 INPUT-OK='0' / INPUT-ERROR='1' / INPUT-PENDING=LOW-VALUES. source: :50-53
    // Initialised to LOW-VALUES (INPUT-PENDING) by INITIALIZE WS-MISC-STORAGE.
    private char _inputFlag = '\0'; // WS-INPUT-FLAG
    private bool InputOk => _inputFlag == '0';     // 88 INPUT-OK
    private bool InputError => _inputFlag == '1';  // 88 INPUT-ERROR
    private void SetInputOk() => _inputFlag = '0';
    private void SetInputError() => _inputFlag = '1';

    // 05 WS-EDIT-ACCT-FLAG: 88 FLG-ACCTFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :58-61
    private char _editAcctFlag = '\0'; // WS-EDIT-ACCT-FLAG
    private bool FlgAcctFilterNotOk => _editAcctFlag == '0';  // 88 FLG-ACCTFILTER-NOT-OK
    private bool FlgAcctFilterBlank => _editAcctFlag == ' ';  // 88 FLG-ACCTFILTER-BLANK
    private void SetFlgAcctFilterNotOk() => _editAcctFlag = '0';
    private void SetFlgAcctFilterIsValid() => _editAcctFlag = '1';
    private void SetFlgAcctFilterBlank() => _editAcctFlag = ' ';

    // 05 WS-EDIT-CUST-FLAG: 88 FLG-CUSTFILTER-NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :62-65
    private char _editCustFlag = '\0'; // WS-EDIT-CUST-FLAG
    private void SetFlgCustFilterNotOk() => _editCustFlag = '0';

    // 05 WS-FILE-READ-FLAGS. source: COACTVWC.cbl:81-85
    // 88 FOUND-ACCT-IN-MASTER VALUE '1' ; 88 FOUND-CUST-IN-MASTER VALUE '1'.
    private char _accountMasterReadFlag = '\0'; // WS-FILE-READ-FLAGS (acct)
    private char _custMasterReadFlag = '\0';    // WS-FILE-READ-FLAGS (cust)
    private bool FoundAcctInMaster => _accountMasterReadFlag == '1'; // 88 FOUND-ACCT-IN-MASTER
    private bool FoundCustInMaster => _custMasterReadFlag == '1';    // 88 FOUND-CUST-IN-MASTER
    private void SetFoundAcctInMaster() => _accountMasterReadFlag = '1';
    private void SetFoundCustInMaster() => _custMasterReadFlag = '1';

    // 05 WS-FILE-ERROR-MESSAGE structure. source: COACTVWC.cbl:86-105
    private string _errorOpname = "        "; // ERROR-OPNAME X(8)
    private string _errorFile = "         ";  // ERROR-FILE   X(9)
    private string _errorResp = "          "; // ERROR-RESP   X(10)
    private string _errorResp2 = "          "; // ERROR-RESP2 X(10)

    // =============================================================================================
    //  WS-INFO-MSG (40) — 88 levels. source: COACTVWC.cbl:110-116
    // =============================================================================================
    // WS-NO-INFO-MESSAGE = SPACES/LOW-VALUES ; WS-PROMPT-FOR-INPUT ; WS-INFORM-OUTPUT (dead).
    private string _infoMessage = ""; // WS-INFO-MSG PIC X(40)
    private bool NoInfoMessage => IsSpacesOrLowValues(_infoMessage); // 88 WS-NO-INFO-MESSAGE
    private void SetNoInfoMessage() => _infoMessage = "";            // SET WS-NO-INFO-MESSAGE TO TRUE (SPACES)
    private const string PromptForInput = "Enter or update id of account to display"; // WS-PROMPT-FOR-INPUT PIC X(40). source: :113-114
    private void SetPromptForInput() => _infoMessage = PromptForInput;

    // =============================================================================================
    //  WS-RETURN-MSG (75) — 88 levels. source: COACTVWC.cbl:117-138
    // =============================================================================================
    // 88 WS-RETURN-MSG-OFF = SPACES.   88 WS-PROMPT-FOR-ACCT = 'Account number not provided'.
    // 88 NO-SEARCH-CRITERIA-RECEIVED = 'No input received'.  (Other 88s are dead — FB-7.)
    private string _returnMessage = ""; // WS-RETURN-MSG PIC X(75)
    private bool ReturnMessageOff => IsSpaces(_returnMessage);          // 88 WS-RETURN-MSG-OFF
    private void SetReturnMessageOff() => _returnMessage = "";          // SET WS-RETURN-MSG-OFF TO TRUE
    private void SetPromptForAcct() => _returnMessage = "Account number not provided"; // source: :121-122
    private void SetNoSearchCriteriaReceived() => _returnMessage = "No input received";  // source: :123-124

    // =============================================================================================
    //  WS-XREF-RID — the VSAM key area + its X redefines. source: COACTVWC.cbl:73-80
    // =============================================================================================
    // 10 WS-CARD-RID-CUST-ID 9(09) REDEFINES WS-CARD-RID-CUST-ID-X X(09)
    // 10 WS-CARD-RID-ACCT-ID 9(11) REDEFINES WS-CARD-RID-ACCT-ID-X X(11)
    private long _acctIdKey;  // WS-CARD-RID-ACCT-ID 9(11)
    private long _custIdKey;  // WS-CARD-RID-CUST-ID 9(09)
    // The -X redefine (zero-padded char form) used for the key and the message text.
    private string AcctIdKeyText => Zoned(_acctIdKey, 11); // WS-CARD-RID-ACCT-ID-X X(11)
    private string CustIdKeyText => Zoned(_custIdKey, 9);  // WS-CARD-RID-CUST-ID-X X(09)

    // =============================================================================================
    //  CC-WORK-AREA (CVCRD01Y): CC-ACCT-ID X(11) (redefine CC-ACCT-ID-N 9(11)). source: CVCRD01Y.cpy
    // =============================================================================================
    // CC-ACCT-ID is the X(11) form used for the class/space tests in 2210-EDIT-ACCOUNT.
    // LOW-VALUES sentinel modeled as null; SPACES as 11 blanks; otherwise the 11-char keyed value.
    private string _accountIdInput = ""; // CC-ACCT-ID X(11)

    // =============================================================================================
    //  Record images read from the files (CVACT01Y / CVCUS01Y / CVACT03Y).
    // =============================================================================================
    private Account? _account;       // ACCOUNT-RECORD
    private Customer? _customer;      // CUSTOMER-RECORD
    private CardXref? _cardXref;      // CARD-XREF-RECORD

    // =============================================================================================
    //  COMMAREA (typed view) + the appended trailer WS-THIS-PROGCOMMAREA. source: :211-216
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    // WS-THIS-PROGCOMMAREA: CA-FROM-PROGRAM X(8) / CA-FROM-TRANID X(4) — INITIALIZEd, parsed, written
    // back, but not otherwise used in logic. source: COACTVWC.cbl:213-216, 288-292, 398-400.
    private string _trailerFromProgram = ""; // CA-FROM-PROGRAM X(8)
    private string _trailerFromTranId = "";  // CA-FROM-TRANID X(4)

    // The CCARD-AID code (from YYYY-STORE-PFKEY), and the post-gate forced ENTER/PF3. source: :306-314
    private CcardAid _ccardAid = CcardAid.None;
    private bool CcardAidEnter => _ccardAid == CcardAid.Enter; // 88 CCARD-AID-ENTER
    private bool CcardAidPfk03 => _ccardAid == CcardAid.Pfk03; // 88 CCARD-AID-PFK03

    // 05 WS-PFK-FLAG: 88 PFK-VALID='0' / PFK-INVALID='1'. source: COACTVWC.cbl:54-57
    private char _pfKeyFlag = '\0'; // WS-PFK-FLAG
    private bool PfkInvalid => _pfKeyFlag == '1';
    private void SetPfkValid() => _pfKeyFlag = '0';
    private void SetPfkInvalid() => _pfKeyFlag = '1';

    // The per-turn symbolic BMS map (CACTVWAI / CACTVWAO).
    private BmsMap _map = null!;

    private readonly RelationalDb _db;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. Per-table repositories are created
    /// from <c>db.Connection</c> inside the read paragraphs when needed (no DB is opened here).
    /// </summary>
    public AccountViewProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public AccountViewProgram()
    {
        _db = null!;
    }

    /// <inheritdoc/>
    public string ProgramName => ProgramId; // PROGRAM-ID. COACTVWC. source: COACTVWC.cbl:22-23

    /// <inheritdoc/>
    public string TransId => TransactionId;  // CSD: CAVW -> COACTVWC. source: CSD_TRANSACTIONS.md; cbl:145-146

    // =============================================================================================
    //  0000-MAIN — source: COACTVWC.cbl:262-393
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // EXEC CICS HANDLE ABEND LABEL(ABEND-ROUTINE). source: :264-266 (modeled as a try/catch wrapper).
        try
        {
            MainProcess(ctx);
        }
        catch (Exception)
        {
            // ABEND-ROUTINE: send ABEND-DATA and ABEND ABCODE('9999'). source: :916-937.
            // Headless model: surface the abend as a plain-text send + terminal end (no real dump).
            AbendRoutine(ctx);
        }
    }

    private void MainProcess(CicsContext ctx) // COBOL paragraph: 0000-MAIN
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE re-initialised per turn).
        _map = BuildMap();

        // INITIALIZE CC-WORK-AREA WS-MISC-STORAGE WS-COMMAREA. source: :268-270
        // (Working-storage starts at COBOL VALUE/SPACES/LOW-VALUES; the handler instance is fresh.)

        // MOVE LIT-THISTRANID TO WS-TRANID. source: :274 (WS-TRANID is informational only.)

        // SET WS-RETURN-MSG-OFF TO TRUE. source: :278
        SetReturnMessageOff();

        // Store passed data if any. source: :282-293
        //   IF EIBCALEN = 0
        //   OR (CDEMO-FROM-PROGRAM = LIT-MENUPGM AND NOT CDEMO-PGM-REENTER)
        //      INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA
        //   ELSE
        //      MOVE DFHCOMMAREA(1:LENGTH OF CARDDEMO-COMMAREA) TO CARDDEMO-COMMAREA
        //      MOVE DFHCOMMAREA(... + 1 : ...)                 TO WS-THIS-PROGCOMMAREA
        bool freshCommarea =
            ctx.EibCalen == 0
            || (ctx.CommArea is { } ca0 && ca0.FromProgram == PadX(MenuProgramId, 8) && !ca0.IsReenter);

        if (freshCommarea)
        {
            // INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA. source: :285-286
            _commArea = new CardDemoCommArea();
            _trailerFromProgram = "";
            _trailerFromTranId = "";
        }
        else
        {
            // Copy the carried COMMAREA + trailer. source: :288-292
            _commArea = ctx.CommArea!;
            _trailerFromProgram = ""; // trailer is not transported by the typed COMMAREA; INITIALIZE-equivalent.
            _trailerFromTranId = "";
        }

        // PERFORM YYYY-STORE-PFKEY. source: :299-300 — EIBAID -> CCARD-AID-*.
        _ccardAid = ctx.StorePfKey();

        // Remap PFkeys: only ENTER / PF3 valid; force everything else to ENTER. source: :306-314
        SetPfkInvalid();                             // SET PFK-INVALID TO TRUE
        if (CcardAidEnter || CcardAidPfk03)          // IF CCARD-AID-ENTER OR CCARD-AID-PFK03
            SetPfkValid();                           //    SET PFK-VALID TO TRUE
        if (PfkInvalid)                              // IF PFK-INVALID
            _ccardAid = CcardAid.Enter;              //    SET CCARD-AID-ENTER TO TRUE

        // EVALUATE TRUE — main dispatch. source: :323-383
        if (CcardAidPfk03)
        {
            // WHEN CCARD-AID-PFK03 — XCTL to caller or main menu. source: :324-352
            // IF CDEMO-FROM-TRANID = LOW-VALUES OR SPACES -> CM00 ELSE FROM-TRANID. source: :328-333
            if (IsLowValuesOrSpaces(_commArea.FromTranId))
                _commArea.ToTranId = MenuTransactionId;
            else
                _commArea.ToTranId = _commArea.FromTranId;

            // IF CDEMO-FROM-PROGRAM = LOW-VALUES OR SPACES -> COMEN01C ELSE FROM-PROGRAM. source: :334-339
            if (IsLowValuesOrSpaces(_commArea.FromProgram))
                _commArea.ToProgram = MenuProgramId;
            else
                _commArea.ToProgram = _commArea.FromProgram;

            _commArea.FromTranId = TransactionId;   // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :341
            _commArea.FromProgram = ProgramId;     // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :342

            _commArea.SetUser();                     // SET CDEMO-USRTYP-USER TO TRUE. source: :344
            _commArea.PgmContext = 0;                // SET CDEMO-PGM-ENTER   TO TRUE. source: :345
            _commArea.LastMapSet = MapSetId;   // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :346
            _commArea.LastMap = MapId;         // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :347

            // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :349-352
            ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
            return;
        }
        else if (_commArea.IsFirstEntry) // WHEN CDEMO-PGM-ENTER. source: :353-360
        {
            // Coming from some other context — gather selection criteria (empty prompt screen).
            SendMap(ctx);                // PERFORM 1000-SEND-MAP. source: :358-359
            CommonReturn(ctx);           // GO TO COMMON-RETURN. source: :360
            return;
        }
        else if (_commArea.IsReenter)    // WHEN CDEMO-PGM-REENTER. source: :361-374
        {
            ProcessInputs(ctx);            // PERFORM 2000-PROCESS-INPUTS. source: :362-363
            if (InputError)                // IF INPUT-ERROR. source: :364
            {
                SendMap(ctx);              // PERFORM 1000-SEND-MAP. source: :365-366
                CommonReturn(ctx);         // GO TO COMMON-RETURN. source: :367
                return;
            }
            else
            {
                ReadAccount(ctx);          // PERFORM 9000-READ-ACCT. source: :369-370
                SendMap(ctx);              // PERFORM 1000-SEND-MAP. source: :371-372
                CommonReturn(ctx);         // GO TO COMMON-RETURN. source: :373
                return;
            }
        }
        else
        {
            // WHEN OTHER — abend scenario (no real abend; SEND-PLAIN-TEXT + RETURN). source: :375-382
            // MOVE 'UNEXPECTED DATA SCENARIO' TO WS-RETURN-MSG. source: :379-380
            _returnMessage = "UNEXPECTED DATA SCENARIO";
            SendPlainText(ctx);           // PERFORM SEND-PLAIN-TEXT. source: :381
            return;                       // SEND-PLAIN-TEXT issues EXEC CICS RETURN (terminal end).
        }

        // NOTE: the fall-through guard below (IF INPUT-ERROR ...) is unreachable from the branches above
        // because each WHEN ends with GO TO COMMON-RETURN / XCTL / RETURN. It is kept for fidelity with
        // the COBOL layout (lines :387-392) but never executes given the structured dispatch.
        // IF INPUT-ERROR MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG; PERFORM 1000-SEND-MAP; GO TO COMMON-RETURN.
    }

    // =============================================================================================
    //  COMMON-RETURN — source: COACTVWC.cbl:394-407
    // =============================================================================================
    private void CommonReturn(CicsContext ctx)
    {
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. source: :395
        // (CCARD-ERROR-MSG is staged into the map's ERRMSG by 1200-SETUP-SCREEN-VARS already; this final
        //  move keeps the COMMAREA-side error field in sync. No observable screen change at this point.)

        // Reassemble WS-COMMAREA = [CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA] and RETURN. source: :397-406
        // The typed CardDemoCommArea carries the first segment; the trailer (CA-FROM-*) is informational
        // and not part of the typed image — preserve the 2000-byte semantics conceptually via TransId.
        _ = _trailerFromProgram;
        _ = _trailerFromTranId;

        // EXEC CICS RETURN TRANSID(LIT-THISTRANID) COMMAREA(WS-COMMAREA) LENGTH(LENGTH OF WS-COMMAREA).
        ctx.ReturnTransId(TransactionId, _commArea); // source: :402-406
    }

    // =============================================================================================
    //  0000-MAIN-EXIT (FB-1: duplicated paragraph). source: COACTVWC.cbl:408-413
    // =============================================================================================
    private static void MainExit() { /* EXIT. source: :408-410 */ } // COBOL paragraph: 0000-MAIN-EXIT
    private static void MainExitDuplicate() { /* EXIT. (duplicate label) source: :411-413 */ } // COBOL paragraph: 0000-MAIN-EXIT (FB-1)

    // =============================================================================================
    //  1000-SEND-MAP — source: COACTVWC.cbl:416-425
    // =============================================================================================
    private void SendMap(CicsContext ctx) // COBOL paragraph: 1000-SEND-MAP
    {
        InitScreen(ctx);           // PERFORM 1100-SCREEN-INIT. source: :417-418
        SetupScreenVars(ctx);      // PERFORM 1200-SETUP-SCREEN-VARS. source: :419-420
        SetupScreenAttrs();        // PERFORM 1300-SETUP-SCREEN-ATTRS. source: :421-422
        SendScreen(ctx);           // PERFORM 1400-SEND-SCREEN. source: :423-424
    }

    // =============================================================================================
    //  1100-SCREEN-INIT — source: COACTVWC.cbl:431-455
    // =============================================================================================
    private void InitScreen(CicsContext ctx) // COBOL paragraph: 1100-SCREEN-INIT
    {
        // MOVE LOW-VALUES TO CACTVWAO. source: :432
        MoveLowValuesToMapOut();

        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA (twice). source: :434,441
        DateTime now = ctx.Clock.Now;

        // MOVE CCDA-TITLE01/02, LIT-THISTRANID, LIT-THISPGM. source: :436-439
        _map.Field("TITLE01").SetValue(Title01);
        _map.Field("TITLE02").SetValue(Title02);
        _map.Field("TRNNAME").SetValue(TransactionId);
        _map.Field("PGMNAME").SetValue(ProgramId);

        // CURDATEO = MM/DD/YY (year = WS-CURDATE-YEAR(3:2), last two digits). source: :443-447
        string mm = Two(now.Month);
        string dd = Two(now.Day);
        string yy = Four(now.Year).Substring(2, 2);
        _map.Field("CURDATE").SetValue($"{mm}/{dd}/{yy}");

        // CURTIMEO = HH:MM:SS. source: :449-453
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}");
    }

    // =============================================================================================
    //  1200-SETUP-SCREEN-VARS — source: COACTVWC.cbl:460-535
    // =============================================================================================
    private void SetupScreenVars(CicsContext ctx) // COBOL paragraph: 1200-SETUP-SCREEN-VARS
    {
        // IF EIBCALEN = 0 SET WS-PROMPT-FOR-INPUT. source: :462-463
        if (ctx.EibCalen == 0)
        {
            SetPromptForInput();
        }
        else
        {
            // Account id echo: LOW-VALUES if filter blank, else CC-ACCT-ID. source: :465-469
            if (FlgAcctFilterBlank)
                _map.Field("ACCTSID").SetValue("", setMdt: false); // MOVE LOW-VALUES TO ACCTSIDO
            else
                _map.Field("ACCTSID").SetValue(_accountIdInput, setMdt: false); // MOVE CC-ACCT-ID TO ACCTSIDO

            // IF FOUND-ACCT-IN-MASTER OR FOUND-CUST-IN-MASTER -> move ACCT-* to screen. source: :471-491
            if (FoundAcctInMaster || FoundCustInMaster)
            {
                Account a = _account ?? new Account();
                _map.Field("ACSTTUS").SetValue(a.ActiveStatus, setMdt: false);   // ACCT-ACTIVE-STATUS. :473
                SetMoney("ACURBAL", a.CurrBal);                                  // ACCT-CURR-BAL. :475
                SetMoney("ACRDLIM", a.CreditLimit);                              // ACCT-CREDIT-LIMIT. :477
                SetMoney("ACSHLIM", a.CashCreditLimit);                          // ACCT-CASH-CREDIT-LIMIT. :479-480
                SetMoney("ACRCYCR", a.CurrCycCredit);                            // ACCT-CURR-CYC-CREDIT. :482-483
                SetMoney("ACRCYDB", a.CurrCycDebit);                             // ACCT-CURR-CYC-DEBIT. :485
                _map.Field("ADTOPEN").SetValue(a.OpenDate, setMdt: false);       // ACCT-OPEN-DATE. :487
                _map.Field("AEXPDT").SetValue(a.ExpirationDate, setMdt: false);  // ACCT-EXPIRAION-DATE. :488
                _map.Field("AREISDT").SetValue(a.ReissueDate, setMdt: false);    // ACCT-REISSUE-DATE. :489
                _map.Field("AADDGRP").SetValue(a.GroupId, setMdt: false);        // ACCT-GROUP-ID. :490
            }

            // IF FOUND-CUST-IN-MASTER -> move CUST-* to screen. source: :493-523
            if (FoundCustInMaster)
            {
                Customer c = _customer ?? new Customer();
                // MOVE CUST-ID TO ACSTNUMO (9(9) -> X(9), zero-padded display). :494
                _map.Field("ACSTNUM").SetValue(Zoned(c.CustId, 9), setMdt: false);

                // STRING CUST-SSN(1:3) '-' (4:2) '-' (6:4) INTO ACSTSSNO. :496-504
                string ssn = Zoned(c.Ssn, 9); // CUST-SSN 9(9) display
                string ssnFmt = $"{ssn.Substring(0, 3)}-{ssn.Substring(3, 2)}-{ssn.Substring(5, 4)}";
                _map.Field("ACSTSSN").SetValue(ssnFmt, setMdt: false);

                // MOVE CUST-FICO-CREDIT-SCORE TO ACSTFCOO (9(3) -> X(3)). :505-506
                _map.Field("ACSTFCO").SetValue(Zoned(c.FicoCreditScore, 3), setMdt: false);
                _map.Field("ACSTDOB").SetValue(c.DobYyyyMmDd, setMdt: false);     // CUST-DOB-YYYY-MM-DD. :507
                _map.Field("ACSFNAM").SetValue(c.FirstName, setMdt: false);      // CUST-FIRST-NAME. :508
                _map.Field("ACSMNAM").SetValue(c.MiddleName, setMdt: false);     // CUST-MIDDLE-NAME. :509
                _map.Field("ACSLNAM").SetValue(c.LastName, setMdt: false);       // CUST-LAST-NAME. :510
                _map.Field("ACSADL1").SetValue(c.AddrLine1, setMdt: false);      // CUST-ADDR-LINE-1. :511
                _map.Field("ACSADL2").SetValue(c.AddrLine2, setMdt: false);      // CUST-ADDR-LINE-2. :512
                _map.Field("ACSCITY").SetValue(c.AddrLine3, setMdt: false);      // CUST-ADDR-LINE-3 -> ACSCITYO. :513
                _map.Field("ACSSTTE").SetValue(c.AddrStateCd, setMdt: false);    // CUST-ADDR-STATE-CD. :514
                _map.Field("ACSZIPC").SetValue(c.AddrZip, setMdt: false);        // CUST-ADDR-ZIP. :515
                _map.Field("ACSCTRY").SetValue(c.AddrCountryCd, setMdt: false);  // CUST-ADDR-COUNTRY-CD. :516
                _map.Field("ACSPHN1").SetValue(c.PhoneNum1, setMdt: false);      // CUST-PHONE-NUM-1. :517
                _map.Field("ACSPHN2").SetValue(c.PhoneNum2, setMdt: false);      // CUST-PHONE-NUM-2. :518
                _map.Field("ACSGOVT").SetValue(c.GovtIssuedId, setMdt: false);   // CUST-GOVT-ISSUED-ID. :519
                _map.Field("ACSEFTC").SetValue(c.EftAccountId, setMdt: false);   // CUST-EFT-ACCOUNT-ID. :520
                _map.Field("ACSPFLG").SetValue(c.PriCardHolderInd, setMdt: false); // CUST-PRI-CARD-HOLDER-IND. :521-522
            }
        }

        // IF WS-NO-INFO-MESSAGE SET WS-PROMPT-FOR-INPUT. source: :528-530
        if (NoInfoMessage)
            SetPromptForInput();

        // MOVE WS-RETURN-MSG TO ERRMSGO. source: :532
        _map.Field("ERRMSG").SetValue(_returnMessage, setMdt: false);

        // MOVE WS-INFO-MSG TO INFOMSGO. source: :534
        _map.Field("INFOMSG").SetValue(_infoMessage, setMdt: false);
    }

    // =============================================================================================
    //  1300-SETUP-SCREEN-ATTRS — source: COACTVWC.cbl:541-572
    // =============================================================================================
    private void SetupScreenAttrs() // COBOL paragraph: 1300-SETUP-SCREEN-ATTRS
    {
        ScreenField acctsid = _map.Field("ACCTSID");

        // MOVE DFHBMFSE TO ACCTSIDA OF CACTVWAI. source: :543
        // DFHBMFSE = unprotected + MDT(FSET) + free-keyboard attribute byte. Apply as an attribute
        // override (the field stays keyable with its MDT set on the next SEND).
        acctsid.AttributeOverride =
            BmsAttribute.Unprotected | BmsAttribute.Normal | BmsAttribute.Fset;

        // POSITION CURSOR: every branch does MOVE -1 TO ACCTSIDL. source: :546-552
        acctsid.CursorLength = -1;

        // SETUP COLOR: default DFHDFCOL; FLG-ACCTFILTER-NOT-OK -> DFHRED. source: :555-559
        acctsid.ColorOverride = BmsColor.Default; // DFHDFCOL (device default colour)
        if (FlgAcctFilterNotOk)
            acctsid.ColorOverride = BmsColor.Red; // DFHRED

        // IF FLG-ACCTFILTER-BLANK AND CDEMO-PGM-REENTER -> ACCTSIDO='*' + DFHRED. source: :561-565
        if (FlgAcctFilterBlank && _commArea.IsReenter)
        {
            acctsid.SetValue("*", setMdt: false);
            acctsid.ColorOverride = BmsColor.Red;
        }

        // INFOMSG colour: no info -> DFHBMDAR (dark/non-display); else DFHNEUTR. source: :567-571
        ScreenField infomsg = _map.Field("INFOMSG");
        if (NoInfoMessage)
            infomsg.AttributeOverride = infomsg.Attribute | BmsAttribute.Dark; // DFHBMDAR (dark)
        else
            infomsg.ColorOverride = BmsColor.Neutral; // DFHNEUTR
    }

    // =============================================================================================
    //  1400-SEND-SCREEN — source: COACTVWC.cbl:577-591
    // =============================================================================================
    private void SendScreen(CicsContext ctx) // COBOL paragraph: 1400-SEND-SCREEN
    {
        // MOVE LIT-THISMAPSET/MAP TO CCARD-NEXT-MAPSET/MAP. source: :579-580
        // SET CDEMO-PGM-REENTER TO TRUE — next turn is a re-entry. source: :581
        _commArea.SetReenter();
        _commArea.LastMapSet = MapSetId;
        _commArea.LastMap = MapId;

        // EXEC CICS SEND MAP(CACTVWA) MAPSET(COACTVW) FROM(CACTVWAO) CURSOR ERASE FREEKB. source: :583-590
        ctx.SendMap(MapId, MapSetId, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
    }

    // =============================================================================================
    //  2000-PROCESS-INPUTS — source: COACTVWC.cbl:596-605
    // =============================================================================================
    private void ProcessInputs(CicsContext ctx) // COBOL paragraph: 2000-PROCESS-INPUTS
    {
        ReceiveMap(ctx);        // PERFORM 2100-RECEIVE-MAP. source: :597-598
        EditMapInputs();        // PERFORM 2200-EDIT-MAP-INPUTS. source: :599-600
        // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG; set next prog/mapset/map literals. source: :601-604
        // (CCARD-* are staging fields; the COMMAREA-side error is rendered via ERRMSGO at SEND time.)
    }

    // =============================================================================================
    //  2100-RECEIVE-MAP — source: COACTVWC.cbl:610-617
    // =============================================================================================
    private void ReceiveMap(CicsContext ctx) // COBOL paragraph: 2100-RECEIVE-MAP
    {
        // EXEC CICS RECEIVE MAP(CACTVWA) MAPSET(COACTVW) INTO(CACTVWAI) RESP RESP2. source: :611-616
        ctx.ReceiveMap(MapId, MapSetId, _map);
        _responseCode = (int)Resp.Normal;
        _reasonCode = 0;
    }

    // =============================================================================================
    //  2200-EDIT-MAP-INPUTS — source: COACTVWC.cbl:622-643
    // =============================================================================================
    private void EditMapInputs() // COBOL paragraph: 2200-EDIT-MAP-INPUTS
    {
        SetInputOk();              // SET INPUT-OK TO TRUE. source: :624
        SetFlgAcctFilterIsValid(); // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :625

        // REPLACE * WITH LOW-VALUES: IF ACCTSIDI = '*' OR SPACES -> CC-ACCT-ID = LOW-VALUES. source: :627-633
        string acctIdRaw = _map.Field("ACCTSID").Value; // ACCTSIDI OF CACTVWAI (raw keyed value)
        if (acctIdRaw == "*" || IsSpaces(acctIdRaw) || string.IsNullOrEmpty(acctIdRaw))
            _accountIdInput = "\0\0\0\0\0\0\0\0\0\0\0"; // LOW-VALUES (11 NULs)
        else
            _accountIdInput = PadX(acctIdRaw, 11);       // MOVE ACCTSIDI TO CC-ACCT-ID (X(11))

        // PERFORM 2210-EDIT-ACCOUNT. source: :636-637
        EditAccount();

        // CROSS FIELD EDITS: IF FLG-ACCTFILTER-BLANK SET NO-SEARCH-CRITERIA-RECEIVED. source: :640-642
        if (FlgAcctFilterBlank)
            SetNoSearchCriteriaReceived();
    }

    // =============================================================================================
    //  2210-EDIT-ACCOUNT — source: COACTVWC.cbl:649-685
    // =============================================================================================
    private void EditAccount() // COBOL paragraph: 2210-EDIT-ACCOUNT
    {
        SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :650

        // Not supplied: IF CC-ACCT-ID = LOW-VALUES OR SPACES. source: :653-662
        if (IsLowValues(_accountIdInput) || IsSpaces(_accountIdInput))
        {
            SetInputError();          // SET INPUT-ERROR TO TRUE. source: :655
            SetFlgAcctFilterBlank();  // SET FLG-ACCTFILTER-BLANK TO TRUE. source: :656
            if (ReturnMessageOff)     // IF WS-RETURN-MSG-OFF. source: :657
                SetPromptForAcct();   // SET WS-PROMPT-FOR-ACCT TO TRUE. source: :658
            _commArea.AcctId = 0;     // MOVE ZEROES TO CDEMO-ACCT-ID. source: :660
            return;                   // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :661
        }

        // Not numeric / not 11 chars: IF CC-ACCT-ID IS NOT NUMERIC OR = ZEROES. source: :666-680
        if (!IsNumeric11(_accountIdInput) || IsZeroes11(_accountIdInput))
        {
            SetInputError();           // SET INPUT-ERROR TO TRUE. source: :668
            SetFlgAcctFilterNotOk();   // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :669
            if (ReturnMessageOff)      // IF WS-RETURN-MSG-OFF. source: :670
                // FB-2/FB-3: the MOVEd literal has a DOUBLE space "must  be". The trailing '00' (col
                // artifact) is not part of the message. source: :671-673
                _returnMessage = "Account Filter must  be a non-zero 11 digit number";
            _commArea.AcctId = 0;      // MOVE ZERO TO CDEMO-ACCT-ID. source: :675
            return;                    // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :676
        }
        else
        {
            _commArea.AcctId = ParseLong11(_accountIdInput); // MOVE CC-ACCT-ID TO CDEMO-ACCT-ID. source: :678
            SetFlgAcctFilterIsValid();                       // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :679
        }
    }

    // =============================================================================================
    //  9000-READ-ACCT — source: COACTVWC.cbl:687-722
    // =============================================================================================
    private void ReadAccount(CicsContext ctx) // COBOL paragraph: 9000-READ-ACCT
    {
        SetNoInfoMessage();                          // SET WS-NO-INFO-MESSAGE TO TRUE. source: :689

        _acctIdKey = _commArea.AcctId;               // MOVE CDEMO-ACCT-ID TO WS-CARD-RID-ACCT-ID. source: :691

        GetCardXrefByAccount(ctx);                   // PERFORM 9200-GETCARDXREF-BYACCT. source: :693-694

        // IF FLG-ACCTFILTER-NOT-OK GO TO 9000-READ-ACCT-EXIT. source: :697-699
        if (FlgAcctFilterNotOk)
            return;

        GetAccountDataByAccount(ctx);                // PERFORM 9300-GETACCTDATA-BYACCT. source: :701-702

        // FB-5: IF DID-NOT-FIND-ACCT-IN-ACCTDAT GO TO EXIT — but DID-NOT-FIND-ACCT-IN-ACCTDAT is never
        // SET (commented out at :792), so this guard is dead and control falls through to 9400 even
        // after an account-not-found. source: :704-706
        if (false /* DID-NOT-FIND-ACCT-IN-ACCTDAT — never set */)
            return;

        _custIdKey = _commArea.CustId;               // MOVE CDEMO-CUST-ID TO WS-CARD-RID-CUST-ID. source: :708

        GetCustomerDataByCustomer(ctx);              // PERFORM 9400-GETCUSTDATA-BYCUST. source: :710-711

        // FB-4: IF DID-NOT-FIND-CUST-IN-CUSTDAT GO TO EXIT — never SET (commented out at :842); dead.
        // source: :713-715
        if (false /* DID-NOT-FIND-CUST-IN-CUSTDAT — never set */)
            return;
    }

    // =============================================================================================
    //  9200-GETCARDXREF-BYACCT — READ CXACAIX (CARD_XREF by acct_id alt index). source: :723-773
    // =============================================================================================
    private void GetCardXrefByAccount(CicsContext ctx) // COBOL paragraph: 9200-GETCARDXREF-BYACCT
    {
        // EXEC CICS READ DATASET(CXACAIX) RIDFLD(WS-CARD-RID-ACCT-ID-X) INTO(CARD-XREF-RECORD). :727-735
        _ = CardXrefFileName; // dataset name (fixed)
        var repo = new CardXrefRepository(_db.Connection);
        string fileStatus = repo.ReadByAltKey(_acctIdKey, out _cardXref);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :737-769
        if (_responseCode == (int)Resp.Normal)
        {
            // WHEN NORMAL — MOVE XREF-CUST-ID/CARD-NUM TO CDEMO-CUST-ID/CARD-NUM. source: :738-740
            _commArea.CustId = _cardXref?.CustId ?? 0;
            _commArea.CardNum = ParseLong(_cardXref?.XrefCardNum ?? "0");
        }
        else if (_responseCode == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :741-758
            SetInputError();         // SET INPUT-ERROR TO TRUE. :742
            SetFlgAcctFilterNotOk(); // SET FLG-ACCTFILTER-NOT-OK TO TRUE. :743
            if (ReturnMessageOff)    // IF WS-RETURN-MSG-OFF. :744
            {
                _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. :745
                _errorResp2 = Alpha(_reasonCode, 10);    // MOVE WS-REAS-CD TO ERROR-RESP2. :746
                // STRING 'Account:' acct-id-X ' not found in' ' Cross ref file.  Resp:' ERROR-RESP
                //        ' Reas:' ERROR-RESP2 INTO WS-RETURN-MSG. :747-757
                _returnMessage = Truncate75(
                    "Account:" + AcctIdKeyText + " not found in" + " Cross ref file.  Resp:" +
                    _errorResp + " Reas:" + _errorResp2);
            }
        }
        else
        {
            // WHEN OTHER — file error. source: :759-769
            SetInputError();          // SET INPUT-ERROR TO TRUE. :760
            SetFlgAcctFilterNotOk();  // SET FLG-ACCTFILTER-NOT-OK TO TRUE. :761
            _errorOpname = PadX("READ", 8);                       // :762
            _errorFile = PadX(CardXrefFileName, 9);               // :763
            _errorResp = Alpha(_responseCode, 10);                // :764
            _errorResp2 = Alpha(_reasonCode, 10);                 // :765
            _returnMessage = Truncate75(BuildFileErrorMessage()); // MOVE WS-FILE-ERROR-MESSAGE TO WS-RETURN-MSG. :766
        }
    }

    // =============================================================================================
    //  9300-GETACCTDATA-BYACCT — READ ACCTDAT (ACCOUNT by acct_id). source: :774-823
    // =============================================================================================
    private void GetAccountDataByAccount(CicsContext ctx) // COBOL paragraph: 9300-GETACCTDATA-BYACCT
    {
        // EXEC CICS READ DATASET(ACCTDAT) RIDFLD(WS-CARD-RID-ACCT-ID-X) INTO(ACCOUNT-RECORD). :776-784
        _ = AccountFileName;
        var repo = new AccountRepository(_db.Connection);
        string fileStatus = repo.ReadByKey(_acctIdKey, out _account);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :786-819
        if (_responseCode == (int)Resp.Normal)
        {
            SetFoundAcctInMaster(); // WHEN NORMAL — SET FOUND-ACCT-IN-MASTER TO TRUE. :787-788
        }
        else if (_responseCode == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :789-807
            SetInputError();          // SET INPUT-ERROR. :790
            SetFlgAcctFilterNotOk();  // SET FLG-ACCTFILTER-NOT-OK. :791
            // (SET DID-NOT-FIND-ACCT-IN-ACCTDAT is commented out — FB-5. :792)
            if (ReturnMessageOff)     // IF WS-RETURN-MSG-OFF. :793
            {
                _errorResp = Alpha(_responseCode, 10);   // :794
                _errorResp2 = Alpha(_reasonCode, 10);    // :795
                // STRING 'Account:' acct-id-X ' not found in' ' Acct Master file.Resp:' ERROR-RESP
                //        ' Reas:' ERROR-RESP2. :796-806
                _returnMessage = Truncate75(
                    "Account:" + AcctIdKeyText + " not found in" + " Acct Master file.Resp:" +
                    _errorResp + " Reas:" + _errorResp2);
            }
        }
        else
        {
            // WHEN OTHER — file error. source: :809-819
            SetInputError();          // :810
            SetFlgAcctFilterNotOk();  // :811
            _errorOpname = PadX("READ", 8);            // :812
            _errorFile = PadX(AccountFileName, 9);     // :813
            _errorResp = Alpha(_responseCode, 10);     // :814
            _errorResp2 = Alpha(_reasonCode, 10);      // :815
            _returnMessage = Truncate75(BuildFileErrorMessage()); // :816
        }
    }

    // =============================================================================================
    //  9400-GETCUSTDATA-BYCUST — READ CUSTDAT (CUSTOMER by cust_id). source: :825-872
    // =============================================================================================
    private void GetCustomerDataByCustomer(CicsContext ctx) // COBOL paragraph: 9400-GETCUSTDATA-BYCUST
    {
        // EXEC CICS READ DATASET(CUSTDAT) RIDFLD(WS-CARD-RID-CUST-ID-X) INTO(CUSTOMER-RECORD). :826-834
        _ = CustomerFileName;
        var repo = new CustomerRepository(_db.Connection);
        string fileStatus = repo.ReadByKey(_custIdKey, out _customer);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :836-868
        if (_responseCode == (int)Resp.Normal)
        {
            SetFoundCustInMaster(); // WHEN NORMAL — SET FOUND-CUST-IN-MASTER TO TRUE. :837-838
        }
        else if (_responseCode == (int)Resp.NotFnd)
        {
            // WHEN NOTFND. source: :839-857
            SetInputError();         // SET INPUT-ERROR. :840
            SetFlgCustFilterNotOk(); // FB-4: SET FLG-CUSTFILTER-NOT-OK (not the read-acct guard flag). :841
            // (SET DID-NOT-FIND-CUST-IN-CUSTDAT is commented out — FB-4. :842)
            // FB-6: ERROR-RESP/RESP2 moved BEFORE the WS-RETURN-MSG-OFF guard (unlike 9200/9300). :843-844
            _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. :843
            _errorResp2 = Alpha(_reasonCode, 10);    // MOVE WS-REAS-CD TO ERROR-RESP2. :844
            if (ReturnMessageOff)    // IF WS-RETURN-MSG-OFF. :845
            {
                // STRING 'CustId:' cust-id-X ' not found' ' in customer master.Resp: ' ERROR-RESP
                //        ' REAS:' ERROR-RESP2. :846-856
                _returnMessage = Truncate75(
                    "CustId:" + CustIdKeyText + " not found" + " in customer master.Resp: " +
                    _errorResp + " REAS:" + _errorResp2);
            }
        }
        else
        {
            // WHEN OTHER — file error. source: :858-868
            SetInputError();          // :859
            SetFlgCustFilterNotOk();  // :860
            _errorOpname = PadX("READ", 8);            // :861
            _errorFile = PadX(CustomerFileName, 9);    // :862
            _errorResp = Alpha(_responseCode, 10);     // :863
            _errorResp2 = Alpha(_reasonCode, 10);      // :864
            _returnMessage = Truncate75(BuildFileErrorMessage()); // :865
        }
    }

    // =============================================================================================
    //  SEND-PLAIN-TEXT — source: COACTVWC.cbl:877-890
    // =============================================================================================
    private void SendPlainText(CicsContext ctx)
    {
        // EXEC CICS SEND TEXT FROM(WS-RETURN-MSG) LENGTH(LENGTH OF WS-RETURN-MSG) ERASE FREEKB. :878-883
        ctx.SendText(PadX(_returnMessage, 75), erase: true, freeKb: true);
        // EXEC CICS RETURN (no TRANSID — ends the conversation). source: :885-886
        ctx.ReturnTerminal();
    }

    // =============================================================================================
    //  ABEND-ROUTINE — source: COACTVWC.cbl:916-937
    // =============================================================================================
    private void AbendRoutine(CicsContext ctx)
    {
        // IF ABEND-MSG EQUAL LOW-VALUES MOVE 'UNEXPECTED ABEND OCCURRED.' TO ABEND-MSG. :918-920
        // MOVE LIT-THISPGM TO ABEND-CULPRIT. :922
        // EXEC CICS SEND FROM(ABEND-DATA) ... NOHANDLE ; HANDLE ABEND CANCEL ; ABEND ABCODE('9999').
        // Headless model: emit the abend message and end the conversation (no real dump). :924-936
        if (ctx.Outcome is null)
        {
            ctx.SendText("UNEXPECTED ABEND OCCURRED.", erase: true, freeKb: true);
            ctx.ReturnTerminal();
        }
    }

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE builder. source: COACTVWC.cbl:86-105
    // =============================================================================================
    // 'File Error: ' OPNAME(8) ' on ' FILE(9) ' returned RESP ' RESP(10) ',RESP2 ' RESP2(10) 5 spaces.
    private string BuildFileErrorMessage() =>
        "File Error: " + _errorOpname + " on " + _errorFile + " returned RESP " +
        _errorResp + ",RESP2 " + _errorResp2 + "     ";

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>Maps a repository FileStatus to the CICS RESP/RESP2 the EVALUATE branches on.</summary>
    private void SetResp(string fileStatus)
    {
        _responseCode = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,             // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd, // '23' -> DFHRESP(NOTFND)
            _ => (int)Resp.Error,                          // any other -> WHEN OTHER (file error)
        };
        _reasonCode = 0; // RESP2 (reason) unavailable from the relational repo; 0 for parity.
    }

    /// <summary>
    /// MOVE LOW-VALUES TO CACTVWAO — blank every named output field and clear per-turn overrides before
    /// the first SEND. source: COACTVWC.cbl:432.
    /// </summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    /// <summary>
    /// MOVE of a money value (<c>S9(10)V99</c>) into a <c>+ZZZ,ZZZ,ZZZ.99</c> PICOUT screen field. The
    /// raw decimal is stored as the field value; the renderer applies the edited-numeric formatting via
    /// <see cref="ScreenField.RenderedText"/> (truncate-toward-zero, leading sign, comma grouping, zero
    /// suppression). source: COACTVWC.cbl:475-485.
    /// </summary>
    private void SetMoney(string field, decimal value)
    {
        // Store the canonical decimal string so RenderedText() parses & PICOUT-edits it on SEND.
        _map.Field(field).SetValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture), setMdt: false);
    }

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>Renders a numeric as a zero-padded zoned-decimal DISPLAY string of width <paramref name="width"/>.</summary>
    private static string Zoned(long value, int width)
    {
        ulong mag = value < 0 ? (ulong)(-value) : (ulong)value;
        string s = mag.ToString();
        if (s.Length >= width) return s[^width..];
        return s.PadLeft(width, '0');
    }

    /// <summary>
    /// COBOL <c>MOVE numeric TO alphanumeric X(width)</c> for a binary <c>S9(09) COMP</c> source: the
    /// 9-digit unsigned zoned display is left-justified into the X(width) field, space-padded on the
    /// right (so a 10-char field shows 9 digits + 1 trailing space). source: COACTVWC.cbl:98,102,745-746.
    /// </summary>
    private static string Alpha(int value, int width)
    {
        // S9(09) -> 9 display digits (unsigned magnitude, zero-padded to 9), left-justified into width.
        string nine = Zoned(value, 9);
        return PadX(nine, width);
    }

    /// <summary>Truncates a built message to the X(75) WS-RETURN-MSG width (STRING DELIMITED BY SIZE).</summary>
    private static string Truncate75(string s) => s.Length > 75 ? s[..75] : s;

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    /// <summary>True when a string is all spaces (or empty).</summary>
    private static bool IsSpaces(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false; // empty != SPACES in COBOL fixed-field terms here
        foreach (char c in s) if (c != ' ') return false;
        return true;
    }

    /// <summary>True when a string is all LOW-VALUES (modeled as NUL).</summary>
    private static bool IsLowValues(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        foreach (char c in s) if (c != '\0') return false;
        return true;
    }

    /// <summary>True when a value is all SPACES or all LOW-VALUES (or empty) — the common COBOL guard.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>True when a value is all LOW-VALUES (NUL) or all SPACES — used for ACCTSID/FROM tests.</summary>
    private static bool IsLowValuesOrSpaces(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// COBOL class test <c>CC-ACCT-ID IS NOT NUMERIC</c> on the X(11) field: every character must be a
    /// digit '0'-'9'. A space-padded or otherwise non-digit value fails. source: COACTVWC.cbl:666.
    /// </summary>
    private static bool IsNumeric11(string ccAcctId)
    {
        string v = PadX(ccAcctId, 11); // CC-ACCT-ID is X(11)
        foreach (char c in v) if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>COBOL <c>CC-ACCT-ID EQUAL ZEROES</c> on the X(11) field. source: COACTVWC.cbl:667.</summary>
    private static bool IsZeroes11(string ccAcctId)
    {
        string v = PadX(ccAcctId, 11);
        foreach (char c in v) if (c != '0') return false;
        return true;
    }

    /// <summary>MOVE CC-ACCT-ID (X(11) of digits) TO CDEMO-ACCT-ID (9(11)). source: COACTVWC.cbl:678.</summary>
    private static long ParseLong11(string ccAcctId) => ParseLong(PadX(ccAcctId, 11));

    /// <summary>Parses a digit string (ignoring non-digits) to a long; empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    // =============================================================================================
    //  BMS map builder — CACTVWA in mapset COACTVW (24x80).
    //  source: app/bms/COACTVW.bms:19-375 / SCREEN_COACTVW.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COACTVW.bms:25.</summary>
    public const string MapName = MapId;          // "CACTVWA"

    /// <summary>The DFHMSD mapset name. source: COACTVW.bms:20.</summary>
    public const string MapsetName = "COACTVW";

    /// <summary>
    /// Constructs the <c>CACTVWA</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with
    /// its exact Row/Col/Length/attribute/colour/highlight/initial value/PICIN/PICOUT, the IC cursor on
    /// <c>ACCTSID</c>, the protected literals and stoppers, and the named in/out fields — in BMS source
    /// order. The single keyable field is <c>ACCTSID</c> (5,38) L11, GREEN, UNDERLINE,
    /// PICIN='99999999999', MUSTFILL, IC. The five money fields carry PICOUT='+ZZZ,ZZZ,ZZZ.99'.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 3-line header -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                                    // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                       // bms:34-37
            Out("TITLE01", 1, 21, 40, Askip, BmsColor.Yellow),                       // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                                   // bms:42-46
            OutInit("CURDATE", 1, 71, 8, Askip, BmsColor.Blue, "mm/dd/yy"),          // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                                    // bms:52-56
            Out("PGMNAME", 2, 7, 8, Askip, BmsColor.Blue),                           // bms:57-60
            Out("TITLE02", 2, 21, 40, Askip, BmsColor.Yellow),                       // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                                   // bms:65-69
            OutInit("CURTIME", 2, 71, 8, Askip, BmsColor.Blue, "hh:mm:ss"),          // bms:70-74

            // ----- 'View Account' heading (default ASKIP, NEUTRAL) -----
            LitAttr(4, 33, 12, AskipDefault, BmsColor.Neutral, "View Account"),      // bms:75-78

            // ----- Account block -----
            Lit(5, 19, 16, BmsColor.Turquoise, "Account Number :"),                  // bms:79-83
            // ACCTSID: ATTRB=(FSET,IC,NORM,UNPROT) GREEN UNDERLINE PICIN MUSTFILL — the ONLY input.
            new ScreenField
            {
                Name = "ACCTSID", Row = 5, Col = 38, Length = 11,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
                PicIn = "99999999999",
            },                                                                        // bms:84-90
            Stopper(5, 50),                                                           // bms:91-92
            LitAttr(5, 57, 12, AskipDefault, BmsColor.Turquoise, "Active Y/N: "),     // bms:93-96
            // ACSTTUS: ATTRB=(ASKIP) HILIGHT=UNDERLINE, no COLOR -> default.
            OutHi("ACSTTUS", 5, 70, 1, Askip, BmsColor.Default, BmsHilight.Underline),// bms:97-100
            Stopper(5, 72),                                                           // bms:101-102

            Lit(6, 8, 7, BmsColor.Turquoise, "Opened:"),                             // bms:103-106
            OutHi("ADTOPEN", 6, 17, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:107-109
            Stopper(6, 28),                                                           // bms:110-111
            Lit(6, 39, 21, BmsColor.Turquoise, "Credit Limit        :"),             // bms:112-116
            // ACRDLIM: HILIGHT=UNDERLINE JUSTIFY=(RIGHT) PICOUT='+ZZZ,ZZZ,ZZZ.99'.
            Money("ACRDLIM", 6, 61, 15),                                             // bms:117-121
            Stopper(6, 77),                                                           // bms:122-123

            Lit(7, 8, 7, BmsColor.Turquoise, "Expiry:"),                             // bms:124-127
            OutHi("AEXPDT", 7, 17, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:128-130
            Stopper(7, 28),                                                           // bms:131-132
            Lit(7, 39, 21, BmsColor.Turquoise, "Cash credit Limit   :"),             // bms:133-137
            Money("ACSHLIM", 7, 61, 15),                                             // bms:138-142
            Stopper(7, 77),                                                           // bms:143-144

            Lit(8, 8, 8, BmsColor.Turquoise, "Reissue:"),                            // bms:145-148
            OutHi("AREISDT", 8, 17, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:149-151
            Stopper(8, 28),                                                           // bms:152-153
            Lit(8, 39, 21, BmsColor.Turquoise, "Current Balance     :"),             // bms:154-158
            Money("ACURBAL", 8, 61, 15),                                             // bms:159-163
            Stopper(8, 77),                                                           // bms:164-165

            Lit(9, 39, 21, BmsColor.Turquoise, "Current Cycle Credit:"),             // bms:166-170
            Money("ACRCYCR", 9, 61, 15),                                             // bms:171-175
            Stopper(9, 77),                                                           // bms:176-177

            Lit(10, 8, 14, BmsColor.Turquoise, "Account Group:"),                    // bms:178-181
            OutHi("AADDGRP", 10, 23, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:182-184
            Stopper(10, 34),                                                          // bms:185-186
            Lit(10, 39, 21, BmsColor.Turquoise, "Current Cycle Debit :"),            // bms:187-191
            Money("ACRCYDB", 10, 61, 15),                                            // bms:192-196
            Stopper(10, 77),                                                          // bms:197-198

            // ----- Customer block -----
            LitAttr(11, 32, 16, AskipDefault, BmsColor.Neutral, "Customer Details"), // bms:199-202
            Lit(12, 8, 14, BmsColor.Turquoise, "Customer id  :"),                    // bms:203-206
            OutHi("ACSTNUM", 12, 23, 9, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:207-209
            Stopper(12, 33),                                                          // bms:210-211
            Lit(12, 49, 4, BmsColor.Turquoise, "SSN:"),                              // bms:212-215
            OutHi("ACSTSSN", 12, 54, 12, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:216-218
            Stopper(12, 67),                                                          // bms:219-220

            Lit(13, 8, 14, BmsColor.Turquoise, "Date of birth:"),                    // bms:221-224
            OutHi("ACSTDOB", 13, 23, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:225-227
            Stopper(13, 34),                                                          // bms:228-229
            Lit(13, 49, 11, BmsColor.Turquoise, "FICO Score:"),                      // bms:230-233
            OutHi("ACSTFCO", 13, 61, 3, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:234-236
            Stopper(13, 65),                                                          // bms:237-238

            Lit(14, 1, 10, BmsColor.Turquoise, "First Name"),                        // bms:239-242
            Lit(14, 28, 13, BmsColor.Turquoise, "Middle Name: "),                    // bms:243-246
            Lit(14, 55, 12, BmsColor.Turquoise, "Last Name : "),                     // bms:247-250
            OutHi("ACSFNAM", 15, 1, 25, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:251-253
            Stopper(15, 27),                                                          // bms:254-255
            OutHi("ACSMNAM", 15, 28, 25, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:256-258
            Stopper(15, 54),                                                          // bms:259-260
            OutHi("ACSLNAM", 15, 55, 25, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:261-263

            Lit(16, 1, 8, BmsColor.Turquoise, "Address:"),                           // bms:264-267
            OutHi("ACSADL1", 16, 10, 50, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:268-270
            Stopper(16, 61),                                                          // bms:271-272
            Lit(16, 63, 6, BmsColor.Turquoise, "State "),                            // bms:273-276
            OutHi("ACSSTTE", 16, 73, 2, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:277-279
            Stopper(16, 76),                                                          // bms:280-281

            OutHi("ACSADL2", 17, 10, 50, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:282-284
            Stopper(17, 61),                                                          // bms:285-286
            Lit(17, 63, 3, BmsColor.Turquoise, "Zip"),                               // bms:287-290
            // ACSZIPC: HILIGHT=UNDERLINE JUSTIFY=(RIGHT).
            OutHiJust("ACSZIPC", 17, 73, 5, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:291-294
            Stopper(17, 79),                                                          // bms:295-296

            Lit(18, 1, 5, BmsColor.Turquoise, "City "),                              // bms:297-300
            OutHi("ACSCITY", 18, 10, 50, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:301-303
            Stopper(18, 61),                                                          // bms:304-305
            Lit(18, 63, 7, BmsColor.Turquoise, "Country"),                           // bms:306-309
            OutHi("ACSCTRY", 18, 73, 3, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:310-312
            Stopper(18, 77),                                                          // bms:313-314

            Lit(19, 1, 8, BmsColor.Turquoise, "Phone 1:"),                           // bms:315-318
            OutHi("ACSPHN1", 19, 10, 13, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:319-321
            Lit(19, 24, 30, BmsColor.Turquoise, "Government Issued Id Ref    : "),   // bms:322-325
            OutHi("ACSGOVT", 19, 58, 20, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:326-328
            Stopper(19, 79),                                                          // bms:329-330

            Lit(20, 1, 8, BmsColor.Turquoise, "Phone 2:"),                           // bms:331-334
            OutHi("ACSPHN2", 20, 10, 13, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:335-337
            Lit(20, 24, 16, BmsColor.Turquoise, "EFT Account Id: "),                 // bms:338-341
            OutHi("ACSEFTC", 20, 41, 10, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:342-344
            Stopper(20, 52),                                                          // bms:345-346
            Lit(20, 53, 24, BmsColor.Turquoise, "Primary Card Holder Y/N:"),         // bms:347-350
            OutHi("ACSPFLG", 20, 78, 1, AskipDefault, BmsColor.Default, BmsHilight.Underline), // bms:351-353
            Stopper(20, 80),                                                          // bms:354-355

            // ----- info line (PROT NEUTRAL, HILIGHT=OFF) -----
            new ScreenField
            {
                Name = "INFOMSG", Row = 22, Col = 23, Length = 45,
                Attribute = BmsAttribute.Protected | BmsAttribute.Normal,
                Color = BmsColor.Neutral,
                Hilight = BmsHilight.Off,
            },                                                                        // bms:356-360
            Stopper(22, 69),                                                          // bms:361-362

            // ----- overlay at (1,1) LEN 9 (no INITIAL/ATTRB -> default ASKIP, blank). FB: byte-fidelity. -----
            LitAttr(1, 1, 9, AskipDefault, BmsColor.Default, ""),                    // bms:363-364

            // ----- error line (ASKIP,BRT,FSET RED, L78) -----
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),                    // bms:365-368

            // ----- footer literal '  F3=Exit ' (ASKIP,NORM TURQUOISE, L60) -----
            Lit(24, 1, 60, BmsColor.Turquoise, "  F3=Exit "),                        // bms:369-373
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;          // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipDefault => BmsAttribute.AutoSkip;                          // default ATTRB omitted -> ASKIP
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) and the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. default-ASKIP for omitted ATTRB).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>Named output field carrying a HILIGHT (e.g. UNDERLINE value fields).</summary>
    private static ScreenField OutHi(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, BmsHilight hi) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = hi };

    /// <summary>Named output field with HILIGHT and JUSTIFY=(RIGHT) (e.g. ACSZIPC).</summary>
    private static ScreenField OutHiJust(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, BmsHilight hi) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Hilight = hi, RightJustify = true };

    /// <summary>
    /// A <c>+ZZZ,ZZZ,ZZZ.99</c> money output field: default-ASKIP, HILIGHT=UNDERLINE, JUSTIFY=(RIGHT),
    /// PICOUT applied by the renderer via <see cref="ScreenField.RenderedText"/>.
    /// </summary>
    private static ScreenField Money(string name, int row, int col, int len) =>
        new()
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = AskipDefault,
            Color = BmsColor.Default,
            Hilight = BmsHilight.Underline,
            RightJustify = true,
            PicOut = "+ZZZ,ZZZ,ZZZ.99",
        };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, default ASKIP).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = AskipDefault, Color = BmsColor.Default };
}
