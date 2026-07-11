using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COUSR02C</c> — the admin "Update User" screen that
/// reads an existing USRSEC (USER_SECURITY) record for update and rewrites the changed fields (TRANSID
/// <c>CU02</c>, BMS map <c>COUSR2A</c> / mapset <c>COUSR02</c>).
/// </summary>
/// <remarks>
/// <para>
/// The operator types an 8-char User ID and presses ENTER to <b>fetch</b> the existing record (first name,
/// last name, password, user type) into the editable fields, edits one or more of them, then presses
/// <b>PF5</b> to save (REWRITE) the record back to USRSEC, or <b>PF3</b> to save-and-exit. PF4 clears the
/// screen; PF12 cancels (no save) back to <c>COADM01C</c>. It is pseudo-conversational: it re-drives itself
/// via <c>RETURN TRANSID('CU02')</c>. It is typically reached by XCTL from the user-list program
/// (<c>COUSR00C</c>), which pre-selects a user in <c>CDEMO-CU02-USR-SELECTED</c> and triggers an auto-fetch.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COUSR02C.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-specific <c>CDEMO-CU02-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the USRSEC master is accessed by primary key (X8 user-id):
/// <c>READ ... UPDATE</c> = <see cref="UserSecurityRepository.ReadByKey"/> (RESP NORMAL/NOTFND), and
/// <c>REWRITE</c> = <see cref="UserSecurityRepository.Update"/> (RESP NORMAL when a row was updated,
/// NOTFND when the key vanished). The repository FileStatus maps to the CICS RESP the COBOL
/// <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13),
/// anything else→the OTHER/"Unable to ..." branch. Per the spec, the READ in <c>UPDATE-USER-INFO</c> is
/// immediately followed by the REWRITE within the same handler invocation, so no held cross-turn lock is
/// needed; the read-then-update is faithful.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — Successful READ fires an <b>extra</b> SEND with "Press PF5 key to save your updates ..."
/// <i>before</i> control returns to the caller. On the ENTER path PROCESS-ENTER-KEY then re-sends the screen
/// with the fetched field values (so the data send wins); on the PF5/PF3 (UPDATE-USER-INFO) path that
/// "Press PF5..." SEND fires mid-update even though the program is about to rewrite. The double-SEND is
/// reproduced exactly. source: COUSR02C.cbl:334-339,166-172,215-217</item>
/// <item>FB-2 — Dead <c>CONTINUE</c> before the message MOVE in the NORMAL read branch — a no-op left in
/// place; preserved structurally. source: COUSR02C.cbl:334-336</item>
/// <item>FB-3 — PASSWD field is FSET+DRK (non-display): it is always returned on RECEIVE even when the
/// operator did not retype it, but the fetched value is never visible. Change-detection compares whatever
/// the terminal returns. Attribute behaviour reproduced as-is. source: bms/COUSR02.bms:130-134; cbl:227-230</item>
/// <item>FB-4 — No re-validation of USRTYPE value: any single non-space char (e.g. 'Z') is accepted and
/// written; no A/U enforcement. Not fixed. source: COUSR02C.cbl:204-209,231-234</item>
/// <item>FB-5 — <c>DISPLAY 'RESP:'...'REAS:'...</c> on the error branches writes to the CICS region log;
/// here it is a no-op (diagnostic sink), not surfaced to the user. source: COUSR02C.cbl:347,384</item>
/// </list>
/// </remarks>
public sealed class UserUpdateProgram : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COUSR02C.cbl:35-47
    // =============================================================================================
    private const string ProgramId = "COUSR02C";     // WS-PGMNAME PIC X(08) VALUE 'COUSR02C'. source: :36
    private const string TranId = "CU02";          // WS-TRANID  PIC X(04) VALUE 'CU02'.     source: :37
    private const string UserSecFileName = "USRSEC  "; // WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC  '. source: :39

    private string _message = "";                   // WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errorFlagOn;                       // WS-ERR-FLG
    private bool ErrFlgOn => _errorFlagOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :43-44
    private int _responseCode;                       // WS-RESP-CD
    private int _reasonCode;                          // WS-REAS-CD

    // 05 WS-USR-MODIFIED PIC X(01) VALUE 'N'. 88 USR-MODIFIED-YES='Y' / USR-MODIFIED-NO='N'. source: :45-47
    private bool _userModified;                       // WS-USR-MODIFIED
    private bool UsrModifiedYes => _userModified;   // 88 USR-MODIFIED-YES
    private void SetUsrModifiedYes() => _userModified = true;  // SET USR-MODIFIED-YES TO TRUE
    private void SetUsrModifiedNo() => _userModified = false;  // SET USR-MODIFIED-NO  TO TRUE

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared screen header / messages.
    private const string Title01 = "      AWS Mainframe Modernization       ";       // CCDA-TITLE01
    private const string Title02 = "              CardDemo                  ";       // CCDA-TITLE02
    private const string InvalidKeyMessage = "Invalid key pressed. Please see below...         "; // CCDA-MSG-INVALID-KEY

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CU02-INFO (the program-private selection state). source: :50-58
    // =============================================================================================
    // Only CDEMO-CU02-USR-SELECTED is read by this program (pre-selected user from the list screen).
    // 10 CDEMO-CU02-USRID-FIRST   PIC X(08). source: :51
    private string _firstUserId = "";                 // CDEMO-CU02-USRID-FIRST
    // 10 CDEMO-CU02-USRID-LAST    PIC X(08). source: :52
    private string _lastUserId = "";                  // CDEMO-CU02-USRID-LAST
    // 10 CDEMO-CU02-PAGE-NUM      PIC 9(08). source: :53
    private int _pageNumber;                          // CDEMO-CU02-PAGE-NUM
    // 10 CDEMO-CU02-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. source: :54
    private char _nextPageFlag = 'N';                 // CDEMO-CU02-NEXT-PAGE-FLG
    // 10 CDEMO-CU02-USR-SEL-FLG   PIC X(01). source: :57
    private string _userSelectFlag = "";              // CDEMO-CU02-USR-SEL-FLG
    // 10 CDEMO-CU02-USR-SELECTED  PIC X(08). source: :58
    private string _selectedUserId = "";              // CDEMO-CU02-USR-SELECTED

    // =============================================================================================
    //  SEC-USER-DATA (CSUSR01Y) — the keyed record read for update. source: :324-326
    // =============================================================================================
    // SEC-USR-ID X(8) — the READ/REWRITE RID; the record buffer mutated by the change-detection MOVEs.
    private string _recordKey = "";                   // SEC-USR-ID
    private UserSecurity? _userRecord;                // SEC-USER-DATA

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :74-76,94,135-138
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private UserSecurityRepository _users = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The USER_SECURITY repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public UserUpdateProgram(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public UserUpdateProgram() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => ProgramId; // PROGRAM-ID. COUSR02C. source: :23

    /// <inheritdoc/>
    public string TransId => TranId;      // CSD: CU02 -> COUSR02C. source: CSD_TRANSACTIONS.md:86; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COUSR02C.cbl:82-138
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COUSR02 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _users = new UserSecurityRepository(_db.Connection);

        // SET ERR-FLG-OFF TO TRUE / SET USR-MODIFIED-NO TO TRUE. source: :84-85
        _errorFlagOn = false;
        SetUsrModifiedNo();

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COUSR2AO. source: :87-88
        _message = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :90-92
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :94
            _commArea = ctx.CommArea!;
            RestoreSelectionInfo();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :95
                _commArea.SetReenter();                 // SET CDEMO-PGM-REENTER TO TRUE. source: :96
                MoveLowValuesToMapOut();                // MOVE LOW-VALUES TO COUSR2AO. source: :97
                _map.Field("USRIDIN").CursorLength = -1; // MOVE -1 TO USRIDINL OF COUSR2AI. source: :98

                // IF CDEMO-CU02-USR-SELECTED NOT = SPACES AND LOW-VALUES. source: :99-100
                if (NotSpacesOrLow(_selectedUserId))
                {
                    // MOVE CDEMO-CU02-USR-SELECTED TO USRIDINI OF COUSR2AI. source: :101-102
                    _map.Field("USRIDIN").SetValue(_selectedUserId, setMdt: false);
                    ProcessEnterKey(ctx);               // PERFORM PROCESS-ENTER-KEY. source: :103
                }
                SendUserUpdateScreen(ctx);                  // PERFORM SEND-USRUPD-SCREEN. source: :105
            }
            else
            {
                ReceiveUserUpdateScreen(ctx);               // PERFORM RECEIVE-USRUPD-SCREEN. source: :107
                // EVALUATE EIBAID. source: :108-131
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);           // WHEN DFHENTER. source: :109-110
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3. source: :111-119
                        UpdateUserInfo(ctx);            // PERFORM UPDATE-USER-INFO. source: :112
                        if (IsSpacesOrLowValues(_commArea.FromProgram)) // IF CDEMO-FROM-PROGRAM = SPACES OR LOW-VALUES. source: :113
                            _commArea.ToProgram = "COADM01C";           // MOVE 'COADM01C' TO CDEMO-TO-PROGRAM. source: :114
                        else
                            _commArea.ToProgram = _commArea.FromProgram; // MOVE CDEMO-FROM-PROGRAM TO CDEMO-TO-PROGRAM. source: :116-117
                        ReturnToPrevScreen(ctx);        // PERFORM RETURN-TO-PREV-SCREEN. source: :119
                        break;
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);        // WHEN DFHPF4 -> PERFORM CLEAR-CURRENT-SCREEN. source: :120-121
                        break;
                    case AidKey.Pf5:
                        UpdateUserInfo(ctx);            // WHEN DFHPF5 -> PERFORM UPDATE-USER-INFO. source: :122-123
                        break;
                    case AidKey.Pf12:
                        _commArea.ToProgram = "COADM01C"; // WHEN DFHPF12 -> MOVE 'COADM01C' TO CDEMO-TO-PROGRAM. source: :124-125
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :126
                        break;
                    default:
                        // WHEN OTHER. source: :127-130
                        _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :128
                        _message = InvalidKeyMessage;             // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. source: :129
                        SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :130
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :135-138
        if (ctx.Outcome is null)
        {
            SaveSelectionInfo();
            ctx.ReturnTransId(TranId, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COUSR02C.cbl:143-172
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE. source: :145-155
        if (IsSpacesOrLowValues(_map.Field("USRIDIN").Value))
        {
            // WHEN USRIDINI = SPACES OR LOW-VALUES. source: :146-151
            _errorFlagOn = true;                            // MOVE 'Y' TO WS-ERR-FLG. source: :147
            _message = "User ID can NOT be empty...";  // source: :148-149
            _map.Field("USRIDIN").CursorLength = -1;      // MOVE -1 TO USRIDINL. source: :150
            SendUserUpdateScreen(ctx);                       // PERFORM SEND-USRUPD-SCREEN. source: :151
        }
        else
        {
            // WHEN OTHER -> MOVE -1 TO USRIDINL; CONTINUE. source: :152-154
            _map.Field("USRIDIN").CursorLength = -1;
        }

        // IF NOT ERR-FLG-ON. source: :157-164
        if (!ErrFlgOn)
        {
            // MOVE SPACES TO FNAMEI/LNAMEI/PASSWDI/USRTYPEI OF COUSR2AI. source: :158-161
            _map.Field("FNAME").SetValue("", setMdt: false);
            _map.Field("LNAME").SetValue("", setMdt: false);
            _map.Field("PASSWD").SetValue("", setMdt: false);
            _map.Field("USRTYPE").SetValue("", setMdt: false);
            // MOVE USRIDINI OF COUSR2AI TO SEC-USR-ID. source: :162
            _recordKey = PadX(_map.Field("USRIDIN").Value, 8);
            ReadUserSecFile(ctx);                        // PERFORM READ-USER-SEC-FILE. source: :163
        }

        // IF NOT ERR-FLG-ON. source: :166-172
        if (!ErrFlgOn)
        {
            // MOVE SEC-USR-FNAME/LNAME/PWD/TYPE TO the corresponding screen input fields. source: :167-170
            _map.Field("FNAME").SetValue(StoredFirstName, setMdt: false);
            _map.Field("LNAME").SetValue(StoredLastName, setMdt: false);
            _map.Field("PASSWD").SetValue(StoredPassword, setMdt: false);
            _map.Field("USRTYPE").SetValue(StoredUserType, setMdt: false);
            SendUserUpdateScreen(ctx);                       // PERFORM SEND-USRUPD-SCREEN. source: :171
        }
    }

    // =============================================================================================
    //  UPDATE-USER-INFO — source: COUSR02C.cbl:177-245
    // =============================================================================================
    private void UpdateUserInfo(CicsContext ctx)
    {
        // EVALUATE TRUE — sequential field-empty validation; first failing branch wins. source: :179-213
        if (IsSpacesOrLowValues(_map.Field("USRIDIN").Value))
        {
            // WHEN USRIDINI = SPACES OR LOW-VALUES. source: :180-185
            _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :181
            _message = "User ID can NOT be empty...";    // source: :182-183
            _map.Field("USRIDIN").CursorLength = -1;        // MOVE -1 TO USRIDINL. source: :184
            SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :185
        }
        else if (IsSpacesOrLowValues(_map.Field("FNAME").Value))
        {
            // WHEN FNAMEI = SPACES OR LOW-VALUES. source: :186-191
            _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :187
            _message = "First Name can NOT be empty...";  // source: :188-189
            _map.Field("FNAME").CursorLength = -1;          // MOVE -1 TO FNAMEL. source: :190
            SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :191
        }
        else if (IsSpacesOrLowValues(_map.Field("LNAME").Value))
        {
            // WHEN LNAMEI = SPACES OR LOW-VALUES. source: :192-197
            _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :193
            _message = "Last Name can NOT be empty...";   // source: :194-195
            _map.Field("LNAME").CursorLength = -1;          // MOVE -1 TO LNAMEL. source: :196
            SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :197
        }
        else if (IsSpacesOrLowValues(_map.Field("PASSWD").Value))
        {
            // WHEN PASSWDI = SPACES OR LOW-VALUES. source: :198-203
            _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :199
            _message = "Password can NOT be empty...";    // source: :200-201
            _map.Field("PASSWD").CursorLength = -1;         // MOVE -1 TO PASSWDL. source: :202
            SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :203
        }
        else if (IsSpacesOrLowValues(_map.Field("USRTYPE").Value))
        {
            // WHEN USRTYPEI = SPACES OR LOW-VALUES. source: :204-209
            _errorFlagOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :205
            _message = "User Type can NOT be empty...";   // source: :206-207
            _map.Field("USRTYPE").CursorLength = -1;        // MOVE -1 TO USRTYPEL. source: :208
            SendUserUpdateScreen(ctx);                         // PERFORM SEND-USRUPD-SCREEN. source: :209
        }
        else
        {
            // WHEN OTHER -> MOVE -1 TO FNAMEL; CONTINUE. source: :210-212
            _map.Field("FNAME").CursorLength = -1;
        }

        // IF NOT ERR-FLG-ON. source: :215-245
        if (!ErrFlgOn)
        {
            // MOVE USRIDINI OF COUSR2AI TO SEC-USR-ID; PERFORM READ-USER-SEC-FILE. source: :216-217
            _recordKey = PadX(_map.Field("USRIDIN").Value, 8);
            ReadUserSecFile(ctx);

            // Field-by-field change detection — fixed-width NOT = byte comparison; if differs, copy into
            // record buffer and SET USR-MODIFIED-YES. FB-4: USRTYPE is not value-validated. source: :219-234
            if (FieldX(_map.Field("FNAME").Value, 20) != StoredFirstName)      // IF FNAMEI NOT = SEC-USR-FNAME. source: :219
            {
                StoredFirstName = FieldX(_map.Field("FNAME").Value, 20);       // MOVE FNAMEI TO SEC-USR-FNAME. source: :220
                SetUsrModifiedYes();                                       // SET USR-MODIFIED-YES TO TRUE. source: :221
            }
            if (FieldX(_map.Field("LNAME").Value, 20) != StoredLastName)      // IF LNAMEI NOT = SEC-USR-LNAME. source: :223
            {
                StoredLastName = FieldX(_map.Field("LNAME").Value, 20);       // MOVE LNAMEI TO SEC-USR-LNAME. source: :224
                SetUsrModifiedYes();                                       // SET USR-MODIFIED-YES TO TRUE. source: :225
            }
            if (FieldX(_map.Field("PASSWD").Value, 8) != StoredPassword)        // IF PASSWDI NOT = SEC-USR-PWD. source: :227
            {
                StoredPassword = FieldX(_map.Field("PASSWD").Value, 8);         // MOVE PASSWDI TO SEC-USR-PWD. source: :228
                SetUsrModifiedYes();                                       // SET USR-MODIFIED-YES TO TRUE. source: :229
            }
            if (FieldX(_map.Field("USRTYPE").Value, 1) != StoredUserType)      // IF USRTYPEI NOT = SEC-USR-TYPE. source: :231
            {
                StoredUserType = FieldX(_map.Field("USRTYPE").Value, 1);       // MOVE USRTYPEI TO SEC-USR-TYPE. source: :232
                SetUsrModifiedYes();                                       // SET USR-MODIFIED-YES TO TRUE. source: :233
            }

            if (UsrModifiedYes)
            {
                UpdateUserSecFile(ctx);                                    // PERFORM UPDATE-USER-SEC-FILE. source: :237
            }
            else
            {
                // ELSE -> 'Please modify to update ...'; ERRMSGC = DFHRED; SEND. source: :238-242
                _message = "Please modify to update ...";                // source: :239-240
                _map.Field("ERRMSG").ColorOverride = BmsColor.Red;         // MOVE DFHRED TO ERRMSGC. source: :241
                SendUserUpdateScreen(ctx);                                     // PERFORM SEND-USRUPD-SCREEN. source: :242
            }
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COUSR02C.cbl:250-261
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :252-254
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = TranId;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :255
        _commArea.FromProgram = ProgramId; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :256
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :257

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :258-261
        SaveSelectionInfo();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-USRUPD-SCREEN — source: COUSR02C.cbl:266-278
    // =============================================================================================
    private void SendUserUpdateScreen(CicsContext ctx)             // COBOL paragraph: SEND-USRUPD-SCREEN
    {
        PopulateHeaderInfo(ctx);                                    // PERFORM POPULATE-HEADER-INFO. source: :268

        _map.Field("ERRMSG").SetValue(_message, setMdt: false);  // MOVE WS-MESSAGE TO ERRMSGO. source: :270

        // EXEC CICS SEND MAP('COUSR2A') MAPSET('COUSR02') FROM(COUSR2AO) ERASE CURSOR. source: :272-278
        ctx.SendMap("COUSR2A", "COUSR02", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO xxxL the handler set this turn.
        });
        _responseCode = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-USRUPD-SCREEN — source: COUSR02C.cbl:283-291
    // =============================================================================================
    private void ReceiveUserUpdateScreen(CicsContext ctx)         // COBOL paragraph: RECEIVE-USRUPD-SCREEN
    {
        // EXEC CICS RECEIVE MAP('COUSR2A') MAPSET('COUSR02') INTO(COUSR2AI) RESP RESP2. source: :285-291
        // (RESP/RESP2 captured but not inspected in this paragraph.)
        ctx.ReceiveMap("COUSR2A", "COUSR02", _map);
        _responseCode = (int)Resp.Normal;
        _reasonCode = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COUSR02C.cbl:296-315
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :298
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(Title01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :300
        _map.Field("TITLE02").SetValue(Title02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :301
        _map.Field("TRNNAME").SetValue(TranId, setMdt: false);    // MOVE WS-TRANID  TO TRNNAMEO. source: :302
        _map.Field("PGMNAME").SetValue(ProgramId, setMdt: false);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :303

        // CURDATEO = mm/dd/yy (year last two digits). source: :305-309
        _map.Field("CURDATE").SetValue(
            $"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :311-315
        _map.Field("CURTIME").SetValue(
            $"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  READ-USER-SEC-FILE — source: COUSR02C.cbl:320-353
    // =============================================================================================
    private void ReadUserSecFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA) RIDFLD(SEC-USR-ID) UPDATE RESP RESP2.
        // The UPDATE intent (record lock) is not required cross-turn: in UPDATE-USER-INFO the REWRITE
        // immediately follows this READ in the same invocation. source: :322-331
        _ = UserSecFileName; // dataset name (fixed) — repository is keyed by usr_id.
        string fileStatus = _users.ReadByKey(_recordKey, out _userRecord);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :333-353
        switch ((Resp)_responseCode)
        {
            case Resp.Normal:
                // WHEN DFHRESP(NORMAL). FB-2: dead CONTINUE then the message MOVE. FB-1: this branch fires
                // an extra SEND with "Press PF5..." BEFORE control returns to the caller (PROCESS-ENTER-KEY
                // re-sends with data; UPDATE-USER-INFO sends this mid-update). source: :334-339
                // (CONTINUE — no-op. source: :335)
                _message = "Press PF5 key to save your updates ...";    // source: :336-337
                _map.Field("ERRMSG").ColorOverride = BmsColor.Neutral;   // MOVE DFHNEUTR TO ERRMSGC. source: :338
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :339
                break;
            case Resp.NotFnd:
                // WHEN DFHRESP(NOTFND). source: :340-345
                _errorFlagOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :341
                _message = "User ID NOT found...";                     // source: :342-343
                _map.Field("USRIDIN").CursorLength = -1;                  // MOVE -1 TO USRIDINL. source: :344
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :345
                break;
            default:
                // WHEN OTHER. FB-5: DISPLAY 'RESP:'/'REAS:' -> job-log trace; no-op here. source: :346-352
                _errorFlagOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :348
                _message = "Unable to lookup User...";                 // source: :349-350
                _map.Field("FNAME").CursorLength = -1;                    // MOVE -1 TO FNAMEL. source: :351
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :352
                break;
        }
    }

    // =============================================================================================
    //  UPDATE-USER-SEC-FILE — source: COUSR02C.cbl:358-390
    // =============================================================================================
    private void UpdateUserSecFile(CicsContext ctx)
    {
        // EXEC CICS REWRITE DATASET(WS-USRSEC-FILE) FROM(SEC-USER-DATA) LENGTH RESP RESP2. source: :360-366
        string fileStatus = _users.Update(_userRecord!);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :368-390
        switch ((Resp)_responseCode)
        {
            case Resp.Normal:
                // WHEN DFHRESP(NORMAL). source: :369-376
                _message = "";                                         // MOVE SPACES TO WS-MESSAGE. source: :370
                _map.Field("ERRMSG").ColorOverride = BmsColor.Green;     // MOVE DFHGREEN TO ERRMSGC. source: :371
                // STRING 'User ' + SEC-USR-ID(DELIMITED BY SPACE) + ' has been updated ...'. source: :372-375
                // DELIMITED BY SPACE copies SEC-USR-ID only up to its FIRST space (not just trailing).
                string userIdDelimited = StoredUserId.Split(' ')[0];
                _message = $"User {userIdDelimited} has been updated ...";
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :376
                break;
            case Resp.NotFnd:
                // WHEN DFHRESP(NOTFND). source: :377-382
                _errorFlagOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :378
                _message = "User ID NOT found...";                     // source: :379-380
                _map.Field("USRIDIN").CursorLength = -1;                  // MOVE -1 TO USRIDINL. source: :381
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :382
                break;
            default:
                // WHEN OTHER. FB-5: DISPLAY 'RESP:'/'REAS:' -> job-log trace; no-op here. source: :383-389
                _errorFlagOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :385
                _message = "Unable to Update User...";                 // source: :386-387
                _map.Field("FNAME").CursorLength = -1;                    // MOVE -1 TO FNAMEL. source: :388
                SendUserUpdateScreen(ctx);                                   // PERFORM SEND-USRUPD-SCREEN. source: :389
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COUSR02C.cbl:395-398
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();   // PERFORM INITIALIZE-ALL-FIELDS. source: :397
        SendUserUpdateScreen(ctx);   // PERFORM SEND-USRUPD-SCREEN. source: :398
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COUSR02C.cbl:403-411
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("USRIDIN").CursorLength = -1;             // MOVE -1 TO USRIDINL. source: :405
        // MOVE SPACES TO USRIDINI / FNAMEI / LNAMEI / PASSWDI / USRTYPEI / WS-MESSAGE. source: :406-411
        _map.Field("USRIDIN").SetValue("", setMdt: false);  // USRIDINI
        _map.Field("FNAME").SetValue("", setMdt: false);    // FNAMEI
        _map.Field("LNAME").SetValue("", setMdt: false);    // LNAMEI
        _map.Field("PASSWD").SetValue("", setMdt: false);   // PASSWDI
        _map.Field("USRTYPE").SetValue("", setMdt: false);  // USRTYPEI
        _message = "";                                    // WS-MESSAGE
    }

    // =============================================================================================
    //  SEC-USER-DATA field accessors (CSUSR01Y), with COBOL fixed-width formatting.
    // =============================================================================================
    private string StoredUserId => PadX(_userRecord?.UsrId, 8);          // SEC-USR-ID    X(8)
    private string StoredFirstName
    {
        get => PadX(_userRecord?.FirstName, 20);                     // SEC-USR-FNAME X(20)
        set { (_userRecord ??= new UserSecurity()).FirstName = value; }
    }
    private string StoredLastName
    {
        get => PadX(_userRecord?.LastName, 20);                      // SEC-USR-LNAME X(20)
        set { (_userRecord ??= new UserSecurity()).LastName = value; }
    }
    private string StoredPassword
    {
        get => PadX(_userRecord?.Pwd, 8);                            // SEC-USR-PWD   X(8)
        set { (_userRecord ??= new UserSecurity()).Pwd = value; }
    }
    private string StoredUserType
    {
        get => PadX(_userRecord?.UsrType, 1);                        // SEC-USR-TYPE  X(1)
        set { (_userRecord ??= new UserSecurity()).UsrType = value; }
    }

    /// <summary>MOVE LOW-VALUES TO COUSR2AO — blank every named output field + clear per-turn overrides. source: :97</summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed) f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    // =============================================================================================
    //  WS-RESP-CD mapper — repository FileStatus -> CICS RESP. source: EVALUATE WS-RESP-CD branches.
    // =============================================================================================
    private void SetResp(string fileStatus)
    {
        _responseCode = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,                  // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd,      // '23' -> DFHRESP(NOTFND)
            FileStatus.EndOfFile => (int)Resp.EndFile,          // '10' -> DFHRESP(ENDFILE)
            FileStatus.DuplicateKey => (int)Resp.DupRec,        // '02' -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                               // any other -> WHEN OTHER (file error)
        };
        _reasonCode = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  CDEMO-CU02-INFO (de)serialize — carried across turns in the COMMAREA's unused customer slots.
    //  source: COUSR02C.cbl:50-58,94,135-138
    // =============================================================================================
    // COUSR02C only reads CDEMO-CU02-USR-SELECTED; the trailer is packed into CDEMO-CUSTOMER-INFO so the
    // selection round-trips losslessly with COUSR00C (which produced it). Pack layout into
    // CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   USRID-FIRST X(8) | USRID-LAST X(8) | PAGE-NUM 9(8) | NEXT X(1) | USR-SEL-FLG X(1) | USR-SELECTED X(8).
    private void SaveSelectionInfo()                              // COBOL: CDEMO-CU02-INFO serialize
    {
        string packed =
            PadX(_firstUserId, 8) +
            PadX(_lastUserId, 8) +
            Zoned(_pageNumber, 8) +
            (_nextPageFlag == '\0' ? "N" : _nextPageFlag.ToString()) +
            PadX(_userSelectFlag, 1) +
            PadX(_selectedUserId, 8);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreSelectionInfo()                          // COBOL: CDEMO-CU02-INFO deserialize
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        if (packed.Length < 75) packed = PadX(packed, 75);
        _firstUserId = packed.Substring(0, 8).TrimEnd();
        _lastUserId = packed.Substring(8, 8).TrimEnd();
        _pageNumber = (int)ParseLong(packed.Substring(16, 8));
        char nx = packed[24];
        _nextPageFlag = nx == 'Y' ? 'Y' : 'N';
        _userSelectFlag = packed.Substring(25, 1).TrimEnd();
        _selectedUserId = packed.Substring(26, 8).TrimEnd();
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "NOT = SPACES AND LOW-VALUES" guard.</summary>
    private static bool NotSpacesOrLow(string? s) => !IsSpacesOrLowValues(s);

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL) — the COBOL "= SPACES OR LOW-VALUES" test.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// A fixed-width X(n) view of a value: NUL (LOW-VALUES) bytes are treated as spaces and the value is
    /// space-padded/truncated to <paramref name="width"/>. Used for the exact-width <c>NOT =</c> change
    /// detection so a never-keyed (LOW-VALUES) field compares equal to a space-filled record field.
    /// source: COUSR02C.cbl:219-234
    /// </summary>
    private static string FieldX(string? value, int width)
    {
        value ??= "";
        var chars = new char[width];
        for (int i = 0; i < width; i++)
        {
            char c = i < value.Length ? value[i] : ' ';
            chars[i] = c == '\0' ? ' ' : c;
        }
        return new string(chars);
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

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    /// <summary>Parses a digit string (ignoring non-digits) to a long; null/empty -> 0.</summary>
    private static long ParseLong(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        long v = 0;
        foreach (char c in s) if (c is >= '0' and <= '9') v = v * 10 + (c - '0');
        return v;
    }

    // =============================================================================================
    //  BMS map builder — COUSR2A in mapset COUSR02 (24x80).
    //  source: app/bms/COUSR02.bms:19-166 / SCREEN_COUSR02.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COUSR02.bms:26.</summary>
    public const string MapName = "COUSR2A";

    /// <summary>The DFHMSD mapset name. source: COUSR02.bms:19.</summary>
    public const string MapsetName = "COUSR02";

    /// <summary>
    /// Constructs the <c>COUSR2A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The keyable fields are <c>USRIDIN</c>
    /// (6,21) L8 (carries <c>IC</c> → the initial cursor lands here), <c>FNAME</c> (11,18) L20,
    /// <c>LNAME</c> (11,56) L20, <c>PASSWD</c> (13,16) L8 (DRK / non-display), and <c>USRTYPE</c> (15,17) L1.
    /// No PICIN/PICOUT clauses appear in this map.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // ----- shared 2-line header (bms:29-74) -----
            Lit(1, 1, 5, BmsColor.Blue, "Tran:"),                               // bms:29-33
            Out("TRNNAME", 1, 7, 4, AskipFset, BmsColor.Blue),                  // bms:34-37
            Out("TITLE01", 1, 21, 40, AskipFset, BmsColor.Yellow),              // bms:38-41
            Lit(1, 65, 5, BmsColor.Blue, "Date:"),                              // bms:42-46
            OutInit("CURDATE", 1, 71, 8, AskipFset, BmsColor.Blue, "mm/dd/yy"), // bms:47-51
            Lit(2, 1, 5, BmsColor.Blue, "Prog:"),                               // bms:52-56
            Out("PGMNAME", 2, 7, 8, AskipFset, BmsColor.Blue),                  // bms:57-60
            Out("TITLE02", 2, 21, 40, AskipFset, BmsColor.Yellow),              // bms:61-64
            Lit(2, 65, 5, BmsColor.Blue, "Time:"),                              // bms:65-69
            OutInit("CURTIME", 2, 71, 8, AskipFset, BmsColor.Blue, "hh:mm:ss"), // bms:70-74

            // ----- 'Update User' heading (bms:75-79) -----
            LitAttr(4, 35, 11, AskipBrt, BmsColor.Neutral, "Update User"),       // bms:75-79

            // ----- 'Enter User ID:' label + USRIDIN input (IC) + stopper (bms:80-92) -----
            Lit(6, 6, 14, BmsColor.Green, "Enter User ID:"),                     // bms:80-84
            // USRIDIN: ATTRB=(FSET,IC,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L8 — IC => initial cursor here.
            new ScreenField
            {
                Name = "USRIDIN", Row = 6, Col = 21, Length = 8,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:85-89
            Stopper(6, 30),                                                     // bms:90-92

            // ----- 70-asterisk separator (bms:93-97) -----
            // DFHMDF with no ATTRB given => default keyable (UNPROT) NORM; COLOR=YELLOW; L70 of '*'.
            LitAttr(8, 6, 70, BmsAttribute.Unprotected | BmsAttribute.Normal, BmsColor.Yellow, new string('*', 70)), // bms:93-97

            // ----- First Name label + FNAME input + stopper (bms:98-110) -----
            Lit(11, 6, 11, BmsColor.Turquoise, "First Name:"),                  // bms:98-102
            // FNAME: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L20.
            new ScreenField
            {
                Name = "FNAME", Row = 11, Col = 18, Length = 20,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:103-107
            Stopper(11, 39),                                                    // bms:108-110

            // ----- Last Name label + LNAME input + stopper (bms:111-124) -----
            Lit(11, 45, 10, BmsColor.Turquoise, "Last Name:"),                 // bms:111-115
            // LNAME: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L20.
            new ScreenField
            {
                Name = "LNAME", Row = 11, Col = 56, Length = 20,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:116-120
            // Stopper at (11,77) — COLOR=GREEN in source. bms:121-124
            new ScreenField { Row = 11, Col = 77, Length = 0, Attribute = Askip, Color = BmsColor.Green },

            // ----- Password label + PASSWD input (DRK) + '(8 Char)' hint (bms:125-139) -----
            Lit(13, 6, 9, BmsColor.Turquoise, "Password:"),                    // bms:125-129
            // PASSWD: ATTRB=(DRK,FSET,UNPROT) GREEN HILIGHT=UNDERLINE L8 — dark / non-display (FB-3).
            new ScreenField
            {
                Name = "PASSWD", Row = 13, Col = 16, Length = 8,
                Attribute = BmsAttribute.Dark | BmsAttribute.Fset | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:130-134
            Lit(13, 25, 8, BmsColor.Blue, "(8 Char)"),                         // bms:135-139

            // ----- User Type label + USRTYPE input + '(A=Admin, U=User)' hint (bms:140-154) -----
            Lit(15, 6, 11, BmsColor.Turquoise, "User Type: "),                 // bms:140-144 (trailing space)
            // USRTYPE: ATTRB=(FSET,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L1.
            new ScreenField
            {
                Name = "USRTYPE", Row = 15, Col = 17, Length = 1,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:145-149
            Lit(15, 19, 17, BmsColor.Blue, "(A=Admin, U=User)"),               // bms:150-154

            // ----- error/status line + F-key legend (bms:155-164) -----
            // ERRMSG: ATTRB=(ASKIP,BRT,FSET) COLOR=RED L78 — bright red message line.
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:155-158
            LitAttr(24, 1, 58, Askip, BmsColor.Yellow,
                "ENTER=Fetch  F3=Save&Exit  F4=Clear  F5=Save  F12=Cancel"),   // bms:159-164
        };

        return new BmsMap(MapName, MapsetName, fields, rows: 24, cols: 80);
    }

    // ---- attribute presets ----
    private static BmsAttribute Askip => BmsAttribute.AutoSkip | BmsAttribute.Normal;             // ATTRB=(ASKIP,NORM)
    private static BmsAttribute AskipFset => BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal; // (ASKIP,FSET,NORM)
    private static BmsAttribute AskipBrt => BmsAttribute.AutoSkip | BmsAttribute.Bright;          // (ASKIP,BRT)
    private static BmsAttribute AskipBrtFset => BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset; // (ASKIP,BRT,FSET)

    // ---- field factory helpers ----

    /// <summary>Unnamed literal field with ATTRB=(ASKIP,NORM) and the given colour + INITIAL text.</summary>
    private static ScreenField Lit(int row, int col, int len, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = Askip, Color = color, Value = text };

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT or the default-keyable separator).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, ATTRB=(ASKIP,NORM)).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };
}
