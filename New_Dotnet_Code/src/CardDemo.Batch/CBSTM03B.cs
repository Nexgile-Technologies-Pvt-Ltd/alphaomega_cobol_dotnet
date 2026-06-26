using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch I/O subroutine <c>CBSTM03B</c> — the file-handling helper the
/// statement driver <see cref="Cbstm03a"/> calls for every read/open/close. The mainframe program is a
/// thin dispatcher over four VSAM KSDS files: it EVALUATEs the DD name in the 1056-byte parameter area
/// <c>LK-M03B-AREA</c> and performs OPEN / sequential-READ / keyed-READ / CLOSE, returning the 2-character
/// COBOL FILE STATUS in <c>LK-M03B-RC</c> and, on a successful read, the record image in the 1000-byte
/// <c>LK-M03B-FLDT</c>.
/// </summary>
/// <remarks>
/// <para>Ported paragraph-by-paragraph from <c>app/cbl/CBSTM03B.cbl</c>; each PROCEDURE-DIVISION paragraph
/// is a method whose name mirrors the COBOL paragraph and whose body keeps the original statement order and
/// GO-TO flow (with <c>// source: CBSTM03B.cbl:NNN</c> citations).</para>
/// <para>Per the relational re-architecture (<c>_design/ARCHITECTURE.md</c>) the four VSAM files map to base
/// tables read through repositories:
/// <list type="bullet">
///   <item><b>TRNXFILE</b> -> TRANSACTION, sequential by the COSTM01 key (card-number + tran-id); the read
///   image is the 350-byte COSTM01 <c>TRNX-RECORD</c> layout (card16 + id16 + 318-byte rest), NOT the
///   CVTRA05Y master layout.</item>
///   <item><b>XREFFILE</b> -> CARD_XREF, sequential by xref_card_num; 50-byte CVACT03Y image.</item>
///   <item><b>CUSTFILE</b> -> CUSTOMER, RANDOM keyed by the 9-digit cust id; 500-byte CVCUS01Y image.</item>
///   <item><b>ACCTFILE</b> -> ACCOUNT, RANDOM keyed by the 11-digit acct id; 300-byte CVACT01Y image.</item>
/// </list>
/// Sequential reads use the repository <c>StartBrowse</c>/<c>ReadNext</c> cursor; keyed reads use
/// <c>ReadByKey</c>. The record image written into FLDT is built with the same fixed-width host codecs the
/// rest of the solution uses (zoned DISPLAY numerics + host-encoded text).</para>
/// <para>Status mapping mirrors the COBOL FILE STATUS the driver branches on: <c>'00'</c> ok, <c>'10'</c>
/// end-of-file on a sequential read, <c>'23'</c> record-not-found on a keyed read. OPEN/CLOSE always
/// return <c>'00'</c> for a present table.</para>
/// </remarks>
public sealed class Cbstm03b
{
    // FLDT is PIC X(1000); the read image (350/50/500/300) is placed in its first bytes, the tail spaces.
    private const int FldtLength = 1000;

    // --- COSTM01 TRNX-RECORD field widths (350 bytes) // source: cpy/COSTM01.cpy:20-36 -------------------
    private const int MoneyDigits = 11; // TRNX-AMT S9(09)V99 -> 11 zoned digits
    private const int MoneyScale = 2;

    private readonly TransactionRepository _transaction;
    private readonly CardXrefRepository _xref;
    private readonly CustomerRepository _customer;
    private readonly AccountRepository _account;
    private readonly HostKind _host;

    // Per-file open/cursor state (a COBOL FD is open or closed; sequential files hold a browse position).
    private IEnumerator<Transaction>? _trnxCursor; // TRNX-FILE sequential cursor (COSTM01 key order)
    private IEnumerator<CardXref>? _xrefCursor;     // XREF-FILE sequential cursor (xref_card_num order)

