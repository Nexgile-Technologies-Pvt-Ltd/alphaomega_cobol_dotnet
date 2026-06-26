using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Online.Programs;

/// <summary>
/// Port of the online CICS program <c>COSGN00C</c> — the CardDemo pseudo-conversational sign-on (logon)
/// screen. It displays the 24x80 BMS login panel (<c>COSGN0A</c> in mapset <c>COSGN00</c>), receives a
/// User ID + Password, upper-cases both, reads the user-security file (<c>USRSEC</c>) by User ID, validates
/// the password, and — on success — XCTLs to either the admin menu (<c>COADM01C</c>, user type <c>'A'</c>)
/// or the main menu (<c>COMEN01C</c>, everyone else), seeding the shared <see cref="CardDemoCommArea"/>.
/// On any validation/lookup failure it re-displays the screen with a red error message; PF3 exits with a
/// thank-you message.
/// </summary>
/// <remarks>
/// This is a near-mechanical, faithful port of the COBOL <c>PROCEDURE DIVISION</c>. Each COBOL paragraph
/// maps to one method with the original paragraph name and <c>// source: COSGN00C.cbl:NNN</c> citations.
/// Statement order, the <c>EVALUATE</c>/<c>PERFORM</c> control flow, COMMAREA field usage, and every
/// faithful bug (FB-1..FB-3, see the port spec §7) are preserved verbatim. Do not "fix" them.
/// </remarks>
public sealed class Cosgn00c : ITransactionHandler
{
    // === WORKING-STORAGE (WS-VARIABLES) — source: COSGN00C.cbl:35-46 ===
    private const string WS_PGMNAME = "COSGN00C";   // 05 WS-PGMNAME    PIC X(08) VALUE 'COSGN00C'  // :36
    private const string WS_TRANID = "CC00";        // 05 WS-TRANID     PIC X(04) VALUE 'CC00'      // :37
    private const string WS_USRSEC_FILE = "USRSEC  "; // 05 WS-USRSEC-FILE PIC X(08) VALUE 'USRSEC  ' // :39

    private string _wsMessage = "";                 // 05 WS-MESSAGE    PIC X(80) VALUE SPACES       // :38
    private bool _errFlgOn;                          // 05 WS-ERR-FLG    PIC X(01) VALUE 'N' (88 ERR-FLG-ON='Y') // :40-42
    private int _wsRespCd;                           // 05 WS-RESP-CD    PIC S9(09) COMP VALUE ZEROS  // :43
    private int _wsReasCd;                           // 05 WS-REAS-CD    PIC S9(09) COMP VALUE ZEROS  // :44 (captured, never inspected)
    private string _wsUserId = "";                  // 05 WS-USER-ID    PIC X(08)                    // :45
    private string _wsUserPwd = "";                 // 05 WS-USER-PWD   PIC X(08)                    // :46

    // === Common-message constants pulled in via CSMSG01Y / COTTL01Y copybooks ===
    // CCDA-MSG-THANK-YOU   PIC X(50)  // CSMSG01Y.cpy:18-19
    private const string CCDA_MSG_THANK_YOU = "Thank you for using CardDemo application...      ";
    // CCDA-MSG-INVALID-KEY PIC X(50)  // CSMSG01Y.cpy:20-21
    private const string CCDA_MSG_INVALID_KEY = "Invalid key pressed. Please see below...         ";
    // CCDA-TITLE01 PIC X(40)          // COTTL01Y.cpy:18-19
    private const string CCDA_TITLE01 = "      AWS Mainframe Modernization       ";
    // CCDA-TITLE02 PIC X(40)          // COTTL01Y.cpy:20-22
    private const string CCDA_TITLE02 = "              CardDemo                  ";

    // === CICS ASSIGN shim values (no real CICS region) — COSGN00C.cbl:198-204 / port spec §8.10 ===
    private const string ApplId = "CICSAWS ";       // EXEC CICS ASSIGN APPLID(...)  fixed parity value
    private const string SysId = "AWS1";            // EXEC CICS ASSIGN SYSID(...)   fixed parity value

    private readonly RelationalDb _db;

