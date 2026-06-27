using CardDemo.Runtime;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util subprogram <c>CSUTLDTC</c> — a thin COBOL wrapper around
/// the Language Environment (LE) callable service <c>CEEDAYS</c>. Given a date string and a CEEDAYS
/// picture/mask string it asks <c>CEEDAYS</c> to convert the date to a Lillian day number; it does NOT use
/// the Lillian result — it inspects the <c>CEEDAYS</c> feedback (condition) token to decide whether the
/// date is valid, builds a fixed 80-byte human-readable result message (severity, message number, a 15-char
/// verdict, the test date, and the mask used), and returns that message plus the LE severity in the COBOL
/// special register <c>RETURN-CODE</c>. In effect a single-date validator: "is this date string parseable
/// under this mask?". // source: CSUTLDTC.cbl:1-2, CSUTLDTC.cbl:20, CSUTLDTC.cbl:116-120, CSUTLDTC.cbl:128-149
/// </summary>
/// <remarks>
/// <para>Per <c>_design/ARCHITECTURE.md</c> and the port spec (<c>_design/specs/CSUTLDTC.md §2</c>) there is
/// <b>nothing to translate to the relational layer</b>: <c>CSUTLDTC</c> has no <c>ENVIRONMENT DIVISION</c>,
/// no <c>FILE-CONTROL</c>, no <c>FD</c>, no SELECT, no DB2/IMS/MQ, and touches no table. The repository
/// contract does not apply. The only external call is the LE service <c>CEEDAYS</c>.</para>
///
/// <para>There is no LE/CEEDAYS in .NET, so <see cref="Ceedays"/> emulates the parse-and-validate behavior
/// for the masks the callers actually use (<c>YYYY-MM-DD</c>, <c>YYYYMMDD</c>) and returns an equivalent
/// feedback model — a <c>(Severity, MsgNo)</c> pair plus the raw 8-byte condition token — so the verdict
/// <c>EVALUATE</c> (CSUTLDTC.cbl:128-149) selects the same branch. Observed call sites: <c>CSUTLDPY.cpy</c>
/// paragraph <c>EDIT-DATE-LE</c> (mask <c>YYYYMMDD</c>), <c>COTRN02C</c> and <c>CORPT00C</c> (mask
/// <c>YYYY-MM-DD</c>). Those callers branch only on bytes 1-4 = severity (<c>'0000'</c> = valid) and bytes
/// 16-19 = message number (they special-case <c>'2513'</c>), so the emulated severity/message-number must at
/// minimum reproduce the valid/invalid decision.</para>
///
/// <para>Paragraphs are methods, COBOL names kept, with <c>// source: CSUTLDTC.cbl:NNN</c> citations and
/// preserved statement order. The three procedure bodies are the unnamed mainline, <see cref="A000Main"/>,
/// and <see cref="A000MainExit"/>.</para>
///
/// <para>FAITHFUL BUGS / quirks reproduced verbatim (port spec §6 — do NOT fix):
/// <list type="number">
/// <item><b>VSTRING-image corruption of the echoed test date.</b> After the call,
/// <c>MOVE WS-DATE-TO-TEST TO WS-DATE</c> (CSUTLDTC.cbl:122) is a <i>group</i> move of a VSTRING whose
/// first 2 bytes are the binary <c>Vstring-length PIC S9(4) BINARY</c> (value 10 = <c>X'000A'</c>). Those 2
/// raw binary length bytes land in positions 1-2 of <c>WS-DATE X(10)</c>, then the first 8 chars of the
/// date text — so the "TstDate:" portion of the result is shifted/garbled by 2 bytes, overwriting the clean
/// date copied at CSUTLDTC.cbl:108. Reproduced byte-for-byte (<c>0x00 0x0A</c> + 8 date chars).</item>
/// <item><b>VSTRING length hard-pinned to 10.</b> Both <c>Vstring-length</c> values are set to
/// <c>LENGTH OF</c> the LINKAGE field (10), never to a trimmed length, so trailing spaces in an 8-char mask
/// like <c>YYYYMMDD</c> are passed to CEEDAYS as part of the picture. Not trimmed. // CSUTLDTC.cbl:105-106,109-110</item>
/// <item><b>RETURN-CODE is the numeric severity</b> from <c>WS-SEVERITY-N</c> (X4-numeric REDEFINES).
/// // CSUTLDTC.cbl:98,123</item>
/// <item><b><c>OUTPUT-LILLIAN</c> is computed by CEEDAYS but never read.</b> Dead output. // CSUTLDTC.cbl:41,114,119</item>
/// <item><b>Misnamed <c>FC-INVALID-DATE</c> 88-level</b> is the all-zeros (success) token and maps to the
/// verdict <c>'Date is valid'</c>. // CSUTLDTC.cbl:62,129-130</item>
/// <item><b><c>INITIALIZE WS-MESSAGE</c> does not restore the FILLER literal labels.</b> The labels
/// <c>'Mesg Code:'</c>, <c>'TstDate:'</c>, <c>'Mask used:'</c> survive at their fixed offsets from their
/// load-time VALUE because INITIALIZE leaves FILLERs untouched; the port emits them at the same offsets.
/// // CSUTLDTC.cbl:90,45/51/54</item>
/// <item><b><c>GOBACK</c> is commented out; uses <c>EXIT PROGRAM</c>.</b> Subprogram return either way.
/// // CSUTLDTC.cbl:100-101</item>
/// </list></para>
/// </remarks>
public sealed class DateValidationUtility
{
    // ====================================================================================================
    // WORKING-STORAGE — the 80-byte WS-MESSAGE template and the CEEDAYS interface fields.
    // ====================================================================================================