    // File-status fields (CBSTM03B working-storage). // source: CBSTM03B.cbl:83-97
    private string _trnxfileStatus = "00"; // TRNXFILE-STATUS
    private string _xreffileStatus = "00"; // XREFFILE-STATUS
    private string _custfileStatus = "00"; // CUSTFILE-STATUS
    private string _acctfileStatus = "00"; // ACCTFILE-STATUS

    /// <summary>
    /// The 1056-byte <c>LK-M03B-AREA</c> linkage record passed by reference on every CALL. The driver fills
    /// DD/OPER/KEY/KEY-LN before the call; CBSTM03B writes RC (and, on a read, FLDT) before returning.
    /// // source: CBSTM03B.cbl:99-112
    /// </summary>
    public sealed class M03BArea
    {
        /// <summary>LK-M03B-DD X(08) — the DD/file name selecting the file to act on.</summary>
        public string Dd { get; set; } = new(' ', 8);

        /// <summary>LK-M03B-OPER X(01) — operation: O=open, C=close, R=read(seq), K=read keyed, W=write, Z=rewrite.</summary>
        public char Oper { get; set; } = ' ';

        /// <summary>LK-M03B-RC X(02) — returned 2-character FILE STATUS.</summary>
        public string Rc { get; set; } = "00";

        /// <summary>LK-M03B-KEY X(25) — keyed-read key (first KeyLn bytes used).</summary>
        public string Key { get; set; } = new(' ', 25);

        /// <summary>LK-M03B-KEY-LN S9(4) — number of key bytes to use from Key.</summary>
        public int KeyLn { get; set; }

        /// <summary>LK-M03B-FLDT X(1000) — record image returned by a successful read.</summary>
        public string Fldt { get; set; } = new(' ', FldtLength);

        // 88-level condition names on LK-M03B-OPER. // source: CBSTM03B.cbl:103-108
        public bool M03BOpen => Oper == 'O';
        public bool M03BClose => Oper == 'C';
        public bool M03BRead => Oper == 'R';
        public bool M03BReadK => Oper == 'K';
        public bool M03BWrite => Oper == 'W';
        public bool M03BRewrite => Oper == 'Z';
    }

    private Cbstm03b(
        TransactionRepository transaction,
        CardXrefRepository xref,
        CustomerRepository customer,
        AccountRepository account,
        HostKind host)
    {
        _transaction = transaction;
        _xref = xref;
        _customer = customer;
        _account = account;
        _host = host;
    }

    /// <summary>
    /// Creates a CBSTM03B I/O handler bound to the relational repositories in <paramref name="support"/>.
    /// One instance models the four open FDs for the life of a driver run (call <see cref="Call"/> per CALL).
    /// </summary>
    /// <param name="support">Provides the TRANSACTION/CARD_XREF/CUSTOMER/ACCOUNT repositories.</param>
    /// <param name="host">Host encoding for the FLDT record images (defaults to EBCDIC, the host form).</param>
    public static Cbstm03b Create(BatchSupport support, HostKind host = HostKind.Ebcdic)
        => new(support.Transaction, support.CardXref, support.Customer, support.Account, host);

    /// <summary>Creates a CBSTM03B I/O handler bound to a <see cref="RelationalDb"/>.</summary>
    public static Cbstm03b Create(RelationalDb db, HostKind host = HostKind.Ebcdic)
        => new(new TransactionRepository(db), new CardXrefRepository(db),
               new CustomerRepository(db), new AccountRepository(db), host);