    // The per-turn BMS symbolic map (COSGN0AI / COSGN0AO overlay), built once per Handle.
    private BmsMap _map = null!;

    /// <summary>
    /// Factory-friendly constructor: takes the shared relational DB. Per-table repositories are created
    /// from <c>db.Connection</c> inside the handler when they are needed (no DB is opened here).
    /// </summary>
    public Cosgn00c(RelationalDb db) => _db = db;

    /// <inheritdoc/>
    public string ProgramName => "COSGN00C";

    /// <inheritdoc/>
    public string TransId => "CC00"; // CSD: CC00 -> COSGN00C (entry point); program declares TRANSID(CC00)

    // -----------------------------------------------------------------------------------------------
    //  MAIN-PARA (entry point) — source: COSGN00C.cbl:73-102
    // -----------------------------------------------------------------------------------------------
    /// <inheritdoc/>
    public void Handle(CicsContext ctx)
    {
        // Build a fresh symbolic map for this task (WORKING-STORAGE COPY COSGN00 re-initialised per turn).
        _map = BuildMap();

        // SET ERR-FLG-OFF TO TRUE                                            // source: COSGN00C.cbl:75
        _errFlgOn = false;

        // MOVE SPACES TO WS-MESSAGE
        //                ERRMSGO OF COSGN0AO                                  // source: COSGN00C.cbl:77-78
        _wsMessage = "";
        _map.Field("ERRMSG").SetValue("", setMdt: false);

        if (ctx.EibCalen == 0)
        {
            // IF EIBCALEN = 0                                                 // source: COSGN00C.cbl:80
            //     MOVE LOW-VALUES TO COSGN0AO                                // source: COSGN00C.cbl:81
            MoveLowValuesToMapOut();
            //     MOVE -1 TO USERIDL OF COSGN0AI                             // source: COSGN00C.cbl:82
            _map.Field("USERID").CursorLength = -1;
            //     PERFORM SEND-SIGNON-SCREEN                                 // source: COSGN00C.cbl:83
            SendSignonScreen(ctx);
        }
        else
        {
            // EVALUATE EIBAID                                                 // source: COSGN00C.cbl:85
            switch (ctx.EibAid)
            {
                case AidKey.Enter:
                    // WHEN DFHENTER PERFORM PROCESS-ENTER-KEY                // source: COSGN00C.cbl:86-87
                    ProcessEnterKey(ctx);
                    break;

                case AidKey.Pf3:
                    // WHEN DFHPF3                                            // source: COSGN00C.cbl:88
                    //     MOVE CCDA-MSG-THANK-YOU TO WS-MESSAGE             // source: COSGN00C.cbl:89
                    _wsMessage = CCDA_MSG_THANK_YOU;
                    //     PERFORM SEND-PLAIN-TEXT                           // source: COSGN00C.cbl:90
                    SendPlainText(ctx);
                    break;

                default:
                    // WHEN OTHER                                            // source: COSGN00C.cbl:91
                    //     MOVE 'Y' TO WS-ERR-FLG                            // source: COSGN00C.cbl:92
                    _errFlgOn = true;
                    //     MOVE CCDA-MSG-INVALID-KEY TO WS-MESSAGE          // source: COSGN00C.cbl:93
                    _wsMessage = CCDA_MSG_INVALID_KEY;
                    //     PERFORM SEND-SIGNON-SCREEN                        // source: COSGN00C.cbl:94
                    SendSignonScreen(ctx);
                    break;
            }
        }

        // The PF3 path already issued EXEC CICS RETURN (no TRANSID) inside SEND-PLAIN-TEXT, and the
        // success path already issued XCTL inside READ-USER-SEC-FILE; in those cases an Outcome is set
        // and this RETURN is unreachable in COBOL terms. Guard so we do not overwrite it.
        if (ctx.Outcome is null)
        {
            // EXEC CICS RETURN
            //           TRANSID  (WS-TRANID)
            //           COMMAREA (CARDDEMO-COMMAREA)
            //           LENGTH(LENGTH OF CARDDEMO-COMMAREA)                  // source: COSGN00C.cbl:98-102
            ctx.CommArea ??= new CardDemoCommArea();
            ctx.ReturnTransId(WS_TRANID, ctx.CommArea);
        }
    }