    // 01 WS-DATE-TO-TEST / 01 WS-DATE-FORMAT — CEEDAYS VSTRING (S9(4) BINARY length + text OCCURS 0..256
    // DEPENDING ON length). // source: CSUTLDTC.cbl:25-39
    private short _dateToTestLength;          // Vstring-length OF WS-DATE-TO-TEST
    private string _dateToTestText = "";      // Vstring-text  OF WS-DATE-TO-TEST
    private short _dateFormatLength;          // Vstring-length OF WS-DATE-FORMAT
    private string _dateFormatText = "";      // Vstring-text  OF WS-DATE-FORMAT

    // 01 OUTPUT-LILLIAN PIC S9(9) BINARY — set to 0, written by CEEDAYS, never read. // source: CSUTLDTC.cbl:41
    private int _outputLillian;

    // ---- WS-MESSAGE (the 80-byte result template). // source: CSUTLDTC.cbl:42-57 ------------------------
    // The named items the program writes; FILLER literal labels are reconstructed in BuildLsResult().
    private int _wsSeverityN;                 // WS-SEVERITY-N PIC 9(4) REDEFINES WS-SEVERITY X(04)  (bytes 1-4)
    private int _wsMsgNoN;                    // WS-MSG-NO-N  PIC 9(4) REDEFINES WS-MSG-NO  X(04)    (bytes 16-19)
    private string _wsResult = "";            // WS-RESULT    PIC X(15)                              (bytes 21-35)
    private byte[] _wsDate = new byte[WsDateWidth];   // WS-DATE     PIC X(10) (raw bytes — bug #1)  (bytes 46-55)
    private string _wsDateFmt = "";           // WS-DATE-FMT  PIC X(10)                              (bytes 67-76)

    // 01 FEEDBACK-CODE — the LE condition token populated by CEEDAYS. // source: CSUTLDTC.cbl:60-80
    private Feedback _feedback;

    // ---- LINKAGE SECTION (the three BY REFERENCE parameters). // source: CSUTLDTC.cbl:83-88 -------------
    private string _lsDate = "";              // LS-DATE        PIC X(10)
    private string _lsDateFormat = "";        // LS-DATE-FORMAT PIC X(10)

    // Field widths from the WS-MESSAGE / LINKAGE PICs.
    private const int LsDateWidth = 10;       // LS-DATE PIC X(10)        // source: CSUTLDTC.cbl:84
    private const int LsDateFormatWidth = 10; // LS-DATE-FORMAT PIC X(10) // source: CSUTLDTC.cbl:85
    private const int LsResultWidth = 80;     // LS-RESULT PIC X(80)      // source: CSUTLDTC.cbl:86
    private const int WsResultWidth = 15;     // WS-RESULT PIC X(15)      // source: CSUTLDTC.cbl:49
    private const int WsDateWidth = 10;       // WS-DATE PIC X(10)        // source: CSUTLDTC.cbl:52
    private const int WsDateFmtWidth = 10;    // WS-DATE-FMT PIC X(10)    // source: CSUTLDTC.cbl:55

    /// <summary>RETURN-CODE set on return (= numeric severity <c>WS-SEVERITY-N</c>). // source: CSUTLDTC.cbl:98</summary>
    public int ReturnCode { get; private set; }

    /// <summary>The 80-byte LS-RESULT message produced by the last <see cref="Run"/>. // source: CSUTLDTC.cbl:97</summary>
    public string LsResult { get; private set; } = new string(' ', LsResultWidth);

    private DateValidationUtility() { }

