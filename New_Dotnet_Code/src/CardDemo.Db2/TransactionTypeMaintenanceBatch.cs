using CardDemo.Runtime;
using CardDemo.Data;
using CardDemo.Domain;
using Microsoft.Data.Sqlite;

namespace CardDemo.Db2;

/// <summary>
/// Faithful relational re-port of the optional batch + DB2 program <c>COBTUPDT</c> (member
/// <c>app/app-transaction-type-db2/cbl/COBTUPDT.cbl</c>) — the batch maintenance utility for the DB2
/// reference table <c>CARDDEMO.TRANSACTION_TYPE</c>. It reads a sequential input dataset (DD
/// <c>INPFILE</c>, 53-byte fixed records) and, for each record, treats the first byte as an action code
/// and drives an embedded static-SQL statement against the table:
/// <c>A</c>=INSERT, <c>U</c>=UPDATE (description only), <c>D</c>=DELETE, <c>*</c>=ignore (comment line),
/// anything else =&gt; "abend". Columns 2-3 carry the transaction-type code (<c>TR_TYPE</c>, the primary
/// key, a 2-char string) and columns 4-53 carry the 50-char transaction description (<c>TR_DESCRIPTION</c>,
/// kept full-width including trailing spaces — the host variable is <c>PIC X(50)</c>, not a VARCHAR group).
/// </summary>
/// <remarks>
/// <para>Per <c>_design/specs/COBTUPDT.md</c> and <c>_design/ARCHITECTURE.md</c> this is the optional DB2
/// Transaction-Type module → target <c>src/CardDemo.Db2</c>, using the same relational database with the
/// <c>TRANSACTION_TYPE</c> table via <see cref="TransactionTypeRepository"/>. EXEC SQL
/// INSERT/UPDATE/DELETE map to the repository's Insert/Update/Delete; the DB2 <c>SQLCODE</c> branching
/// (0 / +100 / &lt;0) is synthesized from the repository's <see cref="FileStatus"/> result and any
/// SQLite exception, so the COBOL <c>EVALUATE TRUE</c> branches fire exactly as on the mainframe.</para>
///
/// <para>Ported paragraph-by-paragraph; each PROCEDURE-DIVISION paragraph is a method whose name mirrors
/// the COBOL paragraph, keeping the original statement order with <c>// source: COBTUPDT.cbl:NNN</c>
/// citations. <see cref="Sysout"/> captures every <c>DISPLAY</c> line in order; <see cref="ReturnCode"/>
/// is the job-step <c>RETURN-CODE</c> (0 normally, 4 if any record errored).</para>
///
/// <para>FAITHFUL BUGS preserved verbatim (do NOT fix — see <c>_design/specs/COBTUPDT.md</c> §6):
/// <list type="number">
/// <item><c>9999-ABEND</c> is a misnomer — it does NOT abend or stop. It only DISPLAYs the message and
/// <c>MOVE 4 TO RETURN-CODE</c>, then returns to the caller; the read loop continues with the next
/// record. The final return code is 4 if any record errored, but all subsequent records are still
/// processed. // source: COBTUPDT.cbl:230-233</item>
/// <item>No COMMIT / ROLLBACK anywhere. The program issues no <c>EXEC SQL COMMIT/ROLLBACK</c>; under
/// IKJEFT01/DB2 the whole run is one unit of work committed at normal end. There is no per-record commit
/// and no per-record rollback of prior successful rows even when a later record errors. The port commits
/// once at end of run. // source: COBTUPDT.cbl:99, 132-226</item>
/// <item>Open-failure is ignored. <c>0001-OPEN-FILES</c> checks the file status only to choose a DISPLAY
/// message; on a non-'00' status it still falls through into the read loop. No abend, no RC set on open
/// failure. // source: COBTUPDT.cbl:82-89</item>
/// <item>Dead FD record / dead host structure. The FD record <c>WS-INPUT-VARS</c> and the DCLGEN host
/// struct from <c>DCLTRTYP</c> are never referenced; binding uses the file-image fields
/// (<c>INPUT-REC-NUMBER</c>/<c>INPUT-REC-DESC</c>), which is why descriptions store as fixed 50-char
/// (space-padded), not trimmed VARCHARs. // source: COBTUPDT.cbl:40-46, 71-77, 101</item>
/// <item>Case-sensitive action codes. Only uppercase <c>A</c>/<c>U</c>/<c>D</c>/<c>*</c> are recognized;
/// any other byte (lowercase, blanks, digits) goes to <c>WHEN OTHER</c> → 'ERROR: TYPE NOT VALID' →
/// "abend" (RC=4) and continues. // source: COBTUPDT.cbl:110-129</item>
/// <item>The JCL comment calls columns 2-3 a "NUMERIC VALUE" but the host variable is <c>PIC X(2)</c> /
/// column <c>CHAR(2)</c> — no numeric validation/conversion; <c>TR_TYPE</c> is an opaque 2-char string.
/// // source: COBTUPDT.cbl:74, 145</item>
/// </list></para>
/// </remarks>
public sealed class TransactionTypeMaintenanceBatch
{
    // Host-variable / FD field widths. INPUT-REC layout = X(1) + X(2) + X(50) = 53 bytes.
    private const int RecordLength = 53;        // FD TR-RECORD RECORDING MODE F (X1 + X2 + X50)
    private const int TypeWidth = 1;            // INPUT-REC-TYPE   PIC X(1)
    private const int NumberWidth = 2;          // INPUT-REC-NUMBER PIC X(2)
    private const int DescWidth = 50;           // INPUT-REC-DESC   PIC X(50)