    // -----------------------------------------------------------------------------------------------
    //  PROCESS-ENTER-KEY — source: COSGN00C.cbl:108-140
    // -----------------------------------------------------------------------------------------------
    private void ProcessEnterKey(CicsContext ctx)
    {
        // EXEC CICS RECEIVE MAP('COSGN0A') MAPSET('COSGN00')
        //           RESP(WS-RESP-CD) RESP2(WS-REAS-CD)                       // source: COSGN00C.cbl:110-115
        ctx.ReceiveMap("COSGN0A", "COSGN00", _map);
        _wsRespCd = (int)Resp.Normal;
        _wsReasCd = 0;

        string useridi = _map.Field("USERID").Value; // USERIDI OF COSGN0AI
        string passwdi = _map.Field("PASSWD").Value; // PASSWDI OF COSGN0AI

        // EVALUATE TRUE                                                      // source: COSGN00C.cbl:117
        if (IsSpacesOrLowValues(useridi))
        {
            // WHEN USERIDI = SPACES OR LOW-VALUES                           // source: COSGN00C.cbl:118
            //     MOVE 'Y' TO WS-ERR-FLG                                    // source: COSGN00C.cbl:119
            _errFlgOn = true;
            //     MOVE 'Please enter User ID ...' TO WS-MESSAGE             // source: COSGN00C.cbl:120
            _wsMessage = "Please enter User ID ...";
            //     MOVE -1 TO USERIDL OF COSGN0AI                            // source: COSGN00C.cbl:121
            _map.Field("USERID").CursorLength = -1;
            //     PERFORM SEND-SIGNON-SCREEN                                // source: COSGN00C.cbl:122
            SendSignonScreen(ctx);
        }
        else if (IsSpacesOrLowValues(passwdi))
        {
            // WHEN PASSWDI = SPACES OR LOW-VALUES                           // source: COSGN00C.cbl:123
            //     MOVE 'Y' TO WS-ERR-FLG                                    // source: COSGN00C.cbl:124
            _errFlgOn = true;
            //     MOVE 'Please enter Password ...' TO WS-MESSAGE            // source: COSGN00C.cbl:125
            _wsMessage = "Please enter Password ...";
            //     MOVE -1 TO PASSWDL OF COSGN0AI                            // source: COSGN00C.cbl:126
            _map.Field("PASSWD").CursorLength = -1;
            //     PERFORM SEND-SIGNON-SCREEN                                // source: COSGN00C.cbl:127
            SendSignonScreen(ctx);
        }
        else
        {
            // WHEN OTHER CONTINUE                                           // source: COSGN00C.cbl:128-129
        }
        // END-EVALUATE                                                      // source: COSGN00C.cbl:130

        // The EVALUATE branches above PERFORM SEND-SIGNON-SCREEN but do NOT exit the paragraph; control
        // falls through to these UNCONDITIONAL upper-case MOVEs (faithful bug FB-1 / FB-3).

        // MOVE FUNCTION UPPER-CASE(USERIDI OF COSGN0AI) TO
        //                 WS-USER-ID
        //                 CDEMO-USER-ID                                      // source: COSGN00C.cbl:132-134
        _wsUserId = UpperCase(useridi);
        ctx.CommArea ??= new CardDemoCommArea();
        ctx.CommArea.UserId = _wsUserId; // CDEMO-USER-ID set even on validation failure (FB-3)

        // MOVE FUNCTION UPPER-CASE(PASSWDI OF COSGN0AI) TO WS-USER-PWD       // source: COSGN00C.cbl:135-136
        _wsUserPwd = UpperCase(passwdi);

        // IF NOT ERR-FLG-ON PERFORM READ-USER-SEC-FILE                       // source: COSGN00C.cbl:138-140
        if (!_errFlgOn)
        {
            ReadUserSecFile(ctx);
        }
    }

