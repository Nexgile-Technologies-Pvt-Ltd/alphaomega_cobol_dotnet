using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Faithful .NET port of the online CICS COBOL program <c>COUSR01C</c> — "Add User": adds a new
/// Regular/Admin user to the USRSEC (USER_SECURITY) file (TRANSID <c>CU01</c>, BMS map <c>COUSR1A</c> /
/// mapset <c>COUSR01</c>).
/// </summary>
/// <remarks>
/// <para>
/// COUSR01C is pseudo-conversational: it paints the Add-User screen, RECEIVEs the five keyed fields
/// (First Name, Last Name, User ID, Password, User Type), validates each one in order, then on ENTER
/// builds a <c>SEC-USER-DATA</c> record and <c>WRITE</c>s it to the USRSEC file. It re-drives itself via
/// <c>RETURN TRANSID('CU01')</c>. PF3 returns to the admin menu (<c>COADM01C</c>); PF4 clears the screen;
/// any other key is "Invalid key pressed".
/// </para>
/// <para>
/// This is a near-mechanical re-implementation of the program's <c>PROCEDURE DIVISION</c>: each COBOL
/// paragraph is one method carrying the original paragraph name and a <c>// source: COUSR01C.cbl:NNN</c>
/// citation. Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, the COMMAREA field usage
/// (<see cref="CardDemoCommArea"/>), the exact literal validation/error messages, and every faithful bug
/// are preserved verbatim.
/// </para>
/// <para><b>VSAM → repository mapping.</b> Only the USRSEC master is touched, by a single keyed
/// <c>WRITE</c>: <c>EXEC CICS WRITE DATASET('USRSEC') FROM(SEC-USER-DATA) RIDFLD(SEC-USR-ID)</c> maps to
/// <see cref="UserSecurityRepository.Insert"/>. The repository FileStatus is mapped to the CICS RESP the
/// COBOL <c>EVALUATE WS-RESP-CD</c> branches on: Ok('00')→NORMAL(0); a PK conflict surfaces as
/// DuplicateKeyError('22') / DuplicateKey('02') → DUPREC(14), which lands on the
/// <c>WHEN DFHRESP(DUPKEY) / WHEN DFHRESP(DUPREC)</c> "User ID already exist..." branch; anything else →
/// the WHEN OTHER "Unable to Add User..." branch.</para>
/// <para><b>Faithful bugs reproduced (do NOT fix):</b></para>
/// <list type="bullet">
/// <item>B-1 — No value check on User Type. Unlike the screen hint "(A=Admin, U=User)", the program only
/// rejects a <i>blank</i> User Type; any non-blank single character (e.g. 'X', 'z', '9') passes validation
/// and is written verbatim to SEC-USR-TYPE. Reproduced (only the SPACES/LOW-VALUES test). source: :142-147</item>
/// <item>B-2 — No length/format validation on any field. First/Last names (X20), User ID (X8), Password
/// (X8) are moved straight from the symbolic input fields into the fixed-width record with no minimum-length,
/// numeric, or character-set check; whatever the operator keyed (truncated to the field width) is written.
/// Reproduced (only blank tests). source: :118-160</item>
/// <item>B-3 — After a successful WRITE, INITIALIZE-ALL-FIELDS moves SPACES to the symbolic <b>input</b>
/// (...I) fields (USERIDI/FNAMEI/LNAMEI/PASSWDI/USRTYPEI), not the <b>output</b> (...O) fields the SEND
/// paints. On the console runtime the named fields are single cells, so this still blanks them for the
/// success SEND; the move targets are the COBOL <c>-I</c> halves exactly as written. source: :287-295</item>
/// <item>B-4 — The success message uses <c>STRING ... DELIMITED BY SPACE</c> on SEC-USR-ID, so a user id
/// that contains an embedded space is truncated at the first space inside the "User &lt;id&gt; has been
/// added ..." text. Reproduced (split on first space). source: :255-258</item>
/// <item>B-5 — PROCESS-ENTER-KEY's <c>WHEN OTHER</c> (all fields non-blank) does
/// <c>MOVE -1 TO FNAMEL</c> then <c>CONTINUE</c>; the cursor is parked on First Name even on the happy
/// path. Reproduced. source: :148-150</item>
/// <item>B-6 — RETURN-TO-PREV-SCREEN's commented-out user-id / user-type carry-forward (the COBOL keeps
/// <c>* MOVE WS-USER-ID TO CDEMO-USER-ID</c> commented out), so the COMMAREA user identity is left as
/// inherited. Reproduced (no move). source: :172-173</item>
/// </list>
/// </remarks>
public sealed class Cousr01c : ITransactionHandler
{
    // =============================================================================================
    //  WS-VARIABLES — source: COUSR01C.cbl:35-44
    // =============================================================================================
    private const string WS_PGMNAME = "COUSR01C";     // 05 WS-PGMNAME PIC X(08) VALUE 'COUSR01C'. source: :36
    private const string WS_TRANID = "CU01";          // 05 WS-TRANID  PIC X(04) VALUE 'CU01'.     source: :37
    private const string WS_USRSEC_FILE = "USRSEC  "; // 05 WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC  '. source: :39