    // DB2 SQLCODE values the program branches on (synthesized from the repository outcome).
    private const int SqlcodeOk = 0;            // SQLCODE = ZERO
    private const int SqlcodeNotFound = 100;    // SQLCODE = +100 (UPDATE/DELETE matched no rows)
    private const int SqlcodeDuplicate = -803;  // DB2 -803 duplicate-key (PK/unique violation) → SQLCODE < 0
    private const int SqlcodeError = -904;      // any other negative SQLCODE (generic access error)

    private readonly RelationalDb _db;
    private readonly TransactionTypeRepository _transactionType;
    private readonly IReadOnlyList<string> _inputRecords;
    private readonly List<string> _sysout = [];

    // WORKING-STORAGE.
    private string _lastrec = " ";                          // 01 FLAGS / LASTREC PIC X(1) VALUE SPACES
    private string _wsReturnMsg = new(' ', 80);             // WS-RETURN-MSG PIC X(80) VALUE SPACES
    private int _wsVarSqlcode;                              // WS-VAR-SQLCODE (edited PIC ----9)
    private string _wsInfStatus = "  ";                    // WS-INF-STATUS (file status)
    private int _returnCode;                               // RETURN-CODE (job-step return code)

    // 01 WS-INPUT-REC (the READ ... INTO target). Initialized to spaces (VALUE SPACES on each field).
    private string _inputRecType = " ";                    // INPUT-REC-TYPE   PIC X(1)
    private string _inputRecNumber = "  ";                 // INPUT-REC-NUMBER PIC X(2)
    private string _inputRecDesc = new(' ', DescWidth);    // INPUT-REC-DESC   PIC X(50)

    // Input cursor over the sequential file (the QSAM read position).
    private int _readIndex;
    private bool _fileOpen;

    private TransactionTypeMaintenanceBatch(RelationalDb db, IReadOnlyList<string> inputRecords)
    {
        _db = db;
        _transactionType = new TransactionTypeRepository(db);
        _inputRecords = inputRecords;
    }

    /// <summary>The SYSOUT (DISPLAY) lines produced by the run, in order.</summary>
    public IReadOnlyList<string> Sysout => _sysout;

    /// <summary>The job-step RETURN-CODE: 0 normally, 4 if any record errored (faithful — see bug #1).</summary>
    public int ReturnCode => _returnCode;

