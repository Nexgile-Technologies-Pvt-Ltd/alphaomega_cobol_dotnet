namespace CardDemo.Runtime;

/// <summary>
/// The two-character COBOL FILE STATUS values that CardDemo programs branch on literally
/// (e.g. <c>CBTRN02C</c> treats <c>'00'</c> or <c>'23'</c> on the TCATBAL read as "ok / create",
/// and <c>'10'</c> as end-of-file). Stored as the exact two-byte string the program would test.
/// </summary>
public static class FileStatus
{
    public const string Ok = "00";
    public const string DuplicateKey = "02";
    public const string EndOfFile = "10";
    public const string InvalidKeyRangeOrSequence = "21";
    public const string DuplicateKeyError = "22";
    public const string RecordNotFound = "23";
    public const string BoundaryViolation = "24";
    public const string PermanentError = "30";
    public const string FileNotFound = "35";
    public const string FileAlreadyOpen = "41";
    public const string FileNotOpen = "42";
    public const string ReadNotDone = "43";
}

/// <summary>
/// The CICS <c>EXEC CICS ... RESP</c> values (the integer values of <c>DFHRESP(...)</c>) that the
/// online programs match against literally — e.g. <c>COSGN00C</c> tests <c>WHEN 0</c> (NORMAL) and
/// <c>WHEN 13</c> (NOTFND) on a file read.
/// </summary>
public enum Resp
{
    Normal = 0,
    Error = 1,
    NotFnd = 13,
    DupRec = 14,
    DupKey = 15,
    InvReq = 16,
    NotOpen = 19,
    EndFile = 20,
    LengErr = 22,
}
