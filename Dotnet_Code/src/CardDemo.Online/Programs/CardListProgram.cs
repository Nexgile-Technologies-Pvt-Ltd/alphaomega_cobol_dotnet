using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COCRDLIC</c> — the "List Credit Cards" paged
/// grid (TRANSID <c>CCLI</c>, BMS map <c>CCRDLIA</c> / mapset <c>COCRDLI</c>).
/// </summary>
/// <remarks>
/// <para>
/// COCRDLIC browses the CARD master file forward/backward in primary-key (card-number) order and displays
/// up to <b>7 cards per page</b> on a 24x80 BMS screen, with optional account-number and/or card-number
/// filter fields. Each listed card carries a per-row selection field; pressing ENTER on a row marked
/// <c>S</c> XCTLs to the card-detail program (<c>COCRDSLC</c>) and a row marked <c>U</c> XCTLs to the
/// card-update program (<c>COCRDUPC</c>). PF3 XCTLs back to the main menu (<c>COMEN01C</c>). PF7/PF8 page
/// up/down. It is pseudo-conversational: it re-drives itself via <c>RETURN TRANSID('CCLI')</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COCRDLIC.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE TRUE</c> / <c>PERFORM</c> / <c>GO TO</c> control flow, the
/// COMMAREA field usage (<see cref="CardDemoCommArea"/> plus the program-private trailer
/// <c>WS-THIS-PROGCOMMAREA</c>), and every faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the CARD master is accessed, by primary-key browse:
/// <c>STARTBR GTEQ</c> = <see cref="CardRepository.StartBrowse"/> (at-or-after the X(16) card-number RID;
/// LOW-VALUES = from the first row), <c>READNEXT</c> = <see cref="CardRepository.ReadNext"/>,
/// <c>READPREV</c> = <see cref="CardRepository.ReadPrevious"/>, <c>ENDBR</c> =
/// <see cref="CardRepository.EndBrowse"/>. The repository FileStatus is mapped to the CICS RESP the COBOL
/// <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), EndOfFile('10')→ENDFILE(20), anything
/// else→an OTHER/file-error. No write/rewrite/delete is performed.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>B-1 — <c>I-SELECTED = 0</c> used as a subscript in the ENTER dispatch. When no row is selected
/// I-SELECTED stays 0; the two view/update WHENs read offset-0 of the OCCURS, which is not 'S'/'U', so
/// control falls through to WHEN OTHER (re-list from first key). Modeled by treating I-SELECTED=0 as "no
/// selection". source: COCRDLIC.cbl:518,546,1097</item>
/// <item>B-2 — Double-negative filter guard <c>IF NOT FLG-ACCTFILTER-NOT-OK AND NOT FLG-CARDFILTER-NOT-OK</c>
/// in the INPUT-ERROR WHEN; reproduced as the exact boolean. source: COCRDLIC.cbl:431-435</item>
/// <item>B-3 — Card-filter error message suppressed when the account filter already set one
/// (<c>IF WS-ERROR-MSG-OFF</c> guard in 2220-EDIT-CARD). source: COCRDLIC.cbl:1056-1060</item>
/// <item>B-4 — Stray lone <c>I</c> token in 1250-SETUP-ARRAY-ATTRIBS row-4 branch; functionally inert,
/// row 4 ported identically to the other rows. source: COCRDLIC.cbl:787-797</item>
/// <item>B-5 — Row-1 protected-empty uses <c>DFHBMPRF</c> (protect+FSET) while rows 2-7 use
/// <c>DFHBMPRO</c> (protect, no FSET). Reproduced per row. source: COCRDLIC.cbl:753,766</item>
/// <item>B-6 — Row-1 selection error writes <c>'*'</c> into CRDSEL1O when blank, while rows 2-7 instead
/// <c>MOVE -1 TO CRDSELnL</c> (cursor). Reproduced. source: COCRDLIC.cbl:755-761,770</item>
/// <item>B-7 — Admin/non-admin listing not implemented; CDEMO-USER-TYPE is never tested, only the on-screen
/// filters drive filtering, and USER-TYPE is hard-SET to USER. No admin gating added. source: COCRDLIC.cbl:4-8,320</item>
/// <item>B-8 — Unused <c>CARDAIX</c> alt-path literal declared but never referenced; no alt index used.
/// source: COCRDLIC.cbl:215-217</item>
/// <item>B-9 — <c>WS-CA-SCREEN-NUM</c> is PIC 9(1); page number truncates to a single decimal digit past 9.
/// Reproduced via single-digit modulo on the page counter. source: COCRDLIC.cbl:237</item>
/// </list>
/// </remarks>
public sealed class CardListProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-CONSTANTS — source: COCRDLIC.cbl:176-217
    // =============================================================================================
    private const int MaxScreenLines = 7;          // source: COCRDLIC.cbl:177-178 // WS-MAX-SCREEN-LINES
    private const string ThisProgramId = "COCRDLIC";      // source: COCRDLIC.cbl:179-180 // LIT-THISPGM
    private const string ThisTranId = "CCLI";       // source: COCRDLIC.cbl:181-182 // LIT-THISTRANID
    private const string ThisMapSet = "COCRDLI";    // source: COCRDLIC.cbl:183-184 ('COCRDLI' X(7)) // LIT-THISMAPSET
    private const string ThisMap = "CCRDLIA";       // source: COCRDLIC.cbl:185-186 // LIT-THISMAP
    private const string MenuProgramId = "COMEN01C";      // source: COCRDLIC.cbl:187-188 // LIT-MENUPGM
    private const string MenuTranId = "CM00";       // source: COCRDLIC.cbl:189-190 // LIT-MENUTRANID
    private const string MenuMapSet = "COMEN01";    // source: COCRDLIC.cbl:191-192 // LIT-MENUMAPSET
    private const string MenuMap = "COMEN1A";       // source: COCRDLIC.cbl:193-194 // LIT-MENUMAP
    private const string CardDetailProgramId = "COCRDSLC";   // source: COCRDLIC.cbl:195-196 // LIT-CARDDTLPGM
    private const string CardDetailTranId = "CCDL";    // source: COCRDLIC.cbl:197-198 // LIT-CARDDTLTRANID
    private const string CardDetailMapSet = "COCRDSL"; // source: COCRDLIC.cbl:199-200 // LIT-CARDDTLMAPSET
    private const string CardDetailMap = "CCRDSLA";    // source: COCRDLIC.cbl:201-202 // LIT-CARDDTLMAP
    private const string CardUpdateProgramId = "COCRDUPC";   // source: COCRDLIC.cbl:203-204 // LIT-CARDUPDPGM
    private const string CardUpdateTranId = "CCUP";    // source: COCRDLIC.cbl:205-206 // LIT-CARDUPDTRANID
    private const string CardUpdateMapSet = "COCRDUP"; // source: COCRDLIC.cbl:207-208 // LIT-CARDUPDMAPSET
    private const string CardUpdateMap = "CCRDUPA";    // source: COCRDLIC.cbl:209-210 // LIT-CARDUPDMAP
    private const string CardFileName = "CARDDAT ";    // source: COCRDLIC.cbl:213-214 // LIT-CARD-FILE
    // B-8: LIT-CARD-FILE-ACCT-PATH = 'CARDAIX ' declared but never referenced. source: COCRDLIC.cbl:215-217
    private const string CardFileAcctPath = "CARDAIX "; // LIT-CARD-FILE-ACCT-PATH

    // CCDA-TITLE01/02 (COTTL01Y) — shared screen header. source: COTTL01Y.cpy.
    private const string Title01 = "      AWS Mainframe Modernization       "; // CCDA-TITLE01
    private const string Title02 = "              CardDemo                  "; // CCDA-TITLE02

    // =============================================================================================
    //  WS-MISC-STORAGE — input edits + flags. source: COCRDLIC.cbl:41-171
    // =============================================================================================

    // 07 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP. source: COCRDLIC.cbl:47-50
    private int _responseCode; // WS-RESP-CD
    private int _reasonCode; // WS-REAS-CD

    // 05 WS-INPUT-FLAG: 88 INPUT-OK={'0',' ',LOW-VALUES} / INPUT-ERROR='1'. source: COCRDLIC.cbl:56-60
    private char _inputFlag = '\0'; // WS-INPUT-FLAG
    private bool InputOk => _inputFlag is '0' or ' ' or '\0'; // 88 INPUT-OK
    private bool InputError => _inputFlag == '1';             // 88 INPUT-ERROR
    private void SetInputOk() => _inputFlag = '0';
    private void SetInputError() => _inputFlag = '1';

    // 05 WS-EDIT-ACCT-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: COCRDLIC.cbl:61-64
    private char _editAcctFlag = '\0'; // WS-EDIT-ACCT-FLAG
    private bool FlgAcctFilterNotOk => _editAcctFlag == '0';  // 88 FLG-ACCTFILTER-NOT-OK
    private bool FlgAcctFilterIsValid => _editAcctFlag == '1'; // 88 FLG-ACCTFILTER-ISVALID
    private void SetFlgAcctFilterNotOk() => _editAcctFlag = '0';
    private void SetFlgAcctFilterIsValid() => _editAcctFlag = '1';
    private void SetFlgAcctFilterBlank() => _editAcctFlag = ' ';

    // 05 WS-EDIT-CARD-FLAG: 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: COCRDLIC.cbl:65-68
    private char _editCardFlag = '\0'; // WS-EDIT-CARD-FLAG
    private bool FlgCardFilterNotOk => _editCardFlag == '0';  // 88 FLG-CARDFILTER-NOT-OK
    private bool FlgCardFilterIsValid => _editCardFlag == '1'; // 88 FLG-CARDFILTER-ISVALID
    private void SetFlgCardFilterNotOk() => _editCardFlag = '0';
    private void SetFlgCardFilterIsValid() => _editCardFlag = '1';
    private void SetFlgCardFilterBlank() => _editCardFlag = ' ';

    // 05 WS-EDIT-SELECT-COUNTER PIC S9(04) COMP-3 (used as the INSPECT tally I). source: COCRDLIC.cbl:69-71
    // 05 WS-EDIT-SELECT-FLAGS X(7) redef WS-EDIT-SELECT OCCURS 7. source: COCRDLIC.cbl:72-82
    // Init LOW-VALUES. 88 SELECT-OK={'S','U'} VIEW='S' UPDATE='U' BLANK={' ',LOW-VALUES}.
    private readonly char[] _editSelect = { '\0', '\0', '\0', '\0', '\0', '\0', '\0' }; // WS-EDIT-SELECT
    private bool SelectOk(int i1) => _editSelect[i1 - 1] is 'S' or 'U';          // 88 SELECT-OK
    private bool ViewRequestedOn(int i1) => _editSelect[i1 - 1] == 'S';          // 88 VIEW-REQUESTED-ON
    private bool UpdateRequestedOn(int i1) => _editSelect[i1 - 1] == 'U';        // 88 UPDATE-REQUESTED-ON
    private bool SelectBlank(int i1) => _editSelect[i1 - 1] is ' ' or '\0';      // 88 SELECT-BLANK

    // 05 WS-EDIT-SELECT-ERROR-FLAGS X(7) redef WS-EDIT-SELECT-ERRORS OCCURS 7 -> WS-ROW-CRDSELECT-ERROR
    //    88 WS-ROW-SELECT-ERROR = '1'. source: COCRDLIC.cbl:83-88
    private readonly char[] _rowCardSelectError = { '0', '0', '0', '0', '0', '0', '0' }; // WS-ROW-CRDSELECT-ERROR

    // 05 WS-SUBSCRIPT-VARS: I (tally) / I-SELECTED (88 DETAIL-WAS-REQUESTED 1 THRU 7). source: COCRDLIC.cbl:89-94
    private int _tally; // I
    private int _selectedRow; // I-SELECTED

    // 05 CICS-OUTPUT-EDIT-VARS: FLG-PROTECT-SELECT-ROWS X(1) 88 NO='0' YES='1'. source: COCRDLIC.cbl:98-107
    private char _protectSelectRows = '\0'; // FLG-PROTECT-SELECT-ROWS
    private bool FlgProtectSelectRowsYes => _protectSelectRows == '1'; // 88 FLG-PROTECT-SELECT-ROWS-YES
    private void SetFlgProtectSelectRowsNo() => _protectSelectRows = '0';
    private void SetFlgProtectSelectRowsYes() => _protectSelectRows = '1';

    // 05 WS-INFO-MSG X(45): 88 NO-INFO-MESSAGE={SPACES,LOW-VALUES} ; INFORM-REC-ACTIONS. source: COCRDLIC.cbl:112-116
    private string _infoMessage = ""; // WS-INFO-MSG
    private const string InformRecActions = "TYPE S FOR DETAIL, U TO UPDATE ANY RECORD"; // source: :115-116 // INFORM-REC-ACTIONS
    private bool HasNoInfoMessage => IsSpacesOrLowValues(_infoMessage);     // 88 WS-NO-INFO-MESSAGE
    private void SetHasNoInfoMessage() => _infoMessage = "";                // SET WS-NO-INFO-MESSAGE (SPACES)
    private void SetWsInformRecActions() => _infoMessage = InformRecActions;

    // 05 WS-ERROR-MSG X(75): 88 OFF=SPACES ; EXIT-MESSAGE ; NO-RECORDS-FOUND ; etc. source: COCRDLIC.cbl:117-126
    private string _errorMessage = ""; // WS-ERROR-MSG
    private const string ExitMessage = "PF03 PRESSED.EXITING";                                  // source: :119-120 // EXIT-MESSAGE
    private const string NoRecordsFound = "NO RECORDS FOUND FOR THIS SEARCH CONDITION.";       // source: :121-122 // NO-RECORDS-FOUND
    private const string MoreThan1Action = "PLEASE SELECT ONLY ONE RECORD TO VIEW OR UPDATE"; // source: :123-124 // MORE-THAN-1-ACTION
    private const string InvalidActionCode = "INVALID ACTION CODE";                            // source: :125-126 // INVALID-ACTION-CODE
    private bool ErrorMessageIsBlank => IsSpaces(_errorMessage) || _errorMessage.Length == 0; // 88 WS-ERROR-MSG-OFF (SPACES)
    private bool IsNoRecordsFoundError => _errorMessage == NoRecordsFound;               // 88 WS-NO-RECORDS-FOUND
    private void SetErrorMessageIsBlank() => _errorMessage = "";                            // SET WS-ERROR-MSG-OFF (SPACES)
    private void SetWsExitMessage() => _errorMessage = ExitMessage;
    private void SetIsNoRecordsFoundError() => _errorMessage = NoRecordsFound;
    private void SetHasMoreThanOneAction() => _errorMessage = MoreThan1Action;
    private void SetWsInvalidActionCode() => _errorMessage = InvalidActionCode;

    // 05 WS-PFK-FLAG: 88 PFK-VALID='0' / PFK-INVALID='1'. source: COCRDLIC.cbl:127-129
    private char _pfKeyFlag = '\0'; // WS-PFK-FLAG
    private bool PfkInvalid => _pfKeyFlag == '1';
    private void SetPfkValid() => _pfKeyFlag = '0';
    private void SetPfkInvalid() => _pfKeyFlag = '1';

    // =============================================================================================
    //  WS-FILE-HANDLING-VARS — source: COCRDLIC.cbl:136-171
    // =============================================================================================
    // 10 WS-CARD-RID: WS-CARD-RID-CARDNUM X(16) + WS-CARD-RID-ACCT-ID 9(11) (redef -X). source: :137-141
    // The browse start key (RIDFLD). Empty string models LOW-VALUES (= browse from first record).
    private string _cardRidCardNum = ""; // WS-CARD-RID-CARDNUM

    // 05 WS-SCRN-COUNTER PIC S9(4) COMP. source: COCRDLIC.cbl:145
    private int _screenCounter; // WS-SCRN-COUNTER

    // 05 WS-FILTER-RECORD-FLAG: 88 EXCLUDE='0' / DONOT-EXCLUDE='1'. source: COCRDLIC.cbl:147-149
    private char _filterRecordFlag = '\0'; // WS-FILTER-RECORD-FLAG
    private bool IncludeThisRecord => _filterRecordFlag == '1'; // 88 WS-DONOT-EXCLUDE-THIS-RECORD
    private void SetWsExcludeThisRecord() => _filterRecordFlag = '0';
    private void SetIncludeThisRecord() => _filterRecordFlag = '1';

    // 05 WS-RECORDS-TO-PROCESS-FLAG: 88 READ-LOOP-EXIT='0' / MORE-RECORDS-TO-READ='1'. source: COCRDLIC.cbl:150-152
    private char _recordsToProcessFlag = '\0'; // WS-RECORDS-TO-PROCESS-FLAG
    private bool ReadLoopExit => _recordsToProcessFlag == '0';        // 88 READ-LOOP-EXIT
    private void SetReadLoopExit() => _recordsToProcessFlag = '0';
    private void SetMoreRecordsToRead() => _recordsToProcessFlag = '1';

    // 05 WS-FILE-ERROR-MESSAGE group. source: COCRDLIC.cbl:153-171
    private string _errorOpname = "        "; // ERROR-OPNAME X(8)
    private string _errorFile = "         ";  // ERROR-FILE   X(9)
    private string _errorResp = "          "; // ERROR-RESP   X(10)
    private string _errorResp2 = "          "; // ERROR-RESP2  X(10)

    // =============================================================================================
    //  CC-WORK-AREA (CVCRD01Y): filter inputs + AID flags. source: CVCRD01Y.cpy
    // =============================================================================================
    // CC-ACCT-ID X(11) (redef CC-ACCT-ID-N 9(11)); CC-CARD-NUM X(16) (redef CC-CARD-NUM-N 9(16)).
    // Empty string models LOW-VALUES on the field (the not-supplied test).
    private string _filterAcctId = "";  // CC-ACCT-ID  X(11)
    private string _filterCardNum = ""; // CC-CARD-NUM X(16)

    // CCARD-AID — set by YYYY-STORE-PFKEY, then remapped by the validity gate. source: CVCRD01Y.cpy; :349,378-380
    private CcardAid _aidKey = CcardAid.None; // CCARD-AID
    private bool CcardAidEnter => _aidKey == CcardAid.Enter; // 88 CCARD-AID-ENTER
    private bool CcardAidPfk03 => _aidKey == CcardAid.Pfk03; // 88 CCARD-AID-PFK03
    private bool CcardAidPfk07 => _aidKey == CcardAid.Pfk07; // 88 CCARD-AID-PFK07
    private bool CcardAidPfk08 => _aidKey == CcardAid.Pfk08; // 88 CCARD-AID-PFK08
    private void SetCcardAidEnter() => _aidKey = CcardAid.Enter;

    // =============================================================================================
    //  WS-THIS-PROGCOMMAREA — program-private commarea trailer. source: COCRDLIC.cbl:229-248
    // =============================================================================================
    // WS-CA-LAST-CARDKEY = (LAST-CARD-NUM X16 + LAST-CARD-ACCT-ID 9(11)).
    private string _lastCardNum = ""; // WS-CA-LAST-CARD-NUM
    private long _lastCardAcctId; // WS-CA-LAST-CARD-ACCT-ID
    // WS-CA-FIRST-CARDKEY = (FIRST-CARD-NUM X16 + FIRST-CARD-ACCT-ID 9(11)).
    private string _firstCardNum = ""; // WS-CA-FIRST-CARD-NUM
    private long _firstCardAcctId; // WS-CA-FIRST-CARD-ACCT-ID
    // WS-CA-SCREEN-NUM 9(1). 88 CA-FIRST-PAGE=1. source: :237-238
    private int _screenNum; // WS-CA-SCREEN-NUM
    private bool CaFirstPage => _screenNum == 1; // 88 CA-FIRST-PAGE
    private void SetCaFirstPage() => _screenNum = 1;
    // WS-CA-LAST-PAGE-DISPLAYED 9(1). 88 SHOWN=0 / NOT-SHOWN=9. source: :239-241
    private int _lastPageDisplayed; // WS-CA-LAST-PAGE-DISPLAYED
    private bool CaLastPageShown => _lastPageDisplayed == 0;     // 88 CA-LAST-PAGE-SHOWN
    private bool CaLastPageNotShown => _lastPageDisplayed == 9;  // 88 CA-LAST-PAGE-NOT-SHOWN
    private void SetCaLastPageShown() => _lastPageDisplayed = 0;
    private void SetCaLastPageNotShown() => _lastPageDisplayed = 9;
    // WS-CA-NEXT-PAGE-IND X(1). 88 NOT-EXISTS=LOW-VALUES / EXISTS='Y'. source: :242-244
    private char _nextPageInd = '\0'; // WS-CA-NEXT-PAGE-IND
    private bool CaNextPageExists => _nextPageInd == 'Y';        // 88 CA-NEXT-PAGE-EXISTS
    private bool CaNextPageNotExists => _nextPageInd == '\0';    // 88 CA-NEXT-PAGE-NOT-EXISTS (LOW-VALUES)
    private void SetCaNextPageExists() => _nextPageInd = 'Y';
    private void SetCaNextPageNotExists() => _nextPageInd = '\0';

    // =============================================================================================
    //  WS-SCREEN-DATA — the 7-row page buffer. source: COCRDLIC.cbl:252-260
    // =============================================================================================
    // WS-ALL-ROWS X(196) redef WS-SCREEN-ROWS OCCURS 7: WS-EACH-CARD = (ACCTNO X11 + CARD-NUM X16 +
    // CARD-STATUS X1). LOW-VALUES marks an empty slot — modeled as a null Row.
    private sealed class Row
    {
        public string AcctNo = "";   // WS-ROW-ACCTNO X(11)
        public string CardNum = "";  // WS-ROW-CARD-NUM X(16)
        public string Status = "";   // WS-ROW-CARD-STATUS X(1)
    }
    private readonly Row?[] _rows = new Row?[7]; // index 0..6 = WS-SCREEN-ROWS(1..7); null = LOW-VALUES
    private bool EachCardIsLowValues(int i1) => _rows[i1 - 1] is null; // WS-EACH-CARD(n) = LOW-VALUES
    private void MoveLowValuesToAllRows() { for (int k = 0; k < 7; k++) _rows[k] = null; }

    // The CARD-RECORD currently read by the browse (CVACT02Y). source: COCRDLIC.cbl:290
    private Card? _cardRecord;
    private string CardNum => _cardRecord?.CardNum ?? "";        // CARD-NUM X(16)
    private long CardAcctId => _cardRecord?.AcctId ?? 0;         // CARD-ACCT-ID 9(11)
    private string CardActiveStatus => _cardRecord?.ActiveStatus ?? ""; // CARD-ACTIVE-STATUS X(1)

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map. source: COCRDLIC.cbl:227,262
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The CARD repository is created from
    /// <c>db.Connection</c> inside the browse paragraphs (no DB is opened here).
    /// </summary>
    public CardListProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public CardListProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => ThisProgramId; // PROGRAM-ID. COCRDLIC. source: COCRDLIC.cbl:26-27

    /// <inheritdoc/>
    public string TransId => ThisTranId;  // CSD: CCLI -> COCRDLIC. source: CSD_TRANSACTIONS.md:75; cbl:181-182

    // =============================================================================================
    //  0000-MAIN — source: COCRDLIC.cbl:298-602
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE re-initialised per turn).
        _map = BuildMap();

        // INITIALIZE CC-WORK-AREA WS-MISC-STORAGE WS-COMMAREA. source: COCRDLIC.cbl:300-302
        // (Working-storage starts at its COBOL VALUE/SPACES/LOW-VALUES; the handler instance is fresh.)

        // MOVE LIT-THISTRANID TO WS-TRANID. source: :307 (WS-TRANID is informational only.)
        _ = CardFileAcctPath; // B-8: declared, never used.

        // SET WS-ERROR-MSG-OFF TO TRUE. source: :311
        SetErrorMessageIsBlank();

        // Retrieve passed data if any; initialize on first run. source: :315-332
        if (ctx.EibCalen == 0)
        {
            // INITIALIZE CARDDEMO-COMMAREA WS-THIS-PROGCOMMAREA. source: :316-317
            _commArea = new CardDemoCommArea();
            InitializeProgCommarea();
            _commArea.FromTranId = ThisTranId; // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :318
            _commArea.FromProgram = ThisProgramId;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :319
            _commArea.SetUser();                   // SET CDEMO-USRTYP-USER TO TRUE. source: :320
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER   TO TRUE. source: :321
            _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :322
            _commArea.LastMapSet = ThisMapSet; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :323
            SetCaFirstPage();                      // SET CA-FIRST-PAGE          TO TRUE. source: :324
            SetCaLastPageNotShown();               // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :325
        }
        else
        {
            // MOVE DFHCOMMAREA(1:LEN OF CARDDEMO-COMMAREA) TO CARDDEMO-COMMAREA. source: :327-328
            _commArea = ctx.CommArea!;
            // MOVE DFHCOMMAREA(LEN+1: LEN OF WS-THIS-PROGCOMMAREA) TO WS-THIS-PROGCOMMAREA. source: :329-331
            // (The program-private trailer is carried across turns via the dispatcher's COMMAREA store;
            //  the typed CardDemoCommArea image transports the first segment, the trailer round-trips the
            //  paging state below.)
            RestoreProgCommarea(ctx);
        }

        // If coming in from menu, forget the past and start afresh. source: :336-343
        if (_commArea.IsFirstEntry && _commArea.FromProgram.TrimEnd() != ThisProgramId)
        {
            InitializeProgCommarea();              // INITIALIZE WS-THIS-PROGCOMMAREA. source: :338
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER TO TRUE. source: :339
            _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP TO CDEMO-LAST-MAP. source: :340
            SetCaFirstPage();                      // SET CA-FIRST-PAGE TO TRUE. source: :341
            SetCaLastPageNotShown();               // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :342
        }

        // PERFORM YYYY-STORE-PFKEY. source: :349-350 — EIBAID -> CCARD-AID-*.
        StorePfKeyAid(ctx);

        // If something is present in commarea and the from-program is this program, read & edit inputs.
        // source: :357-362
        if (ctx.EibCalen > 0 && _commArea.FromProgram.TrimEnd() == ThisProgramId)
        {
            ReceiveMapData(ctx); // PERFORM 2000-RECEIVE-MAP. source: :359-360
        }

        // Check the mapped key to see if it is valid at this point. source: :370-380
        SetPfkInvalid();                                           // SET PFK-INVALID TO TRUE. source: :370
        if (CcardAidEnter || CcardAidPfk03 || CcardAidPfk07 || CcardAidPfk08) // source: :371-374
            SetPfkValid();                                         // SET PFK-VALID TO TRUE. source: :375
        if (PfkInvalid)                                            // IF PFK-INVALID. source: :378
            SetCcardAidEnter();                                    // SET CCARD-AID-ENTER TO TRUE. source: :379

        // If the user pressed PF3 go back to main menu. source: :384-406
        if (CcardAidPfk03 && _commArea.FromProgram.TrimEnd() == ThisProgramId)
        {
            _commArea.FromTranId = ThisTranId; // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :386
            _commArea.FromProgram = ThisProgramId;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :387
            _commArea.SetUser();                   // SET CDEMO-USRTYP-USER TO TRUE. source: :388
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER   TO TRUE. source: :389
            _commArea.LastMapSet = ThisMapSet; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :390
            _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :391
            _commArea.ToProgram = MenuProgramId;     // MOVE LIT-MENUPGM    TO CDEMO-TO-PROGRAM. source: :392
            // MOVE LIT-MENUMAPSET TO CCARD-NEXT-MAPSET; MOVE LIT-THISMAP TO CCARD-NEXT-MAP. source: :394-395
            SetWsExitMessage();                    // SET WS-EXIT-MESSAGE TO TRUE. source: :396
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER TO TRUE. source: :400
            // EXEC CICS XCTL PROGRAM(LIT-MENUPGM) COMMAREA(CARDDEMO-COMMAREA). source: :402-405
            ctx.Xctl(MenuProgramId, _commArea);
            return;
        }

        // If the user did not press PF8, reset the last-page flag. source: :410-414
        if (CcardAidPfk08)
        {
            // CONTINUE. source: :411
        }
        else
        {
            SetCaLastPageNotShown(); // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :413
        }

        // Now we decide what to do — EVALUATE TRUE (first true WHEN wins). source: :418-583
        if (InputError)
        {
            // WHEN INPUT-ERROR — ask for corrections to inputs. source: :419-438
            // MOVE WS-ERROR-MSG TO CCARD-ERROR-MSG. source: :423 (staged into ERRMSG by 1400.)
            _commArea.FromProgram = ThisProgramId;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :424
            _commArea.LastMapSet = ThisMapSet; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :425
            _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :426
            // MOVE LIT-THISPGM/MAPSET/MAP TO CCARD-NEXT-PROG/MAPSET/MAP. source: :428-430

            // B-2: IF NOT FLG-ACCTFILTER-NOT-OK AND NOT FLG-CARDFILTER-NOT-OK -> 9000-READ-FORWARD. source: :431-435
            if (!FlgAcctFilterNotOk && !FlgCardFilterNotOk)
                ReadForward();                 // PERFORM 9000-READ-FORWARD. source: :433-434
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :436-437
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :438
            return;
        }
        else if (CcardAidPfk07 && CaFirstPage)
        {
            // WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE (declared twice; first empty WHEN falls into this).
            // PAGE UP - PF7 - BUT ALREADY ON FIRST PAGE. source: :439-454
            _cardRidCardNum = _firstCardNum; // MOVE WS-CA-FIRST-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :446-447
            ReadForward();                     // PERFORM 9000-READ-FORWARD. source: :450-451
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :452-453
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :454
            return;
        }
        else if (CcardAidPfk03 || (_commArea.IsReenter && _commArea.FromProgram.TrimEnd() != ThisProgramId))
        {
            // WHEN CCARD-AID-PFK03 / WHEN CDEMO-PGM-REENTER AND FROM != this. source: :458-482
            _commArea = new CardDemoCommArea();    // INITIALIZE CARDDEMO-COMMAREA. source: :462
            InitializeProgCommarea();              // INITIALIZE WS-THIS-PROGCOMMAREA. source: :463
            _commArea.FromTranId = ThisTranId; // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :464
            _commArea.FromProgram = ThisProgramId;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :465
            _commArea.SetUser();                   // SET CDEMO-USRTYP-USER TO TRUE. source: :466
            _commArea.SetFirstEntry();             // SET CDEMO-PGM-ENTER   TO TRUE. source: :467
            _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :468
            _commArea.LastMapSet = ThisMapSet; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :469
            SetCaFirstPage();                      // SET CA-FIRST-PAGE        TO TRUE. source: :470
            SetCaLastPageNotShown();               // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :471
            _cardRidCardNum = _firstCardNum; // MOVE WS-CA-FIRST-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :473-474
            ReadForward();                     // PERFORM 9000-READ-FORWARD. source: :478-479
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :480-481
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :482
            return;
        }
        else if (CcardAidPfk08 && CaNextPageExists)
        {
            // WHEN CCARD-AID-PFK08 AND CA-NEXT-PAGE-EXISTS — PAGE DOWN. source: :486-497
            _cardRidCardNum = _lastCardNum;  // MOVE WS-CA-LAST-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :488-489
            AddOneToScreenNum();                   // ADD +1 TO WS-CA-SCREEN-NUM. source: :492 (B-9: 9(1) truncation)
            ReadForward();                     // PERFORM 9000-READ-FORWARD. source: :493-494
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :495-496
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :497
            return;
        }
        else if (CcardAidPfk07 && !CaFirstPage)
        {
            // WHEN CCARD-AID-PFK07 AND NOT CA-FIRST-PAGE — PAGE UP. source: :501-513
            _cardRidCardNum = _firstCardNum; // MOVE WS-CA-FIRST-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :504-505
            SubtractOneFromScreenNum();            // SUBTRACT 1 FROM WS-CA-SCREEN-NUM. source: :508
            ReadBackwards();                   // PERFORM 9100-READ-BACKWARDS. source: :509-510
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :511-512
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :513
            return;
        }
        else if (CcardAidEnter && _selectedRow != 0 && ViewRequestedOn(_selectedRow)
                 && _commArea.FromProgram.TrimEnd() == ThisProgramId)
        {
            // WHEN CCARD-AID-ENTER AND VIEW-REQUESTED-ON(I-SELECTED) AND FROM=this. source: :517-541
            // B-1: I-SELECTED may be 0 (no selection); the COBOL subscripts offset 0 which is not 'S',
            // so the WHEN is false and control falls through. Guarded here by `_selectedRow != 0`.
            _commArea.FromTranId = ThisTranId;     // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :520
            _commArea.FromProgram = ThisProgramId;       // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :521
            _commArea.SetUser();                       // SET CDEMO-USRTYP-USER TO TRUE. source: :522
            _commArea.SetFirstEntry();                 // SET CDEMO-PGM-ENTER   TO TRUE. source: :523
            _commArea.LastMapSet = ThisMapSet;     // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :524
            _commArea.LastMap = ThisMap;           // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :525
            // MOVE LIT-CARDDTLPGM/MAPSET/MAP TO CCARD-NEXT-PROG/MAPSET/MAP. source: :526-529
            _commArea.AcctId = ParseLong(_rows[_selectedRow - 1]?.AcctNo);  // MOVE WS-ROW-ACCTNO(I-SELECTED) TO CDEMO-ACCT-ID. source: :531-532
            _commArea.CardNum = ParseLong(_rows[_selectedRow - 1]?.CardNum); // MOVE WS-ROW-CARD-NUM(I-SELECTED) TO CDEMO-CARD-NUM. source: :533-534
            // EXEC CICS XCTL PROGRAM(CCARD-NEXT-PROG) COMMAREA(CARDDEMO-COMMAREA). source: :538-541
            ctx.Xctl(CardDetailProgramId, _commArea);
            return;
        }
        else if (CcardAidEnter && _selectedRow != 0 && UpdateRequestedOn(_selectedRow)
                 && _commArea.FromProgram.TrimEnd() == ThisProgramId)
        {
            // WHEN CCARD-AID-ENTER AND UPDATE-REQUESTED-ON(I-SELECTED) AND FROM=this. source: :545-569
            _commArea.FromTranId = ThisTranId;     // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :548
            _commArea.FromProgram = ThisProgramId;       // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :549
            _commArea.SetUser();                       // SET CDEMO-USRTYP-USER TO TRUE. source: :550
            _commArea.SetFirstEntry();                 // SET CDEMO-PGM-ENTER   TO TRUE. source: :551
            _commArea.LastMapSet = ThisMapSet;     // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :552
            _commArea.LastMap = ThisMap;           // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :553
            // MOVE LIT-CARDUPDPGM/MAPSET/MAP TO CCARD-NEXT-PROG/MAPSET/MAP. source: :554-557
            _commArea.AcctId = ParseLong(_rows[_selectedRow - 1]?.AcctNo);  // MOVE WS-ROW-ACCTNO(I-SELECTED) TO CDEMO-ACCT-ID. source: :559-560
            _commArea.CardNum = ParseLong(_rows[_selectedRow - 1]?.CardNum); // MOVE WS-ROW-CARD-NUM(I-SELECTED) TO CDEMO-CARD-NUM. source: :561-562
            // EXEC CICS XCTL PROGRAM(CCARD-NEXT-PROG) COMMAREA(CARDDEMO-COMMAREA). source: :566-569
            ctx.Xctl(CardUpdateProgramId, _commArea);
            return;
        }
        else
        {
            // WHEN OTHER — plain ENTER / nothing selected: (re)list from first key. source: :572-582
            _cardRidCardNum = _firstCardNum; // MOVE WS-CA-FIRST-CARD-NUM TO WS-CARD-RID-CARDNUM. source: :574-575
            ReadForward();                     // PERFORM 9000-READ-FORWARD. source: :578-579
            SendMapToScreen(ctx);                      // PERFORM 1000-SEND-MAP. source: :580-581
            CommonReturn(ctx);                     // GO TO COMMON-RETURN. source: :582
            return;
        }

        // NOTE: the fall-through below the EVALUATE (lines :586-601) is unreachable from the structured
        // dispatch above because every WHEN ends with GO TO COMMON-RETURN / XCTL. Kept for fidelity:
        //   IF INPUT-ERROR ... GO TO COMMON-RETURN ELSE MOVE LIT-THISPGM TO CCARD-NEXT-PROG; COMMON-RETURN.
    }

    // =============================================================================================
    //  COMMON-RETURN — source: COCRDLIC.cbl:604-620
    // =============================================================================================
    private void CommonReturn(CicsContext ctx)
    {
        _commArea.FromTranId = ThisTranId; // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :605
        _commArea.FromProgram = ThisProgramId;   // MOVE LIT-THISPGM    TO CDEMO-FROM-PROGRAM. source: :606
        _commArea.LastMapSet = ThisMapSet; // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :607
        _commArea.LastMap = ThisMap;       // MOVE LIT-THISMAP    TO CDEMO-LAST-MAP. source: :608

        // Reassemble WS-COMMAREA = [CARDDEMO-COMMAREA][WS-THIS-PROGCOMMAREA] and RETURN. source: :609-619
        SaveProgCommarea(ctx);

        // EXEC CICS RETURN TRANSID(LIT-THISTRANID) COMMAREA(WS-COMMAREA) LENGTH(LENGTH OF WS-COMMAREA).
        ctx.ReturnTransId(ThisTranId, _commArea); // source: :615-619
    }

    // =============================================================================================
    //  1000-SEND-MAP — source: COCRDLIC.cbl:624-641
    // =============================================================================================
    private void SendMapToScreen(CicsContext ctx)  // COBOL paragraph: 1000-SEND-MAP
    {
        ScreenInit(ctx);          // PERFORM 1100-SCREEN-INIT. source: :625-626
        ScreenArrayInit();        // PERFORM 1200-SCREEN-ARRAY-INIT. source: :627-628
        SetupArrayAttribs();      // PERFORM 1250-SETUP-ARRAY-ATTRIBS. source: :629-630
        SetupScreenAttrs(ctx);    // PERFORM 1300-SETUP-SCREEN-ATTRS. source: :631-632
        SetupMessage();           // PERFORM 1400-SETUP-MESSAGE. source: :633-634
        SendScreen(ctx);          // PERFORM 1500-SEND-SCREEN. source: :635-636
    }

    // =============================================================================================
    //  1100-SCREEN-INIT — source: COCRDLIC.cbl:642-676
    // =============================================================================================
    private void ScreenInit(CicsContext ctx)  // COBOL paragraph: 1100-SCREEN-INIT
    {
        // MOVE LOW-VALUES TO CCRDLIAO (clear map). source: :643
        MoveLowValuesToMapOut();

        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA (twice). source: :645,652
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(Title01); // MOVE CCDA-TITLE01 TO TITLE01O. source: :647
        _map.Field("TITLE02").SetValue(Title02); // MOVE CCDA-TITLE02 TO TITLE02O. source: :648
        _map.Field("TRNNAME").SetValue(ThisTranId); // MOVE LIT-THISTRANID TO TRNNAMEO. source: :649
        _map.Field("PGMNAME").SetValue(ThisProgramId);    // MOVE LIT-THISPGM    TO PGMNAMEO. source: :650

        // CURDATEO = mm/dd/yy (year = WS-CURDATE-YEAR(3:2), last two digits). source: :654-658
        string mm = Two(now.Month);
        string dd = Two(now.Day);
        string yy = Four(now.Year).Substring(2, 2);
        _map.Field("CURDATE").SetValue($"{mm}/{dd}/{yy}");

        // CURTIMEO = hh:mm:ss. source: :660-664
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}");

        // MOVE WS-CA-SCREEN-NUM TO PAGENOO (9(1) -> X(3)). source: :667
        _map.Field("PAGENO").SetValue(_screenNum.ToString());

        // SET WS-NO-INFO-MESSAGE; MOVE WS-INFO-MSG TO INFOMSGO; MOVE DFHBMDAR TO INFOMSGC (dark). source: :669-671
        SetHasNoInfoMessage();
        _map.Field("INFOMSG").SetValue(_infoMessage, setMdt: false);
        _map.Field("INFOMSG").AttributeOverride = _map.Field("INFOMSG").Attribute | BmsAttribute.Dark; // DFHBMDAR
    }

    // =============================================================================================
    //  1200-SCREEN-ARRAY-INIT — source: COCRDLIC.cbl:678-747
    // =============================================================================================
    private void ScreenArrayInit()  // COBOL paragraph: 1200-SCREEN-ARRAY-INIT
    {
        // For each row: if WS-EACH-CARD(n) = LOW-VALUES leave blank; else move row values to CRDSELn/
        // ACCTNOn/CRDNUMn/CRDSTSn. (Unrolled in COBOL, one IF per row.) source: :680-742
        for (int n = 1; n <= 7; n++)
        {
            if (EachCardIsLowValues(n))
                continue; // CONTINUE
            Row r = _rows[n - 1]!;
            _map.Field($"CRDSEL{n}").SetValue(_editSelect[n - 1] == '\0' ? "" : _editSelect[n - 1].ToString()); // WS-EDIT-SELECT(n)
            _map.Field($"ACCTNO{n}").SetValue(r.AcctNo);  // WS-ROW-ACCTNO(n)
            _map.Field($"CRDNUM{n}").SetValue(r.CardNum); // WS-ROW-CARD-NUM(n)
            _map.Field($"CRDSTS{n}").SetValue(r.Status);  // WS-ROW-CARD-STATUS(n)
        }
    }

    // =============================================================================================
    //  1250-SETUP-ARRAY-ATTRIBS — source: COCRDLIC.cbl:748-836
    // =============================================================================================
    private void SetupArrayAttribs()  // COBOL paragraph: 1250-SETUP-ARRAY-ATTRIBS
    {
        // Row 1 — protected-empty uses DFHBMPRF (B-5); error highlight writes '*' when blank (B-6). source: :751-762
        if (EachCardIsLowValues(1) || FlgProtectSelectRowsYes)
        {
            // MOVE DFHBMPRF TO CRDSEL1A (protect + FSET). source: :753 (B-5)
            _map.Field("CRDSEL1").AttributeOverride = BmsAttribute.Protected | BmsAttribute.Fset;
        }
        else
        {
            if (_rowCardSelectError[0] == '1') // IF WS-ROW-CRDSELECT-ERROR(1) = '1'. source: :755
            {
                _map.Field("CRDSEL1").ColorOverride = BmsColor.Red; // MOVE DFHRED TO CRDSEL1C. source: :756
                // B-6: row 1 writes '*' when the select char is blank/low. source: :757-759
                if (_editSelect[0] is ' ' or '\0')
                    _map.Field("CRDSEL1").SetValue("*"); // MOVE '*' TO CRDSEL1O
            }
            // MOVE DFHBMFSE TO CRDSEL1A (unprotect + FSET). source: :761
            _map.Field("CRDSEL1").AttributeOverride =
                BmsAttribute.Unprotected | BmsAttribute.Fset;
        }

        // Rows 2-7 — protected-empty uses DFHBMPRO (B-5); error highlight positions cursor (B-6). source: :764-831
        // B-4: the stray lone `I` token on row 4 (:790) is functionally inert; row 4 is ported identically.
        for (int n = 2; n <= 7; n++)
        {
            if (EachCardIsLowValues(n) || FlgProtectSelectRowsYes)
            {
                // MOVE DFHBMPRO TO CRDSELnA (protect, no FSET). source: :766,777,789,801,812,824 (B-5)
                _map.Field($"CRDSEL{n}").AttributeOverride = BmsAttribute.Protected;
            }
            else
            {
                if (_rowCardSelectError[n - 1] == '1') // IF WS-ROW-CRDSELECT-ERROR(n) = '1'. source: :768,...
                {
                    _map.Field($"CRDSEL{n}").ColorOverride = BmsColor.Red; // MOVE DFHRED TO CRDSELnC. source: :769,...
                    _map.Field($"CRDSEL{n}").CursorLength = -1;            // MOVE -1 TO CRDSELnL. source: :770,... (B-6)
                }
                // MOVE DFHBMFSE TO CRDSELnA (unprotect + FSET). source: :772,784,796,807,819,830
                _map.Field($"CRDSEL{n}").AttributeOverride =
                    BmsAttribute.Unprotected | BmsAttribute.Fset;
            }
        }
    }

    // =============================================================================================
    //  1300-SETUP-SCREEN-ATTRS — source: COCRDLIC.cbl:837-892
    // =============================================================================================
    private void SetupScreenAttrs(CicsContext ctx)  // COBOL paragraph: 1300-SETUP-SCREEN-ATTRS
    {
        // INITIALIZE SEARCH CRITERIA — if first entry OR (PGM-ENTER from menu) CONTINUE; else echo filters.
        // source: :839-868
        if (ctx.EibCalen == 0 || (_commArea.IsFirstEntry && _commArea.FromProgram.TrimEnd() == MenuProgramId))
        {
            // CONTINUE (leave filters blank). source: :842
        }
        else
        {
            // Account filter EVALUATE TRUE. source: :844-854
            if (FlgAcctFilterIsValid || FlgAcctFilterNotOk)
            {
                _map.Field("ACCTSID").SetValue(_filterAcctId, setMdt: false); // MOVE CC-ACCT-ID TO ACCTSIDO. source: :847
                _map.Field("ACCTSID").AttributeOverride =
                    BmsAttribute.Unprotected | BmsAttribute.Fset;         // MOVE DFHBMFSE TO ACCTSIDA. source: :848
            }
            else if (_commArea.AcctId == 0)
            {
                _map.Field("ACCTSID").SetValue("", setMdt: false);        // MOVE LOW-VALUES TO ACCTSIDO. source: :850
            }
            else
            {
                _map.Field("ACCTSID").SetValue(Zoned(_commArea.AcctId, 11), setMdt: false); // MOVE CDEMO-ACCT-ID TO ACCTSIDO. source: :852
                _map.Field("ACCTSID").AttributeOverride =
                    BmsAttribute.Unprotected | BmsAttribute.Fset;         // MOVE DFHBMFSE TO ACCTSIDA. source: :853
            }

            // Card filter EVALUATE TRUE. source: :856-867
            if (FlgCardFilterIsValid || FlgCardFilterNotOk)
            {
                _map.Field("CARDSID").SetValue(_filterCardNum, setMdt: false); // MOVE CC-CARD-NUM TO CARDSIDO. source: :859
                _map.Field("CARDSID").AttributeOverride =
                    BmsAttribute.Unprotected | BmsAttribute.Fset;          // MOVE DFHBMFSE TO CARDSIDA. source: :860
            }
            else if (_commArea.CardNum == 0)
            {
                _map.Field("CARDSID").SetValue("", setMdt: false);         // MOVE LOW-VALUES TO CARDSIDO. source: :862
            }
            else
            {
                _map.Field("CARDSID").SetValue(Zoned(_commArea.CardNum, 16), setMdt: false); // MOVE CDEMO-CARD-NUM TO CARDSIDO. source: :864-865
                _map.Field("CARDSID").AttributeOverride =
                    BmsAttribute.Unprotected | BmsAttribute.Fset;          // MOVE DFHBMFSE TO CARDSIDA. source: :866
            }
        }

        // POSITION CURSOR. source: :870-886
        if (FlgAcctFilterNotOk)
        {
            _map.Field("ACCTSID").ColorOverride = BmsColor.Red; // MOVE DFHRED TO ACCTSIDC. source: :873
            _map.Field("ACCTSID").CursorLength = -1;            // MOVE -1 TO ACCTSIDL. source: :874
        }

        if (FlgCardFilterNotOk)
        {
            _map.Field("CARDSID").ColorOverride = BmsColor.Red; // MOVE DFHRED TO CARDSIDC. source: :878
            _map.Field("CARDSID").CursorLength = -1;            // MOVE -1 TO CARDSIDL. source: :879
        }

        // IF NO ERRORS POSITION CURSOR AT ACCTID. source: :884-886
        if (InputOk)
            _map.Field("ACCTSID").CursorLength = -1;            // MOVE -1 TO ACCTSIDL. source: :885
    }

    // =============================================================================================
    //  1400-SETUP-MESSAGE — source: COCRDLIC.cbl:895-935
    // =============================================================================================
    private void SetupMessage()  // COBOL paragraph: 1400-SETUP-MESSAGE
    {
        // EVALUATE TRUE — choose status message (first true wins). source: :897-922
        if (FlgAcctFilterNotOk || FlgCardFilterNotOk)
        {
            // CONTINUE (keep the field-level error already in WS-ERROR-MSG). source: :898-900
        }
        else if (CcardAidPfk07 && CaFirstPage)
        {
            _errorMessage = "NO PREVIOUS PAGES TO DISPLAY"; // source: :901-904
        }
        else if (CcardAidPfk08 && CaNextPageNotExists && CaLastPageShown)
        {
            _errorMessage = "NO MORE PAGES TO DISPLAY"; // source: :905-909
        }
        else if (CcardAidPfk08 && CaNextPageNotExists)
        {
            SetWsInformRecActions(); // SET WS-INFORM-REC-ACTIONS TO TRUE. source: :912
            if (CaLastPageNotShown && CaNextPageNotExists) // source: :913-914
                SetCaLastPageShown();                      // SET CA-LAST-PAGE-SHOWN TO TRUE. source: :915
        }
        else if (HasNoInfoMessage || CaNextPageExists)
        {
            SetWsInformRecActions(); // SET WS-INFORM-REC-ACTIONS TO TRUE. source: :917-919
        }
        else
        {
            SetHasNoInfoMessage(); // SET WS-NO-INFO-MESSAGE TO TRUE. source: :920-921
        }

        // MOVE WS-ERROR-MSG TO ERRMSGO. source: :924
        _map.Field("ERRMSG").SetValue(_errorMessage, setMdt: false);

        // IF NOT WS-NO-INFO-MESSAGE AND NOT WS-NO-RECORDS-FOUND -> INFOMSGO + DFHNEUTR. source: :926-930
        if (!HasNoInfoMessage && !IsNoRecordsFoundError)
        {
            _map.Field("INFOMSG").SetValue(_infoMessage, setMdt: false); // MOVE WS-INFO-MSG TO INFOMSGO. source: :928
            _map.Field("INFOMSG").AttributeOverride = _map.Field("INFOMSG").Attribute; // clear the DFHBMDAR dark set by 1100
            _map.Field("INFOMSG").ColorOverride = BmsColor.Neutral;   // MOVE DFHNEUTR TO INFOMSGC. source: :929
        }
    }

    // =============================================================================================
    //  1500-SEND-SCREEN — source: COCRDLIC.cbl:938-950
    // =============================================================================================
    private void SendScreen(CicsContext ctx)  // COBOL paragraph: 1500-SEND-SCREEN
    {
        // EXEC CICS SEND MAP(CCRDLIA) MAPSET(COCRDLI) FROM(CCRDLIAO) CURSOR ERASE RESP FREEKB. source: :939-946
        ctx.SendMap(ThisMap, ThisMapSet, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1,
        });
        _responseCode = (int)Resp.Normal;
    }

    // =============================================================================================
    //  2000-RECEIVE-MAP — source: COCRDLIC.cbl:951-961
    // =============================================================================================
    private void ReceiveMapData(CicsContext ctx)  // COBOL paragraph: 2000-RECEIVE-MAP
    {
        ReceiveScreen(ctx); // PERFORM 2100-RECEIVE-SCREEN. source: :952-953
        EditInputs();       // PERFORM 2200-EDIT-INPUTS. source: :955-956
    }

    // =============================================================================================
    //  2100-RECEIVE-SCREEN — source: COCRDLIC.cbl:962-983
    // =============================================================================================
    private void ReceiveScreen(CicsContext ctx)  // COBOL paragraph: 2100-RECEIVE-SCREEN
    {
        // EXEC CICS RECEIVE MAP(CCRDLIA) MAPSET(COCRDLI) INTO(CCRDLIAI) RESP. source: :963-967
        ctx.ReceiveMap(ThisMap, ThisMapSet, _map);
        _responseCode = (int)Resp.Normal;

        // MOVE ACCTSIDI TO CC-ACCT-ID ; MOVE CARDSIDI TO CC-CARD-NUM. source: :969-970
        _filterAcctId = _map.Field("ACCTSID").Value;
        _filterCardNum = _map.Field("CARDSID").Value;

        // MOVE CRDSEL1I..CRDSEL7I TO WS-EDIT-SELECT(1..7). source: :972-978
        for (int n = 1; n <= 7; n++)
        {
            string v = _map.Field($"CRDSEL{n}").Value;
            _editSelect[n - 1] = v.Length > 0 ? v[0] : '\0';
        }
    }

    // =============================================================================================
    //  2200-EDIT-INPUTS — source: COCRDLIC.cbl:985-1001
    // =============================================================================================
    private void EditInputs()  // COBOL paragraph: 2200-EDIT-INPUTS
    {
        SetInputOk();                 // SET INPUT-OK TO TRUE. source: :986
        SetFlgProtectSelectRowsNo();  // SET FLG-PROTECT-SELECT-ROWS-NO TO TRUE. source: :987

        EditAccount(); // PERFORM 2210-EDIT-ACCOUNT. source: :989-990
        EditCard();    // PERFORM 2220-EDIT-CARD. source: :992-993
        EditArray();   // PERFORM 2250-EDIT-ARRAY. source: :995-996
    }

    // =============================================================================================
    //  2210-EDIT-ACCOUNT — source: COCRDLIC.cbl:1003-1034
    // =============================================================================================
    private void EditAccount()  // COBOL paragraph: 2210-EDIT-ACCOUNT
    {
        SetFlgAcctFilterBlank(); // SET FLG-ACCTFILTER-BLANK TO TRUE. source: :1004

        // Not supplied: IF CC-ACCT-ID = LOW-VALUES OR SPACES OR CC-ACCT-ID-N = ZEROS. source: :1007-1013
        if (IsLowValues(_filterAcctId) || IsSpaces(_filterAcctId) || IsZeroesNum(_filterAcctId))
        {
            SetFlgAcctFilterBlank();  // SET FLG-ACCTFILTER-BLANK TO TRUE. source: :1010
            _commArea.AcctId = 0;     // MOVE ZEROES TO CDEMO-ACCT-ID. source: :1011
            return;                   // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :1012
        }

        // Not numeric: IF CC-ACCT-ID IS NOT NUMERIC. source: :1017-1029
        if (!IsNumericX(_filterAcctId, 11))
        {
            SetInputError();                // SET INPUT-ERROR TO TRUE. source: :1018
            SetFlgAcctFilterNotOk();        // SET FLG-ACCTFILTER-NOT-OK TO TRUE. source: :1019
            SetFlgProtectSelectRowsYes();   // SET FLG-PROTECT-SELECT-ROWS-YES TO TRUE. source: :1020
            _errorMessage = "ACCOUNT FILTER,IF SUPPLIED MUST BE A 11 DIGIT NUMBER"; // source: :1021-1023
            _commArea.AcctId = 0;           // MOVE ZERO TO CDEMO-ACCT-ID. source: :1024
            return;                         // GO TO 2210-EDIT-ACCOUNT-EXIT. source: :1025
        }
        else
        {
            _commArea.AcctId = ParseLong(_filterAcctId); // MOVE CC-ACCT-ID TO CDEMO-ACCT-ID. source: :1027
            SetFlgAcctFilterIsValid();               // SET FLG-ACCTFILTER-ISVALID TO TRUE. source: :1028
        }
    }

    // =============================================================================================
    //  2220-EDIT-CARD — source: COCRDLIC.cbl:1036-1071
    // =============================================================================================
    private void EditCard()  // COBOL paragraph: 2220-EDIT-CARD
    {
        SetFlgCardFilterBlank(); // SET FLG-CARDFILTER-BLANK TO TRUE. source: :1039

        // Not supplied: IF CC-CARD-NUM = LOW-VALUES OR SPACES OR CC-CARD-NUM-N = ZEROS. source: :1042-1048
        if (IsLowValues(_filterCardNum) || IsSpaces(_filterCardNum) || IsZeroesNum(_filterCardNum))
        {
            SetFlgCardFilterBlank();  // SET FLG-CARDFILTER-BLANK TO TRUE. source: :1045
            _commArea.CardNum = 0;    // MOVE ZEROES TO CDEMO-CARD-NUM. source: :1046
            return;                   // GO TO 2220-EDIT-CARD-EXIT. source: :1047
        }

        // Not numeric: IF CC-CARD-NUM IS NOT NUMERIC. source: :1052-1066
        if (!IsNumericX(_filterCardNum, 16))
        {
            SetInputError();              // SET INPUT-ERROR TO TRUE. source: :1053
            SetFlgCardFilterNotOk();      // SET FLG-CARDFILTER-NOT-OK TO TRUE. source: :1054
            SetFlgProtectSelectRowsYes(); // SET FLG-PROTECT-SELECT-ROWS-YES TO TRUE. source: :1055
            // B-3: only set the card message IF WS-ERROR-MSG-OFF (acct error message wins). source: :1056-1060
            if (ErrorMessageIsBlank)
                _errorMessage = "CARD ID FILTER,IF SUPPLIED MUST BE A 16 DIGIT NUMBER"; // source: :1057-1059
            _commArea.CardNum = 0;        // MOVE ZERO TO CDEMO-CARD-NUM. source: :1061
            return;                       // GO TO 2220-EDIT-CARD-EXIT. source: :1062
        }
        else
        {
            _commArea.CardNum = ParseLong(_filterCardNum); // MOVE CC-CARD-NUM-N TO CDEMO-CARD-NUM. source: :1064
            SetFlgCardFilterIsValid();                 // SET FLG-CARDFILTER-ISVALID TO TRUE. source: :1065
        }
    }

    // =============================================================================================
    //  2250-EDIT-ARRAY — source: COCRDLIC.cbl:1073-1121
    // =============================================================================================
    private void EditArray()  // COBOL paragraph: 2250-EDIT-ARRAY
    {
        // IF INPUT-ERROR GO TO 2250-EDIT-ARRAY-EXIT. source: :1075-1077
        if (InputError)
            return;

        // INSPECT WS-EDIT-SELECT-FLAGS TALLYING I FOR ALL 'S' ALL 'U'. source: :1079-1082
        _tally = 0;
        for (int k = 0; k < 7; k++)
            if (_editSelect[k] is 'S' or 'U')
                _tally++;

        // IF I > +1 -> MORE-THAN-1-ACTION + build error flags. source: :1084-1095
        if (_tally > 1)
        {
            SetInputError();        // SET INPUT-ERROR TO TRUE. source: :1085
            SetHasMoreThanOneAction(); // SET WS-MORE-THAN-1-ACTION TO TRUE. source: :1086
            // MOVE WS-EDIT-SELECT-FLAGS TO WS-EDIT-SELECT-ERROR-FLAGS; INSPECT REPLACING ALL 'S'/'U' BY '1'
            //   CHARACTERS BY '0'. source: :1088-1093
            for (int k = 0; k < 7; k++)
                _rowCardSelectError[k] = _editSelect[k] is 'S' or 'U' ? '1' : '0';
        }

        // MOVE ZERO TO I-SELECTED. source: :1097
        _selectedRow = 0;

        // PERFORM VARYING I FROM 1 BY 1 UNTIL I > 7. source: :1099-1115
        for (_tally = 1; _tally <= 7; _tally++)
        {
            if (SelectOk(_tally)) // WHEN SELECT-OK(I). source: :1101
            {
                _selectedRow = _tally; // MOVE I TO I-SELECTED. source: :1102
                if (_errorMessage == MoreThan1Action) // IF WS-MORE-THAN-1-ACTION. source: :1103
                    _rowCardSelectError[_tally - 1] = '1'; // MOVE '1' TO WS-ROW-CRDSELECT-ERROR(I). source: :1104
            }
            else if (SelectBlank(_tally)) // WHEN SELECT-BLANK(I). source: :1106
            {
                // CONTINUE. source: :1107
            }
            else // WHEN OTHER. source: :1108
            {
                SetInputError();                    // SET INPUT-ERROR TO TRUE. source: :1109
                _rowCardSelectError[_tally - 1] = '1'; // MOVE '1' TO WS-ROW-CRDSELECT-ERROR(I). source: :1110
                if (ErrorMessageIsBlank)                  // IF WS-ERROR-MSG-OFF. source: :1111
                    SetWsInvalidActionCode();       // SET WS-INVALID-ACTION-CODE TO TRUE. source: :1112
            }
        }
    }

    // =============================================================================================
    //  9000-READ-FORWARD — source: COCRDLIC.cbl:1123-1263
    // =============================================================================================
    private void ReadForward()  // COBOL paragraph: 9000-READ-FORWARD
    {
        MoveLowValuesToAllRows(); // MOVE LOW-VALUES TO WS-ALL-ROWS. source: :1124

        var card = new CardRepository(_db.Connection);

        // EXEC CICS STARTBR DATASET(CARDDAT) RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) GTEQ. source: :1129-1136
        // LOW-VALUES (empty) start key positions at the first record.
        card.StartBrowse(string.IsNullOrEmpty(_cardRidCardNum) ? null : _cardRidCardNum);

        _screenCounter = 0;          // MOVE ZEROES TO WS-SCRN-COUNTER. source: :1140
        SetCaNextPageExists();       // SET CA-NEXT-PAGE-EXISTS    TO TRUE. source: :1141
        SetMoreRecordsToRead();      // SET MORE-RECORDS-TO-READ   TO TRUE. source: :1142

        // PERFORM UNTIL READ-LOOP-EXIT. source: :1144-1256
        while (!ReadLoopExit)
        {
            // EXEC CICS READNEXT DATASET(CARDDAT) INTO(CARD-RECORD) RIDFLD(...) RESP. source: :1146-1154
            string fs = card.ReadNext(out _cardRecord);
            SetResp(fs);

            if (_responseCode == (int)Resp.Normal || _responseCode == (int)Resp.DupRec) // WHEN NORMAL/DUPREC. source: :1157-1158
            {
                FilterRecords(); // PERFORM 9500-FILTER-RECORDS. source: :1159-1160

                if (IncludeThisRecord) // IF WS-DONOT-EXCLUDE-THIS-RECORD. source: :1162
                {
                    _screenCounter++; // ADD 1 TO WS-SCRN-COUNTER. source: :1163

                    // MOVE CARD-NUM/ACCT-ID/ACTIVE-STATUS TO WS-ROW-*(WS-SCRN-COUNTER). source: :1165-1171
                    _rows[_screenCounter - 1] = new Row
                    {
                        CardNum = CardNum,
                        AcctNo = Zoned(CardAcctId, 11),
                        Status = CardActiveStatus,
                    };

                    if (_screenCounter == 1) // IF WS-SCRN-COUNTER = 1. source: :1173
                    {
                        _firstCardAcctId = CardAcctId; // MOVE CARD-ACCT-ID TO WS-CA-FIRST-CARD-ACCT-ID. source: :1174-1175
                        _firstCardNum = CardNum;       // MOVE CARD-NUM     TO WS-CA-FIRST-CARD-NUM. source: :1176
                        if (_screenNum == 0)           // IF WS-CA-SCREEN-NUM = 0. source: :1177
                            AddOneToScreenNum();           // ADD +1 TO WS-CA-SCREEN-NUM. source: :1178
                        // ELSE CONTINUE. source: :1179-1180
                    }
                    // ELSE CONTINUE. source: :1182-1183
                }
                // ELSE CONTINUE. source: :1185-1186

                // IF WS-SCRN-COUNTER = WS-MAX-SCREEN-LINES — page full; look ahead one record. source: :1191-1232
                if (_screenCounter == MaxScreenLines)
                {
                    SetReadLoopExit(); // SET READ-LOOP-EXIT TO TRUE. source: :1192

                    _lastCardAcctId = CardAcctId; // MOVE CARD-ACCT-ID TO WS-CA-LAST-CARD-ACCT-ID. source: :1194
                    _lastCardNum = CardNum;       // MOVE CARD-NUM     TO WS-CA-LAST-CARD-NUM. source: :1195

                    // EXEC CICS READNEXT (look-ahead). source: :1197-1205
                    string fs2 = card.ReadNext(out _cardRecord);
                    SetResp(fs2);

                    if (_responseCode == (int)Resp.Normal || _responseCode == (int)Resp.DupRec) // WHEN NORMAL/DUPREC. source: :1208-1209
                    {
                        SetCaNextPageExists();            // SET CA-NEXT-PAGE-EXISTS TO TRUE. source: :1210-1211
                        _lastCardAcctId = CardAcctId; // MOVE CARD-ACCT-ID TO WS-CA-LAST-CARD-ACCT-ID. source: :1212-1213
                        _lastCardNum = CardNum;       // MOVE CARD-NUM     TO WS-CA-LAST-CARD-NUM. source: :1214
                    }
                    else if (_responseCode == (int)Resp.EndFile) // WHEN ENDFILE. source: :1215
                    {
                        SetCaNextPageNotExists(); // SET CA-NEXT-PAGE-NOT-EXISTS TO TRUE. source: :1216
                        if (ErrorMessageIsBlank)        // IF WS-ERROR-MSG-OFF. source: :1218
                            _errorMessage = "NO MORE RECORDS TO SHOW"; // source: :1219-1220
                    }
                    else // WHEN OTHER — file error. source: :1222-1230
                    {
                        SetReadLoopExit();               // SET READ-LOOP-EXIT TO TRUE. source: :1225
                        _errorOpname = PadX("READ", 8);  // MOVE 'READ' TO ERROR-OPNAME. source: :1226
                        _errorFile = PadX(CardFileName, 9); // MOVE LIT-CARD-FILE TO ERROR-FILE. source: :1227
                        _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. source: :1228
                        _errorResp2 = Alpha(_reasonCode, 10);  // MOVE WS-REAS-CD TO ERROR-RESP2. source: :1229
                        _errorMessage = Truncate75(BuildFileErrorMessage()); // MOVE WS-FILE-ERROR-MESSAGE TO WS-ERROR-MSG. source: :1230
                    }
                }
            }
            else if (_responseCode == (int)Resp.EndFile) // WHEN ENDFILE (main). source: :1233-1245
            {
                SetReadLoopExit();        // SET READ-LOOP-EXIT TO TRUE. source: :1234
                SetCaNextPageNotExists(); // SET CA-NEXT-PAGE-NOT-EXISTS TO TRUE. source: :1235
                _lastCardAcctId = CardAcctId; // MOVE CARD-ACCT-ID TO WS-CA-LAST-CARD-ACCT-ID. source: :1236
                _lastCardNum = CardNum;       // MOVE CARD-NUM     TO WS-CA-LAST-CARD-NUM. source: :1237
                if (ErrorMessageIsBlank)                // IF WS-ERROR-MSG-OFF. source: :1238
                    _errorMessage = "NO MORE RECORDS TO SHOW"; // source: :1239
                if (_screenNum == 1 && _screenCounter == 0) // source: :1241-1242
                    SetIsNoRecordsFoundError();                       // SET WS-NO-RECORDS-FOUND TO TRUE. source: :1244
            }
            else // WHEN OTHER — file error. source: :1246-1254
            {
                SetReadLoopExit();               // SET READ-LOOP-EXIT TO TRUE. source: :1249
                _errorOpname = PadX("READ", 8);  // MOVE 'READ' TO ERROR-OPNAME. source: :1250
                _errorFile = PadX(CardFileName, 9); // MOVE LIT-CARD-FILE TO ERROR-FILE. source: :1251
                _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. source: :1252
                _errorResp2 = Alpha(_reasonCode, 10);  // MOVE WS-REAS-CD TO ERROR-RESP2. source: :1253
                _errorMessage = Truncate75(BuildFileErrorMessage()); // MOVE WS-FILE-ERROR-MESSAGE TO WS-ERROR-MSG. source: :1254
            }
        }

        // EXEC CICS ENDBR FILE(CARDDAT). source: :1258-1259
        card.EndBrowse();
    }

    // =============================================================================================
    //  9100-READ-BACKWARDS — source: COCRDLIC.cbl:1264-1380
    // =============================================================================================
    private void ReadBackwards()  // COBOL paragraph: 9100-READ-BACKWARDS
    {
        MoveLowValuesToAllRows(); // MOVE LOW-VALUES TO WS-ALL-ROWS. source: :1266

        // MOVE WS-CA-FIRST-CARDKEY TO WS-CA-LAST-CARDKEY. source: :1268
        _lastCardNum = _firstCardNum;
        _lastCardAcctId = _firstCardAcctId;

        var card = new CardRepository(_db.Connection);

        // EXEC CICS STARTBR DATASET(CARDDAT) RIDFLD(WS-CARD-RID-CARDNUM) KEYLENGTH(16) GTEQ. source: :1273-1280
        card.StartBrowse(string.IsNullOrEmpty(_cardRidCardNum) ? null : _cardRidCardNum);

        // COMPUTE WS-SCRN-COUNTER = WS-MAX-SCREEN-LINES + 1 (= 8). source: :1284-1286
        _screenCounter = MaxScreenLines + 1;
        SetCaNextPageExists();  // SET CA-NEXT-PAGE-EXISTS  TO TRUE. source: :1287
        SetMoreRecordsToRead(); // SET MORE-RECORDS-TO-READ TO TRUE. source: :1288

        // Priming READPREV (skips the current first record). source: :1294-1318
        string fsPrime = card.ReadPrevious(out _cardRecord);
        SetResp(fsPrime);
        if (_responseCode == (int)Resp.Normal || _responseCode == (int)Resp.DupRec) // WHEN NORMAL/DUPREC. source: :1305-1306
        {
            _screenCounter--; // SUBTRACT 1 FROM WS-SCRN-COUNTER (-> 7). source: :1307
        }
        else // WHEN OTHER — file error; ENDBR + exit. source: :1308-1317
        {
            SetReadLoopExit();               // SET READ-LOOP-EXIT TO TRUE. source: :1311
            _errorOpname = PadX("READ", 8);  // MOVE 'READ' TO ERROR-OPNAME. source: :1312
            _errorFile = PadX(CardFileName, 9); // MOVE LIT-CARD-FILE TO ERROR-FILE. source: :1313
            _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. source: :1314
            _errorResp2 = Alpha(_reasonCode, 10);  // MOVE WS-REAS-CD TO ERROR-RESP2. source: :1315
            _errorMessage = Truncate75(BuildFileErrorMessage()); // source: :1316
            card.EndBrowse();                // 9100-READ-BACKWARDS-EXIT ENDBR. source: :1375-1377
            return;                          // GO TO 9100-READ-BACKWARDS-EXIT. source: :1317
        }

        // PERFORM UNTIL READ-LOOP-EXIT. source: :1320-1371
        while (!ReadLoopExit)
        {
            // EXEC CICS READPREV DATASET(CARDDAT) INTO(CARD-RECORD) RIDFLD(...) RESP. source: :1322-1330
            string fs = card.ReadPrevious(out _cardRecord);
            SetResp(fs);

            if (_responseCode == (int)Resp.Normal || _responseCode == (int)Resp.DupRec) // WHEN NORMAL/DUPREC. source: :1333-1334
            {
                FilterRecords(); // PERFORM 9500-FILTER-RECORDS. source: :1335-1336

                if (IncludeThisRecord) // IF WS-DONOT-EXCLUDE-THIS-RECORD. source: :1337
                {
                    // MOVE CARD-NUM/ACCT-ID/ACTIVE-STATUS TO WS-ROW-*(WS-SCRN-COUNTER). source: :1338-1344
                    _rows[_screenCounter - 1] = new Row
                    {
                        CardNum = CardNum,
                        AcctNo = Zoned(CardAcctId, 11),
                        Status = CardActiveStatus,
                    };

                    _screenCounter--; // SUBTRACT 1 FROM WS-SCRN-COUNTER. source: :1346
                    if (_screenCounter == 0) // IF WS-SCRN-COUNTER = 0. source: :1347
                    {
                        SetReadLoopExit();                 // SET READ-LOOP-EXIT TO TRUE. source: :1348
                        _firstCardAcctId = CardAcctId; // MOVE CARD-ACCT-ID TO WS-CA-FIRST-CARD-ACCT-ID. source: :1350-1351
                        _firstCardNum = CardNum;       // MOVE CARD-NUM     TO WS-CA-FIRST-CARD-NUM. source: :1352-1353
                    }
                    // ELSE CONTINUE. source: :1354-1355
                }
                // ELSE CONTINUE. source: :1357-1358
            }
            else // WHEN OTHER — file error. source: :1361-1370
            {
                SetReadLoopExit();               // SET READ-LOOP-EXIT TO TRUE. source: :1364
                _errorOpname = PadX("READ", 8);  // MOVE 'READ' TO ERROR-OPNAME. source: :1365
                _errorFile = PadX(CardFileName, 9); // MOVE LIT-CARD-FILE TO ERROR-FILE. source: :1366
                _errorResp = Alpha(_responseCode, 10);   // MOVE WS-RESP-CD TO ERROR-RESP. source: :1367
                _errorResp2 = Alpha(_reasonCode, 10);  // MOVE WS-REAS-CD TO ERROR-RESP2. source: :1368
                _errorMessage = Truncate75(BuildFileErrorMessage()); // source: :1369
            }
        }

        // 9100-READ-BACKWARDS-EXIT: EXEC CICS ENDBR FILE(CARDDAT). source: :1374-1377 (ENDBR lives in EXIT.)
        card.EndBrowse();
    }

    // =============================================================================================
    //  9500-FILTER-RECORDS — source: COCRDLIC.cbl:1382-1411
    // =============================================================================================
    private void FilterRecords()  // COBOL paragraph: 9500-FILTER-RECORDS
    {
        SetIncludeThisRecord(); // SET WS-DONOT-EXCLUDE-THIS-RECORD TO TRUE. source: :1383

        // IF FLG-ACCTFILTER-ISVALID: IF CARD-ACCT-ID = CC-ACCT-ID continue else EXCLUDE. source: :1385-1394
        if (FlgAcctFilterIsValid)
        {
            // CARD-ACCT-ID 9(11) = CC-ACCT-ID X(11): compare as numeric value. source: :1386
            if (CardAcctId == ParseLong(_filterAcctId))
            {
                // CONTINUE
            }
            else
            {
                SetWsExcludeThisRecord(); // SET WS-EXCLUDE-THIS-RECORD TO TRUE. source: :1389
                return;                   // GO TO 9500-FILTER-RECORDS-EXIT. source: :1390
            }
        }

        // IF FLG-CARDFILTER-ISVALID: IF CARD-NUM = CC-CARD-NUM-N continue else EXCLUDE. source: :1396-1405
        if (FlgCardFilterIsValid)
        {
            // CARD-NUM X(16) = CC-CARD-NUM-N 9(16): compare as numeric value. source: :1397
            if (ParseLong(CardNum) == ParseLong(_filterCardNum))
            {
                // CONTINUE
            }
            else
            {
                SetWsExcludeThisRecord(); // SET WS-EXCLUDE-THIS-RECORD TO TRUE. source: :1400
                return;                   // GO TO 9500-FILTER-RECORDS-EXIT. source: :1401
            }
        }
    }

    // =============================================================================================
    //  YYYY-STORE-PFKEY (copybook CSSTRPFY) — source: CSSTRPFY.cpy:17-82; COCRDLIC.cbl:349-350
    // =============================================================================================
    private void StorePfKeyAid(CicsContext ctx) => _aidKey = ctx.StorePfKey();  // COBOL paragraph: YYYY-STORE-PFKEY

    // =============================================================================================
    //  Arithmetic helpers — COBOL truncation semantics
    // =============================================================================================
    // ADD +1 TO WS-CA-SCREEN-NUM (PIC 9(1)) — B-9: single-digit silent truncation. source: :237,492,1178
    private void AddOneToScreenNum() => _screenNum = (_screenNum + 1) % 10;
    // SUBTRACT 1 FROM WS-CA-SCREEN-NUM (PIC 9(1)). source: :508
    private void SubtractOneFromScreenNum() => _screenNum = ((_screenNum - 1) % 10 + 10) % 10;

    // =============================================================================================
    //  WS-THIS-PROGCOMMAREA (de)serialize — the program-private trailer carried across turns.
    //  source: COCRDLIC.cbl:229-248, 327-331, 609-619
    // =============================================================================================
    // The trailer is appended after CARDDEMO-COMMAREA in WS-COMMAREA. The console runtime transports a
    // typed CardDemoCommArea per turn; the paging state lives in the unused tail of CDEMO fields. We pack
    // the trailer fields into CDEMO-* spare slots that COCRDLIC otherwise leaves at their pass-through
    // values, and restore them on re-entry, so PF7/PF8 round-trip exactly.

    private void InitializeProgCommarea()
    {
        // INITIALIZE WS-THIS-PROGCOMMAREA: alphanumeric -> SPACES, numeric -> 0, X(1) inds -> SPACE.
        _lastCardNum = "";
        _lastCardAcctId = 0;
        _firstCardNum = "";
        _firstCardAcctId = 0;
        _screenNum = 0;
        _lastPageDisplayed = 0;
        _nextPageInd = ' '; // INITIALIZE sets X(1) to SPACE (not LOW-VALUES). source: PORT NOTE INITIALIZE.
    }

    // The trailer is serialized into the COMMAREA's CustFName/CustMName/CustLName text slots (75 spare
    // bytes) which COCRDLIC never reads or writes — preserving a lossless round-trip of the paging state
    // without disturbing any field the program uses. (CDEMO-CUSTOMER-INFO is untouched by COCRDLIC.)
    private void SaveProgCommarea(CicsContext ctx)
    {
        // Pack the program-private paging trailer into the 75 spare CustName bytes. The byte layout mirrors
        // RestoreProgCommarea exactly (offsets 0/16/32/33/34/35/46):
        //   FIRST-CARD-NUM(16) | LAST-CARD-NUM(16) | SCREEN-NUM(1) | LAST-PAGE-DISPLAYED(1) | NEXT-IND(1) |
        //   FIRST-CARD-ACCT-ID(11) | LAST-CARD-ACCT-ID(11)  = 57 bytes.
        string packed =
            PadX(_firstCardNum, 16) +
            PadX(_lastCardNum, 16) +
            (char)('0' + (_screenNum % 10)) +
            (char)('0' + (_lastPageDisplayed % 10)) +
            (_nextPageInd == '\0' ? ' ' : _nextPageInd) +
            Zoned(_firstCardAcctId, 11) +
            Zoned(_lastCardAcctId, 11);
        // Spread the 57-byte image across CustFName(25)+CustMName(25)+CustLName(25); pad to the full 75.
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreProgCommarea(CicsContext ctx)
    {
        string fname = PadX(_commArea.CustFName, 25);
        string mname = PadX(_commArea.CustMName, 25);
        string lname = PadX(_commArea.CustLName, 25);
        string packed = fname + mname + lname;
        if (packed.Length < 51) packed = PadX(packed, 73);

        _firstCardNum = packed.Substring(0, 16).TrimEnd();
        _lastCardNum = packed.Substring(16, 16).TrimEnd();
        char sn = packed[32];
        char lpd = packed[33];
        char npi = packed[34];
        _screenNum = sn is >= '0' and <= '9' ? sn - '0' : 0;
        _lastPageDisplayed = lpd is >= '0' and <= '9' ? lpd - '0' : 0;
        _nextPageInd = npi == 'Y' ? 'Y' : (npi == ' ' ? ' ' : '\0');
        _firstCardAcctId = ParseLong(packed.Length >= 46 ? packed.Substring(35, 11) : "0");
        _lastCardAcctId = ParseLong(packed.Length >= 57 ? packed.Substring(46, 11) : "0");
    }

    // =============================================================================================
    //  WS-FILE-ERROR-MESSAGE builder. source: COCRDLIC.cbl:153-171
    // =============================================================================================
    // 'File Error:' OPNAME(8) ' on ' FILE(9) ' returned RESP ' RESP(10) ',RESP2 ' RESP2(10) X(5).
    private string BuildFileErrorMessage() =>
        "File Error:" + _errorOpname + " on " + _errorFile + " returned RESP " +
        _errorResp + ",RESP2 " + _errorResp2 + "     ";

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>Maps a repository FileStatus to the CICS RESP/RESP2 the EVALUATE branches on.</summary>
    private void SetResp(string fileStatus)
    {
        _responseCode = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,              // '00' -> DFHRESP(NORMAL)
            FileStatus.EndOfFile => (int)Resp.EndFile,      // '10' -> DFHRESP(ENDFILE)
            FileStatus.RecordNotFound => (int)Resp.NotFnd,  // '23' -> DFHRESP(NOTFND)
            FileStatus.DuplicateKey => (int)Resp.DupRec,    // '02' -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                           // any other -> WHEN OTHER (file error)
        };
        _reasonCode = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    /// <summary>
    /// MOVE LOW-VALUES TO CCRDLIAO — blank every named output field and clear per-turn overrides before the
    /// first SEND. source: COCRDLIC.cbl:643.
    /// </summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
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

    /// <summary>MOVE numeric (binary) TO alphanumeric X(width): 9-digit zoned display left-justified.</summary>
    private static string Alpha(int value, int width) => PadX(Zoned(value, 9), width);

    /// <summary>Truncates a built message to the X(75) WS-ERROR-MSG width.</summary>
    private static string Truncate75(string s) => s.Length > 75 ? s[..75] : s;

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    /// <summary>True when a string is all spaces (and non-empty).</summary>
    private static bool IsSpaces(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (c != ' ') return false;
        return true;
    }

    /// <summary>True when a string is empty or all LOW-VALUES (NUL).</summary>
    private static bool IsLowValues(string? s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        foreach (char c in s) if (c != '\0') return false;
        return true;
    }

    /// <summary>True when a value is all SPACES or all LOW-VALUES (or empty) — the common COBOL guard.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// COBOL <c>field-N EQUAL ZEROS</c> on a numeric REDEFINES view of an X(n) char field: the numeric
    /// value of the (digit) characters is zero. Non-digit chars read as 0 in a zoned-decimal view, so a
    /// space/low-value field also tests equal to ZEROS here. source: COCRDLIC.cbl:1009,1044.
    /// </summary>
    private static bool IsZeroesNum(string? s) => ParseLong(s) == 0;

    /// <summary>
    /// COBOL class test <c>field IS NOT NUMERIC</c> on the X(width) field: every character of the fixed
    /// width must be a digit '0'-'9' (spaces/low-values fail). Returns true when the field IS numeric.
    /// source: COCRDLIC.cbl:1017,1052.
    /// </summary>
    private static bool IsNumericX(string? value, int width)
    {
        string v = PadX(value, width);
        foreach (char c in v) if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>Parses a digit string (ignoring non-digits) to a long; null/empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    // =============================================================================================
    //  BMS map builder — CCRDLIA in mapset COCRDLI (24x80).
    //  source: app/bms/COCRDLI.bms:25-340 / SCREEN_COCRDLI.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COCRDLI.bms:25.</summary>
    public const string MapName = ThisMap;   // "CCRDLIA"

    /// <summary>The DFHMSD mapset name. source: COCRDLI.bms:20.</summary>
    public const string MapsetName = ThisMapSet; // "COCRDLI"

    /// <summary>
    /// Constructs the <c>CCRDLIA</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the IC cursor on <c>ACCTSID</c>, the
    /// protected literals and zero-length stoppers, and the named in/out fields — in BMS source order. The
    /// two keyable filter fields are <c>ACCTSID</c> (6,44) L11 and <c>CARDSID</c> (7,44) L16; the seven
    /// per-row selection fields <c>CRDSEL1..7</c> and the row data fields are defined PROT in the map (the
    /// handler toggles them via attribute overrides). No PICIN/PICOUT clauses appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                              // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                 // bms:34-37
            Out("TITLE01", 1, 21, 40, Askip, BmsColor.Yellow),                 // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                             // bms:42-46
            OutInit("CURDATE", 1, 71, 8, Askip, BmsColor.Blue, "mm/dd/yy"),    // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                              // bms:52-56
            Out("PGMNAME", 2, 7, 8, Askip, BmsColor.Blue),                     // bms:57-60
            Out("TITLE02", 2, 21, 40, Askip, BmsColor.Yellow),                 // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                             // bms:65-69
            OutInit("CURTIME", 2, 71, 8, Askip, BmsColor.Blue, "hh:mm:ss"),    // bms:70-74

            // ----- 'List Credit Cards' heading + Page nbr (default ATTRB) -----
            LitAttr(4, 31, 17, AskipDefault, BmsColor.Neutral, "List Credit Cards"), // bms:75-78
            LitAttr(4, 70, 5, AskipDefault, BmsColor.Default, "Page "),        // bms:79-81
            OutAttr("PAGENO", 4, 76, 3, AskipDefault, BmsColor.Default),       // bms:82-83

            // ----- Account / Card filter labels + input fields -----
            Lit(6, 22, 19, BmsColor.Turquoise, "Account Number    :"),         // bms:84-88
            // ACCTSID: ATTRB=(FSET,IC,NORM,UNPROT) GREEN UNDERLINE — the IC (initial cursor) field.
            new ScreenField
            {
                Name = "ACCTSID", Row = 6, Col = 44, Length = 11,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:89-93
            Stopper(6, 56),                                                     // bms:94-95
            Lit(7, 22, 19, BmsColor.Turquoise, "Credit Card Number:"),         // bms:96-100
            // CARDSID: ATTRB=(FSET,NORM,UNPROT) GREEN UNDERLINE.
            new ScreenField
            {
                Name = "CARDSID", Row = 7, Col = 44, Length = 16,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:101-105
            Stopper(7, 61),                                                     // bms:106-107

            // ----- grid column headers (NEUTRAL, default ATTRB) -----
            LitAttr(9, 10, 10, AskipDefault, BmsColor.Neutral, "Select    "),  // bms:108-111
            LitAttr(9, 21, 14, AskipDefault, BmsColor.Neutral, "Account Number"), // bms:112-115
            LitAttr(9, 45, 13, AskipDefault, BmsColor.Neutral, " Card Number "),// bms:116-119
            LitAttr(9, 66, 7, AskipDefault, BmsColor.Neutral, "Active "),       // bms:120-123
            LitAttr(10, 10, 6, AskipDefault, BmsColor.Neutral, "------"),       // bms:124-127
            LitAttr(10, 20, 15, AskipDefault, BmsColor.Neutral, "---------------"), // bms:128-131
            LitAttr(10, 43, 15, AskipDefault, BmsColor.Neutral, "---------------"), // bms:132-135
            LitAttr(10, 65, 8, AskipDefault, BmsColor.Neutral, "--------"),     // bms:136-139

            // ----- Row 1 -----
            RowSel("CRDSEL1", 11),                                              // bms:140-144
            Stopper(11, 14),                                                    // bms:145-146
            RowData("ACCTNO1", 11, 22, 11),                                     // bms:147-151
            RowData("CRDNUM1", 11, 43, 16),                                     // bms:152-156
            RowData("CRDSTS1", 11, 67, 1),                                      // bms:157-161

            // ----- Row 2 (with hidden CRDSTP2 shadow) -----
            RowSel("CRDSEL2", 12),                                              // bms:162-166
            Stopper(12, 14),                                                    // bms:167-168
            RowDark("CRDSTP2", 12, 14),                                         // bms:169-173
            RowData("ACCTNO2", 12, 22, 11),                                     // bms:174-178
            RowData("CRDNUM2", 12, 43, 16),                                     // bms:179-183
            RowData("CRDSTS2", 12, 67, 1),                                      // bms:184-188

            // ----- Row 3 -----
            RowSel("CRDSEL3", 13),                                              // bms:189-193
            Stopper(13, 14),                                                    // bms:194-195
            RowDark("CRDSTP3", 13, 14),                                         // bms:196-200
            RowData("ACCTNO3", 13, 22, 11),                                     // bms:201-205
            RowData("CRDNUM3", 13, 43, 16),                                     // bms:206-210
            RowData("CRDSTS3", 13, 67, 1),                                      // bms:211-215

            // ----- Row 4 -----
            RowSel("CRDSEL4", 14),                                              // bms:216-220
            Stopper(14, 14),                                                    // bms:221-222
            RowDark("CRDSTP4", 14, 14),                                         // bms:223-227
            RowData("ACCTNO4", 14, 22, 11),                                     // bms:228-232
            RowData("CRDNUM4", 14, 43, 16),                                     // bms:233-237
            RowData("CRDSTS4", 14, 67, 1),                                      // bms:238-242

            // ----- Row 5 -----
            RowSel("CRDSEL5", 15),                                              // bms:243-247
            Stopper(15, 14),                                                    // bms:248-249
            RowDark("CRDSTP5", 15, 14),                                         // bms:250-254
            RowData("ACCTNO5", 15, 22, 11),                                     // bms:255-259
            RowData("CRDNUM5", 15, 43, 16),                                     // bms:260-264
            RowData("CRDSTS5", 15, 67, 1),                                      // bms:265-269

            // ----- Row 6 -----
            RowSel("CRDSEL6", 16),                                              // bms:270-274
            Stopper(16, 14),                                                    // bms:275-276
            RowDark("CRDSTP6", 16, 14),                                         // bms:277-281
            RowData("ACCTNO6", 16, 22, 11),                                     // bms:282-286
            RowData("CRDNUM6", 16, 43, 16),                                     // bms:287-291
            RowData("CRDSTS6", 16, 67, 1),                                      // bms:292-296

            // ----- Row 7 -----
            RowSel("CRDSEL7", 17),                                              // bms:297-301
            Stopper(17, 14),                                                    // bms:302-303
            RowDark("CRDSTP7", 17, 14),                                         // bms:304-308
            RowData("ACCTNO7", 17, 22, 11),                                     // bms:309-313
            RowData("CRDNUM7", 17, 43, 16),                                     // bms:314-318
            RowData("CRDSTS7", 17, 67, 1),                                      // bms:319-323

            // ----- info line (PROT NEUTRAL, HILIGHT=OFF) -----
            new ScreenField
            {
                Name = "INFOMSG", Row = 20, Col = 19, Length = 45,
                Attribute = BmsAttribute.Protected,
                Color = BmsColor.Neutral,
                Hilight = BmsHilight.Off,
            },                                                                  // bms:324-328
            Stopper(20, 65),                                                    // bms:329-330

            // ----- error line (ASKIP,BRT,FSET RED, L78) -----
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:331-334

            // ----- footer literal (ASKIP,NORM TURQUOISE, L78) -----
            Lit(24, 1, 78, BmsColor.Turquoise, "  F3=Exit F7=Backward  F8=Forward"), // bms:335-339
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;          // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipDefault => BmsAttribute.AutoSkip;                          // default ATTRB omitted -> ASKIP
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)
    private static BmsAttribute FsetNormProt => BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected; // (FSET,NORM,PROT)
    private static BmsAttribute NormProt => BmsAttribute.Normal | BmsAttribute.Protected;       // (NORM,PROT)
    private static BmsAttribute AskipDrkFset => BmsAttribute.AutoSkip | BmsAttribute.Dark | BmsAttribute.Fset; // (ASKIP,DRK,FSET)

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

    /// <summary>Named output field with an explicit (e.g. default-ASKIP) attribute.</summary>
    private static ScreenField OutAttr(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>Per-row selection field CRDSELn: ATTRB=(FSET,NORM,PROT) DEFAULT HILIGHT=UNDERLINE L1.</summary>
    private static ScreenField RowSel(string name, int row) =>
        new()
        {
            Name = name, Row = row, Col = 12, Length = 1,
            Attribute = FsetNormProt, Color = BmsColor.Default, Hilight = BmsHilight.Underline,
        };

    /// <summary>Per-row data field (ACCTNOn/CRDNUMn/CRDSTSn): ATTRB=(NORM,PROT) DEFAULT HILIGHT=OFF.</summary>
    private static ScreenField RowData(string name, int row, int col, int len) =>
        new()
        {
            Name = name, Row = row, Col = col, Length = len,
            Attribute = NormProt, Color = BmsColor.Default, Hilight = BmsHilight.Off,
        };

    /// <summary>Per-row hidden shadow field CRDSTPn (rows 2-7): ATTRB=(ASKIP,DRK,FSET) DEFAULT HILIGHT=OFF L1.</summary>
    private static ScreenField RowDark(string name, int row, int col) =>
        new()
        {
            Name = name, Row = row, Col = col, Length = 1,
            Attribute = AskipDrkFset, Color = BmsColor.Default, Hilight = BmsHilight.Off,
        };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, default ATTRB omitted).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = AskipDefault, Color = BmsColor.Default };
}