    /// <summary>
    /// Runs COBTUPDT over the <c>TRANSACTION_TYPE</c> table with the given in-memory input records (each
    /// the raw text image of one 53-byte <c>INPFILE</c> record: action byte + 2-char type + 50-char
    /// description; shorter lines are space-padded, longer ones truncated at 53, matching RECFM=F). The
    /// whole run is one unit of work committed once at end (faithful — no per-record commit/rollback).
    /// Returns the SYSOUT lines in order. // source: COBTUPDT.cbl:80-99
    /// </summary>
    /// <param name="db">The relational database holding <c>TRANSACTION_TYPE</c>.</param>
    /// <param name="inputRecords">The INPFILE records, in file order.</param>
    public static IReadOnlyList<string> Run(
        RelationalDb db,
        IReadOnlyList<string> inputRecords)
    {
        var program = new TransactionTypeMaintenanceBatch(db, inputRecords);
        program.Execute();
        return program.Sysout;
    }

    /// <summary>
    /// Builds the program without running it, so a caller can invoke <see cref="Run()"/> and then inspect
    /// both <see cref="Sysout"/> and <see cref="ReturnCode"/> on the returned instance.
    /// </summary>
    public static TransactionTypeMaintenanceBatch Create(RelationalDb db, IReadOnlyList<string> inputRecords)
        => new(db, inputRecords);

    /// <summary>
    /// Convenience overload that reads the INPFILE dataset from disk (one fixed 53-byte record per chunk;
    /// trailing partial bytes are tolerated and space-padded) and runs the program against the given
    /// <see cref="RelationalDb"/>.
    /// </summary>
    /// <param name="db">The relational database holding <c>TRANSACTION_TYPE</c>.</param>
    /// <param name="inputFilePath">Path to the INPFILE dataset (text or fixed-width record stream).</param>
    public static IReadOnlyList<string> RunFile(RelationalDb db, string inputFilePath)
        => Run(db, ReadInputFile(inputFilePath));

    /// <summary>Runs the program, exposing the resulting instance (SYSOUT + RETURN-CODE) to the caller.</summary>
    public void Run()
    {
        Execute();
    }

    // =================================================================================================
    // De-facto MAIN: COBOL has no top-level driver paragraph. Paragraphs execute top-to-bottom, so after
    // 0001-OPEN-FILES hits its EXIT, control FALLS THROUGH into 1001-READ-NEXT-RECORDS (which loops, then
    // closes, then STOP RUN). The .NET port makes this explicit. // source: COBTUPDT.cbl:80-99
    // =================================================================================================
    private void Execute()
    {
        // One unit of work for the whole run: no per-record commit/rollback (faithful bug #2). On the
        // mainframe IKJEFT01/DB2 implicitly commits at normal end; we mirror that with one transaction
        // committed after STOP RUN (the read loop returns normally even when records "abend").
        using SqliteTransaction tx = _db.BeginTransaction();

        Open0001Files();          // 0001-OPEN-FILES   // source: COBTUPDT.cbl:82-89
        ReadNext1001Records();    // falls through into 1001-READ-NEXT-RECORDS // source: COBTUPDT.cbl:91-99

        tx.Commit();              // implicit commit at normal end (STOP RUN). // source: COBTUPDT.cbl:99
    }

    // -------------------------------------------------------------------------------------------------
    // 0001-OPEN-FILES // source: COBTUPDT.cbl:82-89
    // OPEN INPUT TR-RECORD; DISPLAY 'OPEN FILE OK' / 'OPEN FILE NOT OK' on the file status. No abend on a
    // bad open — it falls through into the read loop regardless (faithful bug #3).
    private void Open0001Files()
    {
        // OPEN INPUT TR-RECORD. A present input dataset opens '00'; an absent one would be non-'00'.
        _fileOpen = true;
        _readIndex = 0;
        _wsInfStatus = FileStatus.Ok;                 // '00' for a normally-opened input file.

        if (_wsInfStatus == FileStatus.Ok)            // IF WS-INF-STATUS = '00' THEN // source: COBTUPDT.cbl:84
            _sysout.Add("OPEN FILE OK");              // source: COBTUPDT.cbl:85
        else
            _sysout.Add("OPEN FILE NOT OK");          // source: COBTUPDT.cbl:87
        // EXIT. // source: COBTUPDT.cbl:89
    }

