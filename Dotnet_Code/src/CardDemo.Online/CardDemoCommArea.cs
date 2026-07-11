using System.Text;

namespace CardDemo.Online;

/// <summary>
/// Byte-exact C# mirror of <c>01 CARDDEMO-COMMAREA</c> (copybook <c>COCOM01Y.cpy</c>) — the chained-program
/// state carried across every pseudo-conversational RETURN/XCTL/LINK. Every elementary field keeps its
/// COBOL <c>PIC</c> width so the fixed-width image (<see cref="ToBytes"/> / <see cref="ToImage"/>) is
/// byte-identical to the mainframe COMMAREA, and <see cref="Parse"/> round-trips it back losslessly.
/// </summary>
/// <remarks>
/// Layout (offsets are 0-based, total <see cref="Length"/> = 160 bytes):
/// <code>
/// CDEMO-GENERAL-INFO
///   FROM-TRANID   X(04)  @0    FROM-PROGRAM  X(08)  @4
///   TO-TRANID     X(04)  @12   TO-PROGRAM    X(08)  @16
///   USER-ID       X(08)  @24   USER-TYPE     X(01)  @32   (88 'A'=Admin 'U'=User)
///   PGM-CONTEXT   9(01)  @33   (88 0=Enter 1=Reenter)
/// CDEMO-CUSTOMER-INFO
///   CUST-ID       9(09)  @34   CUST-FNAME X(25) @43  CUST-MNAME X(25) @68  CUST-LNAME X(25) @93
/// CDEMO-ACCOUNT-INFO
///   ACCT-ID       9(11)  @118  ACCT-STATUS X(01) @129
/// CDEMO-CARD-INFO
///   CARD-NUM      9(16)  @130
/// CDEMO-MORE-INFO
///   LAST-MAP      X(7)   @146  LAST-MAPSET X(7) @153
/// </code>
/// Numeric (<c>9(n)</c>) fields are stored as zoned DISPLAY: a fixed-width run of ASCII digits,
/// right-justified and left zero-filled, matching how CICS keeps an unsigned numeric in a COMMAREA.
/// Alphanumeric fields are kept as exact-width strings (trailing spaces preserved).
/// </remarks>
public sealed class CardDemoCommArea
{
    /// <summary>Total fixed-width length of the COMMAREA image, in bytes (sum of all PIC widths).</summary>
    public const int Length = 160;

    // --- Field widths (COBOL PIC) and offsets, single source of truth for (de)serialize. ---
    private const int OffFromTranId = 0, LenFromTranId = 4;
    private const int OffFromProgram = 4, LenFromProgram = 8;
    private const int OffToTranId = 12, LenToTranId = 4;
    private const int OffToProgram = 16, LenToProgram = 8;
    private const int OffUserId = 24, LenUserId = 8;
    private const int OffUserType = 32, LenUserType = 1;
    private const int OffPgmContext = 33, LenPgmContext = 1;
    private const int OffCustId = 34, LenCustId = 9;
    private const int OffCustFName = 43, LenCustFName = 25;
    private const int OffCustMName = 68, LenCustMName = 25;
    private const int OffCustLName = 93, LenCustLName = 25;
    private const int OffAcctId = 118, LenAcctId = 11;
    private const int OffAcctStatus = 129, LenAcctStatus = 1;
    private const int OffCardNum = 130, LenCardNum = 16;
    private const int OffLastMap = 146, LenLastMap = 7;
    private const int OffLastMapSet = 153, LenLastMapSet = 7;

    // === CDEMO-GENERAL-INFO ===

    /// <summary><c>CDEMO-FROM-TRANID</c> PIC X(04).</summary>
    public string FromTranId { get; set; } = "";

    /// <summary><c>CDEMO-FROM-PROGRAM</c> PIC X(08).</summary>
    public string FromProgram { get; set; } = "";

    /// <summary><c>CDEMO-TO-TRANID</c> PIC X(04).</summary>
    public string ToTranId { get; set; } = "";

    /// <summary><c>CDEMO-TO-PROGRAM</c> PIC X(08).</summary>
    public string ToProgram { get; set; } = "";

    /// <summary><c>CDEMO-USER-ID</c> PIC X(08).</summary>
    public string UserId { get; set; } = "";

    /// <summary><c>CDEMO-USER-TYPE</c> PIC X(01). 88-levels: 'A'=Admin, 'U'=User.</summary>
    public string UserType { get; set; } = "";

    /// <summary><c>CDEMO-PGM-CONTEXT</c> PIC 9(01). 88-levels: 0=Enter (first entry), 1=Reenter.</summary>
    public int PgmContext { get; set; }