    /// <summary>
    /// Runs <c>CSUTLDTC</c> (<c>PROCEDURE DIVISION USING LS-DATE, LS-DATE-FORMAT, LS-RESULT</c>) over the
    /// two input parameters and produces the 80-byte result message plus the severity return code. This is
    /// the .NET equivalent of <c>CALL 'CSUTLDTC' USING ...</c>; <paramref name="lsDate"/> /
    /// <paramref name="lsDateFormat"/> are the BY-REFERENCE inputs and the OUT params carry the BY-REFERENCE
    /// outputs. // source: CSUTLDTC.cbl:88
    /// </summary>
    /// <param name="lsDate">LS-DATE PIC X(10): the date string to validate. // source: CSUTLDTC.cbl:84</param>
    /// <param name="lsDateFormat">LS-DATE-FORMAT PIC X(10): the CEEDAYS picture/mask. // source: CSUTLDTC.cbl:85</param>
    /// <param name="lsResult">OUT — LS-RESULT PIC X(80): the formatted result message. // source: CSUTLDTC.cbl:86</param>
    /// <param name="returnCode">OUT — RETURN-CODE: the numeric LE severity. // source: CSUTLDTC.cbl:98</param>
    /// <param name="host">
    /// Host encoding for the raw-byte echoed test-date field (bug #1) and for any byte-level formatting.
    /// Defaults to EBCDIC, the mainframe form; affects only the non-printable bytes of the corrupted
    /// <c>TstDate:</c> echo, not the severity/message-number/verdict text the callers read.
    /// </param>
    /// <returns>The completed program instance (exposes <see cref="LsResult"/> and <see cref="ReturnCode"/>).</returns>
    public static DateValidationUtility Run(
        string lsDate,
        string lsDateFormat,
        out string lsResult,
        out int returnCode,
        HostKind host = HostKind.Ebcdic)
    {
        var program = new DateValidationUtility();
        program.Execute(lsDate ?? string.Empty, lsDateFormat ?? string.Empty, host);
        lsResult = program.LsResult;
        returnCode = program.ReturnCode;
        return program;
    }

    /// <summary>
    /// Convenience overload returning only the 80-byte <see cref="LsResult"/> (severity is also available via
    /// <see cref="ReturnCode"/> on the returned-by-out path). Mirrors the same <c>CALL 'CSUTLDTC'</c>.
    /// </summary>
    public static string Run(string lsDate, string lsDateFormat, HostKind host = HostKind.Ebcdic)
    {
        Run(lsDate, lsDateFormat, out string lsResult, out _, host);
        return lsResult;
    }

    // ====================================================================================================
    // PROCEDURE DIVISION mainline (the unnamed body following PROCEDURE DIVISION USING ...).
    // source: CSUTLDTC.cbl:88-102
    // ====================================================================================================
    private void Execute(string lsDate, string lsDateFormat, HostKind host)
    {
        // LS-DATE / LS-DATE-FORMAT are PIC X(10): receive left-justified, space-padded / right-truncated to
        // exactly 10 chars (the COBOL fixed-width LINKAGE fields the caller passes BY REFERENCE).
        _lsDate = Fit(lsDate, LsDateWidth);
        _lsDateFormat = Fit(lsDateFormat, LsDateFormatWidth);

        // INITIALIZE WS-MESSAGE — reset the named items: alphanumeric -> spaces, numeric -> 0. The FILLER
        // literal labels are NOT re-applied by INITIALIZE (faithful bug #6); they are reconstructed at their
        // fixed offsets when LS-RESULT is built. // source: CSUTLDTC.cbl:90
        InitializeWsMessage();

        // MOVE SPACES TO WS-DATE — blank the echoed test-date field. // source: CSUTLDTC.cbl:91
        _wsDate = SpacesBytes(WsDateWidth, host);

        // PERFORM A000-MAIN THRU A000-MAIN-EXIT — do the conversion + verdict. // source: CSUTLDTC.cbl:93-94
        A000Main(host);
        A000MainExit();

        // (DISPLAY WS-MESSAGE is commented out in the source. // source: CSUTLDTC.cbl:96)

        // MOVE WS-MESSAGE TO LS-RESULT — return the 80-byte formatted message. // source: CSUTLDTC.cbl:97
        LsResult = BuildLsResult(host);

        // MOVE WS-SEVERITY-N TO RETURN-CODE — set RETURN-CODE to the numeric severity. // source: CSUTLDTC.cbl:98
        ReturnCode = _wsSeverityN;

        // EXIT PROGRAM — return to caller (GOBACK is commented out). // source: CSUTLDTC.cbl:100-101
    }