    // =================================================================================================
    // 0000-START — EVALUATE LK-M03B-DD. // source: CBSTM03B.cbl:116-131
    // =================================================================================================
    /// <summary>
    /// Performs one CBSTM03B CALL: dispatches on <see cref="M03BArea.Dd"/> to the matching file paragraph,
    /// which OPENs / READs / CLOSEs per <see cref="M03BArea.Oper"/> and sets <see cref="M03BArea.Rc"/>.
    /// </summary>
    public void Call(M03BArea area)
    {
        switch (Trim8(area.Dd))                          // EVALUATE LK-M03B-DD // source: CBSTM03B.cbl:118
        {
            case "TRNXFILE":                             // source: CBSTM03B.cbl:119-120
                Trnxfile1000Proc(area);
                break;
            case "XREFFILE":                             // source: CBSTM03B.cbl:121-122
                Xreffile2000Proc(area);
                break;
            case "CUSTFILE":                             // source: CBSTM03B.cbl:123-124
                Custfile3000Proc(area);
                break;
            case "ACCTFILE":                             // source: CBSTM03B.cbl:125-126
                Acctfile4000Proc(area);
                break;
            default:                                     // WHEN OTHER // source: CBSTM03B.cbl:127-128
                Goback9999();                            // GO TO 9999-GOBACK
                break;
        }
    }

    // 9999-GOBACK. // source: CBSTM03B.cbl:130-131
    private static void Goback9999()
    {
        // GOBACK — no status change; the area is returned as-is.
    }

    // =================================================================================================
    // 1000-TRNXFILE-PROC — OPEN INPUT / sequential READ INTO FLDT / CLOSE. // source: CBSTM03B.cbl:133-155
    // =================================================================================================
    private void Trnxfile1000Proc(M03BArea area)
    {
        if (area.M03BOpen)                               // IF M03B-OPEN // source: CBSTM03B.cbl:135
        {
            // OPEN INPUT TRNX-FILE -> position the COSTM01-key forward browse over TRANSACTION. // source: CBSTM03B.cbl:136
            _trnxCursor = _transaction.ReadAll()
                .OrderBy(t => t.CardNum, StringComparer.Ordinal)
                .ThenBy(t => t.TranId, StringComparer.Ordinal)
                .GetEnumerator();
            _trnxfileStatus = FileStatus.Ok;
            Trnx1900Exit(area);                          // GO TO 1900-EXIT // source: CBSTM03B.cbl:137
            return;
        }

        if (area.M03BRead)                               // IF M03B-READ // source: CBSTM03B.cbl:140
        {
            // READ TRNX-FILE INTO LK-M03B-FLDT (sequential next). // source: CBSTM03B.cbl:141-142
            if (_trnxCursor is not null && _trnxCursor.MoveNext())
            {
                area.Fldt = ToFldt(SerializeTrnxRecord(_trnxCursor.Current));
                _trnxfileStatus = FileStatus.Ok;
            }
            else
            {
                _trnxfileStatus = FileStatus.EndOfFile;  // '10' AT END
            }
            Trnx1900Exit(area);                          // GO TO 1900-EXIT // source: CBSTM03B.cbl:143
            return;
        }

        if (area.M03BClose)                              // IF M03B-CLOSE // source: CBSTM03B.cbl:146
        {
            // CLOSE TRNX-FILE. // source: CBSTM03B.cbl:147
            _trnxCursor?.Dispose();
            _trnxCursor = null;
            _trnxfileStatus = FileStatus.Ok;
            Trnx1900Exit(area);                          // GO TO 1900-EXIT // source: CBSTM03B.cbl:148
            return;
        }

        // No matching operation (e.g. W/Z): fall through to 1900-EXIT, returning the current status.
        Trnx1900Exit(area);
    }

    // 1900-EXIT. MOVE TRNXFILE-STATUS TO LK-M03B-RC. // source: CBSTM03B.cbl:151-152
    private void Trnx1900Exit(M03BArea area) => area.Rc = _trnxfileStatus;

