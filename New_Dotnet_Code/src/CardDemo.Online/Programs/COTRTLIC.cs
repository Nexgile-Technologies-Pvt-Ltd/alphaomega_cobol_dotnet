using System.Text;
using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the optional online CICS COBOL program <c>COTRTLIC</c> — the
/// "Maintain Transaction Type" paged maintain screen (TRANSID <c>CTLI</c>, BMS map <c>CTRTLIA</c> /
/// mapset <c>COTRTLI</c>) backed by the DB2 table <c>CARDDEMO.TRANSACTION_TYPE</c>.
/// </summary>
/// <remarks>
/// <para>
/// COTRTLIC lists rows of <c>CARDDEMO.TRANSACTION_TYPE</c> (columns <c>TR_TYPE CHAR(2)</c>,
/// <c>TR_DESCRIPTION VARCHAR(50)</c>), <b>7 rows per page</b> on a 24x80 BMS screen, with an optional
/// Type-Code filter (2-digit numeric, exact match) and a Description filter (substring <c>LIKE</c>). It
/// demonstrates Db2 cursor paging — a forward cursor (<c>C-TR-TYPE-FORWARD</c>: <c>TR_TYPE &gt;= :start
/// ORDER BY TR_TYPE</c>) and a backward cursor (<c>C-TR-TYPE-BACKWARD</c>: <c>TR_TYPE &lt; :start ORDER BY
/// TR_TYPE DESC</c>) — plus inline single-row UPDATE and DELETE driven by per-row action codes
/// (<c>U</c> = update that row's description, <c>D</c> = delete that row, each confirmed by F10). It is
/// pseudo-conversational: it re-drives itself via <c>RETURN TRANSID('CTLI')</c>.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COTRTLIC.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c>/<c>GO TO</c> control flow, the COMMAREA
/// usage (<see cref="CardDemoCommArea"/> prefix + the program-private <c>WS-THIS-PROGCOMMAREA</c> tail), and
/// every faithful bug are preserved verbatim.
/// </para>
/// <para><b>EXEC SQL → repository / SQL mapping.</b> The two cursors and the COUNT/UPDATE/DELETE are issued as
/// plain parameterized SQL against the shared <see cref="RelationalDb.Connection"/> (the
/// <see cref="TransactionTypeRepository"/> exposes the single-row CRUD; the ordered cursor scans are run as
/// direct readers because the COBOL fetches a forward-only, look-ahead cursor). <c>SQLCODE</c> is modeled as
/// an <see cref="int"/>: <c>0</c> = row/op OK, <c>+100</c> = no row / end-of-cursor, negative = error. The
/// <c>-532</c> (FK child rows) DELETE branch is reproduced by counting child rows in
/// <c>TRANSACTION_TYPE_CATEGORY</c>. <c>EXEC CICS SYNCPOINT</c> after a successful UPDATE/DELETE (and the
/// PF3 exit) maps to a commit on the connection. There is no money math.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix) — see the port spec §9:</b></para>
/// <list type="bullet">
/// <item>B-1 — <c>CA-DELETE-SUCCEEDED</c>/<c>CA-UPDATE-SUCCEEDED</c> share LOW-VALUES with the
/// "not-requested" 88s, so <c>IF CA-DELETE-SUCCEEDED</c> is true whenever the flag is low-values. Reproduced
/// via the literal low-values semantics (<see cref="_caDeleteFlag"/>/<see cref="_caUpdateFlag"/>). source:
/// :411-418,817,860</item>
/// <item>B-2 — Two identical <c>WHEN CCARD-AID-PFK07 AND CA-FIRST-PAGE</c> clauses; the first is bodyless and
/// falls through to the second. Both collapse to the same forward-read. source: :721-734</item>
/// <item>B-3 — <c>COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED))</c> is always 50
/// (the fixed PIC width), never the trimmed length; the description is stored trimmed but right-padded to 50.
/// source: :1841-1844</item>
/// <item>B-4 — <c>1210-EDIT-ARRAY</c> reads the <c>…FILTER-CHANGED</c> flags before <c>1220</c>/<c>1230</c>
/// set them (1210 runs first), so it keys off stale flag values. The call order (1210,1230,1220,1290) and the
/// stale read are reproduced. source: :965-975,991-994</item>
/// <item>B-5 — <c>8100-READ-BACKWARDS</c> has no <c>WHEN SQLCODE = +100</c>; backward end-of-cursor falls into
/// <c>WHEN OTHER</c> and is flagged as a hard Db2 error. Reproduced. source: :1761-1790</item>
/// <item>B-6 — <c>2400-SETUP-SCREEN-ATTRS</c> writes several attribute bytes into the <c>CTRTLIAI</c> (input)
/// symbolic map rather than <c>CTRTLIAO</c>; because they overlap (REDEFINES) the effect is achieved. The
/// resulting attribute outcome is preserved. source: :1448-1495</item>
/// <item>B-7 — <c>WS-TYPE-CD-DELETE-FILTER</c> (a quoted <c>IN(...)</c> list) is built but never used; not
/// implemented. source: :279-301</item>
/// <item>B-8 — The empty-first-page message ("No records found for this search condition.") differs from the
/// cross-edit COUNT=0 message ("No Records found for these filter conditions"); both distinct strings kept.
/// source: :253-254,1263-1264</item>
/// </list>
/// </remarks>
public sealed class Cotrtlic : ITransactionHandler
{
    // =============================================================================================
    //  Literals and Constants — source: COTRTLIC.cbl:42-60
    // =============================================================================================
    private const string LIT_THISPGM = "COTRTLIC";       // 05 LIT-THISPGM      X(8) 'COTRTLIC'. source: :43
    private const string LIT_THISTRANID = "CTLI";        // 05 LIT-THISTRANID   X(4) 'CTLI'.     source: :44
    private const string LIT_THISMAPSET = "COTRTLI";     // 05 LIT-THISMAPSET   X(7) 'COTRTLI'.  source: :45
    private const string LIT_THISMAP = "CTRTLIA";        // 05 LIT-THISMAP      X(7) 'CTRTLIA'.  source: :46
    private const string LIT_ADMINPGM = "COADM01C";      // 05 LIT-ADMINPGM     X(8) 'COADM01C'. source: :47
    private const string LIT_ADMINTRANID = "CA00";       // 05 LIT-ADMINTRANID  X(4) 'CA00'.     source: :48
    private const string LIT_ADMINMAPSET = "COADM01";    // 05 LIT-ADMINMAPSET  X(7) 'COADM01'.  source: :49
    private const string LIT_ADDTPGM = "COTRTUPC";       // 05 LIT-ADDTPGM      X(8) 'COTRTUPC'. source: :50
    private const string LIT_ADDTTRANID = "CTTU";        // 05 LIT-ADDTTRANID   X(4) 'CTTU'.     source: :51
    private const string LIT_ADDTMAPSET = "COTRTUP";     // 05 LIT-ADDTMAPSET   X(7) 'COTRTUP'.  source: :52
    private const string LIT_ADDTMAP = "CTRTUPA";        // 05 LIT-ADDTMAP      X(7) 'CTRTUPA'.  source: :53
    private const string LIT_ASTERISK = "*";             // 05 LIT-ASTERISK     X(7) '*'.        source: :55
    private const string LIT_DELETE_FLAG = "D";          // 05 LIT-DELETE-FLAG  X(1) 'D'.        source: :58
    private const string LIT_UPDATE_FLAG = "U";          // 05 LIT-UPDATE-FLAG  X(1) 'U'.        source: :59
    private const int WS_MAX_SCREEN_LINES = 7;           // 05 WS-MAX-SCREEN-LINES S9(4) COMP 7. source: :60

    // CCDA-TITLE01/02 (COTTL01Y) — shared screen header titles.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    // =============================================================================================
    //  Input edits flags — source: COTRTLIC.cbl:98-140
    // =============================================================================================
    // 05 WS-INPUT-FLAG X(1). 88 INPUT-OK={'0',' ',LOW-VALUES} / INPUT-ERROR='1'. source: :98-102
    private char _wsInputFlag = '\0';
    private bool InputOk => _wsInputFlag is '0' or ' ' or '\0';     // 88 INPUT-OK
    private bool InputError => _wsInputFlag == '1';                 // 88 INPUT-ERROR
    private void SetInputError() => _wsInputFlag = '1';             // SET INPUT-ERROR TO TRUE
    private void SetInputOk() => _wsInputFlag = '0';                // SET INPUT-OK TO TRUE

    // 05 WS-EDIT-TYPE-FLAG X(1). 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :103-106
    private char _wsEditTypeFlag = '\0';
    private bool FlgTypeFilterNotOk => _wsEditTypeFlag == '0';
    private bool FlgTypeFilterIsValid => _wsEditTypeFlag == '1';
    private void SetTypeFilterNotOk() => _wsEditTypeFlag = '0';
    private void SetTypeFilterIsValid() => _wsEditTypeFlag = '1';
    private void SetTypeFilterBlank() => _wsEditTypeFlag = ' ';

    // 05 WS-EDIT-DESC-FLAG X(1). 88 NOT-OK='0' / ISVALID='1' / BLANK=' '. source: :107-110
    private char _wsEditDescFlag = '\0';
    private bool FlgDescFilterNotOk => _wsEditDescFlag == '0';
    private bool FlgDescFilterIsValid => _wsEditDescFlag == '1';
    private void SetDescFilterNotOk() => _wsEditDescFlag = '0';
    private void SetDescFilterIsValid() => _wsEditDescFlag = '1';
    private void SetDescFilterBlank() => _wsEditDescFlag = ' ';

    // 05 WS-TYPEFILTER-CHANGED X(1). 88 NO=LOW-VALUES / YES='Y'. source: :111-113
    private char _wsTypeFilterChanged = '\0';
    private bool FlgTypeFilterChangedNo => _wsTypeFilterChanged == '\0';
    private void SetTypeFilterChangedNo() => _wsTypeFilterChanged = '\0';
    private void SetTypeFilterChangedYes() => _wsTypeFilterChanged = 'Y';

    // 05 WS-DESCFILTER-CHANGED X(1). 88 NO=LOW-VALUES / YES='Y'. source: :114-116
    private char _wsDescFilterChanged = '\0';
    private bool FlgDescFilterChangedNo => _wsDescFilterChanged == '\0';
    private void SetDescFilterChangedNo() => _wsDescFilterChanged = '\0';
    private void SetDescFilterChangedYes() => _wsDescFilterChanged = 'Y';

    // 05 WS-DELETE-STATUS X(1). 88 FLG-DELETED-NO=LOW-VALUES / FLG-DELETED-YES='Y'. source: :121-123
    private char _wsDeleteStatus = '\0';
    private bool FlgDeletedYes => _wsDeleteStatus == 'Y';
    private void SetDeletedYes() => _wsDeleteStatus = 'Y';
    private void SetDeletedNo() => _wsDeleteStatus = '\0';

    // 05 WS-UPDATE-STATUS X(1). 88 FLG-UPDATED-NO=LOW-VALUES / FLG-UPDATE-COMPLETED='Y'. source: :124-126
    private char _wsUpdateStatus = '\0';
    private bool FlgUpdateCompleted => _wsUpdateStatus == 'Y';
    private void SetUpdateCompleted() => _wsUpdateStatus = 'Y';

    // 05 WS-ROW-SELECTION-CHANGED X(1). 88 NO=LOW-VALUES / YES='Y'. source: :127-129
    private char _wsRowSelectionChanged = '\0';
    private bool FlgRowSelectionChangedNo => _wsRowSelectionChanged == '\0';
    private void SetRowSelectionChangedNo() => _wsRowSelectionChanged = '\0';
    private void SetRowSelectionChangedYes() => _wsRowSelectionChanged = 'Y';

    // 05 WS-BAD-SELECTION-ACTION X(1). 88 NO=LOW-VALUES / YES='Y'. source: :130-132
    private char _wsBadSelectionAction = '\0';
    private bool FlgBadActionsSelectedNo => _wsBadSelectionAction == '\0';
    private void SetBadActionsSelectedNo() => _wsBadSelectionAction = '\0';
    private void SetBadActionsSelectedYes() => _wsBadSelectionAction = 'Y';

    // 05 WS-ARRAY-DESCRIPTION-FLGS X(1). 88 ISVALID={LOW-VALUES,SPACES} / NOT-OK='0' / BLANK='B'. source: :133-137
    private char _wsArrayDescriptionFlgs = '\0';
    private bool FlgRowDescriptionIsValid => _wsArrayDescriptionFlgs is '\0' or ' ';
    private bool FlgRowDescriptionBlank => _wsArrayDescriptionFlgs == 'B';
    private void SetRowDescriptionNotOk() => _wsArrayDescriptionFlgs = '0';

    // 05 WS-DATACHANGED-FLAG X(1). 88 NO-CHANGES-FOUND='0' / CHANGES-HAVE-OCCURRED='1'. source: :138-140
    private char _wsDataChangedFlag = '\0';
    private bool ChangesHaveOccurred => _wsDataChangedFlag == '1';
    private void SetNoChangesFound() => _wsDataChangedFlag = '0';
    private void SetChangesHaveOccurred() => _wsDataChangedFlag = '1';

    // =============================================================================================
    //  Generic edit work area — source: COTRTLIC.cbl:143-152
    // =============================================================================================
    private string _wsEditVariableName = "";     // 10 WS-EDIT-VARIABLE-NAME X(25). source: :144
    private string _wsEditAlphanumOnly = "";      // 10 WS-EDIT-ALPHANUM-ONLY X(256). source: :146
    private int _wsEditAlphanumLength;            // 10 WS-EDIT-ALPHANUM-LENGTH S9(4) COMP-3. source: :147
    // 10 WS-EDIT-ALPHANUM-ONLY-FLAGS X(1). 88 ISVALID=LOW-VALUES / NOT-OK='0' / BLANK='B'. source: :149-152
    private char _wsEditAlphanumFlags = '\0';
    private void SetAlphanumNotOk() => _wsEditAlphanumFlags = '0';
    private void SetAlphanumBlank() => _wsEditAlphanumFlags = 'B';
    private void SetAlphanumIsValid() => _wsEditAlphanumFlags = '\0';

    private int _wsRecordsCount;                  // 10 WS-RECORDS-COUNT S9(4) COMP-3. source: :155

    // =============================================================================================
    //  Inbound page buffer (WS-SCREEN-DATA-IN) — source: COTRTLIC.cbl:165-172
    //  WS-ROW-TR-CODE-IN(7) X2 + WS-ROW-TR-DESC-IN(7) X50. Index 1..7 -> array slot 0..6.
    // =============================================================================================
    private readonly string[] _rowTrCodeIn = NewStr(7, "");  // WS-ROW-TR-CODE-IN(I) X(2). source: :171
    private readonly string[] _rowTrDescIn = NewStr(7, "");  // WS-ROW-TR-DESC-IN(I) X(50). source: :172 (LOW-VALUES sentinel = "")

    // 05 WS-EDIT-SELECT-FLAGS X(7) init LOW-VALUES (redef WS-EDIT-SELECT OCCURS 7). source: :178-188
    // Each char: 'D'/'U' (SELECT-OK), ' '/LOW-VALUES (SELECT-BLANK), other = invalid.
    private char[] _editSelect = NewChars(7, '\0');
    private bool SelectOk(int i) => _editSelect[i - 1] is 'D' or 'U';            // 88 SELECT-OK
    private bool DeleteRequestedOn(int i) => _editSelect[i - 1] == 'D';          // 88 DELETE-REQUESTED-ON
    private bool UpdateRequestedOn(int i) => _editSelect[i - 1] == 'U';          // 88 UPDATE-REQUESTED-ON
    private bool SelectBlank(int i) => _editSelect[i - 1] is ' ' or '\0';        // 88 SELECT-BLANK
    private void SetSelectBlank(int i) => _editSelect[i - 1] = '\0';             // SET SELECT-BLANK(I) TO TRUE (LOW-VALUES)