    // ====================================================================================================
    // A000-MAIN. // source: CSUTLDTC.cbl:103-151
    // ====================================================================================================
    private void A000Main(HostKind host)
    {
        // MOVE LENGTH OF LS-DATE TO VSTRING-LENGTH OF WS-DATE-TO-TEST — VSTRING length pinned to 10 (bug #2).
        // source: CSUTLDTC.cbl:105-106
        _dateToTestLength = LsDateWidth;

        // MOVE LS-DATE TO VSTRING-TEXT OF WS-DATE-TO-TEST, WS-DATE — multi-receiver MOVE: into the VSTRING
        // text AND into the echoed WS-DATE field. Each receiver gets a per-receiver alphanumeric MOVE
        // (left-justify, space-pad/truncate to its own width). // source: CSUTLDTC.cbl:107-108
        _dateToTestText = Fit(_lsDate, _dateToTestLength);          // VSTRING-TEXT OF WS-DATE-TO-TEST (10)
        _wsDate = Encode(Fit(_lsDate, WsDateWidth), host);          // WS-DATE X(10) — clean date (overwritten below)

        // MOVE LENGTH OF LS-DATE-FORMAT TO VSTRING-LENGTH OF WS-DATE-FORMAT — pinned to 10 (bug #2).
        // source: CSUTLDTC.cbl:109-110
        _dateFormatLength = LsDateFormatWidth;

        // MOVE LS-DATE-FORMAT TO VSTRING-TEXT OF WS-DATE-FORMAT, WS-DATE-FMT — multi-receiver MOVE.
        // source: CSUTLDTC.cbl:111-113
        _dateFormatText = Fit(_lsDateFormat, _dateFormatLength);    // VSTRING-TEXT OF WS-DATE-FORMAT (10)
        _wsDateFmt = Fit(_lsDateFormat, WsDateFmtWidth);           // WS-DATE-FMT X(10) — clean mask (kept)

        // MOVE 0 TO OUTPUT-LILLIAN — clear the Lillian output. // source: CSUTLDTC.cbl:114
        _outputLillian = 0;

        // CALL "CEEDAYS" USING WS-DATE-TO-TEST, WS-DATE-FORMAT, OUTPUT-LILLIAN, FEEDBACK-CODE
        // The LE service sets OUTPUT-LILLIAN (ignored) and FEEDBACK-CODE. // source: CSUTLDTC.cbl:116-120
        _feedback = Ceedays(_dateToTestLength, _dateToTestText, _dateFormatLength, _dateFormatText, out _outputLillian);

        // MOVE WS-DATE-TO-TEST TO WS-DATE — GROUP move of the VSTRING into WS-DATE X(10): the first 2 bytes
        // are the binary length halfword (X'000A' = 10), then the first 8 chars of the date text. This
        // corrupts the echoed date (faithful bug #1) and overwrites the clean copy from line 108.
        // source: CSUTLDTC.cbl:122
        _wsDate = VstringGroupImageToWsDate(_dateToTestLength, _dateToTestText, host);

        // MOVE SEVERITY OF FEEDBACK-CODE TO WS-SEVERITY-N — binary S9(4) -> 9(4). // source: CSUTLDTC.cbl:123
        _wsSeverityN = ToFourDigit(_feedback.Severity);

        // MOVE MSG-NO OF FEEDBACK-CODE TO WS-MSG-NO-N — binary S9(4) -> 9(4). // source: CSUTLDTC.cbl:124
        _wsMsgNoN = ToFourDigit(_feedback.MsgNo);

        // EVALUATE TRUE over the feedback 88-levels -> WS-RESULT verdict text (WHEN OTHER -> 'Date is invalid').
        // The 88s test the full 8-byte FEEDBACK-TOKEN-VALUE. // source: CSUTLDTC.cbl:128-149
        ulong token = _feedback.Token;
        if (token == FcInvalidDate)              // X'0000000000000000' (all-zeros = SUCCESS = date VALID)
            _wsResult = "Date is valid";          // // source: CSUTLDTC.cbl:129-130
        else if (token == FcInsufficientData)    // X'000309CB59C3C5C5'
            _wsResult = "Insufficient";           // // source: CSUTLDTC.cbl:131-132
        else if (token == FcBadDateValue)        // X'000309CC59C3C5C5'
            _wsResult = "Datevalue error";        // // source: CSUTLDTC.cbl:133-134
        else if (token == FcInvalidEra)          // X'000309CD59C3C5C5'
            _wsResult = "Invalid Era    ";        // // source: CSUTLDTC.cbl:135-136
        else if (token == FcUnsuppRange)         // X'000309D159C3C5C5'
            _wsResult = "Unsupp. Range  ";        // // source: CSUTLDTC.cbl:137-138
        else if (token == FcInvalidMonth)        // X'000309D559C3C5C5'
            _wsResult = "Invalid month  ";        // // source: CSUTLDTC.cbl:139-140
        else if (token == FcBadPicString)        // X'000309D659C3C5C5'
            _wsResult = "Bad Pic String ";        // // source: CSUTLDTC.cbl:141-142
        else if (token == FcNonNumericData)      // X'000309D859C3C5C5'
            _wsResult = "Nonnumeric data";        // // source: CSUTLDTC.cbl:143-144
        else if (token == FcYearInEraZero)       // X'000309D959C3C5C5'
            _wsResult = "YearInEra is 0 ";        // // source: CSUTLDTC.cbl:145-146
        else
            _wsResult = "Date is invalid";        // WHEN OTHER // source: CSUTLDTC.cbl:147-148
    }