    // -----------------------------------------------------------------------------------------------
    //  SEND-SIGNON-SCREEN — source: COSGN00C.cbl:145-157
    // -----------------------------------------------------------------------------------------------
    private void SendSignonScreen(CicsContext ctx)
    {
        // PERFORM POPULATE-HEADER-INFO                                       // source: COSGN00C.cbl:147
        PopulateHeaderInfo(ctx);

        // MOVE WS-MESSAGE TO ERRMSGO OF COSGN0AO                            // source: COSGN00C.cbl:149
        // ERRMSGO is X(78); WS-MESSAGE is X(80) -> truncates to 78 (SetValue truncates to field length).
        _map.Field("ERRMSG").SetValue(_wsMessage);

        // EXEC CICS SEND MAP('COSGN0A') MAPSET('COSGN00')
        //           FROM(COSGN0AO) ERASE CURSOR                             // source: COSGN00C.cbl:151-157
        ctx.SendMap("COSGN0A", "COSGN00", _map, new SendMapOptions
        {
            Erase = true,
            FreeKb = false,
            Cursor = -1,
        });
    }

    // -----------------------------------------------------------------------------------------------
    //  SEND-PLAIN-TEXT — source: COSGN00C.cbl:162-172
    // -----------------------------------------------------------------------------------------------
    private void SendPlainText(CicsContext ctx)
    {
        // EXEC CICS SEND TEXT FROM(WS-MESSAGE) LENGTH(LENGTH OF WS-MESSAGE)
        //           ERASE FREEKB                                            // source: COSGN00C.cbl:164-169
        // WS-MESSAGE is X(80): right-pad the message to the full 80 chars for SEND TEXT.
        ctx.SendText(PadToWidth(_wsMessage, 80), erase: true, freeKb: true);

        // EXEC CICS RETURN  (no TRANSID — ends the pseudo-conversation / logoff)   // source: COSGN00C.cbl:171-172
        ctx.ReturnTerminal();
    }

    // -----------------------------------------------------------------------------------------------
    //  POPULATE-HEADER-INFO — source: COSGN00C.cbl:177-204
    // -----------------------------------------------------------------------------------------------
    private void PopulateHeaderInfo(CicsContext ctx)
    {
        // MOVE FUNCTION CURRENT-DATE TO WS-CURDATE-DATA                      // source: COSGN00C.cbl:179
        DateTime now = ctx.Clock.Now;

        // MOVE CCDA-TITLE01 TO TITLE01O OF COSGN0AO                          // source: COSGN00C.cbl:181
        _map.Field("TITLE01").SetValue(CCDA_TITLE01);
        // MOVE CCDA-TITLE02 TO TITLE02O OF COSGN0AO                          // source: COSGN00C.cbl:182
        _map.Field("TITLE02").SetValue(CCDA_TITLE02);
        // MOVE WS-TRANID    TO TRNNAMEO OF COSGN0AO                          // source: COSGN00C.cbl:183
        _map.Field("TRNNAME").SetValue(WS_TRANID);
        // MOVE WS-PGMNAME   TO PGMNAMEO OF COSGN0AO                          // source: COSGN00C.cbl:184
        _map.Field("PGMNAME").SetValue(WS_PGMNAME);

        // Build WS-CURDATE-MM-DD-YY = MM '/' DD '/' YY (YEAR(3:2) = last 2 digits)  // source: COSGN00C.cbl:186-188
        // MOVE WS-CURDATE-MM-DD-YY TO CURDATEO OF COSGN0AO                   // source: COSGN00C.cbl:190
        string mm = Two(now.Month);
        string dd = Two(now.Day);
        string yy = FourDigit(now.Year).Substring(2, 2); // WS-CURDATE-YEAR(3:2)
        _map.Field("CURDATE").SetValue($"{mm}/{dd}/{yy}");

        // Build WS-CURTIME-HH-MM-SS = HH ':' MM ':' SS                       // source: COSGN00C.cbl:192-194
        // MOVE WS-CURTIME-HH-MM-SS TO CURTIMEO OF COSGN0AO                   // source: COSGN00C.cbl:196
        string hh = Two(now.Hour);
        string min = Two(now.Minute);
        string ss = Two(now.Second);
        _map.Field("CURTIME").SetValue($"{hh}:{min}:{ss}");

        // EXEC CICS ASSIGN APPLID(APPLIDO OF COSGN0AO)                       // source: COSGN00C.cbl:198-200
        _map.Field("APPLID").SetValue(ApplId);
        // EXEC CICS ASSIGN SYSID(SYSIDO OF COSGN0AO)                         // source: COSGN00C.cbl:202-204
        _map.Field("SYSID").SetValue(SysId);
    }