    // -------------------------------------------------------------------------------------------------
    // 1001-READ-NEXT-RECORDS // source: COBTUPDT.cbl:91-99
    // Priming read, then PERFORM UNTIL LASTREC = 'Y': treat + read. After the loop: close, then STOP RUN.
    private void ReadNext1001Records()
    {
        Read1002Records();                            // PERFORM 1002-READ-RECORDS (priming) // source: COBTUPDT.cbl:92
        while (_lastrec != "Y")                        // PERFORM UNTIL LASTREC = 'Y' // source: COBTUPDT.cbl:93
        {
            Treat1003Record();                        // PERFORM 1003-TREAT-RECORD // source: COBTUPDT.cbl:94
            Read1002Records();                        // PERFORM 1002-READ-RECORDS // source: COBTUPDT.cbl:95
        }                                              // END-PERFORM // source: COBTUPDT.cbl:96
        CloseStop2001();                              // PERFORM 2001-CLOSE-STOP // source: COBTUPDT.cbl:97
        // EXIT. (no-op) then STOP RUN. — the run terminates here. // source: COBTUPDT.cbl:98-99
    }

    // -------------------------------------------------------------------------------------------------
    // 1002-READ-RECORDS // source: COBTUPDT.cbl:100-107
    // READ TR-RECORD NEXT RECORD INTO WS-INPUT-REC; AT END MOVE 'Y' TO LASTREC. If not EOF, DISPLAY
    // 'PROCESSING   ' followed by the 53-byte WS-INPUT-REC.
    private void Read1002Records()
    {
        if (_readIndex < _inputRecords.Count)         // READ ... NEXT RECORD INTO WS-INPUT-REC // source: COBTUPDT.cbl:101
        {
            string image = _inputRecords[_readIndex];
            _readIndex++;
            MoveImageToInputRec(image);               // INTO WS-INPUT-REC (deconstruct the 53-byte image)
        }
        else
        {
            _lastrec = "Y";                            // AT END MOVE 'Y' TO LASTREC // source: COBTUPDT.cbl:102
        }

        if (_lastrec != "Y")                           // IF LASTREC NOT EQUAL TO 'Y' THEN // source: COBTUPDT.cbl:104
        {
            // DISPLAY 'PROCESSING   ' WS-INPUT-REC (three trailing spaces + the 53-byte record image).
            _sysout.Add("PROCESSING   " + InputRecImage()); // source: COBTUPDT.cbl:105
        }
        // EXIT. // source: COBTUPDT.cbl:107
    }

    // -------------------------------------------------------------------------------------------------
    // 1003-TREAT-RECORD // source: COBTUPDT.cbl:109-130
    // EVALUATE INPUT-REC-TYPE (the action byte). Case-sensitive uppercase only (faithful bug #5).
    private void Treat1003Record()
    {
        switch (_inputRecType)                         // EVALUATE INPUT-REC-TYPE // source: COBTUPDT.cbl:110
        {
            case "A":                                  // WHEN 'A' // source: COBTUPDT.cbl:111
                _sysout.Add("ADDING RECORD");          // source: COBTUPDT.cbl:112
                Insert10031Db();                       // PERFORM 10031-INSERT-DB // source: COBTUPDT.cbl:113
                break;
            case "U":                                  // WHEN 'U' // source: COBTUPDT.cbl:114
                _sysout.Add("UPDATING RECORD");        // source: COBTUPDT.cbl:115
                Update10032Db();                       // PERFORM 10032-UPDATE-DB // source: COBTUPDT.cbl:116
                break;
            case "D":                                  // WHEN 'D' // source: COBTUPDT.cbl:117
                _sysout.Add("DELETING RECORD");        // source: COBTUPDT.cbl:118
                Delete10033Db();                       // PERFORM 10033-DELETE-DB // source: COBTUPDT.cbl:119
                break;
            case "*":                                  // WHEN '*' // source: COBTUPDT.cbl:120
                _sysout.Add("IGNORING COMMENTED LINE");// source: COBTUPDT.cbl:121 (no DB action)
                break;
            default:                                   // WHEN OTHER // source: COBTUPDT.cbl:122
                // STRING 'ERROR: TYPE NOT VALID' DELIMITED BY SIZE INTO WS-RETURN-MSG.
                StringIntoReturnMsg("ERROR: TYPE NOT VALID"); // source: COBTUPDT.cbl:123-127
                Abend9999();                           // PERFORM 9999-ABEND // source: COBTUPDT.cbl:128
                break;
        }
        // END-EVALUATE. EXIT. // source: COBTUPDT.cbl:129-130
    }