    // =================================================================================================
    // 2000-XREFFILE-PROC — OPEN INPUT / sequential READ INTO FLDT / CLOSE. // source: CBSTM03B.cbl:157-179
    // =================================================================================================
    private void Xreffile2000Proc(M03BArea area)
    {
        if (area.M03BOpen)                               // IF M03B-OPEN // source: CBSTM03B.cbl:159
        {
            // OPEN INPUT XREF-FILE -> forward browse over CARD_XREF (xref_card_num order). // source: CBSTM03B.cbl:160
            _xrefCursor = _xref.ReadAll().GetEnumerator();
            _xreffileStatus = FileStatus.Ok;
            Xref2900Exit(area);                          // GO TO 2900-EXIT // source: CBSTM03B.cbl:161
            return;
        }

        if (area.M03BRead)                               // IF M03B-READ // source: CBSTM03B.cbl:164
        {
            // READ XREF-FILE INTO LK-M03B-FLDT (sequential next). // source: CBSTM03B.cbl:165-166
            if (_xrefCursor is not null && _xrefCursor.MoveNext())
            {
                area.Fldt = ToFldt(SerializeXrefRecord(_xrefCursor.Current));
                _xreffileStatus = FileStatus.Ok;
            }
            else
            {
                _xreffileStatus = FileStatus.EndOfFile;  // '10' AT END
            }
            Xref2900Exit(area);                          // GO TO 2900-EXIT // source: CBSTM03B.cbl:167
            return;
        }

        if (area.M03BClose)                              // IF M03B-CLOSE // source: CBSTM03B.cbl:170
        {
            // CLOSE XREF-FILE. // source: CBSTM03B.cbl:171
            _xrefCursor?.Dispose();
            _xrefCursor = null;
            _xreffileStatus = FileStatus.Ok;
            Xref2900Exit(area);                          // GO TO 2900-EXIT // source: CBSTM03B.cbl:172
            return;
        }

        Xref2900Exit(area);
    }

    // 2900-EXIT. MOVE XREFFILE-STATUS TO LK-M03B-RC. // source: CBSTM03B.cbl:175-176
    private void Xref2900Exit(M03BArea area) => area.Rc = _xreffileStatus;

    // =================================================================================================
    // 3000-CUSTFILE-PROC — OPEN INPUT / keyed READ INTO FLDT / CLOSE. // source: CBSTM03B.cbl:181-204
    // =================================================================================================
    private void Custfile3000Proc(M03BArea area)
    {
        if (area.M03BOpen)                               // IF M03B-OPEN // source: CBSTM03B.cbl:183
        {
            // OPEN INPUT CUST-FILE (RANDOM access KSDS — no browse to position). // source: CBSTM03B.cbl:184
            _custfileStatus = FileStatus.Ok;
            Cust3900Exit(area);                          // GO TO 3900-EXIT // source: CBSTM03B.cbl:185
            return;
        }

        if (area.M03BReadK)                              // IF M03B-READ-K // source: CBSTM03B.cbl:188
        {
            // MOVE LK-M03B-KEY (1:LK-M03B-KEY-LN) TO FD-CUST-ID; READ CUST-FILE INTO LK-M03B-FLDT. // source: CBSTM03B.cbl:189-191
            string keyText = KeyPrefix(area);            // FD-CUST-ID PIC X(09)
            long custId = ParseDigits(keyText);
            _custfileStatus = _customer.ReadByKey(custId, out Customer? cust);
            if (_custfileStatus == FileStatus.Ok)
                area.Fldt = ToFldt(SerializeCustRecord(cust!));
            Cust3900Exit(area);                          // GO TO 3900-EXIT // source: CBSTM03B.cbl:192
            return;
        }

        if (area.M03BClose)                              // IF M03B-CLOSE // source: CBSTM03B.cbl:195
        {
            // CLOSE CUST-FILE. // source: CBSTM03B.cbl:196
            _custfileStatus = FileStatus.Ok;
            Cust3900Exit(area);                          // GO TO 3900-EXIT // source: CBSTM03B.cbl:197
            return;
        }

        Cust3900Exit(area);
    }

    // 3900-EXIT. MOVE CUSTFILE-STATUS TO LK-M03B-RC. // source: CBSTM03B.cbl:200-201
    private void Cust3900Exit(M03BArea area) => area.Rc = _custfileStatus;