    private string _wsMessage = "";                   // 05 WS-MESSAGE PIC X(80) VALUE SPACES. source: :38

    // 05 WS-ERR-FLG PIC X(01) VALUE 'N'. 88 ERR-FLG-ON='Y' / ERR-FLG-OFF='N'. source: :40-42
    private bool _errFlgOn;
    private bool ErrFlgOn => _errFlgOn;   // 88 ERR-FLG-ON

    // 05 WS-RESP-CD / WS-REAS-CD PIC S9(09) COMP VALUE ZEROS. source: :43-44
    private int _wsRespCd;
    private int _wsReasCd;

    // CCDA-TITLE01/02 (COTTL01Y) + CCDA-MSG-INVALID-KEY (CSMSG01Y) — shared screen header / messages.
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    private const string CCDA_TITLE02 = "              CardDemo                  ";
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";

    // =============================================================================================
    //  SEC-USER-DATA (CSUSR01Y) — the record built from the screen and WRITTEN to USRSEC. source: :53
    //  01 SEC-USER-DATA. 05 SEC-USR-ID X(08). FNAME X(20). LNAME X(20). PWD X(08). TYPE X(01). FILLER X(23).
    // =============================================================================================
    private string _secUsrId = "";    // SEC-USR-ID    X(08) (also the WRITE RIDFLD)
    private string _secUsrFname = ""; // SEC-USR-FNAME X(20)
    private string _secUsrLname = ""; // SEC-USR-LNAME X(20)
    private string _secUsrPwd = "";   // SEC-USR-PWD   X(08)
    private string _secUsrType = "";  // SEC-USR-TYPE  X(01)

    // =============================================================================================
    //  COMMAREA (typed view) + per-turn symbolic map + DB. source: :46,48,62-65
    // =============================================================================================
    private CardDemoCommArea _commArea = new();
    private BmsMap _map = null!;

    private readonly RelationalDb _db;
    private UserSecurityRepository _users = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. The USER_SECURITY repository is created
    /// from <c>db.Connection</c> inside <see cref="Handle"/> (no DB is opened here).
    /// </summary>
    public Cousr01c(RelationalDb db) => _db = db;

    /// <summary>Parameterless ctor for headless/registry probing where no DB is needed.</summary>
    public Cousr01c() => _db = null!;

    /// <inheritdoc/>
    public string ProgramName => WS_PGMNAME; // PROGRAM-ID. COUSR01C. source: :23

    /// <inheritdoc/>
    public string TransId => WS_TRANID;      // CSD: CU01 -> COUSR01C. source: CSD_TRANSACTIONS.md:85; cbl:37