    // -------------------------------------------------------------------------------------------------
    // 10031-INSERT-DB // source: COBTUPDT.cbl:132-164
    // EXEC SQL INSERT INTO CARDDEMO.TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION)
    //          VALUES (:INPUT-REC-NUMBER, :INPUT-REC-DESC) END-EXEC.
    private void Insert10031Db()
    {
        int sqlcode = ExecInsert(_inputRecNumber, _inputRecDesc); // host vars bound verbatim (full widths)
        _wsVarSqlcode = sqlcode;                                  // MOVE SQLCODE TO WS-VAR-SQLCODE // source: COBTUPDT.cbl:149

        // EVALUATE TRUE // source: COBTUPDT.cbl:151
        if (sqlcode == SqlcodeOk)                                  // WHEN SQLCODE = ZERO // source: COBTUPDT.cbl:152
        {
            _sysout.Add("RECORD INSERTED SUCCESSFULLY");          // source: COBTUPDT.cbl:153
        }
        else if (sqlcode < 0)                                     // WHEN SQLCODE < 0 // source: COBTUPDT.cbl:154
        {
            // STRING 'Error accessing:' ' TRANSACTION_TYPE table. SQLCODE:' WS-VAR-SQLCODE
            //   DELIMITED BY SIZE INTO WS-RETURN-MSG. // source: COBTUPDT.cbl:155-161
            StringIntoReturnMsg(
                "Error accessing:" + " TRANSACTION_TYPE table. SQLCODE:" + EditSqlcode(_wsVarSqlcode));
            Abend9999();                                          // PERFORM 9999-ABEND // source: COBTUPDT.cbl:162
        }
        // END-EVALUATE. EXIT. (INSERT has NO +100 branch.) // source: COBTUPDT.cbl:163-164
    }

    // -------------------------------------------------------------------------------------------------
    // 10032-UPDATE-DB // source: COBTUPDT.cbl:166-195
    // EXEC SQL UPDATE CARDDEMO.TRANSACTION_TYPE SET TR_DESCRIPTION = :INPUT-REC-DESC
    //          WHERE TR_TYPE = :INPUT-REC-NUMBER END-EXEC. (Only TR_DESCRIPTION is updated.)
    private void Update10032Db()
    {
        int sqlcode = ExecUpdate(_inputRecNumber, _inputRecDesc);
        _wsVarSqlcode = sqlcode;                                  // MOVE SQLCODE TO WS-VAR-SQLCODE // source: COBTUPDT.cbl:176

        // EVALUATE TRUE // source: COBTUPDT.cbl:177
        if (sqlcode == SqlcodeOk)                                  // WHEN SQLCODE = ZERO // source: COBTUPDT.cbl:178
        {
            _sysout.Add("RECORD UPDATED SUCCESSFULLY");           // source: COBTUPDT.cbl:179
        }
        else if (sqlcode == SqlcodeNotFound)                      // WHEN SQLCODE = +100 // source: COBTUPDT.cbl:180
        {
            // STRING 'No records found.' DELIMITED BY SIZE INTO WS-RETURN-MSG. // source: COBTUPDT.cbl:181-183
            StringIntoReturnMsg("No records found.");
            Abend9999();                                          // PERFORM 9999-ABEND // source: COBTUPDT.cbl:184
        }
        else if (sqlcode < 0)                                     // WHEN SQLCODE < 0 // source: COBTUPDT.cbl:185
        {
            // STRING error message (same literals as INSERT) INTO WS-RETURN-MSG. // source: COBTUPDT.cbl:186-192
            StringIntoReturnMsg(
                "Error accessing:" + " TRANSACTION_TYPE table. SQLCODE:" + EditSqlcode(_wsVarSqlcode));
            Abend9999();                                          // PERFORM 9999-ABEND // source: COBTUPDT.cbl:193
        }
        // END-EVALUATE. EXIT. // source: COBTUPDT.cbl:194-195
    }