    // =================================================================================================
    // 4000-ACCTFILE-PROC — OPEN INPUT / keyed READ INTO FLDT / CLOSE. // source: CBSTM03B.cbl:206-229
    // =================================================================================================
    private void Acctfile4000Proc(M03BArea area)
    {
        if (area.M03BOpen)                               // IF M03B-OPEN // source: CBSTM03B.cbl:208
        {
            // OPEN INPUT ACCT-FILE (RANDOM access KSDS — no browse to position). // source: CBSTM03B.cbl:209
            _acctfileStatus = FileStatus.Ok;
            Acct4900Exit(area);                          // GO TO 4900-EXIT // source: CBSTM03B.cbl:210
            return;
        }

        if (area.M03BReadK)                              // IF M03B-READ-K // source: CBSTM03B.cbl:213
        {
            // MOVE LK-M03B-KEY (1:LK-M03B-KEY-LN) TO FD-ACCT-ID; READ ACCT-FILE INTO LK-M03B-FLDT. // source: CBSTM03B.cbl:214-216
            string keyText = KeyPrefix(area);            // FD-ACCT-ID PIC 9(11)
            long acctId = ParseDigits(keyText);
            _acctfileStatus = _account.ReadByKey(acctId, out Account? acct);
            if (_acctfileStatus == FileStatus.Ok)
                area.Fldt = ToFldt(SerializeAcctRecord(acct!));
            Acct4900Exit(area);                          // GO TO 4900-EXIT // source: CBSTM03B.cbl:217
            return;
        }

        if (area.M03BClose)                              // IF M03B-CLOSE // source: CBSTM03B.cbl:220
        {
            // CLOSE ACCT-FILE. // source: CBSTM03B.cbl:221
            _acctfileStatus = FileStatus.Ok;
            Acct4900Exit(area);                          // GO TO 4900-EXIT // source: CBSTM03B.cbl:222
            return;
        }

        Acct4900Exit(area);
    }

    // 4900-EXIT. MOVE ACCTFILE-STATUS TO LK-M03B-RC. // source: CBSTM03B.cbl:225-226
    private void Acct4900Exit(M03BArea area) => area.Rc = _acctfileStatus;

    // =================================================================================================
    // Record serializers — build each file's fixed-width host image (the bytes the READ ... INTO places
    // into FLDT). All numerics are USAGE DISPLAY (zoned); all text is host-encoded, space-padded.
    // =================================================================================================

    /// <summary>
    /// COSTM01 TRNX-RECORD image (350 bytes): TRNX-CARD-NUM X(16) + TRNX-ID X(16) + TRNX-REST (318).
    /// // source: cpy/COSTM01.cpy:20-36
    /// </summary>
    private byte[] SerializeTrnxRecord(Transaction t)
    {
        var w = new RecordWriter(_host);
        w.Alpha(t.CardNum, 16);                      // TRNX-CARD-NUM   X(16)
        w.Alpha(t.TranId, 16);                       // TRNX-ID         X(16)
        // --- TRNX-REST (318 bytes) ---
        w.Alpha(t.TypeCd, 2);                        // TRNX-TYPE-CD    X(02)
        w.Zoned(t.CatCd, 4, 0, signed: false);       // TRNX-CAT-CD     9(04)
        w.Alpha(t.Source, 10);                       // TRNX-SOURCE     X(10)
        w.Alpha(t.Desc, 100);                        // TRNX-DESC       X(100)
        w.Zoned(t.Amt, MoneyDigits, MoneyScale, true); // TRNX-AMT     S9(09)V99
        w.Zoned(t.MerchantId, 9, 0, signed: false);  // TRNX-MERCHANT-ID 9(09)
        w.Alpha(t.MerchantName, 50);                 // TRNX-MERCHANT-NAME X(50)
        w.Alpha(t.MerchantCity, 50);                 // TRNX-MERCHANT-CITY X(50)
        w.Alpha(t.MerchantZip, 10);                  // TRNX-MERCHANT-ZIP X(10)
        w.Alpha(t.OrigTs, 26);                       // TRNX-ORIG-TS    X(26)
        w.Alpha(t.ProcTs, 26);                       // TRNX-PROC-TS    X(26)
        w.Alpha("", 20);                             // FILLER          X(20)
        return w.ToArray(350);
    }