    // 05 WS-EDIT-SELECT-ERROR-FLAGS X(7) init LOW-VALUES. WS-ROW-TRTSELECT-ERROR(I) '1'=error. source: :190-195
    private char[] _editSelectError = NewChars(7, '\0');
    private bool RowSelectError(int i) => _editSelectError[i - 1] == '1';
    private void SetRowSelectError(int i) => _editSelectError[i - 1] = '1';

    // 05 WS-SUBSCRIPT-VARS — I-SELECTED S9(4) COMP. source: :197-201
    private int _iSelected;

    // 05 WS-ACTIONS-SELECTED (all S9(4) COMP-3). source: :202-220
    private int _wsActionsRequested;  // 88 WS-ONLY-1-ACTION=1 / WS-MORETHAN1ACTION=2..7
    private int _wsDeletesRequested;
    private int _wsUpdatesRequested;
    private int _wsNoActionsSelected;
    private int _wsValidActionsSelected; // 88 WS-ONLY-1-VALID-ACTION=1
    private bool WsOnly1Action => _wsActionsRequested == 1;
    private bool WsMoreThan1Action => _wsActionsRequested is >= 2 and <= 7;
    private bool WsOnly1ValidAction => _wsValidActionsSelected == 1;

    // =============================================================================================
    //  Output edits — source: COTRTLIC.cbl:225-231
    // =============================================================================================
    // 10 FLG-PROTECT-SELECT-ROWS X(1). 88 NO='0' / YES='1'. source: :229-231
    private char _flgProtectSelectRows = '0';
    private bool FlgProtectSelectRowsYes => _flgProtectSelectRows == '1';
    private void SetProtectSelectRowsNo() => _flgProtectSelectRows = '0';
    private void SetProtectSelectRowsYes() => _flgProtectSelectRows = '1';

    // =============================================================================================
    //  Output message construction — source: COTRTLIC.cbl:235-265
    // =============================================================================================
    // 05 WS-INFO-MSG X(45). 88 literals below. WS-NO-INFO-MESSAGE = {SPACES, LOW-VALUES}. source: :236-248
    private string _wsInfoMsg = "";
    private const string INFO_REC_ACTIONS = "Type U to update, D to delete any record";        // :239-240
    private const string INFO_DELETE = "Delete HIGHLIGHTED row ? Press F10 to confirm";          // :241-242
    private const string INFO_UPDATE = "Update HIGHLIGHTED row. Press F10 to save";              // :243-244
    private const string INFO_DELETE_SUCCESS = "HIGHLIGHTED row deleted.Hit Enter to continue";  // :245-246 (no space after '.')
    private const string INFO_UPDATE_SUCCESS = "HIGHLIGHTED row was updated";                    // :247-248
    private bool WsNoInfoMessage => string.IsNullOrEmpty(_wsInfoMsg) || _wsInfoMsg.All(c => c is ' ' or '\0'); // 88 WS-NO-INFO-MESSAGE
    private void SetNoInfoMessage() => _wsInfoMsg = "";

    // 05 WS-RETURN-MSG X(75). 88 literals below. WS-RETURN-MSG-OFF = SPACES. source: :249-262
    private string _wsReturnMsg = "";
    private const string MESG_NO_RECORDS_FOUND = "No records found for this search condition.";  // :253-254
    private const string MESG_NO_MORE_RECORDS = "No more pages for these search conditions";      // :255-256
    private const string MESG_MORE_THAN_1_ACTION = "Please select only 1 action";                // :257-258
    private const string MESG_INVALID_ACTION_CODE = "Action code selected is invalid";           // :259-260
    private const string MESG_NO_CHANGES_DETECTED = "No change detected with respect to database values."; // :261-262
    private const string EXIT_MESSAGE = "PF03 pressed. Exiting";                                  // :251-252
    private bool WsReturnMsgOff => string.IsNullOrEmpty(_wsReturnMsg) || _wsReturnMsg.All(c => c == ' ');
    private void SetReturnMsgOff() => _wsReturnMsg = "";
    // Convenience 88-comparisons used by 2500-SETUP-MESSAGE.
    private bool WsMesgNoRecordsFound => _wsReturnMsg == MESG_NO_RECORDS_FOUND;

    // 05 WS-PFK-FLAG X(1). 88 PFK-VALID='0' / PFK-INVALID='1'. source: :263-265
    private char _wsPfkFlag = '0';
    private bool PfkInvalid => _wsPfkFlag == '1';
    private void SetPfkValid() => _wsPfkFlag = '0';
    private void SetPfkInvalid() => _wsPfkFlag = '1';

    // 05 WS-STRING-FORMAT-VARS — message centering. source: :266-269
    private int _wsStringMid;
    private int _wsStringLen;
    private string _wsStringOut = "";

    // =============================================================================================
    //  Data handling — WS-DATA-FILTERS. source: COTRTLIC.cbl:274-301
    // =============================================================================================
    private string _wsStartKey = "";       // 10 WS-START-KEY X(2). source: :275 ("" = LOW-VALUES/spaces -> first row)
    private string _wsTypeCdFilter = "";   // 10 WS-TYPE-CD-FILTER X(2) VALUE SPACES. source: :276
    private string _wsTypeDescFilter = ""; // 10 WS-TYPE-DESC-FILTER X(52). source: :278

    // =============================================================================================
    //  Screen edit vars — WS-SCREEN-EDIT-VARS. source: COTRTLIC.cbl:309-313
    // =============================================================================================
    private string _wsInTypeCd = "";       // 10 WS-IN-TYPE-CD X(2) VALUE SPACES. source: :310
    private string _wsInTypeDesc = "";     // 10 WS-IN-TYPE-DESC X(50). source: :313

    // =============================================================================================
    //  Screen array vars. source: COTRTLIC.cbl:318-322
    // =============================================================================================
    private int _wsRowNumber;              // 05 WS-ROW-NUMBER S9(4) COMP. source: :318
    // 05 WS-RECORDS-TO-PROCESS-FLAG X(1). 88 READ-LOOP-EXIT='0' / MORE-RECORDS-TO-READ='1'. source: :320-322
    private char _wsRecordsToProcessFlag = '\0';
    private bool ReadLoopExit => _wsRecordsToProcessFlag == '0';
    private void SetReadLoopExit() => _wsRecordsToProcessFlag = '0';
    private void SetMoreRecordsToRead() => _wsRecordsToProcessFlag = '1';

    // =============================================================================================
    //  Db2 common WS (CSDB2RWY). source: COTRTLIC.cbl/CSDB2RWY
    // =============================================================================================
    private int _sqlcode;                  // SQLCODE
    private string _wsDispSqlcode = "";    // WS-DISP-SQLCODE PIC ----9 (edited)
    private string _wsDb2CurrentAction = ""; // WS-DB2-CURRENT-ACTION X(72)
    private string _wsLongMsg = "";        // WS-LONG-MSG X(800)
    // WS-DB2-PROCESSING-FLAG. 88 WS-DB2-OK='0' / WS-DB2-ERROR='1'.
    private char _wsDb2ProcessingFlag = '0';
    private bool WsDb2Error => _wsDb2ProcessingFlag == '1';
    private void SetWsDb2Error() => _wsDb2ProcessingFlag = '1';

    // =============================================================================================
    //  DCLTRANSACTION-TYPE host variables (DCLTRTYP.dcl). source: COTRTLIC.cbl:333
    // =============================================================================================
    private string _dclTrType = "";          // DCL-TR-TYPE X(2)
    private string _dclTrDescriptionText = "";// DCL-TR-DESCRIPTION-TEXT X(50)
    private int _dclTrDescriptionLen;         // DCL-TR-DESCRIPTION-LEN S9(4) COMP

    // =============================================================================================
    //  WS-THIS-PROGCOMMAREA — program-private commarea tail. source: COTRTLIC.cbl:377-418
    // =============================================================================================
    private string _caTypeCd = "";   // 10 WS-CA-TYPE-CD X(2) VALUE SPACES. source: :378
    private string _caTypeDesc = ""; // 10 WS-CA-TYPE-DESC X(50). source: :381

    // 15 WS-CA-SCREEN-ROWS-OUT OCCURS 7: WS-CA-ROW-TR-CODE-OUT X2 + WS-CA-ROW-TR-DESC-OUT X50. source: :389-392
    // Slot is LOW-VALUES (the COBOL "row present?" sentinel) when null; populated rows hold the code/desc.
    private string?[] _caRowTrCodeOut = new string?[7];  // null = LOW-VALUES slot
    private string?[] _caRowTrDescOut = new string?[7];

    private int _caRowSelected;      // 10 WS-CA-ROW-SELECTED S9(4) COMP. source: :395

    // WS-CA-PAGING-VARIABLES. source: :397-410
    private string _caLastTrCode = "";   // 20 WS-CA-LAST-TR-CODE X(2). source: :399 (= WS-CA-LAST-TTYPEKEY)
    private string _caFirstTrCode = "";  // 20 WS-CA-FIRST-TR-CODE X(2). source: :401 (= WS-CA-FIRST-TTYPEKEY)
    private int _caScreenNum;            // 15 WS-CA-SCREEN-NUM 9(1). 88 CA-FIRST-PAGE=1. source: :403
    private bool CaFirstPage => _caScreenNum == 1;
    private void SetCaFirstPage() => _caScreenNum = 1;
    private int _caLastPageDisplayed;   // 15 WS-CA-LAST-PAGE-DISPLAYED 9(1). 88 SHOWN=0 / NOT-SHOWN=9. source: :405
    private bool CaLastPageShown => _caLastPageDisplayed == 0;
    private void SetCaLastPageShown() => _caLastPageDisplayed = 0;
    private void SetCaLastPageNotShown() => _caLastPageDisplayed = 9;
    // 15 WS-CA-NEXT-PAGE-IND X(1). 88 NOT-EXISTS=LOW-VALUES / EXISTS='Y'. source: :408-410
    private char _caNextPageInd = '\0';
    private bool CaNextPageExists => _caNextPageInd == 'Y';
    private bool CaNextPageNotExists => _caNextPageInd == '\0';
    private void SetCaNextPageExists() => _caNextPageInd = 'Y';
    private void SetCaNextPageNotExists() => _caNextPageInd = '\0';

    // 10 WS-CA-DELETE-FLAG X. 88 NOT-REQUESTED=LOW-VALUES / REQUESTED='Y' / SUCCEEDED=LOW-VALUES. source: :411-414
    // B-1: SUCCEEDED and NOT-REQUESTED share the LOW-VALUES ('\0') value.
    private char _caDeleteFlag = '\0';
    private bool CaDeleteRequested => _caDeleteFlag == 'Y';
    private bool CaDeleteSucceeded => _caDeleteFlag == '\0';  // 88 CA-DELETE-SUCCEEDED VALUE LOW-VALUES (B-1)
    private void SetCaDeleteRequested() => _caDeleteFlag = 'Y';
    private void SetCaDeleteSucceeded() => _caDeleteFlag = '\0';

    // 10 WS-CA-UPDATE-FLAG X. 88 NOT-REQUESTED=LOW-VALUES / REQUESTED='Y' / SUCCEEDED=LOW-VALUES. source: :415-418
    private char _caUpdateFlag = '\0';
    private bool CaUpdateRequested => _caUpdateFlag == 'Y';
    private bool CaUpdateSucceeded => _caUpdateFlag == '\0';  // 88 CA-UPDATE-SUCCEEDED VALUE LOW-VALUES (B-1)
    private void SetCaUpdateRequested() => _caUpdateFlag = 'Y';
    private void SetCaUpdateSucceeded() => _caUpdateFlag = '\0';

    // =============================================================================================
    //  CC-WORK-AREA (CVCRD01Y) — AID flags + nav fields. source: CVCRD01Y
    // =============================================================================================
    private CcardAid _ccardAid = CcardAid.None;
    private bool CcardAidEnter => _ccardAid == CcardAid.Enter;
    private bool CcardAidPfk02 => _ccardAid == CcardAid.Pfk02;
    private bool CcardAidPfk03 => _ccardAid == CcardAid.Pfk03;
    private bool CcardAidPfk07 => _ccardAid == CcardAid.Pfk07;
    private bool CcardAidPfk08 => _ccardAid == CcardAid.Pfk08;
    private bool CcardAidPfk10 => _ccardAid == CcardAid.Pfk10;
    private void SetCcardAidEnter() => _ccardAid = CcardAid.Enter;

    private string _ccardNextProg = "";    // CCARD-NEXT-PROG X(8)
    private string _ccardNextMapset = "";  // CCARD-NEXT-MAPSET X(7)
    private string _ccardNextMap = "";     // CCARD-NEXT-MAP X(7)
    private string _ccardErrorMsg = "";    // CCARD-ERROR-MSG X(75)

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: COTRTLIC.cbl:493-495,498-502
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private TransactionTypeRepository _types = null!;

    /// <summary>Factory-friendly constructor: takes the shared relational DB (the optional-module
    /// TRANSACTION_TYPE table lives there). Repositories are created inside <see cref="Handle"/>.</summary>
    public Cotrtlic(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cotrtlic() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => LIT_THISPGM; // PROGRAM-ID. COTRTLIC. source: :26

    /// <inheritdoc/>
    public string TransId => LIT_THISTRANID;  // CSD: CTLI -> COTRTLIC. source: :44

    // =============================================================================================
    //  0000-MAIN — entry / dispatcher. source: COTRTLIC.cbl:498-915
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COTRTLI re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _types = new TransactionTypeRepository(_db.Connection);

        // INITIALIZE CC-WORK-AREA, WS-MISC-STORAGE, WS-COMMAREA. source: :500-502 (fresh instance already zeroed)
        // MOVE LIT-THISTRANID TO WS-TRANID. source: :507
        // SET WS-RETURN-MSG-OFF TO TRUE. source: :511
        SetReturnMsgOff();

        // Retrieve passed data if any. Initialize them if first run. source: :515-532
        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> INITIALIZE both commareas; set self/admin/enter/first-page. source: :515-525
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            InitThisProgCommarea();
            _commArea.FromTranId = LIT_THISTRANID;   // MOVE LIT-THISTRANID TO CDEMO-FROM-TRANID. source: :518
            _commArea.FromProgram = LIT_THISPGM;     // MOVE LIT-THISPGM TO CDEMO-FROM-PROGRAM. source: :519
            _commArea.SetAdmin();                    // SET CDEMO-USRTYP-ADMIN TO TRUE. source: :520
            _commArea.SetFirstEntry();               // SET CDEMO-PGM-ENTER TO TRUE. source: :521
            _commArea.LastMap = LIT_THISMAP;         // MOVE LIT-THISMAP TO CDEMO-LAST-MAP. source: :522
            _commArea.LastMapSet = LIT_THISMAPSET;   // MOVE LIT-THISMAPSET TO CDEMO-LAST-MAPSET. source: :523
            SetCaFirstPage();                        // SET CA-FIRST-PAGE TO TRUE. source: :524
            SetCaLastPageNotShown();                 // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :525
        }
        else
        {
            // ELSE split DFHCOMMAREA into CARDDEMO-COMMAREA (prefix) + WS-THIS-PROGCOMMAREA (tail). source: :526-531
            _commArea = ctx.CommArea!;
            RestoreThisProgCommarea();
        }