    // -------------------------------------------------------------------------------------------------
    // 10033-DELETE-DB // source: COBTUPDT.cbl:196-226
    // EXEC SQL DELETE FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE = :INPUT-REC-NUMBER END-EXEC.
    private void Delete10033Db()
    {
        int sqlcode = ExecDelete(_inputRecNumber);
        _wsVarSqlcode = sqlcode;                                  // MOVE SQLCODE TO WS-VAR-SQLCODE // source: COBTUPDT.cbl:205

        // EVALUATE TRUE // source: COBTUPDT.cbl:207
        if (sqlcode == SqlcodeOk)                                  // WHEN SQLCODE = ZERO // source: COBTUPDT.cbl:208
        {
            _sysout.Add("RECORD DELETED SUCCESSFULLY");           // source: COBTUPDT.cbl:209
        }
        else if (sqlcode == SqlcodeNotFound)                      // WHEN SQLCODE = +100 // source: COBTUPDT.cbl:210
        {
            // STRING 'No records found.' DELIMITED BY SIZE INTO WS-RETURN-MSG. // source: COBTUPDT.cbl:211-213
            StringIntoReturnMsg("No records found.");
            Abend9999();                                          // PERFORM 9999-ABEND // source: COBTUPDT.cbl:214
        }
        else if (sqlcode < 0)                                     // WHEN SQLCODE < 0 // source: COBTUPDT.cbl:216
        {
            // STRING error message INTO WS-RETURN-MSG. // source: COBTUPDT.cbl:217-223
            StringIntoReturnMsg(
                "Error accessing:" + " TRANSACTION_TYPE table. SQLCODE:" + EditSqlcode(_wsVarSqlcode));
            Abend9999();                                          // PERFORM 9999-ABEND // source: COBTUPDT.cbl:224
        }
        // END-EVALUATE. EXIT. // source: COBTUPDT.cbl:225-226
    }

    // -------------------------------------------------------------------------------------------------
    // 9999-ABEND // source: COBTUPDT.cbl:230-233
    // DISPLAY WS-RETURN-MSG; MOVE 4 TO RETURN-CODE; EXIT.
    // FAITHFUL BUG #1: this does NOT stop the run — it sets RC=4 and RETURNS; processing continues with
    // the next record.
    private void Abend9999()
    {
        _sysout.Add(_wsReturnMsg);                    // DISPLAY WS-RETURN-MSG // source: COBTUPDT.cbl:231
        _returnCode = 4;                              // MOVE 4 TO RETURN-CODE // source: COBTUPDT.cbl:232
        // EXIT. (returns to caller — no STOP/abend) // source: COBTUPDT.cbl:233
    }

    // -------------------------------------------------------------------------------------------------
    // 2001-CLOSE-STOP // source: COBTUPDT.cbl:234-236
    // CLOSE TR-RECORD; EXIT. (Despite the name it does NOT itself STOP RUN — the STOP RUN is in
    // 1001-READ-NEXT-RECORDS after the close.)
    private void CloseStop2001()
    {
        _fileOpen = false;                            // CLOSE TR-RECORD // source: COBTUPDT.cbl:235
        // EXIT. // source: COBTUPDT.cbl:236
    }

