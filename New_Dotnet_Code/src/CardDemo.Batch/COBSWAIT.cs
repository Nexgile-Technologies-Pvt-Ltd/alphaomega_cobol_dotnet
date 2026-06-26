using CardDemo.Cobol.Runtime;

namespace CardDemo.Batch;

/// <summary>
/// Faithful relational re-port of the batch/util program <c>COBSWAIT</c> (MVSWAIT timer-wait utility).
/// The program reads an 8-character parameter from the <c>SYSIN</c> data stream, moves it into a binary
/// <c>9(8) COMP</c> count, and calls the external system timing routine <c>MVSWAIT</c> to pause execution
/// for that many <em>centiseconds</em> (hundredths of a second), then ends the run. It has no business
/// logic, no file I/O, and no database access — it is a pure timing/throttle step used to space out other
/// batch steps. // source: COBSWAIT.cbl:1-6, COBSWAIT.cbl:34-40
/// </summary>
/// <remarks>
/// <para>Ported as a single straight-line method (<see cref="Run"/>): the COBOL PROCEDURE DIVISION has
/// <b>no named paragraphs</b> — four statements executed top to bottom with no PERFORM and no GO TO.
/// Statement order is preserved with <c>// source: COBSWAIT.cbl:NNN</c> citations.</para>
/// <para>Per <c>_design/ARCHITECTURE.md</c>: there is <b>nothing to translate to the relational layer</b>.
/// No <c>FILE-CONTROL</c>, no <c>FD</c>, no SELECT, no table is touched. The repository contract does not
/// apply. <c>SYSIN</c> is delivered through an injectable text source (mirroring <c>SYSIN DD *</c> inline
/// data); the <c>MVSWAIT</c> call is delegated to an injectable <see cref="IWaiter"/> so tests can run
/// instantly.</para>
/// <para>FAITHFUL BUGS preserved verbatim (see <c>_design/faithful-bugs.md</c>, COBSWAIT §6):
/// <list type="number">
/// <item><b>No input validation / no numeric check.</b> <c>PARM-VALUE</c> (X(8)) is moved straight into a
/// numeric <c>9(8) COMP</c> field with no <c>IS NUMERIC</c> test. Non-numeric/blank/short input is NOT
/// rejected; the raw de-edit-and-truncate result is used as the centisecond count.
/// // source: COBSWAIT.cbl:31, COBSWAIT.cbl:36-37</item>
/// <item><b>Reads from SYSIN, not PARM.</b> The JCL banner and FUNCTION comment say "PARM", but the value
/// is consumed via <c>ACCEPT ... FROM SYSIN</c>. The SYSIN-stream source is kept (not a CLI arg).
/// // source: COBSWAIT.cbl:36, WAITSTEP.jcl:20-26</item>
/// <item><b>Silent overflow / truncation on MOVE.</b> <c>9(8)</c> holds at most 99,999,999 centiseconds;
/// excess high-order digits are silently dropped (truncate toward zero, no rounding, no overflow check).
/// // source: COBSWAIT.cbl:30, COBSWAIT.cbl:37</item>
/// <item><b>Hard dependency on external MVSWAIT.</b> The actual timing is delegated to a non-COBOL system
/// routine not present in the repository; its return code is not consumed. // source: COBSWAIT.cbl:38,
/// WAITSTEP.jcl:23</item>
/// </list></para>
/// </remarks>
public sealed class Cobswait
{
    // 01 MVSWAIT-TIME PIC 9(8) COMP — unsigned binary count, range 0..99,999,999. // source: COBSWAIT.cbl:30
    private const int MvswaitTimeDigits = 8;

    // 01 PARM-VALUE PIC X(8). // source: COBSWAIT.cbl:31
    private const int ParmValueWidth = 8;

    /// <summary>
    /// Abstraction over <c>CALL 'MVSWAIT' USING MVSWAIT-TIME</c>: blocks the task for the given number of
    /// centiseconds (hundredths of a second). Injectable so tests can run without a real wall-clock delay.
    /// </summary>
    public interface IWaiter
    {
        /// <summary>Wait for <paramref name="centiseconds"/> centiseconds (cs × 10 ms).</summary>
        void Wait(int centiseconds);
    }

    /// <summary>
    /// Production <see cref="IWaiter"/>: a real blocking delay of <c>centiseconds × 10</c> milliseconds,
    /// the assumed semantics of the mainframe <c>MVSWAIT</c> system service. // source: COBSWAIT.cbl:38
    /// </summary>
    public sealed class RealWaiter : IWaiter
    {
        public static readonly RealWaiter Instance = new();

        public void Wait(int centiseconds)
        {
            // MVSWAIT pauses for the centisecond count; a 9(8) COMP is unsigned, so the count is >= 0.
            if (centiseconds > 0)
                Thread.Sleep(TimeSpan.FromMilliseconds((long)centiseconds * 10L));
        }
    }

    /// <summary>
    /// No-op <see cref="IWaiter"/> that records the requested centisecond count but does not sleep. Use it
    /// for tests/characterization and for the .NET port where a real wall-clock throttle is unnecessary.
    /// </summary>
    public sealed class RecordingWaiter : IWaiter
    {
        /// <summary>The most recent centisecond count passed to <see cref="Wait"/> (null until called).</summary>
        public int? LastCentiseconds { get; private set; }

        /// <summary>True once <see cref="Wait"/> has been invoked.</summary>
        public bool WasCalled { get; private set; }

        public void Wait(int centiseconds)
        {
            WasCalled = true;
            LastCentiseconds = centiseconds;
        }
    }

    /// <summary>The MVSWAIT-TIME centisecond count derived from the SYSIN card (set by <see cref="Run"/>).</summary>
    public int MvswaitTime { get; private set; }