    // === CDEMO-CUSTOMER-INFO ===

    /// <summary><c>CDEMO-CUST-ID</c> PIC 9(09).</summary>
    public long CustId { get; set; }

    /// <summary><c>CDEMO-CUST-FNAME</c> PIC X(25).</summary>
    public string CustFName { get; set; } = "";

    /// <summary><c>CDEMO-CUST-MNAME</c> PIC X(25).</summary>
    public string CustMName { get; set; } = "";

    /// <summary><c>CDEMO-CUST-LNAME</c> PIC X(25).</summary>
    public string CustLName { get; set; } = "";

    // === CDEMO-ACCOUNT-INFO ===

    /// <summary><c>CDEMO-ACCT-ID</c> PIC 9(11).</summary>
    public long AcctId { get; set; }

    /// <summary><c>CDEMO-ACCT-STATUS</c> PIC X(01).</summary>
    public string AcctStatus { get; set; } = "";

    // === CDEMO-CARD-INFO ===

    /// <summary><c>CDEMO-CARD-NUM</c> PIC 9(16). 16 digits exceeds <see cref="int"/>; kept as <see cref="long"/>.</summary>
    public long CardNum { get; set; }

    // === CDEMO-MORE-INFO ===

    /// <summary><c>CDEMO-LAST-MAP</c> PIC X(7).</summary>
    public string LastMap { get; set; } = "";

    /// <summary><c>CDEMO-LAST-MAPSET</c> PIC X(7).</summary>
    public string LastMapSet { get; set; } = "";

    // === Typed accessors mirroring the 88-level condition names ===

    /// <summary><c>88 CDEMO-USRTYP-ADMIN VALUE 'A'</c>.</summary>
    public bool IsAdmin => UserType.StartsWith('A');

    /// <summary><c>88 CDEMO-USRTYP-USER VALUE 'U'</c>.</summary>
    public bool IsUser => UserType.StartsWith('U');

    /// <summary><c>88 CDEMO-PGM-ENTER VALUE 0</c> — first entry to a program.</summary>
    public bool IsFirstEntry => PgmContext == 0;

    /// <summary><c>88 CDEMO-PGM-REENTER VALUE 1</c> — a re-entry (operator pressed a key).</summary>
    public bool IsReenter => PgmContext == 1;

    /// <summary>SET <c>CDEMO-USRTYP-ADMIN TO TRUE</c> ('A').</summary>
    public void SetAdmin() => UserType = "A";

    /// <summary>SET <c>CDEMO-USRTYP-USER TO TRUE</c> ('U').</summary>
    public void SetUser() => UserType = "U";

    /// <summary>SET <c>CDEMO-PGM-ENTER TO TRUE</c> (0).</summary>
    public void SetFirstEntry() => PgmContext = 0;

    /// <summary>SET <c>CDEMO-PGM-REENTER TO TRUE</c> (1).</summary>
    public void SetReenter() => PgmContext = 1;

    /// <summary>Deep copy — XCTL/RETURN pass the COMMAREA by value, never by shared reference.</summary>
    public CardDemoCommArea Clone() => new()
    {
        FromTranId = FromTranId,
        FromProgram = FromProgram,
        ToTranId = ToTranId,
        ToProgram = ToProgram,
        UserId = UserId,
        UserType = UserType,
        PgmContext = PgmContext,
        CustId = CustId,
        CustFName = CustFName,
        CustMName = CustMName,
        CustLName = CustLName,
        AcctId = AcctId,
        AcctStatus = AcctStatus,
        CardNum = CardNum,
        LastMap = LastMap,
        LastMapSet = LastMapSet,
    };