    // =================================================================================================
    // EXEC SQL → repository mapping. Each returns a synthesized DB2-style SQLCODE so the COBOL EVALUATE
    // branches (0 / +100 / <0) fire identically. // source: COBTUPDT.cbl:137-148, 171-175, 201-204
    // =================================================================================================

    // INSERT INTO TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION) VALUES (:n, :d).
    // Repository Insert → '00' (SQLCODE 0) or '22' duplicate-PK (SQLCODE -803). DB2 -803 is NOT specially
    // handled by the program — it falls into the generic SQLCODE < 0 branch (open question §8). Any other
    // SQLite error surfaces as a negative SQLCODE (generic access error), never +100.
    private int ExecInsert(string trType, string trDescription)
    {
        try
        {
            string status = _transactionType.Insert(new TransactionType
            {
                TrType = trType,                       // :INPUT-REC-NUMBER (2 chars, opaque) // source: COBTUPDT.cbl:145
                TrDescription = trDescription,         // :INPUT-REC-DESC (50 chars incl. trailing spaces) // source: COBTUPDT.cbl:146
            });
            return status == FileStatus.Ok ? SqlcodeOk : SqlcodeDuplicate; // '00' → 0; '22' (dup PK) → -803
        }
        catch (SqliteException)
        {
            return SqlcodeError;                       // any other DB error → negative SQLCODE
        }
    }

    // UPDATE TRANSACTION_TYPE SET TR_DESCRIPTION = :d WHERE TR_TYPE = :n.
    // Repository Update → '00' (rows>0, SQLCODE 0) or '23' (rows=0, SQLCODE +100). Error → negative.
    private int ExecUpdate(string trType, string trDescription)
    {
        try
        {
            string status = _transactionType.Update(new TransactionType
            {
                TrType = trType,                       // WHERE TR_TYPE = :INPUT-REC-NUMBER // source: COBTUPDT.cbl:174
                TrDescription = trDescription,         // SET TR_DESCRIPTION = :INPUT-REC-DESC // source: COBTUPDT.cbl:173
            });
            return status == FileStatus.Ok ? SqlcodeOk : SqlcodeNotFound; // '00' → 0; '23' (no rows) → +100
        }
        catch (SqliteException)
        {
            return SqlcodeError;
        }
    }

    // DELETE FROM TRANSACTION_TYPE WHERE TR_TYPE = :n.
    // Repository Delete → '00' (rows>0, SQLCODE 0) or '23' (rows=0, SQLCODE +100). Error → negative.
    private int ExecDelete(string trType)
    {
        try
        {
            string status = _transactionType.Delete(trType); // WHERE TR_TYPE = :INPUT-REC-NUMBER // source: COBTUPDT.cbl:203
            return status == FileStatus.Ok ? SqlcodeOk : SqlcodeNotFound; // '00' → 0; '23' (no rows) → +100
        }
        catch (SqliteException)
        {
            return SqlcodeError;
        }
    }

    // =================================================================================================
    // WS-INPUT-REC handling (the READ ... INTO target) + the input image rendering.
    // =================================================================================================

    // Deconstruct a 53-byte fixed record image into WS-INPUT-REC fields (X1 + X2 + X50). The COBOL READ
    // INTO copies the whole record over WS-INPUT-REC; we keep each elementary field at its fixed width.
    private void MoveImageToInputRec(string image)
    {
        string padded = FixedWidth(image, RecordLength);  // RECFM F: exactly 53 bytes (pad/truncate).
        _inputRecType = padded.Substring(0, TypeWidth);                       // INPUT-REC-TYPE   X(1)
        _inputRecNumber = padded.Substring(TypeWidth, NumberWidth);          // INPUT-REC-NUMBER X(2)
        _inputRecDesc = padded.Substring(TypeWidth + NumberWidth, DescWidth); // INPUT-REC-DESC   X(50)
    }