    // -----------------------------------------------------------------------------------------------
    //  READ-USER-SEC-FILE — source: COSGN00C.cbl:209-257
    // -----------------------------------------------------------------------------------------------
    private void ReadUserSecFile(CicsContext ctx)
    {
        // EXEC CICS READ DATASET(WS-USRSEC-FILE) INTO(SEC-USER-DATA)
        //           RIDFLD(WS-USER-ID) KEYLENGTH(LENGTH OF WS-USER-ID)
        //           RESP(WS-RESP-CD) RESP2(WS-REAS-CD)                       // source: COSGN00C.cbl:211-219
        //
        // VSAM keyed READ -> repository ReadByKey. The key is WS-USER-ID padded to X(8) (the lookup key
        // is the upper-cased input right-padded to 8 with spaces; usr_id is stored X(8)). Map the FileStatus
        // outcome to the CICS RESP code the COBOL EVALUATE branches on: Found('00')->0, NotFnd('23')->13,
        // any other infrastructure failure -> the "other" branch. _ = WS_USRSEC_FILE (dataset name, fixed).
        _ = WS_USRSEC_FILE;
        var repo = new UserSecurityRepository(_db.Connection);
        string ridfld = PadToWidth(_wsUserId, 8);

        UserSecurity? secUser;
        string fileStatus;
        try
        {
            fileStatus = repo.ReadByKey(ridfld, out secUser);
            _wsRespCd = fileStatus switch
            {
                FileStatus.Ok => (int)Resp.Normal,            // 0  -> DFHRESP(NORMAL)
                FileStatus.RecordNotFound => (int)Resp.NotFnd, // 13 -> DFHRESP(NOTFND)
                _ => (int)Resp.Error,                          // any other -> "other"
            };
        }
        catch
        {
            // Infrastructure/exception -> "other" branch (WHEN OTHER), per port spec §2.
            secUser = null;
            _wsRespCd = (int)Resp.Error;
        }

        // EVALUATE WS-RESP-CD                                                // source: COSGN00C.cbl:221
        if (_wsRespCd == 0)
        {
            // WHEN 0                                                        // source: COSGN00C.cbl:222
            string secUsrPwd = secUser?.Pwd ?? "";   // SEC-USR-PWD  X(8)
            string secUsrType = secUser?.UsrType ?? ""; // SEC-USR-TYPE X(1)

            // IF SEC-USR-PWD = WS-USER-PWD  (byte-for-byte X(8) equality)   // source: COSGN00C.cbl:223
            if (PadToWidth(secUsrPwd, 8) == PadToWidth(_wsUserPwd, 8))
            {
                ctx.CommArea ??= new CardDemoCommArea();
                // MOVE WS-TRANID    TO CDEMO-FROM-TRANID                     // source: COSGN00C.cbl:224
                ctx.CommArea.FromTranId = WS_TRANID;
                // MOVE WS-PGMNAME   TO CDEMO-FROM-PROGRAM                    // source: COSGN00C.cbl:225
                ctx.CommArea.FromProgram = WS_PGMNAME;
                // MOVE WS-USER-ID   TO CDEMO-USER-ID                         // source: COSGN00C.cbl:226
                ctx.CommArea.UserId = _wsUserId;
                // MOVE SEC-USR-TYPE TO CDEMO-USER-TYPE                       // source: COSGN00C.cbl:227
                ctx.CommArea.UserType = secUsrType;
                // MOVE ZEROS        TO CDEMO-PGM-CONTEXT                     // source: COSGN00C.cbl:228
                ctx.CommArea.PgmContext = 0;

                // IF CDEMO-USRTYP-ADMIN                                      // source: COSGN00C.cbl:230
                if (ctx.CommArea.IsAdmin)
                {
                    // EXEC CICS XCTL PROGRAM('COADM01C') COMMAREA(...)       // source: COSGN00C.cbl:231-234
                    ctx.Xctl("COADM01C", ctx.CommArea);
                }
                else
                {
                    // EXEC CICS XCTL PROGRAM('COMEN01C') COMMAREA(...)       // source: COSGN00C.cbl:236-239
                    ctx.Xctl("COMEN01C", ctx.CommArea);
                }
            }
            else
            {
                // ELSE (password mismatch — note: NO WS-ERR-FLG set here)    // source: COSGN00C.cbl:241
                // MOVE 'Wrong Password. Try again ...' TO WS-MESSAGE        // source: COSGN00C.cbl:242-243
                _wsMessage = "Wrong Password. Try again ...";
                // MOVE -1 TO PASSWDL OF COSGN0AI                            // source: COSGN00C.cbl:244
                _map.Field("PASSWD").CursorLength = -1;
                // PERFORM SEND-SIGNON-SCREEN                                // source: COSGN00C.cbl:245
                SendSignonScreen(ctx);
            }
        }
        else if (_wsRespCd == 13)
        {
            // WHEN 13                                                       // source: COSGN00C.cbl:247
            // MOVE 'Y' TO WS-ERR-FLG                                        // source: COSGN00C.cbl:248
            _errFlgOn = true;
            // MOVE 'User not found. Try again ...' TO WS-MESSAGE            // source: COSGN00C.cbl:249
            _wsMessage = "User not found. Try again ...";
            // MOVE -1 TO USERIDL OF COSGN0AI                               // source: COSGN00C.cbl:250
            _map.Field("USERID").CursorLength = -1;
            // PERFORM SEND-SIGNON-SCREEN                                    // source: COSGN00C.cbl:251
            SendSignonScreen(ctx);
        }
        else
        {
            // WHEN OTHER                                                    // source: COSGN00C.cbl:252
            // MOVE 'Y' TO WS-ERR-FLG                                        // source: COSGN00C.cbl:253
            _errFlgOn = true;
            // MOVE 'Unable to verify the User ...' TO WS-MESSAGE            // source: COSGN00C.cbl:254
            _wsMessage = "Unable to verify the User ...";
            // MOVE -1 TO USERIDL OF COSGN0AI                               // source: COSGN00C.cbl:255
            _map.Field("USERID").CursorLength = -1;
            // PERFORM SEND-SIGNON-SCREEN                                    // source: COSGN00C.cbl:256
            SendSignonScreen(ctx);
        }
        // END-EVALUATE                                                      // source: COSGN00C.cbl:257
    }