    /// <summary>CVACT03Y CARD-XREF-RECORD image (50 bytes). // source: cpy/CVACT03Y.cpy:4-8</summary>
    private byte[] SerializeXrefRecord(CardXref x)
    {
        var w = new RecordWriter(_host);
        w.Alpha(x.XrefCardNum, 16);                  // XREF-CARD-NUM   X(16)
        w.Zoned(x.CustId, 9, 0, signed: false);      // XREF-CUST-ID    9(09)
        w.Zoned(x.AcctId, 11, 0, signed: false);     // XREF-ACCT-ID    9(11)
        w.Alpha("", 14);                             // FILLER          X(14)
        return w.ToArray(50);
    }

    /// <summary>CVCUS01Y CUSTOMER-RECORD image (500 bytes). // source: cpy/CUSTREC.cpy:4-23</summary>
    private byte[] SerializeCustRecord(Customer c)
    {
        var w = new RecordWriter(_host);
        w.Zoned(c.CustId, 9, 0, signed: false);      // CUST-ID                 9(09)
        w.Alpha(c.FirstName, 25);                    // CUST-FIRST-NAME         X(25)
        w.Alpha(c.MiddleName, 25);                   // CUST-MIDDLE-NAME        X(25)
        w.Alpha(c.LastName, 25);                     // CUST-LAST-NAME          X(25)
        w.Alpha(c.AddrLine1, 50);                    // CUST-ADDR-LINE-1        X(50)
        w.Alpha(c.AddrLine2, 50);                    // CUST-ADDR-LINE-2        X(50)
        w.Alpha(c.AddrLine3, 50);                    // CUST-ADDR-LINE-3        X(50)
        w.Alpha(c.AddrStateCd, 2);                   // CUST-ADDR-STATE-CD      X(02)
        w.Alpha(c.AddrCountryCd, 3);                 // CUST-ADDR-COUNTRY-CD    X(03)
        w.Alpha(c.AddrZip, 10);                      // CUST-ADDR-ZIP           X(10)
        w.Alpha(c.PhoneNum1, 15);                    // CUST-PHONE-NUM-1        X(15)
        w.Alpha(c.PhoneNum2, 15);                    // CUST-PHONE-NUM-2        X(15)
        w.Zoned(c.Ssn, 9, 0, signed: false);         // CUST-SSN                9(09)
        w.Alpha(c.GovtIssuedId, 20);                 // CUST-GOVT-ISSUED-ID     X(20)
        w.Alpha(c.DobYyyyMmDd, 10);                  // CUST-DOB-YYYYMMDD       X(10)
        w.Alpha(c.EftAccountId, 10);                 // CUST-EFT-ACCOUNT-ID     X(10)
        w.Alpha(c.PriCardHolderInd, 1);              // CUST-PRI-CARD-HOLDER-IND X(01)
        w.Zoned(c.FicoCreditScore, 3, 0, signed: false); // CUST-FICO-CREDIT-SCORE 9(03)
        w.Alpha("", 168);                            // FILLER                  X(168)
        return w.ToArray(500);
    }