    // The 53-byte WS-INPUT-REC image (type + number + desc, each at its fixed width) as DISPLAY would show.
    private string InputRecImage()
        => FixedWidth(_inputRecType, TypeWidth)
         + FixedWidth(_inputRecNumber, NumberWidth)
         + FixedWidth(_inputRecDesc, DescWidth);

    // =================================================================================================
    // WS-RETURN-MSG (PIC X(80)) STRING ... DELIMITED BY SIZE handling.
    // =================================================================================================

    // STRING <text> DELIMITED BY SIZE INTO WS-RETURN-MSG. COBOL STRING starts at position 1 and does NOT
    // clear the field first; trailing bytes beyond what is moved keep their prior content (faithful — see
    // §7). WS-RETURN-MSG is X(80) initialized to spaces at program start; here we overwrite from position
    // 1 and leave the residual tail bytes untouched, then DISPLAY shows the full 80-char field.
    private void StringIntoReturnMsg(string text)
    {
        if (text.Length >= 80)
        {
            _wsReturnMsg = text[..80];                 // STRING stops at the field width (X(80)).
        }
        else
        {
            // Overwrite positions 1..text.Length; keep the residual tail (faithful, no clear-to-spaces).
            _wsReturnMsg = text + _wsReturnMsg[text.Length..];
        }
    }

    // =================================================================================================
    // WS-VAR-SQLCODE editing: PIC ----9 (floating minus, 4 minus positions + 1 nine = width 5).
    // A non-negative code renders with leading spaces (0 → '    0', 100 → '  100'); a negative code shows
    // a single floating '-' immediately left of the magnitude (-803 → ' -803'). The rightmost '9' always
    // shows a digit (so zero is '    0', not all-blank). // source: COBTUPDT.cbl:65, 158, 189, 220
    // =================================================================================================
    internal static string EditSqlcode(int value)
    {
        const int width = 5;                           // ----9
        var cells = new char[width];
        for (int i = 0; i < width; i++) cells[i] = ' ';

        bool negative = value < 0;
        string digits = Math.Abs((long)value).ToString(); // magnitude digits (at least "0")

        // Place digits right-justified into the 5 cells.
        int pos = width - 1;
        for (int i = digits.Length - 1; i >= 0 && pos >= 0; i--, pos--)
            cells[pos] = digits[i];

        // Floating minus: a single '-' just left of the most-significant digit (if a cell remains).
        if (negative && pos >= 0)
            cells[pos] = '-';

        return new string(cells);
    }

    // =================================================================================================
    // Input-file reading helpers.
    // =================================================================================================

    // Reads the INPFILE dataset. Splits on line breaks if the file is line-oriented; otherwise treats it
    // as a contiguous stream of fixed 53-byte records (RECFM=F). Each returned image is the raw record
    // text (un-padded here; MoveImageToInputRec pads/truncates to 53 on READ INTO).
    public static IReadOnlyList<string> ReadInputFile(string path)
    {
        string text = File.ReadAllText(path);
        if (text.Contains('\n') || text.Contains('\r'))
        {
            // Line-oriented dataset: one record per line (strip trailing CR/LF; keep interior spacing).
            var lines = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string rec = line.TrimEnd('\r');
                // A trailing empty line (file ends in newline) is not a record.
                lines.Add(rec);
            }
            if (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        // Contiguous fixed-length records (RECFM=F, LRECL 53).
        var records = new List<string>();
        for (int i = 0; i < text.Length; i += RecordLength)
        {
            int len = Math.Min(RecordLength, text.Length - i);
            records.Add(text.Substring(i, len));
        }
        return records;
    }

    // COBOL fixed-width semantics: left-justify, space-pad to width, right-truncate beyond width.
    private static string FixedWidth(string text, int width)
    {
        text ??= "";
        return text.Length >= width ? text[..width] : text.PadRight(width, ' ');
    }
}