    // -----------------------------------------------------------------------------------------------
    //  Helpers (COBOL idioms)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// MOVE LOW-VALUES TO COSGN0AO — zero-fill the entire output map (all named output fields blank,
    /// per-turn overrides cleared) before the first SEND. // source: COSGN00C.cbl:81
    /// </summary>
    private void MoveLowValuesToMapOut()
    {
        foreach (ScreenField f in _map.Fields)
        {
            if (f.IsNamed)
                f.SetValue("", setMdt: false);
            f.ResetTurnState();
        }
    }

    /// <summary>
    /// COBOL <c>= SPACES OR LOW-VALUES</c> test on a BMS input field. From a RECEIVE, an un-keyed field
    /// arrives as LOW-VALUES (modeled here as empty/null) or as all spaces; both mean "empty". // source: COSGN00C.cbl:118,123
    /// </summary>
    private static bool IsSpacesOrLowValues(string? value)
        => string.IsNullOrEmpty(value) || value.All(c => c == ' ' || c == '\0');

    /// <summary>
    /// <c>FUNCTION UPPER-CASE</c>: upper-cases ASCII A-Z only (invariant, not Turkish-locale); digits and
    /// punctuation pass through unchanged. // source: COSGN00C.cbl:132,135
    /// </summary>
    private static string UpperCase(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        Span<char> buf = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            buf[i] = (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
        }
        return new string(buf);
    }