    /// <summary>CVACT01Y ACCOUNT-RECORD image (300 bytes). // source: cpy/CVACT01Y.cpy:4-17</summary>
    private byte[] SerializeAcctRecord(Account a)
    {
        var w = new RecordWriter(_host);
        w.Zoned(a.AcctId, 11, 0, signed: false);     // ACCT-ID                9(11)
        w.Alpha(a.ActiveStatus, 1);                  // ACCT-ACTIVE-STATUS     X(01)
        w.Zoned(a.CurrBal, 12, 2, true);             // ACCT-CURR-BAL          S9(10)V99
        w.Zoned(a.CreditLimit, 12, 2, true);         // ACCT-CREDIT-LIMIT      S9(10)V99
        w.Zoned(a.CashCreditLimit, 12, 2, true);     // ACCT-CASH-CREDIT-LIMIT S9(10)V99
        w.Alpha(a.OpenDate, 10);                     // ACCT-OPEN-DATE         X(10)
        w.Alpha(a.ExpirationDate, 10);               // ACCT-EXPIRAION-DATE    X(10)
        w.Alpha(a.ReissueDate, 10);                  // ACCT-REISSUE-DATE      X(10)
        w.Zoned(a.CurrCycCredit, 12, 2, true);       // ACCT-CURR-CYC-CREDIT   S9(10)V99
        w.Zoned(a.CurrCycDebit, 12, 2, true);        // ACCT-CURR-CYC-DEBIT    S9(10)V99
        w.Alpha(a.AddrZip, 10);                      // ACCT-ADDR-ZIP          X(10)
        w.Alpha(a.GroupId, 10);                      // ACCT-GROUP-ID          X(10)
        w.Alpha("", 178);                            // FILLER                 X(178)
        return w.ToArray(300);
    }

    // =================================================================================================
    // Small helpers
    // =================================================================================================

    /// <summary>
    /// READ ... INTO LK-M03B-FLDT — the record image lands left-justified in the 1000-byte FLDT, the tail
    /// padded with spaces (a shorter logical record fills the leading bytes of the larger linkage field).
    /// </summary>
    private string ToFldt(byte[] image)
    {
        string text = HostEncoding.For(_host).GetString(image);
        return text.Length >= FldtLength ? text[..FldtLength] : text.PadRight(FldtLength, ' ');
    }

    /// <summary>LK-M03B-DD is X(08); EVALUATE compares the trimmed DD name.</summary>
    private static string Trim8(string dd) => (dd ?? "").PadRight(8).TrimEnd();

    /// <summary>LK-M03B-KEY (1:LK-M03B-KEY-LN) — the first KeyLn characters of the 25-byte key field.</summary>
    private static string KeyPrefix(M03BArea area)
    {
        string key = area.Key ?? "";
        int n = area.KeyLn;
        if (n <= 0) return "";
        if (n > key.Length) key = key.PadRight(n, ' ');
        return key[..n];
    }

    /// <summary>
    /// MOVE of an alphanumeric key (the driver moves a 9(09)/9(11) value into the X(25) key) to the FD's
    /// numeric record key. Digits are parsed; spaces/non-digits are ignored, matching a numeric MOVE of a
    /// zoned key region.
    /// </summary>
    private static long ParseDigits(string text)
    {
        long value = 0;
        foreach (char ch in text)
            if (ch >= '0' && ch <= '9')
                value = value * 10 + (ch - '0');
        return value;
    }

    /// <summary>Builds a fixed-width record image, packing zoned (DISPLAY) numeric and alphanumeric fields.</summary>
    private sealed class RecordWriter(HostKind host)
    {
        private readonly List<byte> _bytes = [];
        private readonly HostKind _host = host;

        public void Zoned(decimal value, int totalDigits, int scale, bool signed)
        {
            var field = new byte[totalDigits];
            ZonedDecimalCodec.Encode(value, field, totalDigits, scale, signed, _host);
            _bytes.AddRange(field);
        }

        public void Zoned(long value, int totalDigits, int scale, bool signed)
            => Zoned((decimal)value, totalDigits, scale, signed);

        public void Alpha(string? text, int width)
        {
            text ??= "";
            string padded = text.Length >= width ? text[..width] : text.PadRight(width, ' ');
            _bytes.AddRange(HostEncoding.For(_host).GetBytes(padded));
        }

        public byte[] ToArray(int expectedLength)
        {
            if (_bytes.Count != expectedLength)
                throw new InvalidOperationException(
                    $"Record length {_bytes.Count} != expected {expectedLength}.");
            return _bytes.ToArray();
        }
    }
}