    private Cobswait() { }

    /// <summary>
    /// Runs COBSWAIT over the SYSIN card <paramref name="sysin"/>, deriving the centisecond count and
    /// invoking <paramref name="waiter"/> exactly as the COBOL does. Returns the resulting program state
    /// (the derived <see cref="MvswaitTime"/>).
    /// </summary>
    /// <param name="sysin">
    /// The SYSIN inline data (the first logical record / card). Mirrors <c>SYSIN DD *</c>; only the first 8
    /// characters are used (X(8) receiving). A short/blank/non-numeric card is processed faithfully (no
    /// validation — faithful bug #1).
    /// </param>
    /// <param name="waiter">
    /// The <c>MVSWAIT</c> implementation to call (defaults to <see cref="RealWaiter"/>, a real cs × 10 ms
    /// delay). Pass a <see cref="RecordingWaiter"/> to run instantly.
    /// </param>
    /// <param name="host">
    /// Host encoding used to de-edit the X(8) characters when moving into the numeric field (defaults to
    /// EBCDIC, the mainframe form). Affects only the zone-strip of non-digit bytes (faithful bug #1).
    /// </param>
    public static Cobswait Run(string sysin, IWaiter? waiter = null, HostKind host = HostKind.Ebcdic)
    {
        waiter ??= RealWaiter.Instance;
        var program = new Cobswait();
        program.Execute(sysin ?? string.Empty, waiter, host);
        return program;
    }

    // =================================================================================================
    // PROCEDURE DIVISION (single unnamed straight-line body). // source: COBSWAIT.cbl:34-40
    // =================================================================================================
    private void Execute(string sysin, IWaiter waiter, HostKind host)
    {
        // ACCEPT PARM-VALUE FROM SYSIN. // source: COBSWAIT.cbl:36
        // Read the first logical record (card) from the SYSIN stream into PARM-VALUE (X(8)): take the
        // first 8 chars; if shorter, COBOL left-justifies and space-fills to width 8.
        string parmValue = AcceptParmValueFromSysin(sysin);

        // MOVE PARM-VALUE TO MVSWAIT-TIME. // source: COBSWAIT.cbl:37
        // Alphanumeric (X(8)) -> numeric (9(8) COMP) move: de-edit each character (zone-strip to its low
        // nibble), right-justify into the 8-digit receiver, truncate toward zero with silent high-order
        // overflow. No IS NUMERIC test, no validation (faithful bug #1, #3).
        MvswaitTime = MoveAlphanumericToNumeric(parmValue, host);

        // CALL 'MVSWAIT' USING MVSWAIT-TIME. // source: COBSWAIT.cbl:38
        // Pass the binary centisecond count BY REFERENCE; MVSWAIT only reads it (treated as input).
        waiter.Wait(MvswaitTime);

        // STOP RUN. // source: COBSWAIT.cbl:40
        // Return cleanly from the batch step (process/step exit code 0 on normal completion).
    }

    /// <summary>
    /// ACCEPT ... FROM SYSIN into PIC X(8): take the first <see cref="ParmValueWidth"/> characters of the
    /// first SYSIN card; if the card is shorter, left-justify and space-fill to width 8. // source:
    /// COBSWAIT.cbl:36
    /// </summary>
    private static string AcceptParmValueFromSysin(string sysin)
    {
        // The SYSIN "card" is the first logical record (line) of the stream. ACCEPT reads one record.
        int nl = sysin.IndexOfAny(['\r', '\n']);
        string card = nl >= 0 ? sysin[..nl] : sysin;

        // PIC X(8) receiving: left-justify, space-pad / right-truncate to exactly 8 characters.
        return card.Length >= ParmValueWidth
            ? card[..ParmValueWidth]
            : card.PadRight(ParmValueWidth, ' ');
    }

    /// <summary>
    /// MOVE PARM-VALUE (X(8)) TO MVSWAIT-TIME (9(8) COMP): the COBOL alphanumeric-to-numeric move. Each of
    /// the 8 source characters is de-edited (zone-stripped to the low nibble of its host byte), the 8
    /// digits are assembled into an unsigned integer, then stored into the 8-digit receiver with
    /// truncate-toward-zero and silent high-order overflow (no rounding, no sign, no validation). Faithful
    /// bugs #1 and #3. // source: COBSWAIT.cbl:37
    /// </summary>
    private static int MoveAlphanumericToNumeric(string parmValue, HostKind host)
    {
        // Source X(8) and receiver 9(8) are both 8 digits wide; the move is digit-for-digit (no shift).
        byte[] bytes = HostEncoding.For(host).GetBytes(parmValue);

        decimal assembled = 0m;
        for (int i = 0; i < MvswaitTimeDigits; i++)
        {
            // De-edit: a numeric DISPLAY/alphanumeric MOVE keeps only the digit nibble (low 4 bits) of
            // each byte. For '0'-'9' this yields 0-9; for a space (EBCDIC 0x40 / ASCII 0x20) it yields 0;
            // for other characters it yields whatever the low nibble holds (the faithful, undefined-ish
            // result the mainframe move would produce — no validation, no rejection).
            int digit = i < bytes.Length ? bytes[i] & 0x0F : 0;
            assembled = (assembled * 10m) + digit;
        }

        // Store into 9(8) COMP: unsigned, scale 0, 8 integer digits. Truncate toward zero + silent
        // high-order overflow (modulo 10^8) per the Runtime decimal helper.
        decimal stored = Decimals.Store(assembled, integerDigits: MvswaitTimeDigits, scale: 0, signed: false);
        return (int)stored;
    }
}