    // ====================================================================================================
    // A000-MAIN-EXIT. // source: CSUTLDTC.cbl:152-154
    // ====================================================================================================
    private void A000MainExit()
    {
        // EXIT. — return target of the PERFORM ... THRU. No-op.
    }

    // ====================================================================================================
    // INITIALIZE WS-MESSAGE — reset the NAMED items only (FILLER labels left untouched: bug #6).
    // source: CSUTLDTC.cbl:90, CSUTLDTC.cbl:42-57
    // ====================================================================================================
    private void InitializeWsMessage()
    {
        _wsSeverityN = 0;                       // WS-SEVERITY-N / WS-SEVERITY -> 0
        _wsMsgNoN = 0;                          // WS-MSG-NO-N / WS-MSG-NO -> 0
        _wsResult = new string(' ', WsResultWidth);   // WS-RESULT -> spaces
        _wsDate = SpacesBytes(WsDateWidth, HostKind.Ebcdic); // WS-DATE -> spaces (re-blanked at line 91 too)
        _wsDateFmt = new string(' ', WsDateFmtWidth); // WS-DATE-FMT -> spaces
    }

    // ====================================================================================================
    // Assemble the 80-byte WS-MESSAGE image == LS-RESULT, with FILLER literal labels at their fixed offsets
    // (bug #6) and the raw-byte WS-DATE echo (bug #1). // source: CSUTLDTC.cbl:42-57, CSUTLDTC.cbl:97
    // ====================================================================================================
    private string BuildLsResult(HostKind host)
    {
        var image = new byte[LsResultWidth];
        var enc = HostEncoding.For(host);
        int pos = 0;

        void PutText(string s, int width)
        {
            byte[] bytes = enc.GetBytes(Fit(s, width));
            Array.Copy(bytes, 0, image, pos, width);
            pos += width;
        }

        void PutBytes(byte[] src, int width)
        {
            for (int i = 0; i < width; i++)
                image[pos + i] = i < src.Length ? src[i] : enc.GetBytes(" ")[0];
            pos += width;
        }

        // bytes 1-4  : WS-SEVERITY (X4-numeric view WS-SEVERITY-N PIC 9(4), zero-filled). // CSUTLDTC.cbl:43-44
        PutText(Pic9(_wsSeverityN, 4), 4);
        // bytes 5-15 : FILLER X(11) VALUE 'Mesg Code:' (10 chars + 1 trailing space). // CSUTLDTC.cbl:45
        PutText("Mesg Code:", 11);
        // bytes 16-19: WS-MSG-NO (X4-numeric view WS-MSG-NO-N PIC 9(4), zero-filled). // CSUTLDTC.cbl:46-47
        PutText(Pic9(_wsMsgNoN, 4), 4);
        // byte  20   : FILLER X(01) VALUE SPACE. // CSUTLDTC.cbl:48
        PutText(" ", 1);
        // bytes 21-35: WS-RESULT X(15) verdict text. // CSUTLDTC.cbl:49
        PutText(_wsResult, WsResultWidth);
        // byte  36   : FILLER X(01) VALUE SPACE. // CSUTLDTC.cbl:50
        PutText(" ", 1);
        // bytes 37-45: FILLER X(09) VALUE 'TstDate:' (8 chars + 1 trailing space). // CSUTLDTC.cbl:51
        PutText("TstDate:", 9);
        // bytes 46-55: WS-DATE X(10) — RAW bytes (bug #1: X'000A' + 8 date chars). // CSUTLDTC.cbl:52
        PutBytes(_wsDate, WsDateWidth);
        // byte  56   : FILLER X(01) VALUE SPACE. // CSUTLDTC.cbl:53
        PutText(" ", 1);
        // bytes 57-66: FILLER X(10) VALUE 'Mask used:'. // CSUTLDTC.cbl:54
        PutText("Mask used:", 10);
        // bytes 67-76: WS-DATE-FMT X(10) — clean mask. // CSUTLDTC.cbl:55
        PutText(_wsDateFmt, WsDateFmtWidth);
        // byte  77   : FILLER X(01) VALUE SPACE. // CSUTLDTC.cbl:56
        PutText(" ", 1);
        // bytes 78-80: FILLER X(03) VALUE SPACES. // CSUTLDTC.cbl:57
        PutText("   ", 3);

        // Decode the 80-byte image back to a string view (single-byte code page is an identity round-trip,
        // so callers reading bytes 1-4 / 16-19 see exactly the severity / message-number text). The two raw
        // VSTRING-length bytes in the WS-DATE echo decode to their code-page chars (e.g. EBCDIC 0x00->NUL,
        // 0x0A->the CP037 char) — the corruption is preserved.
        return enc.GetString(image);
    }

