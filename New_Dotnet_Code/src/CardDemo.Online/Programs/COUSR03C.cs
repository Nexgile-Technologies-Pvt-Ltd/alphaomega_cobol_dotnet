using CardDemo.Cobol.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COUSR03C</c> — the admin "Delete User" screen that
/// reads an existing USRSEC (USER_SECURITY) record for update and then deletes it (TRANSID <c>CU03</c>, BMS
/// map <c>COUSR3A</c> / mapset <c>COUSR03</c>).
/// </summary>
/// <remarks>
/// <para>
/// The operator types an 8-char User ID and presses ENTER to <b>fetch</b> the existing record (first name,
/// last name, user type) into the read-only display fields, confirms it is the right user, then presses
/// <b>PF5</b> to delete (READ-for-UPDATE then DELETE) the record from USRSEC, getting a green
/// "&lt;id&gt; has been deleted ..." confirmation. PF3 goes back to the caller / admin menu, PF4 clears the
/// screen, PF12 returns to <c>COADM01C</c>, and any other key is rejected. It is pseudo-conversational: it
/// re-drives itself via <c>RETURN TRANSID('CU03')</c>. It is typically reached by XCTL from the admin menu
/// (<c>COADM01C</c>, option 4) or from the user-list program (<c>COUSR00C</c>), which pre-selects a user in
/// <c>CDEMO-CU03-USR-SELECTED</c> and triggers an auto-fetch on first entry.
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and <c>// source: COUSR03C.cbl:NNN</c>
/// citations. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/> plus the program-specific <c>CDEMO-CU03-INFO</c> trailer), and every
/// faithful bug are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the USRSEC master is accessed by primary key (X8 user-id).
/// <c>READ ... UPDATE</c> (READ-USER-SEC-FILE) = <see cref="UserSecurityRepository.ReadByKey"/> which begins
/// a tracked read of the row that the subsequent DELETE targets, and <c>DELETE</c> with <b>no RIDFLD</b>
/// (DELETE-USER-SEC-FILE) = <see cref="UserSecurityRepository.Delete"/> of the <i>same</i> key the READ just
/// resolved (<see cref="_secUsrId"/>) — the "delete the read-for-update record" idiom. The repository
/// FileStatus maps to the CICS RESP the COBOL <c>EVALUATE WS-RESP-CD</c> branches on:
/// Ok('00')→NORMAL(0), RecordNotFound('23')→NOTFND(13), anything else→the OTHER/"Unable to ..." branch.
/// The READ and DELETE happen back-to-back in one handler invocation, so no held cross-turn lock is modelled.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>FB-1 — <b>PF5 deletes even when the immediately-preceding READ fails.</b> In DELETE-USER-INFO the
/// <c>IF NOT ERR-FLG-ON</c> block performs READ-USER-SEC-FILE then DELETE-USER-SEC-FILE <i>unconditionally</i>,
/// with no re-check of ERR-FLG between them. If the READ returns NOTFND it SENDs "User ID NOT found..." and
/// sets ERR-FLG-ON, but DELETE-USER-SEC-FILE still runs (and also returns NOTFND, SENDing its own
/// "User ID NOT found...", the second SEND winning). No guard is inserted around the DELETE. source: :188-192</item>
/// <item>FB-2 — <b>Double SEND on a successful ENTER fetch.</b> READ-USER-SEC-FILE's NORMAL branch SENDs the
/// neutral "Press PF5 key to delete this user ..." screen, then PROCESS-ENTER-KEY's step-3 <c>IF NOT ERR-FLG-ON</c>
/// moves the fetched names into the screen and SENDs again. Two SENDs per ENTER; the color byte set to
/// DFHNEUTR on the first SEND carries over (it is not reset) onto the second. Both SENDs and the carry-over
/// are reproduced. source: :160-169,281-286</item>
/// <item>FB-3 — <b><c>WS-USR-MODIFIED</c> is a dead flag</b>: initialised to NO, never SET to YES and never
/// tested. Vestigial (copied from the Add/Update siblings); not wired to any behaviour. source: :45-47,85</item>
/// <item>FB-4 — <b>RESP/RESP2 from RECEIVE never checked</b>: RECEIVE-USRDEL-SCREEN captures WS-RESP-CD/
/// WS-REAS-CD but the program never inspects them (no MAPFAIL handling). No RECEIVE error handling added. source: :232-238</item>
/// <item>FB-5 — <b>DELETE error branch message says "Update", not "Delete".</b> The WHEN OTHER branch of
/// DELETE-USER-SEC-FILE shows <c>'Unable to Update User...'</c> (a copy-paste from COUSR02C). Kept verbatim. source: :332</item>
/// <item>FB-6 — <b><c>MOVE -1 TO FNAMEL</c> on lookup/other-error paths puts the cursor on a protected field.</b>
/// The WHEN OTHER branches of both READ and DELETE set FNAMEL = -1, but FNAME is ASKIP (protected) here.
/// Placing the cursor on a protected field is an odd no-op on real CICS; reproduced exactly (cursor goes to
/// FNAME, not USRIDIN). source: :298,334; bms:103-107</item>
/// <item>FB-7 — <b>WS-MESSAGE X(80) → ERRMSGO X(78) silent 2-char truncation</b> on the move into ERRMSG.
/// None of the shipped literals exceed 78, so no visible effect today; ERRMSG is not widened. source: :38,217</item>
/// <item>FB-8 — <b>PF5 re-reads whatever is now in USRIDIN</b>, not the previously-fetched id (USRIDIN is
/// unprotected and could have been changed after an ENTER-fetch). PF5 always uses the current USRIDINI. source: :188-191</item>
/// <item>FB-9 — <b>Auto-fetch on first entry can show a found user with the "Press PF5…" prompt without the
/// operator ever pressing ENTER</b>, and fires an extra SEND from the first-entry path (PROCESS-ENTER-KEY
/// reads+SENDs, then MAIN-PARA SENDs again at line 105). Reproduced. source: :99-105</item>
/// </list>
/// </remarks>
public sealed class Cousr03c : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COUSR03C.cbl:35-47
    // =============================================================================================
    private const string WS_PGMNAME = "COUSR03C";     // 05 WS-PGMNAME PIC X(08) VALUE 'COUSR03C'. source: :36
    private const string WS_TRANID = "CU03";          // 05 WS-TRANID  PIC X(04) VALUE 'CU03'.     source: :37
    private const string WS_USRSEC_FILE = "USRSEC  "; // 05 WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC  '. source: :39

    private string _wsMessage = "";                   // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :43-44
    private int _wsRespCd;
    private int _wsReasCd;

    // 05 WS-USR-MODIFIED PIC X(01) VALUE 'N'. 88 USR-MODIFIED-YES='Y' / USR-MODIFIED-NO='N'.
    // FB-3: dead flag — set to NO in MAIN-PARA, never set YES, never tested. source: :45-47
    private bool _usrModified;
    private void SetUsrModifiedNo() => _usrModified = false;  // SET USR-MODIFIED-NO TO TRUE. source: :85

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared screen header / messages.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  COCOM01Y trailer — CDEMO-CU03-INFO (the program-private selection state). source: :50-58
    // =============================================================================================
    // Only CDEMO-CU03-USR-SELECTED is read by this program (pre-selected user from the list screen).
    // 10 CDEMO-CU03-USRID-FIRST   PIC X(08). source: :51
    private string _cu03UsridFirst = "";
    // 10 CDEMO-CU03-USRID-LAST    PIC X(08). source: :52
    private string _cu03UsridLast = "";
    // 10 CDEMO-CU03-PAGE-NUM      PIC 9(08). source: :53
    private int _cu03PageNum;
    // 10 CDEMO-CU03-NEXT-PAGE-FLG PIC X(01) VALUE 'N'. source: :54-56
    private char _cu03NextPageFlg = 'N';
    // 10 CDEMO-CU03-USR-SEL-FLG   PIC X(01). source: :57
    private string _cu03UsrSelFlg = "";
    // 10 CDEMO-CU03-USR-SELECTED  PIC X(08). source: :58
    private string _cu03UsrSelected = "";

    // =============================================================================================
    //  SEC-USER-DATA (CSUSR01Y) — the keyed record read for update. source: :271-274
    // =============================================================================================
    // SEC-USR-ID X(8) — the READ RID, and the key the no-RIDFLD DELETE targets (held read-for-update).
    private string _secUsrId = "";
    private UserSecurity? _secUserData;

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn map + DB. source: :74-76,94,134-137
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private UserSecurityRepository _users = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The USER_SECURITY repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public Cousr03c(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cousr03c() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COUSR03C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CU03 -> COUSR03C. source: CSD_TRANSACTIONS.md:87; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COUSR03C.cbl:82-137
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COUSR03 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _users = new UserSecurityRepository(_db.Connection);

        // SET ERR-FLG-OFF TO TRUE / SET USR-MODIFIED-NO TO TRUE. source: :84-85
        _errFlgOn = false;
        SetUsrModifiedNo();

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COUSR3AO. source: :87-88
        _wsMessage = "";
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
            RestoreCu03Info();

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :95
                _commArea.SetReenter();                 // SET CDEMO-PGM-REENTER TO TRUE. source: :96
                MoveLowValuesToMapOut();                // MOVE LOW-VALUES TO COUSR3AO. source: :97
                _map.Field("USRIDIN").CursorLength = -1; // MOVE -1 TO USRIDINL OF COUSR3AI. source: :98

                // IF CDEMO-CU03-USR-SELECTED NOT = SPACES AND LOW-VALUES. source: :99-100
                if (NotSpacesOrLow(_cu03UsrSelected))
                {
                    // MOVE CDEMO-CU03-USR-SELECTED TO USRIDINI OF COUSR3AI. source: :101-102
                    _map.Field("USRIDIN").SetValue(_cu03UsrSelected, setMdt: false);
                    ProcessEnterKey(ctx);               // PERFORM PROCESS-ENTER-KEY. source: :103
                }
                SendUsrdelScreen(ctx);                  // PERFORM SEND-USRDEL-SCREEN. source: :105 (FB-9 extra SEND)
            }
            else
            {
                ReceiveUsrdelScreen(ctx);               // PERFORM RECEIVE-USRDEL-SCREEN. source: :107
                // EVALUATE EIBAID. source: :108-130
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);           // WHEN DFHENTER. source: :109-110
                        break;
                    case AidKey.Pf3:
                        // WHEN DFHPF3. source: :111-118
                        if (IsSpacesOrLowValues(_commArea.FromProgram)) // IF CDEMO-FROM-PROGRAM = SPACES OR LOW-VALUES. source: :112
                            _commArea.ToProgram = "COADM01C";           // MOVE 'COADM01C' TO CDEMO-TO-PROGRAM. source: :113
                        else
                            _commArea.ToProgram = _commArea.FromProgram; // MOVE CDEMO-FROM-PROGRAM TO CDEMO-TO-PROGRAM. source: :115-116
                        ReturnToPrevScreen(ctx);        // PERFORM RETURN-TO-PREV-SCREEN. source: :118
                        break;
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);        // WHEN DFHPF4 -> PERFORM CLEAR-CURRENT-SCREEN. source: :119-120
                        break;
                    case AidKey.Pf5:
                        DeleteUserInfo(ctx);            // WHEN DFHPF5 -> PERFORM DELETE-USER-INFO. source: :121-122
                        break;
                    case AidKey.Pf12:
                        _commArea.ToProgram = "COADM01C"; // WHEN DFHPF12 -> MOVE 'COADM01C' TO CDEMO-TO-PROGRAM. source: :123-124
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :125
                        break;
                    default:
                        // WHEN OTHER. source: :126-129
                        _errFlgOn = true;                              // MOVE 'Y' TO WS-ERR-FLG. source: :127
                        _wsMessage = CCDA_MSG_INVALID_KEY;             // MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE. source: :128
                        SendUsrdelScreen(ctx);                         // PERFORM SEND-USRDEL-SCREEN. source: :129
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :134-137
        if (ctx.Outcome is null)
        {
            SaveCu03Info();
            ctx.ReturnTransId(WS_TRANID, _commArea);
        }
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COUSR03C.cbl:142-169
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE. source: :144-154
        if (IsSpacesOrLowValues(_map.Field("USRIDIN").Value))
        {
            // WHEN USRIDINI = SPACES OR LOW-VALUES. source: :145-150
            _errFlgOn = true;                            // MOVE 'Y' TO WS-ERR-FLG. source: :146
            _wsMessage = "User ID can NOT be empty...";  // source: :147-148
            _map.Field("USRIDIN").CursorLength = -1;      // MOVE -1 TO USRIDINL. source: :149
            SendUsrdelScreen(ctx);                       // PERFORM SEND-USRDEL-SCREEN. source: :150
        }
        else
        {
            // WHEN OTHER -> MOVE -1 TO USRIDINL; CONTINUE. source: :151-153
            _map.Field("USRIDIN").CursorLength = -1;
        }

        // IF NOT ERR-FLG-ON. source: :156-162
        if (!ErrFlgOn)
        {
            // MOVE SPACES TO FNAMEI/LNAMEI/USRTYPEI OF COUSR3AI. source: :157-159
            _map.Field("FNAME").SetValue("", setMdt: false);
            _map.Field("LNAME").SetValue("", setMdt: false);
            _map.Field("USRTYPE").SetValue("", setMdt: false);
            // MOVE USRIDINI OF COUSR3AI TO SEC-USR-ID. source: :160
            _secUsrId = PadX(_map.Field("USRIDIN").Value, 8);
            ReadUserSecFile(ctx);                        // PERFORM READ-USER-SEC-FILE. source: :161
        }

        // IF NOT ERR-FLG-ON. source: :164-169
        if (!ErrFlgOn)
        {
            // MOVE SEC-USR-FNAME/LNAME/TYPE TO the corresponding screen display fields. source: :165-167
            _map.Field("FNAME").SetValue(SecUsrFname, setMdt: false);
            _map.Field("LNAME").SetValue(SecUsrLname, setMdt: false);
            _map.Field("USRTYPE").SetValue(SecUsrType, setMdt: false);
            SendUsrdelScreen(ctx);                       // PERFORM SEND-USRDEL-SCREEN. source: :168 (FB-2 second SEND)
        }
    }

    // =============================================================================================
    //  DELETE-USER-INFO — source: COUSR03C.cbl:174-192  (PF5 path)
    // =============================================================================================
    private void DeleteUserInfo(CicsContext ctx)
    {
        // EVALUATE TRUE. source: :176-186
        if (IsSpacesOrLowValues(_map.Field("USRIDIN").Value))
        {
            // WHEN USRIDINI = SPACES OR LOW-VALUES. source: :177-182
            _errFlgOn = true;                            // MOVE 'Y' TO WS-ERR-FLG. source: :178
            _wsMessage = "User ID can NOT be empty...";  // source: :179-180
            _map.Field("USRIDIN").CursorLength = -1;      // MOVE -1 TO USRIDINL. source: :181
            SendUsrdelScreen(ctx);                       // PERFORM SEND-USRDEL-SCREEN. source: :182
        }
        else
        {
            // WHEN OTHER -> MOVE -1 TO USRIDINL; CONTINUE. source: :183-185
            _map.Field("USRIDIN").CursorLength = -1;
        }

        // IF NOT ERR-FLG-ON. source: :188-192
        if (!ErrFlgOn)
        {
            // MOVE USRIDINI OF COUSR3AI TO SEC-USR-ID (FB-8: whatever is currently in USRIDIN). source: :189
            _secUsrId = PadX(_map.Field("USRIDIN").Value, 8);
            ReadUserSecFile(ctx);                        // PERFORM READ-USER-SEC-FILE. source: :190
            // FB-1: DELETE runs unconditionally here, even if READ set ERR-FLG-ON (NOTFND). No guard. source: :191
            DeleteUserSecFile(ctx);                      // PERFORM DELETE-USER-SEC-FILE. source: :191
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COUSR03C.cbl:197-208
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :199-201
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID. source: :202
        _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :203
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :204

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :205-208
        SaveCu03Info();
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-USRDEL-SCREEN — source: COUSR03C.cbl:213-225
    // =============================================================================================
    private void SendUsrdelScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                    // PERFORM POPULATE-HEADER-INFO. source: :215

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false);  // MOVE WS-MESSAGE TO ERRMSGO. source: :217 (FB-7 trunc 80->78)

        // EXEC CICS SEND MAP('COUSR3A') MAPSET('COUSR03') FROM(COUSR3AO) ERASE CURSOR. source: :219-225
        ctx.SendMap("COUSR3A", "COUSR03", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true,
            Cursor = -1, // CURSOR — honour the MOVE -1 TO xxxL the handler set this turn.
        });
        _wsRespCd = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-USRDEL-SCREEN — source: COUSR03C.cbl:230-238
    // =============================================================================================
    private void ReceiveUsrdelScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COUSR3A') MAPSET('COUSR03') INTO(COUSR3AI) RESP RESP2. source: :232-238
        // FB-4: RESP/RESP2 captured but never inspected (no MAPFAIL handling).
        ctx.ReceiveMap("COUSR3A", "COUSR03", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COUSR03C.cbl:243-262
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :245
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :247
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :248
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);    // MOVE WS-TRANID  TO TRNNAMEO. source: :249
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :250

        // CURDATEO = mm/dd/yy (year last two digits). source: :252-256
        _map.Field("CURDATE").SetValue(
            $"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :258-262
        _map.Field("CURTIME").SetValue(
            $"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  READ-USER-SEC-FILE — source: COUSR03C.cbl:267-300
    // =============================================================================================
    private void ReadUserSecFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA) RIDFLD(SEC-USR-ID) KEYLENGTH UPDATE
        // RESP RESP2. The UPDATE intent (record lock) is not required cross-turn: in DELETE-USER-INFO the
        // DELETE immediately follows this READ in the same invocation, deleting the same key. source: :269-278
        _ = WS_USRSEC_FILE; // dataset name (fixed) — repository is keyed by usr_id.
        string fileStatus = _users.ReadByKey(_secUsrId, out _secUserData);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :280-300
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:
                // WHEN DFHRESP(NORMAL). The branch opens with a dead CONTINUE (no-op), then the MOVEs.
                // FB-2: this SENDs "Press PF5 key to delete this user ..." (neutral) BEFORE control returns to
                // the caller, which (on the ENTER path) re-SENDs with the fetched names. source: :281-286
                // (CONTINUE — no-op. source: :282)
                _wsMessage = "Press PF5 key to delete this user ...";    // source: :283-284
                _map.Field("ERRMSG").ColorOverride = BmsColor.Neutral;   // MOVE DFHNEUTR TO ERRMSGC. source: :285
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :286
                break;
            case Resp.NotFnd:
                // WHEN DFHRESP(NOTFND). source: :287-292
                _errFlgOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :288
                _wsMessage = "User ID NOT found...";                     // source: :289-290
                _map.Field("USRIDIN").CursorLength = -1;                  // MOVE -1 TO USRIDINL. source: :291
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :292
                break;
            default:
                // WHEN OTHER. DISPLAY 'RESP:'/'REAS:' -> region-log trace; no-op here. FB-6: -1 to FNAMEL
                // (a protected field). source: :293-299
                _errFlgOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :295
                _wsMessage = "Unable to lookup User...";                 // source: :296-297
                _map.Field("FNAME").CursorLength = -1;                    // MOVE -1 TO FNAMEL. source: :298
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :299
                break;
        }
    }

    // =============================================================================================
    //  DELETE-USER-SEC-FILE — source: COUSR03C.cbl:305-336
    // =============================================================================================
    private void DeleteUserSecFile(CicsContext ctx)
    {
        // EXEC CICS DELETE DATASET(WS-USRSEC-FILE) RESP RESP2 — NO RIDFLD; deletes the held READ-for-UPDATE
        // record. Modelled as deleting the same key the immediately-preceding READ resolved (_secUsrId).
        // Because FB-1 lets this run after a NOTFND READ, the repository DELETE itself detects "not found"
        // and returns the '23'/NOTFND branch. source: :307-311
        string fileStatus = _users.Delete(_secUsrId);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :313-336
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal:
                // WHEN DFHRESP(NORMAL). source: :314-322
                InitializeAllFields();                                   // PERFORM INITIALIZE-ALL-FIELDS. source: :315
                _wsMessage = "";                                         // MOVE SPACES TO WS-MESSAGE. source: :316
                _map.Field("ERRMSG").ColorOverride = BmsColor.Green;     // MOVE DFHGREEN TO ERRMSGC. source: :317
                // STRING 'User ' (SIZE) + SEC-USR-ID (DELIMITED BY SPACE = right-trim) + ' has been deleted ...'
                // (SIZE) INTO WS-MESSAGE. INITIALIZE-ALL-FIELDS cleared the screen fields, but SEC-USR-ID lives
                // in SEC-USER-DATA (not a screen field), so it survives. source: :318-321
                _wsMessage = $"User {SecUsrId.TrimEnd(' ')} has been deleted ...";
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :322
                break;
            case Resp.NotFnd:
                // WHEN DFHRESP(NOTFND). source: :323-328
                _errFlgOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :324
                _wsMessage = "User ID NOT found...";                     // source: :325-326
                _map.Field("USRIDIN").CursorLength = -1;                  // MOVE -1 TO USRIDINL. source: :327
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :328
                break;
            default:
                // WHEN OTHER. DISPLAY 'RESP:'/'REAS:' -> region-log trace; no-op here. FB-5: message says
                // "Update", not "Delete" (copy-paste from COUSR02C). FB-6: -1 to FNAMEL (protected). source: :329-335
                _errFlgOn = true;                                        // MOVE 'Y' TO WS-ERR-FLG. source: :331
                _wsMessage = "Unable to Update User...";                 // source: :332-333 (FB-5 verbatim)
                _map.Field("FNAME").CursorLength = -1;                    // MOVE -1 TO FNAMEL. source: :334
                SendUsrdelScreen(ctx);                                   // PERFORM SEND-USRDEL-SCREEN. source: :335
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COUSR03C.cbl:341-344
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();   // PERFORM INITIALIZE-ALL-FIELDS. source: :343
        SendUsrdelScreen(ctx);   // PERFORM SEND-USRDEL-SCREEN. source: :344
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COUSR03C.cbl:349-356
    // =============================================================================================
    private void InitializeAllFields()
    {
        _map.Field("USRIDIN").CursorLength = -1;             // MOVE -1 TO USRIDINL. source: :351
        // MOVE SPACES TO USRIDINI / FNAMEI / LNAMEI / USRTYPEI / WS-MESSAGE. source: :352-356
        _map.Field("USRIDIN").SetValue("", setMdt: false);  // USRIDINI
        _map.Field("FNAME").SetValue("", setMdt: false);    // FNAMEI
        _map.Field("LNAME").SetValue("", setMdt: false);    // LNAMEI
        _map.Field("USRTYPE").SetValue("", setMdt: false);  // USRTYPEI
        _wsMessage = "";                                    // WS-MESSAGE
    }

    // =============================================================================================
    //  SEC-USER-DATA field accessors (CSUSR01Y), with COBOL fixed-width formatting.
    // =============================================================================================
    private string SecUsrId => PadX(_secUserData?.UsrId, 8);          // SEC-USR-ID    X(8)
    private string SecUsrFname => PadX(_secUserData?.FirstName, 20);  // SEC-USR-FNAME X(20)
    private string SecUsrLname => PadX(_secUserData?.LastName, 20);   // SEC-USR-LNAME X(20)
    private string SecUsrType => PadX(_secUserData?.UsrType, 1);      // SEC-USR-TYPE  X(1)

    /// <summary>MOVE LOW-VALUES TO COUSR3AO — blank every named output field + clear per-turn overrides. source: :97</summary>
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
        _wsRespCd = fileStatus switch
        {
            FileStatus.Ok => (int)Resp.Normal,                  // '00' -> DFHRESP(NORMAL)
            FileStatus.RecordNotFound => (int)Resp.NotFnd,      // '23' -> DFHRESP(NOTFND)
            FileStatus.EndOfFile => (int)Resp.EndFile,          // '10' -> DFHRESP(ENDFILE)
            FileStatus.DuplicateKey => (int)Resp.DupRec,        // '02' -> DFHRESP(DUPREC)
            _ => (int)Resp.Error,                               // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  CDEMO-CU03-INFO (de)serialize — carried across turns in the COMMAREA's unused customer slots.
    //  source: COUSR03C.cbl:50-58,94,134-137
    // =============================================================================================
    // COUSR03C only reads CDEMO-CU03-USR-SELECTED; the trailer is packed into CDEMO-CUSTOMER-INFO so the
    // selection round-trips losslessly with COUSR00C (which produced it). Pack layout into
    // CustFName(25)+CustMName(25)+CustLName(25) = 75 bytes:
    //   USRID-FIRST X(8) | USRID-LAST X(8) | PAGE-NUM 9(8) | NEXT X(1) | USR-SEL-FLG X(1) | USR-SELECTED X(8).
    private void SaveCu03Info()
    {
        string packed =
            PadX(_cu03UsridFirst, 8) +
            PadX(_cu03UsridLast, 8) +
            Zoned(_cu03PageNum, 8) +
            (_cu03NextPageFlg == '\0' ? "N" : _cu03NextPageFlg.ToString()) +
            PadX(_cu03UsrSelFlg, 1) +
            PadX(_cu03UsrSelected, 8);
        packed = PadX(packed, 75);
        _commArea.CustFName = packed.Substring(0, 25);
        _commArea.CustMName = packed.Substring(25, 25);
        _commArea.CustLName = packed.Substring(50, 25);
    }

    private void RestoreCu03Info()
    {
        string packed = PadX(_commArea.CustFName, 25) + PadX(_commArea.CustMName, 25) + PadX(_commArea.CustLName, 25);
        if (packed.Length < 75) packed = PadX(packed, 75);
        _cu03UsridFirst = packed.Substring(0, 8).TrimEnd();
        _cu03UsridLast = packed.Substring(8, 8).TrimEnd();
        _cu03PageNum = (int)ParseLong(packed.Substring(16, 8));
        char nx = packed[24];
        _cu03NextPageFlg = nx == 'Y' ? 'Y' : 'N';
        _cu03UsrSelFlg = packed.Substring(25, 1).TrimEnd();
        _cu03UsrSelected = packed.Substring(26, 8).TrimEnd();
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is NOT (all SPACES) AND NOT (all LOW-VALUES) — the COBOL "NOT = SPACES AND LOW-VALUES" guard.</summary>
    private static bool NotSpacesOrLow(string? s) => !IsSpacesOrLowValues(s);

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL) — the COBOL "= SPACES OR LOW-VALUES" test.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ' || c == '\0');

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
    //  BMS map builder — COUSR3A in mapset COUSR03 (24x80).
    //  source: app/bms/COUSR03.bms:19-150 / SCREEN_COUSR03.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COUSR03.bms:26.</summary>
    public const string MapName = "COUSR3A";

    /// <summary>The DFHMSD mapset name. source: COUSR03.bms:19.</summary>
    public const string MapsetName = "COUSR03";

    /// <summary>
    /// Constructs the <c>COUSR3A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and zero-length
    /// stoppers, and the named in/out fields — in BMS source order. The one keyable field is <c>USRIDIN</c>
    /// (6,21) L8 (carries <c>IC</c> → the initial cursor lands here). The fetched-user display fields
    /// <c>FNAME</c> (11,18) L20, <c>LNAME</c> (13,18) L20 and <c>USRTYPE</c> (15,17) L1 are <b>ASKIP</b>
    /// (protected/display-only). No PICIN/PICOUT clauses appear in this map.
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

            // ----- 'Delete User' heading (bms:75-79) -----
            LitAttr(4, 35, 11, AskipBrt, BmsColor.Neutral, "Delete User"),       // bms:75-79

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

            // ----- First Name label + FNAME display (ASKIP/protected) + stopper (bms:98-110) -----
            Lit(11, 6, 11, BmsColor.Turquoise, "First Name:"),                  // bms:98-102
            // FNAME: ATTRB=(ASKIP,FSET,NORM) BLUE HILIGHT=UNDERLINE L20 — protected display-only.
            new ScreenField
            {
                Name = "FNAME", Row = 11, Col = 18, Length = 20,
                Attribute = AskipFset,
                Color = BmsColor.Blue,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:103-107
            Stopper(11, 39),                                                    // bms:108-110

            // ----- Last Name label + LNAME display (ASKIP/protected) + stopper (bms:111-124) -----
            Lit(13, 6, 10, BmsColor.Turquoise, "Last Name:"),                  // bms:111-115
            // LNAME: ATTRB=(ASKIP,FSET,NORM) BLUE HILIGHT=UNDERLINE L20 — protected display-only.
            new ScreenField
            {
                Name = "LNAME", Row = 13, Col = 18, Length = 20,
                Attribute = AskipFset,
                Color = BmsColor.Blue,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:116-120
            // Stopper at (13,39) — COLOR=GREEN in source. bms:121-124
            new ScreenField { Row = 13, Col = 39, Length = 0, Attribute = Askip, Color = BmsColor.Green },

            // ----- User Type label + USRTYPE display (ASKIP/protected) + '(A=Admin, U=User)' hint (bms:125-139) -----
            Lit(15, 6, 11, BmsColor.Turquoise, "User Type: "),                 // bms:125-129 (trailing space)
            // USRTYPE: ATTRB=(ASKIP,FSET,NORM) BLUE HILIGHT=UNDERLINE L1 — protected display-only.
            new ScreenField
            {
                Name = "USRTYPE", Row = 15, Col = 17, Length = 1,
                Attribute = AskipFset,
                Color = BmsColor.Blue,
                Hilight = BmsHilight.Underline,
            },                                                                  // bms:130-134
            Lit(15, 19, 17, BmsColor.Blue, "(A=Admin, U=User)"),               // bms:135-139

            // ----- error/status line + F-key legend (bms:140-148) -----
            // ERRMSG: ATTRB=(ASKIP,BRT,FSET) COLOR=RED L78 — bright red message line.
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),              // bms:140-143
            LitAttr(24, 1, 58, Askip, BmsColor.Yellow,
                "ENTER=Fetch  F3=Back  F4=Clear  F5=Delete"),                  // bms:144-148
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