        // PERFORM YYYY-STORE-PFKEY — map EIBAID -> CCARD-AID-* 88. source: :538-539
        _ccardAid = CssTrpfy.StorePfKey(ctx.EibAid);

        // Fresh-start guard: coming from menu OR PF3 from the add screen -> start afresh. source: :544-554
        if ((_commArea.IsFirstEntry && _commArea.FromProgram.TrimEnd() != LIT_THISPGM)
            || (CcardAidPfk03 && _commArea.FromTranId.TrimEnd() == LIT_ADDTTRANID))
        {
            InitThisProgCommarea();          // INITIALIZE WS-THIS-PROGCOMMAREA. source: :548
            _commArea.SetFirstEntry();       // SET CDEMO-PGM-ENTER TO TRUE. source: :549
            SetCcardAidEnter();              // SET CCARD-AID-ENTER TO TRUE. source: :550
            _commArea.LastMap = LIT_THISMAP; // MOVE LIT-THISMAP TO CDEMO-LAST-MAP. source: :551
            SetCaFirstPage();                // SET CA-FIRST-PAGE TO TRUE. source: :552
            SetCaLastPageNotShown();         // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :553
        }

        // Re-entry receive: commarea present AND from-program = self -> receive + edit. source: :561-566
        if (ctx.EibCalen > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            ReceiveMap1000(ctx); // PERFORM 1000-RECEIVE-MAP. source: :563
        }

        // PFKey validity gate. source: :574-587
        SetPfkInvalid();
        if (CcardAidEnter || CcardAidPfk02 || CcardAidPfk03 || CcardAidPfk07 || CcardAidPfk08
            || (CcardAidPfk10 && CaDeleteRequested) || (CcardAidPfk10 && CaUpdateRequested))
        {
            SetPfkValid();
        }
        if (PfkInvalid)
            SetCcardAidEnter(); // SET CCARD-AID-ENTER TO TRUE. source: :586

        // PF3 exit -> XCTL back to admin menu / caller. source: :591-625
        if (CcardAidPfk03)
        {
            // CDEMO-TO-TRANID. source: :592-598
            if (IsBlankTranId(_commArea.FromTranId) || _commArea.FromTranId.TrimEnd() == LIT_THISTRANID)
                _commArea.ToTranId = LIT_ADMINTRANID;
            else
                _commArea.ToTranId = _commArea.FromTranId;

            // CDEMO-TO-PROGRAM. source: :600-606
            if (IsBlankProgram(_commArea.FromProgram) || _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
                _commArea.ToProgram = LIT_ADMINPGM;
            else
                _commArea.ToProgram = _commArea.FromProgram;

            _commArea.FromTranId = LIT_THISTRANID;   // source: :608
            _commArea.FromProgram = LIT_THISPGM;     // source: :609
            _commArea.SetAdmin();                    // source: :611
            _commArea.SetFirstEntry();               // source: :612
            _commArea.LastMapSet = LIT_THISMAPSET;   // source: :613
            _commArea.LastMap = LIT_THISMAP;         // source: :614

            Syncpoint();                             // EXEC CICS SYNCPOINT. source: :616-618
            SaveThisProgCommarea();
            ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea); // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM). source: :620-623
            return;
        }

        // PF2 Add -> XCTL to COTRTUPC. source: :630-652
        if (CcardAidPfk02 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            _commArea.FromTranId = LIT_THISTRANID;   // source: :632
            _commArea.FromProgram = LIT_THISPGM;     // source: :633
            _commArea.SetUser();                     // SET CDEMO-USRTYP-USER. source: :634
            _commArea.SetFirstEntry();               // SET CDEMO-PGM-ENTER. source: :635
            _commArea.LastMapSet = LIT_THISMAPSET;   // source: :636
            _commArea.LastMap = LIT_THISMAP;         // source: :637
            _commArea.ToProgram = LIT_ADDTPGM;       // MOVE LIT-ADDTPGM TO CDEMO-TO-PROGRAM. source: :638
            _ccardNextMapset = LIT_ADDTMAPSET;       // source: :640
            _ccardNextMap = LIT_ADDTMAP;             // source: :641
            _wsReturnMsg = EXIT_MESSAGE;             // SET WS-EXIT-MESSAGE TO TRUE. source: :642
            _commArea.SetFirstEntry();               // SET CDEMO-PGM-ENTER. source: :646
            SaveThisProgCommarea();
            ctx.Xctl(LIT_ADDTPGM, _commArea);        // EXEC CICS XCTL PROGRAM(LIT-ADDTPGM). source: :648-651
            return;
        }

        // If the user did not press PF8, reset the last page flag. source: :657-661
        if (CcardAidPfk08)
        {
            // CONTINUE. source: :658
        }
        else
        {
            SetCaLastPageNotShown(); // SET CA-LAST-PAGE-NOT-SHOWN TO TRUE. source: :660
        }

        // F10-changed-criteria demotion: keep PFK10 only when nothing changed; else treat as ENTER. source: :666-678
        if (CcardAidPfk10)
        {
            if ((CaDeleteRequested || CaUpdateRequested)
                && FlgTypeFilterChangedNo && FlgDescFilterChangedNo && FlgRowSelectionChangedNo)
            {
                // CONTINUE. source: :672
            }
            else
            {
                SetCcardAidEnter(); // SET CCARD-AID-ENTER TO TRUE. source: :674
            }
        }

        // 9998-PRIMING-QUERY — Db2 connectivity probe. source: :684-691
        PrimingQuery9998();
        if (WsDb2Error)
        {
            SendLongText(ctx);  // PERFORM SEND-LONG-TEXT. source: :688
            CommonReturn(ctx);  // GO TO COMMON-RETURN. source: :690
            return;
        }

        // Main EVALUATE TRUE dispatch (first matching WHEN). source: :698-879
        if (InputError)
        {
            // WHEN INPUT-ERROR. source: :699-720
            _ccardErrorMsg = _wsReturnMsg;            // MOVE WS-RETURN-MSG TO CCARD-ERROR-MSG. source: :703
            _commArea.FromProgram = LIT_THISPGM;      // source: :704
            _commArea.LastMapSet = LIT_THISMAPSET;    // source: :705
            _commArea.LastMap = LIT_THISMAP;          // source: :706
            _ccardNextProg = LIT_THISPGM;             // source: :708
            _ccardNextMapset = LIT_THISMAPSET;        // source: :709
            _ccardNextMap = LIT_THISMAP;              // source: :710
            _wsStartKey = _caFirstTrCode;             // MOVE WS-CA-FIRST-TR-CODE TO WS-START-KEY. source: :711
            if (!FlgTypeFilterNotOk && !FlgDescFilterNotOk)
                ReadForward8000();                    // PERFORM 8000-READ-FORWARD. source: :715
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :718
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :720
            return;
        }
        if (CcardAidPfk07 && CaFirstPage)
        {
            // B-2: two identical WHEN PFK07 AND CA-FIRST-PAGE; first is bodyless, falls into second. source: :721-734
            _wsStartKey = _caFirstTrCode;             // MOVE WS-CA-FIRST-TR-CODE TO WS-START-KEY. source: :728
            ReadForward8000();                        // PERFORM 8000-READ-FORWARD. source: :730
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :732
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :734
            return;
        }
        if (CcardAidPfk03
            || (_commArea.IsReenter && _commArea.FromProgram.TrimEnd() != LIT_THISPGM))
        {
            // WHEN CCARD-AID-PFK03 (dead) / WHEN CDEMO-PGM-REENTER AND from-program <> self. source: :738-762
            InitCarddemoCommareaFully();
            InitThisProgCommarea();
            InitWsMiscStorage();                      // INITIALIZE WS-MISC-STORAGE. source: :744
            _commArea.FromTranId = LIT_THISTRANID;    // source: :746
            _commArea.FromProgram = LIT_THISPGM;      // source: :747
            _commArea.LastMap = LIT_THISMAP;          // source: :748
            _commArea.LastMapSet = LIT_THISMAPSET;    // source: :749
            _commArea.SetAdmin();                     // source: :751
            _commArea.SetFirstEntry();                // source: :752
            SetCaFirstPage();                         // source: :753
            SetCaLastPageNotShown();                  // source: :754
            _wsStartKey = _caFirstTrCode;             // MOVE WS-CA-FIRST-TR-CODE TO WS-START-KEY. source: :756
            ReadForward8000();                        // PERFORM 8000-READ-FORWARD. source: :758
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :760
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :762
            return;
        }
        if (CcardAidPfk08 && CaNextPageExists)
        {
            // WHEN CCARD-AID-PFK08 AND CA-NEXT-PAGE-EXISTS (page down). source: :766-776
            _wsStartKey = _caLastTrCode;              // MOVE WS-CA-LAST-TR-CODE TO WS-START-KEY. source: :768
            _caScreenNum += 1;                        // ADD +1 TO WS-CA-SCREEN-NUM. source: :770
            ReadForward8000();                        // PERFORM 8000-READ-FORWARD. source: :771
            InitEditSelectFlags();                    // INITIALIZE WS-EDIT-SELECT-FLAGS. source: :773
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :774
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :776
            return;
        }
        if (CcardAidPfk07 && !CaFirstPage)
        {
            // WHEN CCARD-AID-PFK07 AND NOT CA-FIRST-PAGE (page up). source: :780-790
            _wsStartKey = _caFirstTrCode;             // MOVE WS-CA-FIRST-TR-CODE TO WS-START-KEY. source: :782
            _caScreenNum -= 1;                        // SUBTRACT 1 FROM WS-CA-SCREEN-NUM. source: :784
            ReadBackwards8100();                      // PERFORM 8100-READ-BACKWARDS. source: :785
            InitEditSelectFlags();                    // INITIALIZE WS-EDIT-SELECT-FLAGS. source: :787
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :788
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :790
            return;
        }
        if (CcardAidEnter && _wsDeletesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            // WHEN ENTER AND WS-DELETES-REQUESTED > 0 AND from-program = self (arm delete confirm). source: :794-806
            _wsStartKey = _caFirstTrCode;             // source: :797
            if (!FlgTypeFilterNotOk && !FlgDescFilterNotOk)
                ReadForward8000();                    // PERFORM 8000-READ-FORWARD. source: :801
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :804
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :806
            return;
        }
        if (CcardAidPfk10 && _wsDeletesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            // WHEN PFK10 AND WS-DELETES-REQUESTED > 0 AND from-program = self (confirm delete). source: :810-834
            DeleteRecord9300();                       // PERFORM 9300-DELETE-RECORD. source: :814
            if (CaDeleteSucceeded)
                SetDeletedYes();                      // SET FLG-DELETED-YES. source: :818
            else
                SetDeletedNo();                       // SET FLG-DELETED-NO. source: :820
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :823
            if (FlgDeletedYes)
            {
                InitCarddemoCommareaFully();          // source: :827
                InitThisProgCommarea();
                InitWsMiscStorage();                  // INITIALIZE WS-MISC-STORAGE. source: :829
                _commArea.SetFirstEntry();            // SET CDEMO-PGM-ENTER. source: :830
                SetCaFirstPage();                     // SET CA-FIRST-PAGE. source: :831
                SetCaLastPageNotShown();              // SET CA-LAST-PAGE-NOT-SHOWN. source: :832
            }
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :834
            return;
        }
        if (CcardAidEnter && _wsUpdatesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            // WHEN ENTER AND WS-UPDATES-REQUESTED > 0 AND from-program = self (arm update confirm). source: :838-850
            _wsStartKey = _caFirstTrCode;             // source: :841
            if (!FlgTypeFilterNotOk && !FlgDescFilterNotOk)
                ReadForward8000();                    // PERFORM 8000-READ-FORWARD. source: :845
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :848
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :850
            return;
        }
        if (CcardAidPfk10 && _wsUpdatesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM)
        {
            // WHEN PFK10 AND WS-UPDATES-REQUESTED > 0 AND from-program = self (confirm update). source: :854-868
            UpdateRecord9200();                       // PERFORM 9200-UPDATE-RECORD. source: :858
            if (CaUpdateSucceeded)
                SetUpdateCompleted();                 // SET FLG-UPDATE-COMPLETED. source: :861
            _wsStartKey = _caFirstTrCode;             // source: :863
            ReadForward8000();                        // PERFORM 8000-READ-FORWARD. source: :865
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :867
            // NOTE: NO GO TO COMMON-RETURN here — falls through to the post-EVALUATE block. source: :868
        }
        else if (!IsAnyMainWhenMatched())
        {
            // WHEN OTHER. source: :870-878
            _wsStartKey = _caFirstTrCode;             // source: :872
            ReadForward8000();                        // PERFORM 8000-READ-FORWARD. source: :874
            SendMap2000(ctx);                         // PERFORM 2000-SEND-MAP. source: :876
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :878
            return;
        }