    // ====================================================================================================
    // CEEDAYS emulation. // source: CSUTLDTC.cbl:116-120 (the LE callable service is absent from .NET)
    //
    // Reproduces the parse-and-validate behavior CEEDAYS performs for the masks the CardDemo callers use
    // (YYYY-MM-DD, YYYYMMDD). Returns the equivalent feedback model: an 8-byte condition token plus the
    // SEVERITY / MSG-NO halfwords that the program reads into WS-SEVERITY-N / WS-MSG-NO-N. The token drives
    // the verdict EVALUATE; severity/msg-no feed bytes 1-4 / 16-19 the callers branch on.
    //   - success         -> Severity 0, MsgNo 0, token X'0000000000000000' (FC-INVALID-DATE = "valid").
    //   - bad date value  -> Severity 3, MsgNo 2508 (0x09CC), token FC-BAD-DATE-VALUE; the condition CEEDAYS
    //                        returns for an unparseable/invalid date here. The MsgNo 2513 (0x09D1) that the
    //                        CardDemo callers tolerate via the '2513' check pairs with FC-UNSUPP-RANGE, not
    //                        FC-BAD-DATE-VALUE. // see CSUTLDTC.cbl:64,66
    //   - non-numeric     -> Severity 3, MsgNo 2520, token FC-NON-NUMERIC-DATA.
    //   - bad pic string  -> Severity 3, MsgNo 2518, token FC-BAD-PIC-STRING (unrecognized mask).
    // The (severity, msgNo) -> token mapping is consistent with the 88-level hex constants in CSUTLDTC.cbl:62-70
    // (msg-no = the low halfword of the 4-byte condition id, e.g. 0x09CC = 2508). // source: CSUTLDTC.cbl:60-80
    // ====================================================================================================
    private static Feedback Ceedays(short dateLength, string dateText, short fmtLength, string fmtText, out int lillian)
    {
        lillian = 0;

        // The VSTRING text is exactly `length` chars (pinned to 10 by bug #2), trailing spaces included.
        string date = (dateText.Length >= dateLength ? dateText[..dateLength] : dateText.PadRight(dateLength, ' '));
        string mask = (fmtText.Length >= fmtLength ? fmtText[..fmtLength] : fmtText.PadRight(fmtLength, ' '));

        // Recognize the mask -> a fixed parse template. Trailing spaces are literals that must "match"
        // (bug #2): for 'YYYYMMDD' (8 sig + 2 spaces) the date's positions 9-10 must therefore be spaces.
        if (!TryParseWithMask(date, mask, out int year, out int month, out int day))
        {
            // Unrecognized/ill-formed picture string -> CEEDAYS bad-picture-string condition.
            // Distinguish a structurally-unparseable mask from a non-numeric date below; an unknown mask
            // shape is FC-BAD-PIC-STRING.
            if (!MaskIsKnown(mask))
                return Feedback.Error(FcBadPicString, msgNo: 2518);

            // Known mask but the date had non-digit chars where digits were required -> non-numeric data.
            return Feedback.Error(FcNonNumericData, msgNo: 2520);
        }

        // Range/calendar validation. CEEDAYS reports an impossible date by its SPECIFIC LE feedback condition,
        // each with its own message number (the low halfword of the condition id): an out-of-range month is
        // FC-INVALID-MONTH (0x09D5 = 2517), a year-in-era of zero is FC-YEAR-IN-ERA-ZERO (0x09D9 = 2521), and
        // any other bad day-of-month / value (e.g. 2023-02-29, 2023-04-31, day 00) is FC-BAD-DATE-VALUE
        // (0x09CC = 2508, verdict "Datevalue error"). CardDemo's callers tolerate ONLY FC-UNSUPP-RANGE (2513);
        // every other non-zero condition is an invalid-date rejection. // CSUTLDTC.cbl:62-70,133-148
        if (!IsValidCalendarDate(year, month, day))
        {
            if (month < 1 || month > 12) return Feedback.Error(FcInvalidMonth, msgNo: 2517);
            if (year < 1)                return Feedback.Error(FcYearInEraZero, msgNo: 2521);
            return Feedback.Error(FcBadDateValue, msgNo: 2508);
        }

        // Valid date: CEEDAYS returns the all-zero (success) token. The Lillian day number is computed but
        // the caller never reads it (faithful bug #4); compute a faithful value anyway.
        lillian = LillianDayNumber(year, month, day);
        return Feedback.Success();
    }