    /// <summary>Right-pads (or truncates) a value to a fixed COBOL X(n) width with spaces.</summary>
    private static string PadToWidth(string? value, int width)
    {
        value ??= "";
        if (value.Length == width) return value;
        if (value.Length > width) return value[..width];
        return value.PadRight(width, ' ');
    }

    private static string Two(int value) => value.ToString("D2"); // zoned 9(02) display
    private static string FourDigit(int value) => value.ToString("D4"); // 9(04) display

    // -----------------------------------------------------------------------------------------------
    //  BMS map builder — COSGN0A in mapset COSGN00 (24x80). source: COSGN00.bms:19-205 / SCREEN_COSGN00.md
    // -----------------------------------------------------------------------------------------------
    /// <summary>
    /// Constructs the <c>COSGN0A</c> screen map from the BMS definition: every <c>DFHMDF</c> as a
    /// <see cref="ScreenField"/> with its exact Row/Col/Length/attribute/colour/initial value, the IC
    /// cursor on <c>USERID</c>, the protected literals, and the named in/out fields. Field order matches
    /// the BMS source so the renderer paints them in the same sequence.
    /// </summary>
    public static BmsMap BuildMap()
    {
        var fields = new List<ScreenField>
        {
            // 1: Tran : label                                               // bms:29-33
            Lit(1, 1, 6, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "Tran :"),
            // 2: TRNNAME (out)                                              // bms:34-37
            Out("TRNNAME", 1, 8, 4, BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal, BmsColor.Blue),
            // 3: TITLE01 (out)                                             // bms:38-41
            Out("TITLE01", 1, 21, 40, BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal, BmsColor.Yellow),
            // 4: Date : label                                             // bms:42-46
            Lit(1, 64, 6, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "Date :"),
            // 5: CURDATE (out) INITIAL 'mm/dd/yy'                         // bms:47-51
            Out("CURDATE", 1, 71, 8, BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal, BmsColor.Blue, "mm/dd/yy"),
            // 6: Prog : label                                             // bms:52-56
            Lit(2, 1, 6, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "Prog :"),
            // 7: PGMNAME (out)                                            // bms:57-60
            Out("PGMNAME", 2, 8, 8, BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected, BmsColor.Blue),
            // 8: TITLE02 (out)                                            // bms:61-64
            Out("TITLE02", 2, 21, 40, BmsAttribute.AutoSkip | BmsAttribute.Fset | BmsAttribute.Normal, BmsColor.Yellow),
            // 9: Time : label                                             // bms:65-69
            Lit(2, 64, 6, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "Time :"),
            // 10: CURTIME (out) INITIAL 'Ahh:mm:ss'                       // bms:70-74
            Out("CURTIME", 2, 71, 9, BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected, BmsColor.Blue, "Ahh:mm:ss"),
            // 11: AppID: label                                            // bms:75-79
            Lit(3, 1, 6, BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected, BmsColor.Blue, "AppID:"),
            // 12: APPLID (out)                                            // bms:80-83
            Out("APPLID", 3, 8, 8, BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected, BmsColor.Blue),
            // 13: SysID: label                                            // bms:84-88
            Lit(3, 64, 6, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "SysID:"),
            // 14: SYSID (out) INITIAL 8 spaces                            // bms:89-93
            Out("SYSID", 3, 71, 8, BmsAttribute.Fset | BmsAttribute.Normal | BmsAttribute.Protected, BmsColor.Blue, "        "),
            // 15: app banner (row 5)                                      // bms:94-99
            Lit(5, 6, 66, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Neutral,
                "This is a Credit Card Demo Application for Mainframe Modernization"),
            // 16-24: ONE DOLLAR ASCII-art box (rows 7-15)                 // bms:100-144
            Lit(7, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "+========================================+"),
            Lit(8, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%%%%%%%  NATIONAL RESERVE NOTE  %%%%%%%%|"),
            Lit(9, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%(1)  THE UNITED STATES OF KICSLAND (1)%|"),
            Lit(10, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%$$              ___       ********  $$%|"),
            Lit(11, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%$    {x}       (o o)                 $%|"),
            Lit(12, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%$     ******  (  V  )      O N E     $%|"),
            Lit(13, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%(1)          ---m-m---             (1)%|"),
            Lit(14, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "|%%~~~~~~~~~~~ ONE DOLLAR ~~~~~~~~~~~~~%%|"),
            Lit(15, 21, 42, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "+========================================+"),
            // 25: prompt (row 17)                                         // bms:145-150
            Lit(17, 16, 49, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Turquoise,
                "Type your User ID and Password, then press ENTER:"),
            // 26: 'User ID     :' label                                   // bms:151-155
            Lit(19, 29, 13, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Turquoise, "User ID     :"),
            // 27: USERID (in) — FSET,IC,NORM,UNPROT GREEN, HILIGHT=OFF, IC cursor  // bms:156-160
            In("USERID", 19, 43, 8,
                BmsAttribute.Fset | BmsAttribute.Ic | BmsAttribute.Normal | BmsAttribute.Unprotected,
                BmsColor.Green),
            // 28: zero-length stopper for USERID                          // bms:161-164
            Lit(19, 52, 0, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Green, ""),
            // 29: '(8 Char)' hint                                         // bms:165-169
            Lit(19, 52, 8, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "(8 Char)"),
            // 30: 'Password    :' label                                   // bms:170-174
            Lit(20, 29, 13, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Turquoise, "Password    :"),
            // 31: PASSWD (in) — DRK,FSET,UNPROT GREEN, INITIAL '________', HILIGHT=OFF (non-display)  // bms:175-180
            In("PASSWD", 20, 43, 8,
                BmsAttribute.Dark | BmsAttribute.Fset | BmsAttribute.Unprotected,
                BmsColor.Green, "________"),
            // 32: zero-length stopper for PASSWD                          // bms:181-184
            Lit(20, 52, 0, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Green, ""),
            // 33: '(8 Char)' hint                                         // bms:185-189
            Lit(20, 52, 8, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Blue, "(8 Char)"),
            // 34: 1-char DRK,UNPROT spacer (default color), INITIAL ' '   // bms:190-193
            Lit(20, 61, 1, BmsAttribute.Dark | BmsAttribute.Unprotected, BmsColor.Default, " "),
            // 35: zero-length stopper (default color)                     // bms:194-196
            Lit(20, 63, 0, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Default, ""),
            // 36: ERRMSG (out) — ASKIP,BRT,FSET RED, LENGTH=78            // bms:197-200
            Out("ERRMSG", 23, 1, 78, BmsAttribute.AutoSkip | BmsAttribute.Bright | BmsAttribute.Fset, BmsColor.Red),
            // 37: footer 'ENTER=Sign-on  F3=Exit'                         // bms:201-205
            Lit(24, 1, 22, BmsAttribute.AutoSkip | BmsAttribute.Normal, BmsColor.Yellow, "ENTER=Sign-on  F3=Exit"),
        };

        return new BmsMap("COSGN0A", "COSGN00", fields, rows: 24, cols: 80);
    }

    /// <summary>Builds an unnamed literal/constant field (its INITIAL text painted verbatim).</summary>
    private static ScreenField Lit(int row, int col, int len, BmsAttribute attr, BmsColor color, string initial)
        => new()
        {
            Row = row,
            Col = col,
            Length = len,
            Attribute = attr,
            Color = color,
            Hilight = BmsHilight.Off,
            Value = initial,
        };

    /// <summary>Builds a named output field (protected/autoskip), with an optional INITIAL value.</summary>
    private static ScreenField Out(string name, int row, int col, int len, BmsAttribute attr, BmsColor color,
        string initial = "")
        => new()
        {
            Name = name,
            Row = row,
            Col = col,
            Length = len,
            Attribute = attr,
            Color = color,
            Hilight = BmsHilight.Off,
            Value = initial,
        };

    /// <summary>Builds a named input field (unprotected/keyable), with an optional INITIAL value. HILIGHT=OFF.</summary>
    private static ScreenField In(string name, int row, int col, int len, BmsAttribute attr, BmsColor color,
        string initial = "")
        => new()
        {
            Name = name,
            Row = row,
            Col = col,
            Length = len,
            Attribute = attr,
            Color = color,
            Hilight = BmsHilight.Off,
            Value = initial,
        };
}