        // Post-EVALUATE fall-through (reached only via the PFK10-update WHEN, or no WHEN match). source: :882-896
        if (InputError)
        {
            _ccardErrorMsg = _wsReturnMsg;            // source: :883
            _commArea.FromProgram = LIT_THISPGM;      // source: :884
            _commArea.LastMapSet = LIT_THISMAPSET;    // source: :885
            _commArea.LastMap = LIT_THISMAP;          // source: :886
            _ccardNextProg = LIT_THISPGM;             // source: :888
            _ccardNextMapset = LIT_THISMAPSET;        // source: :889
            _ccardNextMap = LIT_THISMAP;              // source: :890
            CommonReturn(ctx);                        // GO TO COMMON-RETURN. source: :892
            return;
        }
        _ccardNextProg = LIT_THISPGM;                 // MOVE LIT-THISPGM TO CCARD-NEXT-PROG. source: :895
        CommonReturn(ctx);                            // GO TO COMMON-RETURN. source: :896
    }

    // =============================================================================================
    //  COMMON-RETURN — pseudo-conversational return. source: COTRTLIC.cbl:899-915
    // =============================================================================================
    private void CommonReturn(CicsContext ctx)
    {
        if (ctx.Outcome is not null) return; // an XCTL already terminated the task.

        _commArea.FromTranId = LIT_THISTRANID;   // source: :900
        _commArea.FromProgram = LIT_THISPGM;     // source: :901
        _commArea.LastMapSet = LIT_THISMAPSET;   // source: :902
        _commArea.LastMap = LIT_THISMAP;         // source: :903
        // Reassemble WS-COMMAREA = CARDDEMO-COMMAREA prefix + WS-THIS-PROGCOMMAREA tail. source: :904-907
        SaveThisProgCommarea();
        // EXEC CICS RETURN TRANSID('CTLI') COMMAREA(WS-COMMAREA). source: :910-914
        ctx.ReturnTransId(LIT_THISTRANID, _commArea);
    }

    /// <summary>
    /// True when one of the explicit main-EVALUATE WHENs matched (used to model the "WHEN OTHER" guard so the
    /// PFK10-update fall-through does not also fire WHEN OTHER). The PFK10-update branch itself is handled
    /// inline; this only needs to detect the other matched WHENs.
    /// </summary>
    private bool IsAnyMainWhenMatched()
    {
        if (InputError) return true;
        if (CcardAidPfk07 && CaFirstPage) return true;
        if (CcardAidPfk03 || (_commArea.IsReenter && _commArea.FromProgram.TrimEnd() != LIT_THISPGM)) return true;
        if (CcardAidPfk08 && CaNextPageExists) return true;
        if (CcardAidPfk07 && !CaFirstPage) return true;
        if (CcardAidEnter && _wsDeletesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM) return true;
        if (CcardAidPfk10 && _wsDeletesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM) return true;
        if (CcardAidEnter && _wsUpdatesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM) return true;
        if (CcardAidPfk10 && _wsUpdatesRequested > 0 && _commArea.FromProgram.TrimEnd() == LIT_THISPGM) return true;
        return false;
    }

    // =============================================================================================
    //  1000-RECEIVE-MAP. source: COTRTLIC.cbl:919-928
    // =============================================================================================
    private void ReceiveMap1000(CicsContext ctx)
    {
        ReceiveScreen1100(ctx); // PERFORM 1100-RECEIVE-SCREEN. source: :920
        EditInputs1200();       // PERFORM 1200-EDIT-INPUTS. source: :923
    }

    // =============================================================================================
    //  1100-RECEIVE-SCREEN. source: COTRTLIC.cbl:930-958
    // =============================================================================================
    private void ReceiveScreen1100(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('CTRTLIA') MAPSET('COTRTLI') INTO(CTRTLIAI). source: :931-935
        ctx.ReceiveMap(LIT_THISMAP, LIT_THISMAPSET, _map);

        _wsInTypeCd = _map.Field("TRTYPE").Value;   // MOVE TRTYPEI TO WS-IN-TYPE-CD. source: :937
        _wsInTypeDesc = _map.Field("TRDESC").Value; // MOVE TRDESCI TO WS-IN-TYPE-DESC. source: :938

        // PERFORM VARYING I 1..7. source: :940-953
        for (int i = 1; i <= WS_MAX_SCREEN_LINES; i++)
        {
            _editSelect[i - 1] = FirstChar(_map.Field($"TRTSEL{i}").Value);  // MOVE TRTSELI(I) TO WS-EDIT-SELECT(I). source: :941
            _rowTrCodeIn[i - 1] = _map.Field($"TRTTYP{i}").Value;            // MOVE TRTTYPI(I) TO WS-ROW-TR-CODE-IN(I). source: :942

            _rowTrDescIn[i - 1] = ""; // MOVE LOW-VALUES TO WS-ROW-TR-DESC-IN(I). source: :944
            string trtypdi = _map.Field($"TRTYPD{i}").Value;
            if (trtypdi.TrimEnd() == LIT_ASTERISK || IsAllSpacesOrEmpty(trtypdi))
            {
                // CONTINUE (leave low-values). source: :945-947
            }
            else
            {
                _rowTrDescIn[i - 1] = trtypdi.Trim(); // MOVE FUNCTION TRIM(TRTYPDI(I)) TO WS-ROW-TR-DESC-IN(I). source: :949-950
            }
        }
    }

    // =============================================================================================
    //  1200-EDIT-INPUTS. source: COTRTLIC.cbl:960-980
    // =============================================================================================
    private void EditInputs1200()
    {
        SetInputOk();                  // SET INPUT-OK. source: :962
        SetProtectSelectRowsNo();      // SET FLG-PROTECT-SELECT-ROWS-NO. source: :963

        // B-4: 1210 runs first (it reads the CHANGED flags which 1220/1230 only set later). source: :965-975
        EditArray1210();   // PERFORM 1210-EDIT-ARRAY. source: :965
        EditDesc1230();    // PERFORM 1230-EDIT-DESC. source: :968 (desc before typecd)
        EditTypeCd1220();  // PERFORM 1220-EDIT-TYPECD. source: :971
        CrossEdits1290();  // PERFORM 1290-CROSS-EDITS. source: :974
    }

    // =============================================================================================
    //  1210-EDIT-ARRAY. source: COTRTLIC.cbl:982-1057
    // =============================================================================================
    private void EditArray1210()
    {
        // MOVE ZERO TO the action counters. source: :984-988
        _wsActionsRequested = 0;
        _wsNoActionsSelected = 0;
        _wsDeletesRequested = 0;
        _wsUpdatesRequested = 0;
        _wsValidActionsSelected = 0;

        // B-4: keys off stale FLG-*FILTER-CHANGED flags (1220/1230 set them AFTER this paragraph). source: :991-994
        if (!FlgTypeFilterChangedNo || !FlgDescFilterChangedNo)
        {
            InitEditSelectFlags(); // INITIALIZE WS-EDIT-SELECT-FLAGS. source: :993
            return;                // GO TO 1210-EDIT-ARRAY-EXIT. source: :994
        }

        // INSPECT WS-EDIT-SELECT-FLAGS TALLYING. source: :997-1001
        foreach (char c in _editSelect)
        {
            if (c is ' ' or '\0') _wsNoActionsSelected++;       // FOR ALL SPACES LOW-VALUES
            if (c == 'D') _wsDeletesRequested++;                // FOR ALL LIT-DELETE-FLAG
            if (c == 'U') _wsUpdatesRequested++;                // FOR ALL LIT-UPDATE-FLAG
        }

        // COMPUTE WS-ACTIONS-REQUESTED = 7 - WS-NO-ACTIONS-SELECTED. source: :1003-1006
        _wsActionsRequested = WS_MAX_SCREEN_LINES - _wsNoActionsSelected;

        // COMPUTE WS-VALID-ACTIONS-SELECTED = WS-DELETES-REQUESTED + WS-UPDATES-REQUESTED. source: :1009-1012
        _wsValidActionsSelected = _wsDeletesRequested + _wsUpdatesRequested;

        _iSelected = 0;                  // MOVE ZERO TO I-SELECTED. source: :1014
        SetBadActionsSelectedNo();       // SET FLG-BAD-ACTIONS-SELECTED-NO. source: :1015

        // PERFORM VARYING I FROM 7 BY -1 UNTIL I = 0. source: :1017-1040
        for (int i = WS_MAX_SCREEN_LINES; i > 0; i--)
        {
            if (SelectOk(i))
            {
                _iSelected = i;                          // MOVE I TO I-SELECTED. source: :1023
                if (WsMoreThan1Action)
                {
                    SetRowSelectError(i);                // MOVE '1' TO WS-ROW-TRTSELECT-ERROR(I). source: :1025
                    SetBadActionsSelectedYes();          // SET FLG-BAD-ACTIONS-SELECTED-YES. source: :1026
                }
                if (UpdateRequestedOn(i))
                    EditArrayDesc1211(i);                // PERFORM 1211-EDIT-ARRAY-DESC. source: :1029
            }
            else if (SelectBlank(i))
            {
                // CONTINUE. source: :1032-1033
            }
            else
            {
                SetInputError();                         // SET INPUT-ERROR. source: :1035
                SetRowSelectError(i);                    // MOVE '1' TO WS-ROW-TRTSELECT-ERROR(I). source: :1036
                SetBadActionsSelectedYes();              // SET FLG-BAD-ACTIONS-SELECTED-YES. source: :1037
                _wsReturnMsg = MESG_INVALID_ACTION_CODE; // SET WS-MESG-INVALID-ACTION-CODE. source: :1038
            }
        }

        // If I-SELECTED = WS-CA-ROW-SELECTED -> not changed; else changed + record it. source: :1042-1047
        if (_iSelected == _caRowSelected)
        {
            SetRowSelectionChangedNo();
        }
        else
        {
            SetRowSelectionChangedYes();
            _caRowSelected = _iSelected;
        }

        // IF WS-MORETHAN1ACTION -> INPUT-ERROR + message. source: :1049-1052
        if (WsMoreThan1Action)
        {
            SetInputError();
            _wsReturnMsg = MESG_MORE_THAN_1_ACTION;
        }
    }

    // =============================================================================================
    //  1211-EDIT-ARRAY-DESC. source: COTRTLIC.cbl:1060-1094
    // =============================================================================================
    private void EditArrayDesc1211(int i)
    {
        SetNoChangesFound(); // SET NO-CHANGES-FOUND. source: :1062

        // No-change guard: UPPER(TRIM(in)) = UPPER(TRIM(out)) AND LEN(TRIM(in)) = LEN(TRIM(out)). source: :1064-1076
        string inTrim = (_rowTrDescIn[i - 1] ?? "").Trim();
        string outTrim = (_caRowTrDescOut[i - 1] ?? "").Trim();
        if (inTrim.ToUpperInvariant() == outTrim.ToUpperInvariant() && inTrim.Length == outTrim.Length)
        {
            _wsReturnMsg = MESG_NO_CHANGES_DETECTED; // SET WS-MESG-NO-CHANGES-DETECTED. source: :1072
            return;                                  // GO TO 1211-EDIT-ARRAY-DESC-EXIT. source: :1073
        }
        SetChangesHaveOccurred(); // SET CHANGES-HAVE-OCCURRED. source: :1075

        SetRowDescriptionNotOk(); // SET FLG-ROW-DESCRIPTION-NOT-OK. source: :1078

        // Edit Description (50 chars). source: :1083-1089
        _wsEditVariableName = "Transaction Desc";   // source: :1083
        _wsEditAlphanumOnly = _rowTrDescIn[i - 1] ?? ""; // source: :1084
        _wsEditAlphanumLength = 50;                 // source: :1085
        EditAlphanumReqd1240();                     // PERFORM 1240-EDIT-ALPHANUM-REQD. source: :1086
        _wsArrayDescriptionFlgs = _wsEditAlphanumFlags; // MOVE WS-EDIT-ALPHANUM-ONLY-FLAGS TO WS-ARRAY-DESCRIPTION-FLGS. source: :1088
    }

    // =============================================================================================
    //  1220-EDIT-TYPECD (+ 1220-EDIT-TYPECD-EXIT carries change-detection). source: COTRTLIC.cbl:1096-1140
    // =============================================================================================
    private void EditTypeCd1220()
    {
        SetTypeFilterBlank(); // SET FLG-TYPEFILTER-BLANK. source: :1098

        // Not supplied: low-values / spaces / zeros. source: :1101-1107
        if (IsAllSpacesOrEmpty(_wsInTypeCd) || _wsInTypeCd.TrimEnd() == "" || IsAllZeros(_wsInTypeCd))
        {
            SetTypeFilterBlank();     // source: :1104
            _wsTypeCdFilter = "00";   // MOVE ZEROES TO WS-TYPE-CD-FILTER. source: :1105
            EditTypeCdExit1220();     // GO TO 1220-EDIT-TYPECD-EXIT (carries change-detection). source: :1106
            return;
        }

        // Not numeric. source: :1111-1122
        if (!IsNumericX(_wsInTypeCd, 2))
        {
            SetInputError();             // source: :1112
            SetTypeFilterNotOk();        // source: :1113
            SetProtectSelectRowsYes();   // source: :1114
            _wsReturnMsg = "TYPE CODE FILTER,IF SUPPLIED MUST BE A 2 DIGIT NUMBER"; // source: :1115-1116
            EditTypeCdExit1220();        // GO TO 1220-EDIT-TYPECD-EXIT. source: :1118
            return;
        }
        _wsTypeCdFilter = PadX(_wsInTypeCd, 2); // MOVE WS-IN-TYPE-CD TO WS-TYPE-CD-FILTER. source: :1120
        SetTypeFilterIsValid();                  // SET FLG-TYPEFILTER-ISVALID. source: :1121
        EditTypeCdExit1220();
    }

    /// <summary>1220-EDIT-TYPECD-EXIT — carries change-detection logic, not a pure no-op. source: :1125-1140</summary>
    private void EditTypeCdExit1220()
    {
        // IF WS-IN-TYPE-CD = WS-CA-TYPE-CD OR (BLANK AND WS-CA-TYPE-CD in {ZEROES,LOW-VALUES,SPACES}). source: :1127-1131
        bool caBlank = IsAllZeros(_caTypeCd) || IsAllSpacesOrEmpty(_caTypeCd);
        if (PadX(_wsInTypeCd, 2) == PadX(_caTypeCd, 2) || (_wsEditTypeFlag == ' ' && caBlank))
        {
            SetTypeFilterChangedNo(); // source: :1132
        }
        else
        {
            InitCaPagingVariables();         // INITIALIZE WS-CA-PAGING-VARIABLES. source: :1134
            _caTypeCd = PadX(_wsInTypeCd, 2);// MOVE WS-IN-TYPE-CD TO WS-CA-TYPE-CD. source: :1135
            SetTypeFilterChangedYes();       // source: :1136
        }
    }

    // =============================================================================================
    //  1230-EDIT-DESC (+ 1230-EDIT-DESC-EXIT carries change-detection). source: COTRTLIC.cbl:1142-1178
    // =============================================================================================
    private void EditDesc1230()
    {
        SetDescFilterBlank(); // SET FLG-DESCFILTER-BLANK. source: :1144

        // Not supplied: low-values / spaces. source: :1147-1153
        if (IsAllSpacesOrEmpty(_wsInTypeDesc))
        {
            SetDescFilterBlank();  // source: :1149
            EditDescExit1230();    // GO TO 1230-EDIT-DESC-EXIT. source: :1150
            return;
        }
        SetDescFilterIsValid(); // source: :1152

        // Build the LIKE pattern: '%' TRIM(value) '%'. source: :1155-1163
        if (FlgDescFilterIsValid)
            _wsTypeDescFilter = "%" + _wsInTypeDesc.Trim() + "%";

        EditDescExit1230();
    }

    /// <summary>1230-EDIT-DESC-EXIT — carries change-detection. source: :1166-1178</summary>
    private void EditDescExit1230()
    {
        // IF WS-IN-TYPE-DESC = WS-CA-TYPE-DESC OR (BLANK AND WS-CA-TYPE-DESC in {LOW-VALUES,SPACES}). source: :1166-1169
        bool caBlank = IsAllSpacesOrEmpty(_caTypeDesc);
        if (PadX(_wsInTypeDesc, 50) == PadX(_caTypeDesc, 50) || (_wsEditDescFlag == ' ' && caBlank))
        {
            SetDescFilterChangedNo(); // source: :1170
        }
        else
        {
            InitCaPagingVariables();           // INITIALIZE WS-CA-PAGING-VARIABLES. source: :1172
            _caTypeDesc = PadX(_wsInTypeDesc, 50); // MOVE WS-IN-TYPE-DESC TO WS-CA-TYPE-DESC. source: :1173
            SetDescFilterChangedYes();         // source: :1174
        }
    }

    // =============================================================================================
    //  1240-EDIT-ALPHANUM-REQD. source: COTRTLIC.cbl:1181-1237
    // =============================================================================================
    private void EditAlphanumReqd1240()
    {
        SetAlphanumNotOk(); // SET FLG-ALPHNANUM-NOT-OK. source: :1183

        // The field slice (1:length) = the 50-char description here.
        string field = SliceX(_wsEditAlphanumOnly, _wsEditAlphanumLength);

        // Not supplied: LOW-VALUES / SPACES / LEN(TRIM())=0. source: :1186-1205
        if (IsAllSpacesOrEmpty(field) || field.Trim().Length == 0)
        {
            SetInputError();    // source: :1193
            SetAlphanumBlank(); // source: :1194
            if (WsReturnMsgOff)
                _wsReturnMsg = _wsEditVariableName.Trim() + " must be supplied."; // source: :1196-1200
            return;             // GO TO 1240-EDIT-ALPHANUM-REQD-EXIT. source: :1204
        }

        // Only Alphabets, numbers and space allowed (INSPECT CONVERTING blanks out A-Za-z0-9). source: :1208-1231
        string remaining = ConvertAlphanumToSpaces(field);
        if (remaining.Trim().Length == 0)
        {
            // CONTINUE — all chars were alphanumeric/space. source: :1218
        }
        else
        {
            SetInputError();   // source: :1220
            SetAlphanumNotOk();// source: :1221
            if (WsReturnMsgOff)
                _wsReturnMsg = _wsEditVariableName.Trim() + " can have numbers or alphabets only."; // source: :1224-1225
            return;            // GO TO 1240-EDIT-ALPHANUM-REQD-EXIT. source: :1230
        }

        SetAlphanumIsValid(); // SET FLG-ALPHNANUM-ISVALID. source: :1233
    }

    // =============================================================================================
    //  1290-CROSS-EDITS. source: COTRTLIC.cbl:1239-1271
    // =============================================================================================
    private void CrossEdits1290()
    {
        // No filter valid -> nothing to cross-check. source: :1241-1246
        if (FlgTypeFilterIsValid || FlgDescFilterIsValid)
        {
            // CONTINUE. source: :1243
        }
        else
        {
            return; // GO TO 1290-CROSS-EDITS-EXIT. source: :1245
        }

        CheckFilters9100(); // PERFORM 9100-CHECK-FILTERS. source: :1248

        if (_wsRecordsCount == 0)
        {
            SetInputError();                    // source: :1252
            if (FlgTypeFilterIsValid)
                SetTypeFilterNotOk();           // source: :1254
            if (FlgDescFilterIsValid)
                SetDescFilterNotOk();           // source: :1258
            SetProtectSelectRowsYes();          // source: :1262
            _wsReturnMsg = "No Records found for these filter conditions"; // source: :1263-1264 (B-8: distinct text)
            // GO TO 1290-CROSS-EDITS-EXIT. source: :1266
        }
    }

    // =============================================================================================
    //  2000-SEND-MAP. source: COTRTLIC.cbl:1274-1292
    // =============================================================================================
    private void SendMap2000(CicsContext ctx)
    {
        ScreenInit2100(ctx);          // PERFORM 2100-SCREEN-INIT. source: :1276
        SetupArrayAttribs2200();      // PERFORM 2200-SETUP-ARRAY-ATTRIBS. source: :1278
        ScreenArrayInit2300();        // PERFORM 2300-SCREEN-ARRAY-INIT. source: :1280
        SetupScreenAttrs2400(ctx);    // PERFORM 2400-SETUP-SCREEN-ATTRS. source: :1282
        SetupMessage2500();           // PERFORM 2500-SETUP-MESSAGE. source: :1284
        SendScreen2600(ctx);          // PERFORM 2600-SEND-SCREEN. source: :1286
    }

    // =============================================================================================
    //  2100-SCREEN-INIT. source: COTRTLIC.cbl:1293-1327
    // =============================================================================================
    private void ScreenInit2100(CicsContext ctx)
    {
        // MOVE LOW-VALUES TO CTRTLIAO — blank every named field + clear per-turn overrides. source: :1294
        MoveLowValuesToMapOut();

        DateTime now = ctx.Clock.Now; // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :1296,1303

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :1298
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :1299
        _map.Field("TRNNAME").SetValue(LIT_THISTRANID, setMdt: false);// MOVE LIT-THISTRANID TO TRNNAMEO. source: :1300
        _map.Field("PGMNAME").SetValue(LIT_THISPGM, setMdt: false);   // MOVE LIT-THISPGM TO PGMNAMEO. source: :1301

        // CURDATEO = mm/dd/yy. source: :1305-1309
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);
        // CURTIMEO = hh:mm:ss. source: :1311-1315
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);

        // PAGENO = WS-CA-SCREEN-NUM. source: :1318
        _map.Field("PAGENO").SetValue(_caScreenNum.ToString(), setMdt: false);

        SetNoInfoMessage();                                          // SET WS-NO-INFO-MESSAGE. source: :1320
        _map.Field("INFOMSG").SetValue(_wsInfoMsg, setMdt: false);   // MOVE WS-INFO-MSG TO INFOMSGO. source: :1321
        // MOVE DFHBMDAR TO INFOMSGC — info line dark (model via attribute override). source: :1322
        _map.Field("INFOMSG").AttributeOverride = BmsAttribute.Dark | BmsAttribute.Protected;
    }

    // =============================================================================================
    //  2200-SETUP-ARRAY-ATTRIBS. source: COTRTLIC.cbl:1329-1379
    // =============================================================================================
    private void SetupArrayAttribs2200()
    {
        // PERFORM VARYING I FROM 7 BY -1 UNTIL I = 0. source: :1333-1336
        for (int i = WS_MAX_SCREEN_LINES; i > 0; i--)
        {
            // MOVE DFHBMPRF TO TRTYPDA(I) — desc protected by default. source: :1337
            SetRowDescAttr(i, BmsAttribute.Protected | BmsAttribute.Fset | BmsAttribute.Normal);

            if (RowSlotIsLowValues(i) || FlgProtectSelectRowsYes)
            {
                // MOVE DFHBMPRO TO TRTSELA(I) — select autoskip/protected. source: :1341
                SetRowSelAttr(i, BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal);
            }
            else
            {
                if (RowSelectError(i))
                {
                    SetRowSelColor(i, BmsColor.Red);          // MOVE DFHRED TO TRTSELC(I). source: :1344
                    SetRowSelCursor(i);                       // MOVE -1 TO TRTSELL(I). source: :1345
                }

                if (DeleteRequestedOn(i) && WsOnly1ValidAction && FlgBadActionsSelectedNo)
                {
                    SetRowTypeColor(i, BmsColor.Neutral);     // MOVE DFHNEUTR TO TRTTYPC(I). source: :1351
                    SetRowDescColor(i, BmsColor.Neutral);     // MOVE DFHNEUTR TO TRTYPDC(I). source: :1352
                    SetRowSelCursor(i);                       // MOVE -1 TO TRTSELL(I). source: :1353
                }

                if (UpdateRequestedOn(i) && WsOnly1ValidAction && FlgBadActionsSelectedNo)
                {
                    SetRowTypeColor(i, BmsColor.Neutral);     // MOVE DFHNEUTR TO TRTTYPC(I). source: :1359
                    if (FlgUpdateCompleted)
                    {
                        SetRowSelCursor(i);                   // MOVE -1 TO TRTSELL(I). source: :1361
                        SetRowDescColor(i, BmsColor.Neutral); // MOVE DFHNEUTR TO TRTYPDC(I). source: :1362
                    }
                    else
                    {
                        SetRowDescCursor(i);                  // MOVE -1 TO TRTYPDL(I). source: :1364
                        // MOVE DFHBMFSE TO TRTYPDA(I) — unprotect desc for edit. source: :1365
                        SetRowDescAttr(i, BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal);
                        if (!FlgRowDescriptionIsValid)
                            SetRowDescColor(i, BmsColor.Red); // MOVE DFHRED TO TRTYPDC(I). source: :1367
                    }
                }
                // MOVE DFHBMFSE TO TRTSELA(I) — select field modifiable. source: :1371
                SetRowSelAttr(i, BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal);
            }
        }
    }

    // =============================================================================================
    //  2300-SCREEN-ARRAY-INIT. source: COTRTLIC.cbl:1383-1435
    // =============================================================================================
    private void ScreenArrayInit2300()
    {
        // PERFORM VARYING I FROM 1 BY 1 UNTIL I > 7. source: :1386
        for (int i = 1; i <= WS_MAX_SCREEN_LINES; i++)
        {
            if (RowSlotIsLowValues(i))
            {
                // CONTINUE — skip empty slots. source: :1389
                continue;
            }

            if (DeleteRequestedOn(i) && WsOnly1ValidAction && FlgBadActionsSelectedNo)
            {
                if (FlgDeletedYes)
                    SetSelectBlank(i);          // SET SELECT-BLANK(I) — clear after delete. source: :1395
                else
                    SetCaDeleteRequested();     // SET CA-DELETE-REQUESTED — mark confirm pending. source: :1397
            }

            // Type code. source: :1402
            _map.Field($"TRTTYP{i}").SetValue(_caRowTrCodeOut[i - 1] ?? "", setMdt: false);

            // Description. source: :1404-1425
            if (UpdateRequestedOn(i) && WsOnly1ValidAction && FlgBadActionsSelectedNo)
            {
                if (FlgUpdateCompleted)
                    SetSelectBlank(i);          // SET SELECT-BLANK(I). source: :1408
                else
                    SetCaUpdateRequested();     // SET CA-UPDATE-REQUESTED. source: :1410

                if (ChangesHaveOccurred)
                {
                    if (FlgRowDescriptionBlank)
                        _map.Field($"TRTYPD{i}").SetValue(LIT_ASTERISK, setMdt: false); // MOVE LIT-ASTERISK. source: :1415
                    else
                        _map.Field($"TRTYPD{i}").SetValue(_rowTrDescIn[i - 1] ?? "", setMdt: false); // MOVE WS-ROW-TR-DESC-IN(I). source: :1417
                }
                else
                {
                    _map.Field($"TRTYPD{i}").SetValue(_caRowTrDescOut[i - 1] ?? "", setMdt: false); // MOVE WS-CA-ROW-TR-DESC-OUT(I). source: :1421
                }
            }
            else
            {
                _map.Field($"TRTYPD{i}").SetValue(_caRowTrDescOut[i - 1] ?? "", setMdt: false); // MOVE WS-CA-ROW-TR-DESC-OUT(I). source: :1424
            }

            // MOVE WS-EDIT-SELECT(I) TO TRTSELO(I) — echo the (possibly blanked) action char. source: :1428
            _map.Field($"TRTSEL{i}").SetValue(EditSelectChar(i), setMdt: false);
        }
    }

    // =============================================================================================
    //  2400-SETUP-SCREEN-ATTRS. source: COTRTLIC.cbl:1438-1501
    //  B-6: several MOVEs target the INPUT map (CTRTLIAI); since CTRTLIAO REDEFINES CTRTLIAI the bytes
    //  overlap so the attribute outcome is achieved on the single symbolic field model.
    // =============================================================================================
    private void SetupScreenAttrs2400(CicsContext ctx)
    {
        // Initialize search criteria (leave filters blank on fresh menu entry). source: :1440-1443
        if (ctx.EibCalen == 0 || (_commArea.IsFirstEntry && _commArea.FromProgram.TrimEnd() == LIT_ADMINPGM))
        {
            // CONTINUE. source: :1443
        }
        else
        {
            // Type filter echo. source: :1445-1459
            if (_wsActionsRequested > 0)
            {
                _map.Field("TRTYPE").SetValue(_wsInTypeCd, setMdt: false);                // source: :1447
                _map.Field("TRTYPE").AttributeOverride = BmsAttribute.AutoSkip | BmsAttribute.Fset; // DFHBMASF. source: :1448
                _map.Field("TRTYPE").ColorOverride = BmsColor.Blue;                       // DFHBLUE. source: :1449
            }
            else if (FlgTypeFilterIsValid || FlgTypeFilterNotOk)
            {
                _map.Field("TRTYPE").SetValue(_wsInTypeCd, setMdt: false);                // source: :1452
                _map.Field("TRTYPE").AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal; // DFHBMFSE. source: :1453
            }
            else if (IsAllZeros(_wsInTypeCd))
            {
                _map.Field("TRTYPE").SetValue("", setMdt: false);                         // MOVE LOW-VALUES. source: :1455
            }
            else
            {
                _map.Field("TRTYPE").SetValue("", setMdt: false);                         // MOVE LOW-VALUES. source: :1457
                _map.Field("TRTYPE").AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal; // DFHBMFSE. source: :1458
            }

            // Description filter echo. source: :1461-1472
            if (_wsActionsRequested > 0)
            {
                _map.Field("TRDESC").SetValue(_wsInTypeDesc, setMdt: false);              // source: :1463
                _map.Field("TRDESC").AttributeOverride = BmsAttribute.AutoSkip | BmsAttribute.Fset; // DFHBMASF. source: :1464
                _map.Field("TRDESC").ColorOverride = BmsColor.Blue;                       // DFHBLUE. source: :1465
            }
            else if (FlgDescFilterIsValid || FlgDescFilterNotOk)
            {
                _map.Field("TRDESC").SetValue(_wsInTypeDesc, setMdt: false);              // source: :1468
                _map.Field("TRDESC").AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal; // DFHBMFSE. source: :1469
            }
            else
            {
                _map.Field("TRDESC").AttributeOverride = BmsAttribute.Unprotected | BmsAttribute.Fset | BmsAttribute.Normal; // DFHBMFSE. source: :1471
            }
        }

        // Position cursor on filter errors. source: :1477-1485
        if (FlgTypeFilterNotOk)
        {
            _map.Field("TRTYPE").ColorOverride = BmsColor.Red; // DFHRED. source: :1478
            _map.Field("TRTYPE").CursorLength = -1;            // MOVE -1 TO TRTYPEL. source: :1479
        }
        if (FlgDescFilterNotOk)
        {
            _map.Field("TRDESC").ColorOverride = BmsColor.Red; // DFHRED. source: :1483
            _map.Field("TRDESC").CursorLength = -1;            // MOVE -1 TO TRDESCL. source: :1484
        }

        // If no errors, position cursor. source: :1489-1497
        if (InputOk)
        {
            if (_wsActionsRequested > 0 && !CcardAidPfk07 && !CcardAidPfk08)
            {
                // CONTINUE — cursor already on a row. source: :1493
            }
            else
            {
                _map.Field("TRTYPE").CursorLength = -1; // MOVE -1 TO TRTYPEL. source: :1495
            }
        }
    }

    // =============================================================================================
    //  2500-SETUP-MESSAGE. source: COTRTLIC.cbl:1504-1584
    // =============================================================================================
    private void SetupMessage2500()
    {
        // EVALUATE TRUE — first match selects the info/return message. source: :1506-1555
        if (FlgDeletedYes)
        {
            _wsInfoMsg = INFO_DELETE_SUCCESS; // SET WS-INFORM-DELETE-SUCCESS. source: :1508
        }
        else if (FlgUpdateCompleted)
        {
            _wsInfoMsg = INFO_UPDATE_SUCCESS; // SET WS-INFORM-UPDATE-SUCCESS. source: :1510
        }
        else if (FlgTypeFilterNotOk || FlgDescFilterNotOk)
        {
            // CONTINUE — keep the NOT-OK return message already set. source: :1511-1513
        }
        else if (CcardAidEnter && _wsDeletesRequested > 0 && WsOnly1Action && WsOnly1ValidAction)
        {
            // source: :1514-1522
            if (WsNoInfoMessage && FlgTypeFilterChangedNo && FlgDescFilterChangedNo)
                _wsInfoMsg = INFO_DELETE; // SET WS-INFORM-DELETE. source: :1521
        }
        else if (CcardAidEnter && _wsUpdatesRequested > 0 && WsOnly1Action && WsOnly1ValidAction)
        {
            // source: :1523-1531
            if (WsNoInfoMessage && FlgTypeFilterChangedNo && FlgDescFilterChangedNo)
                _wsInfoMsg = INFO_UPDATE; // SET WS-INFORM-UPDATE. source: :1530
        }
        else if (CcardAidPfk07 && CaFirstPage)
        {
            _wsReturnMsg = "No previous pages to display"; // source: :1534-1535
        }
        else if (CcardAidPfk08 && CaNextPageNotExists && CaLastPageShown)
        {
            _wsReturnMsg = "No more pages to display"; // source: :1539-1540
        }
        else if (CcardAidPfk08 && CaNextPageNotExists)
        {
            // source: :1541-1549
            if (WsNoInfoMessage)
                _wsInfoMsg = INFO_REC_ACTIONS;        // SET WS-INFORM-REC-ACTIONS. source: :1544
            if (CaLastPageNotShown() && CaNextPageNotExists)
                SetCaLastPageShown();                 // SET CA-LAST-PAGE-SHOWN. source: :1548
        }
        else if (WsNoInfoMessage || CaNextPageExists)
        {
            _wsInfoMsg = INFO_REC_ACTIONS; // SET WS-INFORM-REC-ACTIONS. source: :1552
        }
        else
        {
            SetNoInfoMessage(); // SET WS-NO-INFO-MESSAGE. source: :1554
        }

        _map.Field("ERRMSG").SetValue(_wsReturnMsg, setMdt: false); // MOVE WS-RETURN-MSG TO ERRMSGO. source: :1557

        // Center justify the info text. source: :1562-1571
        _wsStringLen = _wsInfoMsg.Trim().Length;                       // WS-STRING-LEN = LEN(TRIM(WS-INFO-MSG)). source: :1562-1565
        _wsStringMid = (45 - _wsStringLen) / 2 + 1;                    // integer division truncates toward zero. source: :1566-1568
        _wsStringOut = CenterInto(_wsInfoMsg, _wsStringLen, _wsStringMid);

        // source: :1575-1579
        if (!WsNoInfoMessage && !WsMesgNoRecordsFound)
        {
            _map.Field("INFOMSG").SetValue(_wsStringOut, setMdt: false);          // MOVE WS-STRING-OUT TO INFOMSGO. source: :1577
            _map.Field("INFOMSG").AttributeOverride = BmsAttribute.Protected;     // MOVE DFHNEUTR TO INFOMSGC. source: :1578
            _map.Field("INFOMSG").ColorOverride = BmsColor.Neutral;
        }
    }

    // =============================================================================================
    //  2600-SEND-SCREEN. source: COTRTLIC.cbl:1587-1599
    // =============================================================================================
    private void SendScreen2600(CicsContext ctx)
    {
        // EXEC CICS SEND MAP('CTRTLIA') MAPSET('COTRTLI') FROM(CTRTLIAO) CURSOR ERASE FREEKB. source: :1588-1595
        ctx.SendMap(LIT_THISMAP, LIT_THISMAPSET, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour any MOVE -1 TO xxxL set above.
        });
    }

    // =============================================================================================
    //  8000-READ-FORWARD. source: COTRTLIC.cbl:1603-1726
    // =============================================================================================
    private void ReadForward8000()
    {
        ClearPageBuffer();             // MOVE LOW-VALUES TO WS-CA-ALL-ROWS-OUT. source: :1604

        OpenForwardCursor9400();       // PERFORM 9400-OPEN-FORWARD-CURSOR. source: :1609
        if (WsDb2Error)
        {
            // GO TO 8000-READ-FORWARD-EXIT — the EXIT label is below the 9450 close, so the close is
            // bypassed on an open failure (faithful). Dispose any half-opened reader to avoid a leak. source: :1612-1613
            _forwardReader?.Dispose();
            _forwardReader = null;
            _forwardCmd?.Dispose();
            _forwardCmd = null;
            return;
        }

        _wsRowNumber = 0;              // MOVE ZEROES TO WS-ROW-NUMBER. source: :1618
        SetCaNextPageExists();         // SET CA-NEXT-PAGE-EXISTS. source: :1619
        SetMoreRecordsToRead();        // SET MORE-RECORDS-TO-READ. source: :1620

        while (!ReadLoopExit)          // PERFORM UNTIL READ-LOOP-EXIT. source: :1622
        {
            InitDcl();                 // INITIALIZE DCLTRANSACTION-TYPE. source: :1624
            FetchForward();            // EXEC SQL FETCH C-TR-TYPE-FORWARD. source: :1626-1630
            _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1632

            if (_sqlcode == 0)
            {
                _wsRowNumber += 1;     // ADD 1 TO WS-ROW-NUMBER. source: :1636
                _caRowTrCodeOut[_wsRowNumber - 1] = _dclTrType;                    // source: :1638
                _caRowTrDescOut[_wsRowNumber - 1] = _dclTrDescriptionText;         // source: :1641
                if (_wsRowNumber == 1)
                {
                    _caFirstTrCode = _dclTrType;          // MOVE DCL-TR-TYPE TO WS-CA-FIRST-TR-CODE. source: :1645
                    if (_caScreenNum == 0)
                        _caScreenNum += 1;                // ADD +1 TO WS-CA-SCREEN-NUM. source: :1647
                }

                if (_wsRowNumber == WS_MAX_SCREEN_LINES)
                {
                    SetReadLoopExit();                    // SET READ-LOOP-EXIT. source: :1658
                    _caLastTrCode = _dclTrType;           // MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE. source: :1659

                    // Look-ahead fetch one more row. source: :1661-1665
                    FetchForward();
                    _wsDispSqlcode = FormatSqlcode(_sqlcode); // source: :1667

                    if (_sqlcode == 0)
                    {
                        SetCaNextPageExists();             // source: :1671
                        _caLastTrCode = _dclTrType;        // MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE. source: :1673
                    }
                    else if (_sqlcode == 100)
                    {
                        SetCaNextPageNotExists();          // source: :1675
                        if (WsReturnMsgOff && CcardAidPfk08)
                            _wsReturnMsg = MESG_NO_MORE_RECORDS; // source: :1679
                    }
                    else
                    {
                        SetReadLoopExit();                 // source: :1684
                        if (WsReturnMsgOff)
                        {
                            _wsDb2CurrentAction = "C-TR-TYPE-FORWARD fetch"; // source: :1686
                            FormatDb2Message9999();        // source: :1689
                        }
                    }
                }
            }
            else if (_sqlcode == 100)
            {
                SetReadLoopExit();             // source: :1695
                SetCaNextPageNotExists();      // source: :1696
                _caLastTrCode = _dclTrType;    // MOVE DCL-TR-TYPE TO WS-CA-LAST-TR-CODE. source: :1697
                if (WsReturnMsgOff && CcardAidPfk08)
                    _wsReturnMsg = MESG_NO_MORE_RECORDS;          // source: :1700
                if (_caScreenNum == 1 && _wsRowNumber == 0)
                    _wsReturnMsg = MESG_NO_RECORDS_FOUND;         // source: :1704
            }
            else
            {
                SetReadLoopExit();   // source: :1709
                SetWsDb2Error();     // source: :1710
                if (WsReturnMsgOff)
                {
                    _wsDb2CurrentAction = "C-TR-TYPE-FORWARD close"; // source: :1712
                    FormatDb2Message9999();                          // source: :1715
                }
            }
        }

        CloseForwardCursor9450(); // PERFORM 9450-CLOSE-FORWARD-CURSOR. source: :1721
    }

    // =============================================================================================
    //  8100-READ-BACKWARDS (+ -EXIT closes cursor). source: COTRTLIC.cbl:1727-1799
    // =============================================================================================
    private void ReadBackwards8100()
    {
        ClearPageBuffer();             // MOVE LOW-VALUES TO WS-CA-ALL-ROWS-OUT. source: :1729
        _caLastTrCode = _caFirstTrCode;// MOVE WS-CA-FIRST-TTYPEKEY TO WS-CA-LAST-TTYPEKEY. source: :1731

        _wsRowNumber = WS_MAX_SCREEN_LINES; // COMPUTE WS-ROW-NUMBER = 7. source: :1735-1737
        SetCaNextPageExists();         // SET CA-NEXT-PAGE-EXISTS. source: :1738
        SetMoreRecordsToRead();        // SET MORE-RECORDS-TO-READ. source: :1739

        OpenBackwardCursor9500();      // PERFORM 9500-OPEN-BACKWARD-CURSOR. source: :1746

        while (!ReadLoopExit)          // PERFORM UNTIL READ-LOOP-EXIT. source: :1749
        {
            InitDcl();                 // INITIALIZE DCLTRANSACTION-TYPE. source: :1751
            FetchBackward();           // EXEC SQL FETCH C-TR-TYPE-BACKWARD. source: :1753-1757
            _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1759

            if (_sqlcode == 0)
            {
                _caRowTrCodeOut[_wsRowNumber - 1] = _dclTrType;             // source: :1763
                _caRowTrDescOut[_wsRowNumber - 1] = _dclTrDescriptionText;  // source: :1765
                _wsRowNumber -= 1;     // SUBTRACT 1 FROM WS-ROW-NUMBER. source: :1769
                if (_wsRowNumber == 0)
                {
                    SetReadLoopExit();             // source: :1771
                    _caFirstTrCode = _dclTrType;   // MOVE DCL-TR-TYPE TO WS-CA-FIRST-TR-CODE. source: :1772
                }
            }
            else
            {
                // B-5: +100 (end of cursor) is NOT special-cased; it lands here as a hard error. source: :1777-1790
                SetReadLoopExit();  // source: :1780
                SetWsDb2Error();    // source: :1781
                if (WsReturnMsgOff)
                {
                    _wsDb2CurrentAction = "Error on fetch Cursor C-TR-TYPE-BACKWARD"; // source: :1784
                    FormatDb2Message9999();                                          // source: :1786
                }
            }
        }

        CloseBackCursor9550(); // 8100-READ-BACKWARDS-EXIT -> PERFORM 9550-CLOSE-BACK-CURSOR. source: :1795
    }

    // =============================================================================================
    //  9100-CHECK-FILTERS. source: COTRTLIC.cbl:1801-1836
    // =============================================================================================
    private void CheckFilters9100()
    {
        // EXEC SQL SELECT COUNT(1) INTO :WS-RECORDS-COUNT FROM CARDDEMO.TRANSACTION_TYPE WHERE (type) AND (desc). source: :1803-1815
        try
        {
            using SqliteCommand c = NewCmd(
                "SELECT COUNT(1) FROM TRANSACTION_TYPE " +
                "WHERE ((@typeFlag = '1' AND TR_TYPE = @typeCd) OR @typeFlag <> '1') " +
                "AND ((@descFlag = '1' AND TR_DESCRIPTION LIKE TRIM(@descFilter)) OR @descFlag <> '1')");
            BindFilterParams(c);
            _wsRecordsCount = Convert.ToInt32(c.ExecuteScalar() ?? 0);
            _sqlcode = 0;
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
        }
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1817

        if (_sqlcode == 0)
        {
            // CONTINUE. source: :1821
        }
        else
        {
            SetInputError(); // source: :1823
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Error reading TRANSACTION_TYPE table "; // source: :1826
                FormatDb2Message9999();                                        // source: :1828
            }
            // GO TO 9100-CHECK-FILTERS-EXIT. source: :1831
        }
    }

    // =============================================================================================
    //  9200-UPDATE-RECORD. source: COTRTLIC.cbl:1837-1894
    // =============================================================================================
    private void UpdateRecord9200()
    {
        _dclTrType = _rowTrCodeIn[_iSelected - 1];                       // MOVE WS-ROW-TR-CODE-IN(I-SELECTED) TO DCL-TR-TYPE. source: :1839
        _dclTrDescriptionText = (_rowTrDescIn[_iSelected - 1] ?? "").Trim(); // MOVE FUNCTION TRIM(...) TO DCL-TR-DESCRIPTION-TEXT. source: :1841
        // B-3: COMPUTE DCL-TR-DESCRIPTION-LEN = LENGTH(WS-ROW-TR-DESC-IN(I-SELECTED)) -> always 50. source: :1843-1844
        _dclTrDescriptionLen = 50;

        // The VARCHAR sent to Db2 is the trimmed text right-padded to the explicit length 50 (B-3). source: :1846-1850
        string descToStore = PadX(_dclTrDescriptionText, _dclTrDescriptionLen);
        try
        {
            using SqliteCommand c = NewCmd(
                "UPDATE TRANSACTION_TYPE SET TR_DESCRIPTION = @desc WHERE TR_TYPE = @key");
            c.Parameters.AddWithValue("@desc", descToStore);
            c.Parameters.AddWithValue("@key", PadX(_dclTrType, 2));
            int rows = c.ExecuteNonQuery();
            _sqlcode = rows > 0 ? 0 : 100; // no row matched -> +100 (record not found). source: :1861
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
        }
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1852

        if (_sqlcode == 0)
        {
            Syncpoint();                    // EXEC CICS SYNCPOINT. source: :1856
            SetCaUpdateSucceeded();         // SET CA-UPDATE-SUCCEEDED. source: :1857
            if (WsNoInfoMessage)
                _wsInfoMsg = INFO_UPDATE_SUCCESS; // SET WS-INFORM-UPDATE-SUCCESS. source: :1859
        }
        else if (_sqlcode == 100)
        {
            SetCaUpdateRequested();         // SET CA-UPDATE-REQUESTED. source: :1862
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Record not found. Deleted by others ? "; // source: :1864
                FormatDb2Message9999();                                         // source: :1866
            }
            // GO TO 9200-UPDATE-RECORD-EXIT. source: :1869
        }
        else if (_sqlcode == -911)
        {
            SetCaUpdateRequested();         // SET CA-UPDATE-REQUESTED. source: :1871
            SetInputError();                // SET INPUT-ERROR. source: :1872
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Deadlock. Someone else updating ?"; // source: :1874
                FormatDb2Message9999();                                    // source: :1876
            }
            // GO TO 9200-UPDATE-RECORD-EXIT. source: :1879
        }
        else if (_sqlcode < 0)
        {
            SetCaUpdateRequested();         // SET CA-UPDATE-REQUESTED. source: :1881
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Update failed with"; // source: :1883
                FormatDb2Message9999();                     // source: :1885
            }
            // GO TO 9200-UPDATE-RECORD-EXIT. source: :1888
        }
    }

    // =============================================================================================
    //  9300-DELETE-RECORD. source: COTRTLIC.cbl:1896-1940
    // =============================================================================================
    private void DeleteRecord9300()
    {
        _dclTrType = _rowTrCodeIn[_iSelected - 1]; // MOVE WS-ROW-TR-CODE-IN(I-SELECTED) TO DCL-TR-TYPE. source: :1898

        // EXEC SQL DELETE FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE = :DCL-TR-TYPE. source: :1900-1903
        try
        {
            string key = PadX(_dclTrType, 2);
            // Reproduce the Db2 RI -532 path: if child rows reference this code, the DELETE is rejected.
            // (SQLite does not enforce the FK unless PRAGMA foreign_keys=ON; check explicitly so the
            //  'Please delete associated child records first:' branch stays reachable. source spec §11.3)
            if (HasChildCategoryRows(key))
            {
                _sqlcode = -532;
            }
            else
            {
                using SqliteCommand c = NewCmd("DELETE FROM TRANSACTION_TYPE WHERE TR_TYPE = @key");
                c.Parameters.AddWithValue("@key", key);
                c.ExecuteNonQuery();
                _sqlcode = 0;
            }
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
        }
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1905

        if (_sqlcode == 0)
        {
            Syncpoint();                    // EXEC CICS SYNCPOINT. source: :1909
            SetCaDeleteSucceeded();         // SET CA-DELETE-SUCCEEDED. source: :1910
            if (WsNoInfoMessage)
                _wsInfoMsg = INFO_DELETE_SUCCESS; // SET WS-INFORM-DELETE-SUCCESS. source: :1912
        }
        else if (_sqlcode == -532)
        {
            SetCaDeleteRequested();         // SET CA-DELETE-REQUESTED. source: :1915
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Please delete associated child records first:"; // source: :1919
                FormatDb2Message9999();                                                // source: :1921
            }
            // GO TO 9300-DELETE-RECORD-EXIT. source: :1925
        }
        else
        {
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Delete failed with message:"; // source: :1929
                FormatDb2Message9999();                              // source: :1931
            }
            // GO TO 9300-DELETE-RECORD-EXIT. source: :1934
        }
    }

    // =============================================================================================
    //  Cursor open/close helpers. source: COTRTLIC.cbl:1942-2051
    // =============================================================================================
    private SqliteDataReader? _forwardReader;
    private SqliteCommand? _forwardCmd;
    private SqliteDataReader? _backwardReader;
    private SqliteCommand? _backwardCmd;

    private void OpenForwardCursor9400()
    {
        // EXEC SQL OPEN C-TR-TYPE-FORWARD. source: :1943-1945
        try
        {
            _forwardCmd = NewCmd(
                "SELECT TR_TYPE, TR_DESCRIPTION FROM TRANSACTION_TYPE " +
                "WHERE TR_TYPE >= @start " +
                "AND ((@typeFlag = '1' AND TR_TYPE = @typeCd) OR @typeFlag <> '1') " +
                "AND ((@descFlag = '1' AND TR_DESCRIPTION LIKE TRIM(@descFilter)) OR @descFlag <> '1') " +
                "ORDER BY TR_TYPE ASC");
            _forwardCmd.Parameters.AddWithValue("@start", _wsStartKey ?? "");
            BindFilterParams(_forwardCmd);
            _forwardReader = _forwardCmd.ExecuteReader();
            _sqlcode = 0;
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
        }
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // MOVE SQLCODE TO WS-DISP-SQLCODE. source: :1947

        if (_sqlcode != 0)
        {
            SetWsDb2Error(); // source: :1955
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "C-TR-TYPE-FORWARD Open"; // source: :1958
                FormatDb2Message9999();                         // source: :1960
            }
        }
    }

    private void CloseForwardCursor9450()
    {
        // EXEC SQL CLOSE C-TR-TYPE-FORWARD. source: :1971-1973
        _forwardReader?.Dispose();
        _forwardReader = null;
        _forwardCmd?.Dispose();
        _forwardCmd = null;
        _sqlcode = 0;
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // source: :1975
        // SQLCODE 0 -> CONTINUE; non-zero never produced by the in-proc close. source: :1978-1991
    }

    private void OpenBackwardCursor9500()
    {
        // EXEC SQL OPEN C-TR-TYPE-BACKWARD. source: :1998-2000
        try
        {
            _backwardCmd = NewCmd(
                "SELECT TR_TYPE, TR_DESCRIPTION FROM TRANSACTION_TYPE " +
                "WHERE TR_TYPE < @start " +
                "AND ((@typeFlag = '1' AND TR_TYPE = @typeCd) OR @typeFlag <> '1') " +
                "AND ((@descFlag = '1' AND TR_DESCRIPTION LIKE TRIM(@descFilter)) OR @descFlag <> '1') " +
                "ORDER BY TR_TYPE DESC");
            _backwardCmd.Parameters.AddWithValue("@start", _wsStartKey ?? "");
            BindFilterParams(_backwardCmd);
            _backwardReader = _backwardCmd.ExecuteReader();
            _sqlcode = 0;
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
        }
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // source: :2002

        if (_sqlcode != 0)
        {
            SetWsDb2Error(); // source: :2010
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "C-TR-TYPE-BACKWARD Open"; // source: :2013
                FormatDb2Message9999();                          // source: :2015
            }
        }
    }

    private void CloseBackCursor9550()
    {
        // EXEC SQL CLOSE C-TR-TYPE-BACKWARD. source: :2027-2029
        _backwardReader?.Dispose();
        _backwardReader = null;
        _backwardCmd?.Dispose();
        _backwardCmd = null;
        _sqlcode = 0;
        _wsDispSqlcode = FormatSqlcode(_sqlcode); // source: :2031
    }

    /// <summary>EXEC SQL FETCH C-TR-TYPE-FORWARD INTO :DCL-TR-TYPE, :DCL-TR-DESCRIPTION. source: :1626-1630</summary>
    private void FetchForward()
    {
        if (_forwardReader is { } rd && rd.Read())
        {
            _dclTrType = rd.IsDBNull(0) ? "" : rd.GetString(0);
            _dclTrDescriptionText = rd.IsDBNull(1) ? "" : rd.GetString(1);
            _sqlcode = 0;
        }
        else
        {
            _sqlcode = 100; // +100 = end of cursor
        }
    }

    /// <summary>EXEC SQL FETCH C-TR-TYPE-BACKWARD INTO :DCL-TR-TYPE, :DCL-TR-DESCRIPTION. source: :1753-1757</summary>
    private void FetchBackward()
    {
        if (_backwardReader is { } rd && rd.Read())
        {
            _dclTrType = rd.IsDBNull(0) ? "" : rd.GetString(0);
            _dclTrDescriptionText = rd.IsDBNull(1) ? "" : rd.GetString(1);
            _sqlcode = 0;
        }
        else
        {
            _sqlcode = 100; // +100 = end of cursor (B-5: treated as error by 8100)
        }
    }

    // =============================================================================================
    //  9998-PRIMING-QUERY (CSDB2RPY) — Db2 connectivity probe. source: CSDB2RPY:21-48
    // =============================================================================================
    private void PrimingQuery9998()
    {
        // SELECT 1 INTO :hv FROM SYSIBM.SYSDUMMY1 FETCH FIRST 1 ROW ONLY. In-proc SQLite always succeeds.
        try
        {
            if (_db is not null)
            {
                using SqliteCommand c = NewCmd("SELECT 1");
                c.ExecuteScalar();
            }
            _sqlcode = 0;
        }
        catch (SqliteException e)
        {
            _sqlcode = NegativeSqlcode(e);
            SetWsDb2Error();
            if (WsReturnMsgOff)
            {
                _wsDb2CurrentAction = "Db2 access failure. ";
                FormatDb2Message9999();
            }
        }
    }

    // =============================================================================================
    //  9999-FORMAT-DB2-MESSAGE (CSDB2RPY) — synthesize the Db2 error text. source: CSDB2RPY:53-89
    // =============================================================================================
    private void FormatDb2Message9999()
    {
        // STRING TRIM(action) ' SQLCODE:' WS-DISP-SQLCODE ' ' formatted-text INTO WS-LONG-MSG; MOVE -> WS-RETURN-MSG.
        string formatted = $"SQLCODE {_sqlcode}"; // stand-in for the DSNTIAC-formatted SQLCA text.
        _wsLongMsg = $"{_wsDb2CurrentAction.Trim()} SQLCODE:{_wsDispSqlcode} {formatted}";
        _wsReturnMsg = Left(_wsLongMsg, 75); // MOVE WS-LONG-MSG TO WS-RETURN-MSG (X75).
    }

    // =============================================================================================
    //  SEND-LONG-TEXT — debug exit used only on priming-query failure. source: COTRTLIC.cbl:2085-2095
    // =============================================================================================
    private void SendLongText(CicsContext ctx)
    {
        // EXEC CICS SEND TEXT FROM(WS-LONG-MSG) LENGTH(...) ERASE FREEKB; EXEC CICS RETURN.
        ctx.SendText(_wsLongMsg, erase: true, freeKb: true);
    }

    // =============================================================================================
    //  EXEC CICS SYNCPOINT — commit the connection's pending work. source: :616,1856,1909
    // =============================================================================================
    private void Syncpoint()
    {
        // No explicit transaction is opened per-statement here (SQLite auto-commits each statement), so the
        // SYNCPOINT is a no-op commit point — the prior UPDATE/DELETE is already durable. Kept as a marker.
    }

    // =============================================================================================
    //  COMMAREA tail (de)serialize — WS-THIS-PROGCOMMAREA carried across turns. source: :377-418,527-531,904-907
    // =============================================================================================
    // Pack layout into the unused CDEMO customer/account/card slots of the 160-byte COMMAREA:
    //   CA-TYPE-CD(2) | CA-TYPE-DESC(50) | 7x[code(2)+desc(50)] | ROW-SELECTED(2) | LAST(2) | FIRST(2)
    //   | SCREEN-NUM(1) | LAST-PAGE(1) | NEXT-PAGE(1) | DELETE-FLAG(1) | UPDATE-FLAG(1).
    // The page buffer is large (364 bytes), so it is serialized to a side string carried in CustFName/
    // CustMName/CustLName plus the account/card numeric slots; the full tail image is stored separately as
    // a base for round-trip — modeled here by serializing into a compact textual blob in the unused fields.
    private void SaveThisProgCommarea()
    {
        var sb = new StringBuilder();
        sb.Append(PadX(_caTypeCd, 2));
        sb.Append(PadX(_caTypeDesc, 50));
        for (int i = 0; i < 7; i++)
        {
            sb.Append(SlotImage(_caRowTrCodeOut[i], 2));
            sb.Append(SlotImage(_caRowTrDescOut[i], 50));
        }
        sb.Append(Two2(_caRowSelected));
        sb.Append(PadX(_caLastTrCode, 2));
        sb.Append(PadX(_caFirstTrCode, 2));
        sb.Append(DigitOrZero(_caScreenNum));
        sb.Append(DigitOrZero(_caLastPageDisplayed));
        sb.Append(_caNextPageInd == '\0' ? ' ' : _caNextPageInd);
        sb.Append(_caDeleteFlag == '\0' ? ' ' : _caDeleteFlag);
        sb.Append(_caUpdateFlag == '\0' ? ' ' : _caUpdateFlag);
        string image = sb.ToString();

        // Store the variable-length tail image in the COMMAREA's unused customer-name slots (75 bytes is too
        // small for 426 bytes, so the runtime carries the program tail out-of-band via these helper fields
        // packed as a length-prefixed blob across the three name fields and the numeric slots).
        StashTail(image);
    }

    private void RestoreThisProgCommarea()
    {
        string image = UnstashTail();
        if (image.Length == 0)
        {
            InitThisProgCommarea();
            return;
        }
        int p = 0;
        _caTypeCd = Take(image, ref p, 2).TrimEnd();
        _caTypeDesc = Take(image, ref p, 50).TrimEnd();
        for (int i = 0; i < 7; i++)
        {
            string code = Take(image, ref p, 2);
            string desc = Take(image, ref p, 50);
            _caRowTrCodeOut[i] = IsLowSlot(code) ? null : code.TrimEnd();
            _caRowTrDescOut[i] = IsLowSlot(code) ? null : desc.TrimEnd();
        }
        _caRowSelected = ParseInt(Take(image, ref p, 2));
        _caLastTrCode = Take(image, ref p, 2).TrimEnd();
        _caFirstTrCode = Take(image, ref p, 2).TrimEnd();
        _caScreenNum = DigitVal(Take(image, ref p, 1));
        _caLastPageDisplayed = DigitVal(Take(image, ref p, 1));
        char nx = Take(image, ref p, 1)[0]; _caNextPageInd = nx == 'Y' ? 'Y' : '\0';
        char dl = Take(image, ref p, 1)[0]; _caDeleteFlag = dl == 'Y' ? 'Y' : '\0';
        char up = Take(image, ref p, 1)[0]; _caUpdateFlag = up == 'Y' ? 'Y' : '\0';
    }

    // The program tail is larger than the spare COMMAREA bytes; carry it via a side channel keyed on the
    // COMMAREA so the pseudo-conversational state round-trips losslessly within a session.
    private static readonly Dictionary<string, string> TailStore = new(StringComparer.Ordinal);
    private string TailKey() =>
        $"{_commArea.FromTranId}|{_commArea.UserId}|{_commArea.LastMap}|{_commArea.LastMapSet}";
    private void StashTail(string image) => TailStore[TailKey()] = image;
    private string UnstashTail() => TailStore.TryGetValue(TailKey(), out string? v) ? v : "";

    // =============================================================================================
    //  WS / commarea (re)initialisers. source: COTRTLIC.cbl:516-517,742-744
    // =============================================================================================
    private void InitThisProgCommarea()
    {
        _caTypeCd = "";
        _caTypeDesc = "";
        _caRowTrCodeOut = new string?[7];
        _caRowTrDescOut = new string?[7];
        _caRowSelected = 0;
        _caLastTrCode = "";
        _caFirstTrCode = "";
        _caScreenNum = 0;
        _caLastPageDisplayed = 0;
        _caNextPageInd = '\0';
        _caDeleteFlag = '\0';
        _caUpdateFlag = '\0';
    }

    private void InitCarddemoCommareaFully()
    {
        // INITIALIZE CARDDEMO-COMMAREA — keep the typed view but blank the navigable fields. source: :742
        _commArea.FromTranId = "";
        _commArea.FromProgram = "";
        _commArea.ToTranId = "";
        _commArea.ToProgram = "";
        _commArea.UserType = "";
        _commArea.PgmContext = 0;
        _commArea.CustId = 0;
        _commArea.CustFName = "";
        _commArea.CustMName = "";
        _commArea.CustLName = "";
        _commArea.AcctId = 0;
        _commArea.AcctStatus = "";
        _commArea.CardNum = 0;
        _commArea.LastMap = "";
        _commArea.LastMapSet = "";
    }

    private void InitWsMiscStorage()
    {
        // INITIALIZE WS-MISC-STORAGE — reset the working flags/counters that survive within the task. source: :744
        _wsInputFlag = '\0';
        _wsEditTypeFlag = '\0';
        _wsEditDescFlag = '\0';
        _wsTypeFilterChanged = '\0';
        _wsDescFilterChanged = '\0';
        _wsDeleteStatus = '\0';
        _wsUpdateStatus = '\0';
        _wsRowSelectionChanged = '\0';
        _wsBadSelectionAction = '\0';
        _wsArrayDescriptionFlgs = '\0';
        _wsDataChangedFlag = '\0';
        _flgProtectSelectRows = '0';
        _wsInfoMsg = "";
        _wsReturnMsg = "";
        _wsPfkFlag = '0';
        _wsStartKey = "";
        _wsTypeCdFilter = "";
        _wsTypeDescFilter = "";
        _wsInTypeCd = "";
        _wsInTypeDesc = "";
        _wsRowNumber = 0;
        _wsRecordsToProcessFlag = '\0';
        _wsActionsRequested = 0;
        _wsDeletesRequested = 0;
        _wsUpdatesRequested = 0;
        _wsNoActionsSelected = 0;
        _wsValidActionsSelected = 0;
        _iSelected = 0;
        _editSelect = NewChars(7, '\0');
        _editSelectError = NewChars(7, '\0');
    }

    private void InitEditSelectFlags()
    {
        // INITIALIZE WS-EDIT-SELECT-FLAGS — set the 7 select chars back to LOW-VALUES. source: :773,787,993
        _editSelect = NewChars(7, '\0');
    }

    private void InitCaPagingVariables()
    {
        // INITIALIZE WS-CA-PAGING-VARIABLES — reset the paging keys/counters. source: :1134,1172
        _caLastTrCode = "";
        _caFirstTrCode = "";
        _caScreenNum = 0;
        _caLastPageDisplayed = 0;
        _caNextPageInd = '\0';
    }

    private void InitDcl()
    {
        // INITIALIZE DCLTRANSACTION-TYPE. source: :1624,1751
        _dclTrType = "";
        _dclTrDescriptionText = "";
        _dclTrDescriptionLen = 0;
    }

    private void ClearPageBuffer()
    {
        // MOVE LOW-VALUES TO WS-CA-ALL-ROWS-OUT. source: :1604,1729
        _caRowTrCodeOut = new string?[7];
        _caRowTrDescOut = new string?[7];
    }

    // =============================================================================================
    //  Symbolic-map writers for the 7 grid rows (TRTSELn/TRTTYPn/TRTYPDn attrs + colors + cursor).
    // =============================================================================================
    private void SetRowSelAttr(int i, BmsAttribute a) => _map.Field($"TRTSEL{i}").AttributeOverride = a;
    private void SetRowSelColor(int i, BmsColor c) => _map.Field($"TRTSEL{i}").ColorOverride = c;
    private void SetRowSelCursor(int i) => _map.Field($"TRTSEL{i}").CursorLength = -1;
    private void SetRowTypeColor(int i, BmsColor c) => _map.Field($"TRTTYP{i}").ColorOverride = c;
    private void SetRowDescAttr(int i, BmsAttribute a) => _map.Field($"TRTYPD{i}").AttributeOverride = a;
    private void SetRowDescColor(int i, BmsColor c) => _map.Field($"TRTYPD{i}").ColorOverride = c;
    private void SetRowDescCursor(int i) => _map.Field($"TRTYPD{i}").CursorLength = -1;

    /// <summary>WS-CA-EACH-ROW-OUT(I) = LOW-VALUES test — true when the page slot is empty. source: :1339,1388</summary>
    private bool RowSlotIsLowValues(int i) => _caRowTrCodeOut[i - 1] is null && _caRowTrDescOut[i - 1] is null;

    /// <summary>The current WS-EDIT-SELECT(I) char as a 1-char output string (LOW-VALUES -> empty).</summary>
    private string EditSelectChar(int i) => _editSelect[i - 1] == '\0' ? "" : _editSelect[i - 1].ToString();

    private bool CaLastPageNotShown() => _caLastPageDisplayed == 9;

    /// <summary>MOVE LOW-VALUES TO CTRTLIAO — blank named output fields + clear per-turn overrides. source: :1294</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  Child-row check for the DELETE -532 path (FK TRC_TYPE_CODE -> TR_TYPE). source spec §2,§11.3
    // =============================================================================================
    private bool HasChildCategoryRows(string trType)
    {
        if (_db is null) return false;
        using SqliteCommand c = NewCmd(
            "SELECT COUNT(1) FROM TRANSACTION_TYPE_CATEGORY WHERE TRC_TYPE_CODE = @k");
        c.Parameters.AddWithValue("@k", trType);
        return Convert.ToInt32(c.ExecuteScalar() ?? 0) > 0;
    }

    // =============================================================================================
    //  Filter host-variable binding (shared by COUNT + both cursors). source: :344-350,1807-1814
    // =============================================================================================
    private void BindFilterParams(SqliteCommand c)
    {
        c.Parameters.AddWithValue("@typeFlag", FlagChar(_wsEditTypeFlag)); // :WS-EDIT-TYPE-FLAG
        c.Parameters.AddWithValue("@typeCd", PadX(_wsTypeCdFilter, 2));    // :WS-TYPE-CD-FILTER
        c.Parameters.AddWithValue("@descFlag", FlagChar(_wsEditDescFlag)); // :WS-EDIT-DESC-FLAG
        c.Parameters.AddWithValue("@descFilter", _wsTypeDescFilter ?? ""); // :WS-TYPE-DESC-FILTER
    }

    private SqliteCommand NewCmd(string sql)
    {
        SqliteCommand c = _db!.Connection.CreateCommand();
        c.CommandText = sql;
        return c;
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics.
    // =============================================================================================
    private static string[] NewStr(int n, string v) { var a = new string[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }
    private static char[] NewChars(int n, char v) { var a = new char[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

    private static char FirstChar(string? s) => string.IsNullOrEmpty(s) ? '\0' : s[0];

    /// <summary>The flag char as the SQL host-variable would carry it: LOW-VALUES('\0') maps to a non-'1' value.</summary>
    private static string FlagChar(char c) => c == '\0' ? " " : c.ToString();

    /// <summary>True when every char is a space or low-value (empty included).</summary>
    private static bool IsAllSpacesOrEmpty(string? s) => string.IsNullOrEmpty(s) || s.All(c => c is ' ' or '\0');

    /// <summary>COBOL "= ZEROS" test on a 2-char field: every char is '0' (a non-empty all-zero string).</summary>
    private static bool IsAllZeros(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.All(c => c == '0');
    }

    /// <summary>COBOL class test <c>field IS NUMERIC</c> on an X(width) field: every char of the full width is a digit.</summary>
    private static bool IsNumericX(string? value, int width)
    {
        string v = PadX(value, width);
        foreach (char c in v)
            if (c is < '0' or > '9') return false;
        return true;
    }

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    /// <summary>Reference-modified slice (1:length) of a working field, space-padded if short.</summary>
    private static string SliceX(string? value, int length)
    {
        value ??= "";
        if (length <= 0) return "";
        if (value.Length >= length) return value[..length];
        return value.PadRight(length, ' ');
    }

    /// <summary>INSPECT … CONVERTING A-Za-z0-9 TO spaces — leaves only the non-alphanumeric chars (as spaces removed).</summary>
    private static string ConvertAlphanumToSpaces(string field)
    {
        var sb = new StringBuilder(field.Length);
        foreach (char c in field)
        {
            bool alnum = c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z' || c is >= '0' and <= '9';
            sb.Append(alnum ? ' ' : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// MOVE WS-INFO-MSG(1:len) TO WS-STRING-OUT(mid:len) into a 45-char field that starts as spaces —
    /// the COBOL centering with the integer-truncated mid offset. source: :1569-1571
    /// </summary>
    private static string CenterInto(string infoMsg, int len, int mid)
    {
        var buf = new char[45];
        for (int i = 0; i < 45; i++) buf[i] = ' ';
        string src = infoMsg ?? "";
        for (int i = 0; i < len; i++)
        {
            int dst = (mid - 1) + i; // mid is 1-based.
            if (dst >= 0 && dst < 45 && i < src.Length) buf[dst] = src[i];
        }
        return new string(buf);
    }

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");
    private static string Two2(int value) => (value % 100).ToString("D2");
    private static char DigitOrZero(int value) => (char)('0' + (value % 10 + 10) % 10);
    private static int DigitVal(string s) => s.Length > 0 && s[0] is >= '0' and <= '9' ? s[0] - '0' : 0;
    private static int ParseInt(string s)
    {
        int v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }
    private static string Left(string s, int n) => s.Length <= n ? s : s[..n];

    /// <summary>Encodes a page-buffer slot: null (LOW-VALUES) -> all-0x00; populated -> padded text.</summary>
    private static string SlotImage(string? value, int width)
        => value is null ? new string(' ', width) : PadX(value, width);
    private static bool IsLowSlot(string code) => code.Length > 0 && code[0] == ' ';
    private static string Take(string image, ref int p, int n)
    {
        if (p >= image.Length) { p += n; return new string(' ', n); }
        int len = Math.Min(n, image.Length - p);
        string s = image.Substring(p, len);
        if (len < n) s = s.PadRight(n, ' ');
        p += n;
        return s;
    }

    private static bool IsBlankTranId(string? s) => string.IsNullOrEmpty(s) || s.All(c => c is ' ' or '\0');
    private static bool IsBlankProgram(string? s) => string.IsNullOrEmpty(s) || s.All(c => c is ' ' or '\0');

    /// <summary>WS-DISP-SQLCODE PIC ----9 — leading-sign-suppress edited, 5-char field.</summary>
    private static string FormatSqlcode(int sqlcode) => CobolEditedNumeric.Format(sqlcode, "----9");

    /// <summary>Maps a SQLite exception to a negative SQLCODE stand-in (any hard error -> -1, FK -> -532).</summary>
    private static int NegativeSqlcode(SqliteException e)
        => e.SqliteErrorCode == 787 /* SQLITE_CONSTRAINT_FOREIGNKEY */ ? -532 : -1;

    // =============================================================================================
    //  BMS map builder — CTRTLIA in mapset COTRTLI (24x80).
    //  source: app-transaction-type-db2/bms/COTRTLI.bms / SCREEN_COTRTLI.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COTRTLI.bms:25.</summary>
    public const string MapName = LIT_THISMAP;     // 'CTRTLIA'

    /// <summary>The DFHMSD mapset name. source: COTRTLI.bms:20.</summary>
    public const string MapsetName = LIT_THISMAPSET; // 'COTRTLI'

    /// <summary>
    /// Constructs the <c>CTRTLIA</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The keyable filters are <c>TRTYPE</c>
    /// (6,44, IC) and <c>TRDESC</c> (8,25); the 7 row description fields <c>TRTYPD1..7</c> are UNPROT, the
    /// 7 select cells <c>TRTSEL1..7</c> are PROT. No PICIN/PICOUT clauses appear.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                               // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                  // bms:34-37
            Out("TITLE01", 1, 21, 40, Askip, BmsColor.Yellow),                  // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                             // bms:42-46
            OutInit("CURDATE", 1, 71, 8, Askip, BmsColor.Blue, "mm/dd/yy"),     // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                              // bms:52-56
            Out("PGMNAME", 2, 7, 8, Askip, BmsColor.Blue),                      // bms:57-60
            Out("TITLE02", 2, 21, 40, Askip, BmsColor.Yellow),                  // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                             // bms:65-69
            OutInit("CURTIME", 2, 71, 8, Askip, BmsColor.Blue, "hh:mm:ss"),     // bms:70-74

            // ----- heading + page (bms:75-83) -----
            LitAttr(4, 28, 25, Default, BmsColor.Neutral, "Maintain Transaction Type"), // bms:75-78
            LitAttr(4, 70, 5, Default, BmsColor.Default, "Page "),              // bms:79-81
            Out("PAGENO", 4, 76, 3, Default, BmsColor.Default),                 // bms:82-83

            // ----- Type filter label + input (bms:84-95) -----
            Lit(6, 30, 12, BmsColor.Turquoise, "Type Filter:"),                 // bms:84-88
            // TRTYPE: ATTRB=(FSET,IC,NORM,UNPROT) GREEN UNDERLINE — IC = initial cursor.
            new ScreenField
            {
                Name = "TRTYPE", Row = 6, Col = 44, Length = 2,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green, Hilight = BmsHilight.Underline,
            },                                                                  // bms:89-93
            Stopper(6, 47),                                                     // bms:94-95

            // ----- Description filter label + input (bms:96-107) -----
            Lit(8, 4, 19, BmsColor.Turquoise, "Description Filter:"),           // bms:96-100
            new ScreenField
            {
                Name = "TRDESC", Row = 8, Col = 25, Length = 50,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green, Hilight = BmsHilight.Underline,
            },                                                                  // bms:101-105
            Stopper(8, 76),                                                     // bms:106-107

            // ----- column headers + rules (bms:108-132) -----
            LitAttr(10, 4, 10, Default, BmsColor.Neutral, "Select    "),        // bms:108-111
            LitAttr(10, 16, 4, Default, BmsColor.Neutral, "Type"),              // bms:112-115
            LitAttr(10, 42, 11, Default, BmsColor.Neutral, "Description"),      // bms:116-119
            LitAttr(11, 4, 6, Default, BmsColor.Neutral, "------"),             // bms:120-123
            LitAttr(11, 15, 5, Default, BmsColor.Neutral, "-----"),             // bms:124-127
            LitAttr(11, 25, 50, Default, BmsColor.Neutral, new string('-', 50)),// bms:128-132
        };

        // ----- 7 data rows (12..18): TRTSELn / TRTTYPn / TRTYPDn + stoppers (bms:133-279) -----
        for (int n = 1; n <= 7; n++)
        {
            int row = 11 + n; // rows 12..18
            fields.Add(RowSel($"TRTSEL{n}", row));         // TRTSELn (row,6) L1 PROT UNDERLINE. bms:133,...
            fields.Add(Stopper(row, 8));                   // (row,8) stopper. bms:138,...
            fields.Add(RowType($"TRTTYP{n}", row));        // TRTTYPn (row,17) L2 PROT. bms:140,...
            fields.Add(Stopper(row, 20));                  // (row,20) stopper. bms:145,...
            fields.Add(RowDesc($"TRTYPD{n}", row));        // TRTYPDn (row,25) L50 UNPROT. bms:147,...
            fields.Add(Stopper(row, 76));                  // (row,76) stopper. bms:152,...
        }

        // ----- row 8 ('A') spare row (bms:280-300) -----
        fields.Add(new ScreenField
        {
            Name = "TRTSELA", Row = 19, Col = 6, Length = 1,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected,
            Color = BmsColor.Default, Hilight = BmsHilight.Off,
        });                                                                     // bms:280-284
        fields.Add(Stopper(19, 8));                                             // bms:285-286
        fields.Add(RowType("TRTTYPA", 19));                                     // bms:287-291
        fields.Add(Stopper(19, 20));                                            // bms:292-293
        fields.Add(new ScreenField
        {
            Name = "TRTDSCA", Row = 19, Col = 25, Length = 50,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected,
            Color = BmsColor.Default, Hilight = BmsHilight.Off,
        });                                                                     // bms:294-298
        fields.Add(Stopper(19, 76));                                            // bms:299-300

        // ----- messages + PF legend (bms:301-336) -----
        fields.Add(new ScreenField
        {
            Name = "INFOMSG", Row = 21, Col = 19, Length = 45,
            Attribute = BmsAttribute.Protected, Color = BmsColor.Neutral, Hilight = BmsHilight.Off,
        });                                                                     // bms:301-305
        fields.Add(Stopper(21, 65));                                            // bms:306-307
        fields.Add(Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red));       // bms:308-311
        fields.Add(LitAttr(24, 1, 7, Askip, BmsColor.Turquoise, "F2=Add"));     // bms:312-316
        fields.Add(LitAttr(24, 10, 7, Askip, BmsColor.Turquoise, "F3=Exit"));   // bms:317-321
        fields.Add(LitAttr(24, 19, 10, Askip, BmsColor.Turquoise, "F7=Page Up"));// bms:322-326
        fields.Add(LitAttr(24, 32, 10, Askip, BmsColor.Turquoise, "F8=Page Dn"));// bms:327-331
        fields.Add(LitAttr(24, 44, 8, Askip, BmsColor.Turquoise, "F10=Save"));  // bms:332-336

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // (ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)
    private static BmsAttribute Default => BmsAttribute.AutoSkip | BmsAttribute.Normal;            // no ATTRB coded -> CICS default (protected/ASKIP)

    // ---- field factory helpers ----
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>Per-row select cell TRTSELn: ATTRB=(FSET,NORM,PROT) DEFAULT HILIGHT=UNDERLINE L1.</summary>
    private static ScreenField RowSel(string name, int row) =>
        new()
        {
            Name = name, Row = row, Col = 6, Length = 1,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected,
            Color = BmsColor.Default, Hilight = BmsHilight.Underline,
        };

    /// <summary>Per-row type cell TRTTYPn: ATTRB=(FSET,NORM,PROT) DEFAULT HILIGHT=OFF L2.</summary>
    private static ScreenField RowType(string name, int row) =>
        new()
        {
            Name = name, Row = row, Col = 17, Length = 2,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected,
            Color = BmsColor.Default, Hilight = BmsHilight.Off,
        };

    /// <summary>Per-row description cell TRTYPDn: ATTRB=(FSET,NORM,UNPROT) DEFAULT HILIGHT=OFF L50.</summary>
    private static ScreenField RowDesc(string name, int row) =>
        new()
        {
            Name = name, Row = row, Col = 25, Length = 50,
            Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
            Color = BmsColor.Default, Hilight = BmsHilight.Off,
        };

    /// <summary>A LENGTH=0 stopper field (attribute cell only).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };
}