    /// <summary>
    /// True when <paramref name="mask"/> is one of the CEEDAYS picture strings CardDemo uses
    /// (<c>YYYY-MM-DD</c> or <c>YYYYMMDD</c>, each in a 10-char field padded with trailing spaces).
    /// </summary>
    private static bool MaskIsKnown(string mask)
    {
        string trimmed = mask.TrimEnd(' ');
        return trimmed is "YYYY-MM-DD" or "YYYYMMDD";
    }

    /// <summary>
    /// Parse <paramref name="date"/> under <paramref name="mask"/> (positional <c>YYYY</c>/<c>MM</c>/<c>DD</c>
    /// with literal separators and trailing-space literals — bug #2). Returns false when the literal
    /// positions do not match or a digit position is non-numeric.
    /// </summary>
    private static bool TryParseWithMask(string date, string mask, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (!MaskIsKnown(mask)) return false;

        string trimmedMask = mask.TrimEnd(' ');
        int maskLen = trimmedMask.Length;     // 10 for YYYY-MM-DD, 8 for YYYYMMDD

        // Bug #2: the field is 10 wide; positions past the significant mask length must be spaces in the date
        // (the trailing-space literals of the picture). A YYYYMMDD mask therefore requires date[8..10] blank.
        for (int i = maskLen; i < date.Length; i++)
            if (date[i] != ' ')
                return false;

        // Match each mask position against the corresponding date char.
        var y = new System.Text.StringBuilder();
        var m = new System.Text.StringBuilder();
        var d = new System.Text.StringBuilder();
        for (int i = 0; i < maskLen; i++)
        {
            char mc = trimmedMask[i];
            if (i >= date.Length) return false;
            char dc = date[i];
            switch (mc)
            {
                case 'Y': if (!char.IsDigit(dc)) return false; y.Append(dc); break;
                case 'M': if (!char.IsDigit(dc)) return false; m.Append(dc); break;
                case 'D': if (!char.IsDigit(dc)) return false; d.Append(dc); break;
                default:  if (dc != mc) return false; break;     // literal separator (e.g. '-') must match
            }
        }

        year = int.Parse(y.ToString());
        month = int.Parse(m.ToString());
        day = int.Parse(d.ToString());
        return true;
    }

    /// <summary>Calendar validity (1..9999 era CEEDAYS supports, real month/day incl. leap-year Feb 29).</summary>
    private static bool IsValidCalendarDate(int year, int month, int day)
    {
        if (year < 1 || year > 9999) return false;
        if (month < 1 || month > 12) return false;
        if (day < 1) return false;
        return day <= DateTime.DaysInMonth(year, month);
    }

    /// <summary>
    /// Lillian day number = days since the Gregorian start of the Lillian calendar (1582-10-14 is day 0;
    /// 1582-10-15 is day 1). Computed for faithfulness; the caller never reads it (bug #4).
    /// </summary>
    private static int LillianDayNumber(int year, int month, int day)
    {
        var d = new DateTime(year, month, day);
        var epoch = new DateTime(1582, 10, 14);
        return (int)(d - epoch).TotalDays;
    }

    // ====================================================================================================
    // FEEDBACK-CODE model (the 8-byte LE condition token + its severity/msg-no halfwords).
    // source: CSUTLDTC.cbl:60-80
    // ====================================================================================================
    private readonly struct Feedback
    {
        /// <summary>The full 8-byte FEEDBACK-TOKEN-VALUE the 88-level conditions test. // source: CSUTLDTC.cbl:61-70</summary>
        public ulong Token { get; }

        /// <summary>SEVERITY OF FEEDBACK-CODE (S9(4) BINARY halfword). // source: CSUTLDTC.cbl:72</summary>
        public int Severity { get; }

        /// <summary>MSG-NO OF FEEDBACK-CODE (S9(4) BINARY halfword). // source: CSUTLDTC.cbl:73</summary>
        public int MsgNo { get; }

        private Feedback(ulong token, int severity, int msgNo)
        {
            Token = token;
            Severity = severity;
            MsgNo = msgNo;
        }

        /// <summary>CEEDAYS success: all-zero token, severity 0, msg-no 0 (FC-INVALID-DATE = "valid").</summary>
        public static Feedback Success() => new(FcInvalidDate, severity: 0, msgNo: 0);

        /// <summary>A CEEDAYS error condition: severity 3 (LE error) plus the condition token + LE message number.</summary>
        public static Feedback Error(ulong token, int msgNo) => new(token, severity: 3, msgNo: msgNo);
    }