    /// <summary>
    /// Serializes to the 160-byte fixed-width image (ASCII), byte-identical to the mainframe COMMAREA
    /// layout. Alphanumeric fields are space-padded to width (right-truncated if over); numeric fields are
    /// rendered as right-justified, left zero-filled DISPLAY digits.
    /// </summary>
    public byte[] ToBytes()
    {
        var buf = new byte[Length];
        // Pre-fill spaces so any gap is SPACES, not 0x00 (matches CICS COMMAREA conventions here).
        for (int i = 0; i < Length; i++) buf[i] = (byte)' ';

        WriteText(buf, OffFromTranId, LenFromTranId, FromTranId);
        WriteText(buf, OffFromProgram, LenFromProgram, FromProgram);
        WriteText(buf, OffToTranId, LenToTranId, ToTranId);
        WriteText(buf, OffToProgram, LenToProgram, ToProgram);
        WriteText(buf, OffUserId, LenUserId, UserId);
        WriteText(buf, OffUserType, LenUserType, UserType);
        WriteNum(buf, OffPgmContext, LenPgmContext, PgmContext);
        WriteNum(buf, OffCustId, LenCustId, CustId);
        WriteText(buf, OffCustFName, LenCustFName, CustFName);
        WriteText(buf, OffCustMName, LenCustMName, CustMName);
        WriteText(buf, OffCustLName, LenCustLName, CustLName);
        WriteNum(buf, OffAcctId, LenAcctId, AcctId);
        WriteText(buf, OffAcctStatus, LenAcctStatus, AcctStatus);
        WriteNum(buf, OffCardNum, LenCardNum, CardNum);
        WriteText(buf, OffLastMap, LenLastMap, LastMap);
        WriteText(buf, OffLastMapSet, LenLastMapSet, LastMapSet);
        return buf;
    }

    /// <summary>The fixed-width image as a 160-character string (one char per byte).</summary>
    public string ToImage() => Encoding.ASCII.GetString(ToBytes());

    /// <summary>
    /// Parses a fixed-width COMMAREA image back into typed fields. The image must be exactly
    /// <see cref="Length"/> bytes (CICS would pass a short COMMAREA only on a cold start, modeled by a
    /// <c>null</c> COMMAREA in <see cref="CicsContext"/>, not by a short image here).
    /// </summary>
    public static CardDemoCommArea Parse(ReadOnlySpan<byte> image)
    {
        if (image.Length != Length)
            throw new ArgumentException(
                $"COMMAREA image length {image.Length} != expected {Length}.", nameof(image));

        return new CardDemoCommArea
        {
            FromTranId = ReadText(image, OffFromTranId, LenFromTranId),
            FromProgram = ReadText(image, OffFromProgram, LenFromProgram),
            ToTranId = ReadText(image, OffToTranId, LenToTranId),
            ToProgram = ReadText(image, OffToProgram, LenToProgram),
            UserId = ReadText(image, OffUserId, LenUserId),
            UserType = ReadText(image, OffUserType, LenUserType),
            PgmContext = (int)ReadNum(image, OffPgmContext, LenPgmContext),
            CustId = ReadNum(image, OffCustId, LenCustId),
            CustFName = ReadText(image, OffCustFName, LenCustFName),
            CustMName = ReadText(image, OffCustMName, LenCustMName),
            CustLName = ReadText(image, OffCustLName, LenCustLName),
            AcctId = ReadNum(image, OffAcctId, LenAcctId),
            AcctStatus = ReadText(image, OffAcctStatus, LenAcctStatus),
            CardNum = ReadNum(image, OffCardNum, LenCardNum),
            LastMap = ReadText(image, OffLastMap, LenLastMap),
            LastMapSet = ReadText(image, OffLastMapSet, LenLastMapSet),
        };
    }

    /// <summary>Parses a 160-character string image (inverse of <see cref="ToImage"/>).</summary>
    public static CardDemoCommArea Parse(string image) => Parse(Encoding.ASCII.GetBytes(image));

    // --- Fixed-width field codec (ASCII, COBOL DISPLAY conventions). ---

    private static void WriteText(byte[] buf, int off, int len, string value)
    {
        value ??= "";
        for (int i = 0; i < len; i++)
        {
            char c = i < value.Length ? value[i] : ' ';
            buf[off + i] = (byte)c;
        }
    }

    private static void WriteNum(byte[] buf, int off, int len, long value)
    {
        // Unsigned zoned DISPLAY: magnitude, right-justified, left zero-filled. Silent high-order
        // overflow keeps the low `len` digits, matching COBOL truncation on MOVE.
        ulong mag = value < 0 ? (ulong)(-value) : (ulong)value;
        for (int i = len - 1; i >= 0; i--)
        {
            buf[off + i] = (byte)('0' + (int)(mag % 10));
            mag /= 10;
        }
    }

    private static string ReadText(ReadOnlySpan<byte> image, int off, int len) =>
        Encoding.ASCII.GetString(image.Slice(off, len));

    private static long ReadNum(ReadOnlySpan<byte> image, int off, int len)
    {
        long value = 0;
        for (int i = 0; i < len; i++)
        {
            byte b = image[off + i];
            // Treat spaces / LOW-VALUES (uninitialised numeric) as zero, like a zoned-decimal read.
            int digit = (b >= '0' && b <= '9') ? b - '0' : 0;
            value = value * 10 + digit;
        }
        return value;
    }
}