    // =============================================================================================
    //  MAIN-PARA — source: COUSR01C.cbl:71-110
    // =============================================================================================
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Fresh symbolic map for this task (WORKING-STORAGE COPY COUSR01 re-initialised per turn).
        _map = BuildMap();
        if (_db is not null) _users = new UserSecurityRepository(_db.Connection);

        // SET ERR-FLG-OFF TO TRUE. source: :73
        _errFlgOn = false;

        // MOVE SPACES TO WS-MESSAGE  ERRMSGO OF COUSR1AO. source: :75-76
        _wsMessage = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0 -> MOVE 'COSGN00C' TO CDEMO-TO-PROGRAM; PERFORM RETURN-TO-PREV-SCREEN. source: :78-80
            _commArea = ctx.CommArea ?? new CardDemoCommArea();
            _commArea.ToProgram = "COSGN00C";
            ReturnToPrevScreen(ctx);
        }
        else
        {
            // MOVE DFHCOMMAREA(1:EIBCALEN) TO CARDDEMO-COMMAREA. source: :82
            _commArea = ctx.CommArea!;

            if (!_commArea.IsReenter)
            {
                // IF NOT CDEMO-PGM-REENTER. source: :83
                _commArea.SetReenter();          // SET CDEMO-PGM-REENTER TO TRUE. source: :84
                MoveLowValuesToMapOut();          // MOVE LOW-VALUES TO COUSR1AO. source: :85
                _map.Field("FNAME").CursorLength = -1; // MOVE -1 TO FNAMEL OF COUSR1AI. source: :86
                SendUsraddScreen(ctx);            // PERFORM SEND-USRADD-SCREEN. source: :87
            }
            else
            {
                ReceiveUsraddScreen(ctx);         // PERFORM RECEIVE-USRADD-SCREEN. source: :89
                // EVALUATE EIBAID. source: :90
                switch (ctx.EibAid)
                {
                    case AidKey.Enter:
                        ProcessEnterKey(ctx);     // WHEN DFHENTER. source: :91-92
                        break;
                    case AidKey.Pf3:
                        _commArea.ToProgram = "COADM01C"; // WHEN DFHPF3 -> MOVE 'COADM01C'. source: :93-94
                        ReturnToPrevScreen(ctx);          // PERFORM RETURN-TO-PREV-SCREEN. source: :95
                        break;
                    case AidKey.Pf4:
                        ClearCurrentScreen(ctx);  // WHEN DFHPF4 -> PERFORM CLEAR-CURRENT-SCREEN. source: :96-97
                        break;
                    default:
                        // WHEN OTHER. source: :98-102
                        _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :99
                        _map.Field("FNAME").CursorLength = -1;             // MOVE -1 TO FNAMEL. source: :100
                        _wsMessage = CCDA_MSG_INVALID_KEY;                 // MOVE CCDA-MSG-INVALID-KEY. source: :101
                        SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :102
                        break;
                }
            }
        }

        // EXEC CICS RETURN TRANSID(WS-TRANID) COMMAREA(CARDDEMO-COMMAREA). source: :107-110
        if (ctx.Outcome is null)
            ctx.ReturnTransId(WS_TRANID, _commArea);
    }

    // =============================================================================================
    //  PROCESS-ENTER-KEY — source: COUSR01C.cbl:115-160
    // =============================================================================================
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EVALUATE TRUE — first blank field wins; each sets its own message + cursor + SENDs. source: :117-151
        if (IsSpacesOrLowValues(FnameIn))
        {
            // WHEN FNAMEI = SPACES OR LOW-VALUES. source: :118-123
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :119
            _wsMessage = "First Name can NOT be empty...";     // source: :120-121
            _map.Field("FNAME").CursorLength = -1;             // MOVE -1 TO FNAMEL. source: :122
            SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :123
        }
        else if (IsSpacesOrLowValues(LnameIn))
        {
            // WHEN LNAMEI = SPACES OR LOW-VALUES. source: :124-129
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :125
            _wsMessage = "Last Name can NOT be empty...";      // source: :126-127
            _map.Field("LNAME").CursorLength = -1;             // MOVE -1 TO LNAMEL. source: :128
            SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :129
        }
        else if (IsSpacesOrLowValues(UseridIn))
        {
            // WHEN USERIDI = SPACES OR LOW-VALUES. source: :130-135
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :131
            _wsMessage = "User ID can NOT be empty...";        // source: :132-133
            _map.Field("USERID").CursorLength = -1;            // MOVE -1 TO USERIDL. source: :134
            SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :135
        }
        else if (IsSpacesOrLowValues(PasswdIn))
        {
            // WHEN PASSWDI = SPACES OR LOW-VALUES. source: :136-141
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :137
            _wsMessage = "Password can NOT be empty...";       // source: :138-139
            _map.Field("PASSWD").CursorLength = -1;            // MOVE -1 TO PASSWDL. source: :140
            SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :141
        }
        else if (IsSpacesOrLowValues(UsrtypeIn))
        {
            // WHEN USRTYPEI = SPACES OR LOW-VALUES. source: :142-147
            // B-1: ONLY a blank User Type is rejected; any non-blank char is accepted (no A/U check).
            _errFlgOn = true;                                  // MOVE 'Y' TO WS-ERR-FLG. source: :143
            _wsMessage = "User Type can NOT be empty...";      // source: :144-145
            _map.Field("USRTYPE").CursorLength = -1;           // MOVE -1 TO USRTYPEL. source: :146
            SendUsraddScreen(ctx);                             // PERFORM SEND-USRADD-SCREEN. source: :147
        }
        else
        {
            // WHEN OTHER — all five present. B-5: park cursor on First Name then CONTINUE. source: :148-150
            _map.Field("FNAME").CursorLength = -1;             // MOVE -1 TO FNAMEL. source: :149
            // CONTINUE. source: :150
        }

        // IF NOT ERR-FLG-ON -> move the five fields into SEC-USER-DATA and WRITE. source: :153-160
        if (!ErrFlgOn)
        {
            // B-2: no length/format validation — straight MOVE of the symbolic input cells (X-width) into
            //   the fixed-width record fields.
            _secUsrId = PadX(UseridIn, 8);    // MOVE USERIDI  TO SEC-USR-ID.    source: :154
            _secUsrFname = PadX(FnameIn, 20); // MOVE FNAMEI   TO SEC-USR-FNAME. source: :155
            _secUsrLname = PadX(LnameIn, 20); // MOVE LNAMEI   TO SEC-USR-LNAME. source: :156
            _secUsrPwd = PadX(PasswdIn, 8);   // MOVE PASSWDI  TO SEC-USR-PWD.   source: :157
            _secUsrType = PadX(UsrtypeIn, 1); // MOVE USRTYPEI TO SEC-USR-TYPE.  source: :158
            WriteUserSecFile(ctx);            // PERFORM WRITE-USER-SEC-FILE.    source: :159
        }
    }

    // =============================================================================================
    //  RETURN-TO-PREV-SCREEN — source: COUSR01C.cbl:165-178
    // =============================================================================================
    private void ReturnToPrevScreen(CicsContext ctx)
    {
        // IF CDEMO-TO-PROGRAM = LOW-VALUES OR SPACES -> MOVE 'COSGN00C'. source: :167-169
        if (IsSpacesOrLowValues(_commArea.ToProgram))
            _commArea.ToProgram = "COSGN00C";

        _commArea.FromTranId = WS_TRANID;   // MOVE WS-TRANID  TO CDEMO-FROM-TRANID.  source: :170
        _commArea.FromProgram = WS_PGMNAME; // MOVE WS-PGMNAME TO CDEMO-FROM-PROGRAM. source: :171
        // B-6: MOVE WS-USER-ID TO CDEMO-USER-ID and MOVE SEC-USR-TYPE TO CDEMO-USER-TYPE are commented out
        //   in the COBOL; the COMMAREA user identity is left as inherited. source: :172-173
        _commArea.SetFirstEntry();          // MOVE ZEROS TO CDEMO-PGM-CONTEXT. source: :174

        // EXEC CICS XCTL PROGRAM(CDEMO-TO-PROGRAM) COMMAREA(CARDDEMO-COMMAREA). source: :175-178
        ctx.Xctl(_commArea.ToProgram.TrimEnd(), _commArea);
    }

    // =============================================================================================
    //  SEND-USRADD-SCREEN — source: COUSR01C.cbl:184-196
    // =============================================================================================
    private void SendUsraddScreen(CicsContext ctx)
    {
        PopulateHeaderInfo(ctx);                                   // PERFORM POPULATE-HEADER-INFO. source: :186

        _map.Field("ERRMSG").SetValue(_wsMessage, setMdt: false); // MOVE WS-MESSAGE TO ERRMSGO. source: :188

        // EXEC CICS SEND MAP('COUSR1A') MAPSET('COUSR01') FROM(COUSR1AO) ERASE CURSOR. source: :190-196
        ctx.SendMap(MapName, MapsetName, _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = true, // DFHMSD CTRL=(ALARM,FREEKB) -> keyboard unlocked on SEND.
            Cursor = -1,   // CURSOR — honour the MOVE -1 TO xxxL set on the in-error / IC field.
        });
        _wsRespCd = (int)Resp.Normal;
    }

    // =============================================================================================
    //  RECEIVE-USRADD-SCREEN — source: COUSR01C.cbl:201-209
    // =============================================================================================
    private void ReceiveUsraddScreen(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COUSR1A') MAPSET('COUSR01') INTO(COUSR1AI) RESP(WS-RESP-CD) RESP2. source: :203-209
        ctx.ReceiveMap(MapName, MapsetName, _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;
    }

    // =============================================================================================
    //  POPULATE-HEADER-INFO — source: COUSR01C.cbl:214-233
    // =============================================================================================
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA. source: :216
        DateTime now = ctx.Clock.Now;

        _map.Field("TITLE01").SetValue(CCDA_TITLE01, setMdt: false); // MOVE CCDA-TITLE01 TO TITLE01O. source: :218
        _map.Field("TITLE02").SetValue(CCDA_TITLE02, setMdt: false); // MOVE CCDA-TITLE02 TO TITLE02O. source: :219
        _map.Field("TRNNAME").SetValue(WS_TRANID, setMdt: false);    // MOVE WS-TRANID  TO TRNNAMEO. source: :220
        _map.Field("PGMNAME").SetValue(WS_PGMNAME, setMdt: false);   // MOVE WS-PGMNAME TO PGMNAMEO. source: :221

        // CURDATEO = mm/dd/yy (year last two digits). source: :223-227
        _map.Field("CURDATE").SetValue($"{Two(now.Month)}/{Two(now.Day)}/{Four(now.Year).Substring(2, 2)}", setMdt: false);

        // CURTIMEO = hh:mm:ss. source: :229-233
        _map.Field("CURTIME").SetValue($"{Two(now.Hour)}:{Two(now.Minute)}:{Two(now.Second)}", setMdt: false);
    }

    // =============================================================================================
    //  WRITE-USER-SEC-FILE — source: COUSR01C.cbl:238-274
    // =============================================================================================
    private void WriteUserSecFile(CicsContext ctx)
    {
        // EXEC CICS WRITE DATASET(WS-USRSEC-FILE) FROM(SEC-USER-DATA) RIDFLD(SEC-USR-ID) RESP. source: :240-248
        _ = WS_USRSEC_FILE; // dataset name (fixed) — repository is keyed by usr_id.
        var rec = new UserSecurity
        {
            UsrId = _secUsrId,        // SEC-USR-ID    X(08)
            FirstName = _secUsrFname, // SEC-USR-FNAME X(20)
            LastName = _secUsrLname,  // SEC-USR-LNAME X(20)
            Pwd = _secUsrPwd,         // SEC-USR-PWD   X(08)
            UsrType = _secUsrType,    // SEC-USR-TYPE  X(01)
        };
        string fileStatus = _users.Insert(rec);
        SetResp(fileStatus);

        // EVALUATE WS-RESP-CD. source: :250-274
        switch ((Resp)_wsRespCd)
        {
            case Resp.Normal: // WHEN DFHRESP(NORMAL). source: :251-259
                InitializeAllFields();                          // PERFORM INITIALIZE-ALL-FIELDS. source: :252
                _wsMessage = "";                                // MOVE SPACES TO WS-MESSAGE. source: :253
                _map.Field("ERRMSG").ColorOverride = BmsColor.Green; // MOVE DFHGREEN TO ERRMSGC. source: :254
                // STRING 'User ' SEC-USR-ID(DELIMITED BY SPACE) ' has been added ...' INTO WS-MESSAGE. source: :255-258
                // B-4: SEC-USR-ID is delimited by the first SPACE, truncating an id with an embedded space.
                _wsMessage = "User " + DelimitedBySpace(_secUsrId) + " has been added ...";
                SendUsraddScreen(ctx);                          // PERFORM SEND-USRADD-SCREEN. source: :259
                break;

            case Resp.DupKey: // WHEN DFHRESP(DUPKEY). source: :260
            case Resp.DupRec: // WHEN DFHRESP(DUPREC). source: :261-266
                _errFlgOn = true;                               // MOVE 'Y' TO WS-ERR-FLG. source: :262
                _wsMessage = "User ID already exist...";        // source: :263-264
                _map.Field("USERID").CursorLength = -1;         // MOVE -1 TO USERIDL. source: :265
                SendUsraddScreen(ctx);                          // PERFORM SEND-USRADD-SCREEN. source: :266
                break;

            default: // WHEN OTHER. source: :267-273
                _errFlgOn = true;                               // MOVE 'Y' TO WS-ERR-FLG. source: :269
                _wsMessage = "Unable to Add User...";           // source: :270-271
                _map.Field("FNAME").CursorLength = -1;          // MOVE -1 TO FNAMEL. source: :272
                SendUsraddScreen(ctx);                          // PERFORM SEND-USRADD-SCREEN. source: :273
                break;
        }
    }

    // =============================================================================================
    //  CLEAR-CURRENT-SCREEN — source: COUSR01C.cbl:279-282
    // =============================================================================================
    private void ClearCurrentScreen(CicsContext ctx)
    {
        InitializeAllFields();   // PERFORM INITIALIZE-ALL-FIELDS. source: :281
        SendUsraddScreen(ctx);   // PERFORM SEND-USRADD-SCREEN. source: :282
    }

    // =============================================================================================
    //  INITIALIZE-ALL-FIELDS — source: COUSR01C.cbl:287-295
    // =============================================================================================
    private void InitializeAllFields()
    {
        // MOVE -1 TO FNAMEL OF COUSR1AI. source: :289
        _map.Field("FNAME").CursorLength = -1;

        // MOVE SPACES TO USERIDI/FNAMEI/LNAMEI/PASSWDI/USRTYPEI OF COUSR1AI, WS-MESSAGE. source: :290-295
        // B-3: the COBOL clears the symbolic INPUT (...I) halves; on the single-cell console field this
        //   blanks each named field's value (which the subsequent SEND repaints) and resets WS-MESSAGE.
        _map.Field("USERID").SetValue(" ", setMdt: false);
        _map.Field("FNAME").SetValue(" ", setMdt: false);
        _map.Field("LNAME").SetValue(" ", setMdt: false);
        _map.Field("PASSWD").SetValue(" ", setMdt: false);
        _map.Field("USRTYPE").SetValue(" ", setMdt: false);
        _wsMessage = "";
    }

    // =============================================================================================
    //  Symbolic-map input readers — FNAMEI / LNAMEI / USERIDI / PASSWDI / USRTYPEI.
    // =============================================================================================
    private string FnameIn => _map.Field("FNAME").Value;     // FNAMEI   OF COUSR1AI
    private string LnameIn => _map.Field("LNAME").Value;     // LNAMEI   OF COUSR1AI
    private string UseridIn => _map.Field("USERID").Value;   // USERIDI  OF COUSR1AI
    private string PasswdIn => _map.Field("PASSWD").Value;   // PASSWDI  OF COUSR1AI
    private string UsrtypeIn => _map.Field("USRTYPE").Value; // USRTYPEI OF COUSR1AI

    /// <summary>MOVE LOW-VALUES TO COUSR1AO — blank every named output field + clear per-turn overrides. source: :85</summary>
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
            FileStatus.Ok => (int)Resp.Normal,                 // '00' -> DFHRESP(NORMAL)
            FileStatus.DuplicateKey => (int)Resp.DupRec,       // '02' -> DFHRESP(DUPREC)
            FileStatus.DuplicateKeyError => (int)Resp.DupRec,  // '22' (insert PK conflict) -> DFHRESP(DUPREC)
            FileStatus.RecordNotFound => (int)Resp.NotFnd,     // '23' -> DFHRESP(NOTFND)
            FileStatus.EndOfFile => (int)Resp.EndFile,         // '10' -> DFHRESP(ENDFILE)
            _ => (int)Resp.Error,                              // any other -> WHEN OTHER (file error)
        };
        _wsReasCd = 0; // RESP2 unavailable from the relational repo; 0 for parity.
    }

    // =============================================================================================
    //  Helpers — COBOL primitive semantics
    // =============================================================================================

    /// <summary>True when a value is empty, all SPACES, or all LOW-VALUES (NUL) — the COBOL "= SPACES OR LOW-VALUES" guard.</summary>
    private static bool IsSpacesOrLowValues(string? s)
        => string.IsNullOrEmpty(s) || s.All(c => c == ' ') || s.All(c => c == '\0');

    /// <summary>The leading run up to the first SPACE — COBOL <c>STRING ... DELIMITED BY SPACE</c> on SEC-USR-ID. source: :256</summary>
    private static string DelimitedBySpace(string? s)
    {
        s ??= "";
        int i = s.IndexOf(' ');
        return i < 0 ? s : s[..i];
    }

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadX(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    private static string Two(int value) => value.ToString("D2");
    private static string Four(int value) => value.ToString("D4");

    // =============================================================================================
    //  BMS map builder — COUSR1A in mapset COUSR01 (24x80).
    //  source: app/bms/COUSR01.bms:19-161 / SCREEN_COUSR01.md
    // =============================================================================================
    /// <summary>The DFHMDI map name. source: COUSR01.bms:26.</summary>
    public const string MapName = "COUSR1A";

    /// <summary>The DFHMSD mapset name. source: COUSR01.bms:19.</summary>
    public const string MapsetName = "COUSR01";

    /// <summary>
    /// Constructs the <c>COUSR1A</c> screen map: every <c>DFHMDF</c> as a <see cref="ScreenField"/> with its
    /// exact Row/Col/Length/attribute/colour/highlight/initial value, the protected literals and the
    /// zero-length stoppers, and the named in/out fields — in BMS source order. The five keyable fields are
    /// FNAME (8,18 L20, carries <c>IC</c>), LNAME (8,56 L20), USERID (11,15 L8), PASSWD (11,55 L8, DRK/
    /// non-display) and USRTYPE (14,17 L1); the IC drops the cursor on FNAME on first display. No
    /// PICIN/PICOUT clauses appear in this map.
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

            // ----- 'Add User' heading (bms:75-79) -----
            LitAttr(4, 35, 9, AskipBrt, BmsColor.Neutral, "Add User"),         // bms:75-79

            // ----- First Name label + input + stopper (bms:80-91) -----
            Lit(8, 6, 11, BmsColor.Turquoise, "First Name:"),                  // bms:80-83 (no ATTRB clause -> ASKIP,NORM default)
            // FNAME: ATTRB=(FSET,IC,NORM,UNPROT) GREEN HILIGHT=UNDERLINE L20 — IC carries the initial cursor.
            new ScreenField
            {
                Name = "FNAME", Row = 8, Col = 18, Length = 20,
                Attribute = BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:84-88
            Stopper(8, 39),                                                    // bms:89-91 (ASKIP,NORM L0)

            // ----- Last Name label + input + stopper (bms:92-105) -----
            Lit(8, 45, 10, BmsColor.Turquoise, "Last Name:"),                 // bms:92-96
            new ScreenField
            {
                Name = "LNAME", Row = 8, Col = 56, Length = 20,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:97-101
            StopperColor(8, 77, BmsColor.Green),                              // bms:102-105 (ASKIP,NORM,GREEN L0)

            // ----- User ID label + input + hint (bms:106-120) -----
            Lit(11, 6, 8, BmsColor.Turquoise, "User ID:"),                    // bms:106-110
            new ScreenField
            {
                Name = "USERID", Row = 11, Col = 15, Length = 8,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:111-115
            Lit(11, 24, 8, BmsColor.Blue, "(8 Char)"),                        // bms:116-120

            // ----- Password label + input (DRK) + hint (bms:121-135) -----
            Lit(11, 45, 9, BmsColor.Turquoise, "Password:"),                  // bms:121-125
            // PASSWD: ATTRB=(DRK,FSET,UNPROT) GREEN HILIGHT=UNDERLINE L8 — dark / non-display (masked).
            new ScreenField
            {
                Name = "PASSWD", Row = 11, Col = 55, Length = 8,
                Attribute = BmsAttribute.Dark | BmsAttribute.Fset | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:126-130
            Lit(11, 64, 8, BmsColor.Blue, "(8 Char)"),                        // bms:131-135

            // ----- User Type label + input + hint (bms:136-150) -----
            Lit(14, 6, 11, BmsColor.Turquoise, "User Type: "),                // bms:136-140 (trailing space in literal)
            new ScreenField
            {
                Name = "USRTYPE", Row = 14, Col = 17, Length = 1,
                Attribute = BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Unprotected,
                Color = BmsColor.Green,
                Hilight = BmsHilight.Underline,
            },                                                                 // bms:141-145
            Lit(14, 19, 17, BmsColor.Blue, "(A=Admin, U=User)"),              // bms:146-150

            // ----- error message line + PF-key legend (bms:151-159) -----
            Out("ERRMSG", 23, 1, 78, AskipBrtFset, BmsColor.Red),             // bms:151-154
            LitAttr(24, 1, 43, Askip, BmsColor.Yellow,
                "ENTER=Add User  F3=Back  F4=Clear  F12=Exit"),               // bms:155-159
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

    /// <summary>Unnamed literal with an explicit attribute (e.g. ASKIP,BRT).</summary>
    private static ScreenField LitAttr(int row, int col, int len, BmsAttribute attr, BmsColor color, string text) =>
        new() { Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = text };

    /// <summary>Named output field (no highlight).</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color };

    /// <summary>Named output field with an INITIAL value.</summary>
    private static ScreenField OutInit(string name, int row, int col, int len, BmsAttribute attr, BmsColor color, string initial) =>
        new() { Name = name, Row = row, Col = col, Length = len, Attribute = attr, Color = color, Value = initial };

    /// <summary>A LENGTH=0 stopper field (attribute cell only, ATTRB=(ASKIP,NORM), default colour).</summary>
    private static ScreenField Stopper(int row, int col) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = BmsColor.Default };

    /// <summary>A LENGTH=0 stopper field with an explicit colour (ATTRB=(ASKIP,NORM)).</summary>
    private static ScreenField StopperColor(int row, int col, BmsColor color) =>
        new() { Row = row, Col = col, Length = 0, Attribute = Askip, Color = color };
}