    // ---- 88-level FEEDBACK-TOKEN-VALUE constants (8-byte raw tokens). // source: CSUTLDTC.cbl:62-70 ------
    // Matched as raw 8-byte values (the trailing 59C3C5C5 = severity-control 0x59 + facility "CEE" in EBCDIC;
    // the leading 000309xx = severity 3 + the LE message-number halfword). Never re-encoded.
    private const ulong FcInvalidDate      = 0x0000000000000000UL; // success / "Date is valid"
    private const ulong FcInsufficientData = 0x000309CB59C3C5C5UL;
    private const ulong FcBadDateValue     = 0x000309CC59C3C5C5UL;
    private const ulong FcInvalidEra       = 0x000309CD59C3C5C5UL;
    private const ulong FcUnsuppRange      = 0x000309D159C3C5C5UL;
    private const ulong FcInvalidMonth     = 0x000309D559C3C5C5UL;
    private const ulong FcBadPicString     = 0x000309D659C3C5C5UL;
    private const ulong FcNonNumericData   = 0x000309D859C3C5C5UL;
    private const ulong FcYearInEraZero    = 0x000309D959C3C5C5UL;

    // ====================================================================================================
    // Small COBOL-MOVE helpers.
    // ====================================================================================================

    /// <summary>PIC X(width) alphanumeric receive: left-justify, space-pad / right-truncate to exactly width.</summary>
    private static string Fit(string s, int width)
    {
        s ??= string.Empty;
        return s.Length >= width ? s[..width] : s.PadRight(width, ' ');
    }

    /// <summary>Encode a string to host bytes (already fitted to the receiving width by the caller).</summary>
    private static byte[] Encode(string s, HostKind host) => HostEncoding.For(host).GetBytes(s);

    /// <summary>A width-byte field of host SPACE bytes (EBCDIC 0x40 / ASCII 0x20).</summary>
    private static byte[] SpacesBytes(int width, HostKind host) => HostEncoding.For(host).GetBytes(new string(' ', width));

    /// <summary>
    /// Group MOVE of WS-DATE-TO-TEST (VSTRING) into WS-DATE X(10): 2 raw binary length bytes (big-endian
    /// S9(4) BINARY, value 10 = <c>0x00 0x0A</c>) followed by the first 8 chars of the date text, padded to
    /// 10 with host spaces if needed. This is faithful bug #1. // source: CSUTLDTC.cbl:122
    /// </summary>
    private static byte[] VstringGroupImageToWsDate(short vLength, string vText, HostKind host)
    {
        var result = new byte[WsDateWidth];
        // Vstring-length PIC S9(4) BINARY is a 2-byte big-endian halfword on the mainframe.
        result[0] = (byte)((vLength >> 8) & 0xFF);   // high byte (0x00 for length 10)
        result[1] = (byte)(vLength & 0xFF);          // low byte  (0x0A for length 10)

        byte[] textBytes = Encode(vText, host);
        byte space = SpacesBytes(1, host)[0];
        for (int i = 2; i < WsDateWidth; i++)
        {
            int srcIndex = i - 2;                     // the first 8 chars of the date text shift in after the 2 length bytes
            result[i] = srcIndex < textBytes.Length ? textBytes[srcIndex] : space;
        }
        return result;
    }

    /// <summary>
    /// Binary S9(4) -> PIC 9(4) MOVE: take the numeric value (absolute, the X(4) numeric display view carries
    /// no sign), reduced modulo 10000 (4-digit silent overflow). Values in the corpus (severity 0/3, small
    /// msg-no) fit exactly. // source: CSUTLDTC.cbl:123-124
    /// </summary>
    private static int ToFourDigit(int value)
    {
        int v = Math.Abs(value) % 10000;
        return v;
    }

    /// <summary>Format an unsigned value as PIC 9(width): zero-filled, right-justified, low-order truncated.</summary>
    private static string Pic9(int value, int width)
    {
        string digits = (Math.Abs(value) % (int)Math.Pow(10, width)).ToString();
        return digits.PadLeft(width, '0');
    }
}
