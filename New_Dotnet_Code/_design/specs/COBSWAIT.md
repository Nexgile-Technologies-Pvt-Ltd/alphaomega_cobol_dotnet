# PORT SPEC — COBSWAIT (batch wait utility)

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/COBSWAIT.cbl`
JCL:    `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/WAITSTEP.jcl`
Target: `New_Dotnet_Code/src/CardDemo.Batch/` (utility) — see `_design/ARCHITECTURE.md`
Program kind: **util** (batch). No screen, no transaction, no DB2/IMS/MQ.

---

## 1. Purpose

`COBSWAIT` is a trivial mainframe utility batch program whose only job is to **pause execution for a
fixed amount of time** expressed in **centiseconds** (hundredths of a second). It reads an 8-character
parameter from the `SYSIN` data stream, moves it into a binary count field, and calls the external
assembler/system service routine `MVSWAIT` to perform the actual time delay, then ends the run.
The program contains no business logic, no file I/O, and no database access — it is a pure timing/throttle
step used to space out other batch steps.
// source: COBSWAIT.cbl:1-6 (header: "UTILITY PROGRAM TO WAIT (PARM IN CENTISECONDS)")
// source: COBSWAIT.cbl:34-40 (entire PROCEDURE DIVISION)

### How it is invoked

- **As a JCL batch step.** Step `WAIT` runs `PGM=COBSWAIT`. // source: WAITSTEP.jcl:22
- The wait duration comes from the **`SYSIN` DD inline data**, NOT from a JCL `PARM=` keyword.
  The inline card is `00003600` = 3600 centiseconds = **36 seconds**. // source: WAITSTEP.jcl:25-26, WAITSTEP.jcl:20
- `STEPLIB` points at `AWS.M2.CARDDEMO.LOADLIB`; `SYSOUT DD SYSOUT=*`. // source: WAITSTEP.jcl:23-24
- The program in turn **`CALL`s the external subprogram `MVSWAIT`** passing the binary count.
  // source: COBSWAIT.cbl:38

> Note on terminology: despite the JCL comment saying "PARM", the value is delivered through `SYSIN`
> (via `ACCEPT ... FROM SYSIN`), not through the JCL `PARM=` parameter list. // source: COBSWAIT.cbl:36

---

## 2. FILE / TABLE access

**None.** This program performs **no VSAM/QSAM file I/O and no database access.** There is no
`FILE-CONTROL`, no `FD`, no SELECT, and no relational table is touched.

| COBOL resource | Direction | Relational table (per ARCHITECTURE.md) | Operation | SQL |
|---|---|---|---|---|
| `SYSIN` (DD `*` inline data) | input | — (not a data file; control parameter) | `ACCEPT FROM SYSIN` | — |
| `SYSOUT` (DD `SYSOUT=*`) | output | — (spool only; program writes nothing) | — | — |
| `MVSWAIT` (called subprogram) | call | — (system timing service) | `CALL ... USING` | — |

> There is nothing to map onto any of the 11 base-app tables or any optional-module table.
> The repository contract in ARCHITECTURE.md §"VSAM-semantics -> SQL" does not apply here.

---

## 3. WORKING-STORAGE / data layout

| COBOL field | PIC / USAGE | Meaning | C# type (per ARCHITECTURE map) | Notes |
|---|---|---|---|---|
| `MVSWAIT-TIME` | `PIC 9(8) COMP` | wait duration in centiseconds, binary | `int` (INTEGER) | unsigned 9(8) binary; `COMP`/COMP-4 binary fullword; value range 0–99,999,999 cs. // source: COBSWAIT.cbl:30 |
| `PARM-VALUE`   | `PIC X(8)`     | the 8 raw characters read from SYSIN | `string` (8 chars) | display alphanumeric; expected to contain 8 numeric digits. // source: COBSWAIT.cbl:31 |

Note: `9(8) COMP` is a binary integer; `X(8)` is display. The `MOVE PARM-VALUE TO MVSWAIT-TIME`
is an **alphanumeric-to-numeric move** (see §4 / §6 for the exact COBOL semantics and the truncation/
de-edit behavior to preserve).

---

## 4. PARAGRAPH-BY-PARAGRAPH outline

The PROCEDURE DIVISION has **no named paragraphs** — it is a single unnamed top-level body of four
statements executed top to bottom with no PERFORM and no GO TO. Port it as **one method**, e.g.
`COBSWAIT.Run()`, preserving statement order exactly. // source: COBSWAIT.cbl:34-40

`PROCEDURE DIVISION` (single straight-line body) — method `Run()`:
1. `ACCEPT PARM-VALUE FROM SYSIN` — read 8 characters from the SYSIN stream into `PARM-VALUE` (X(8)).
   If fewer than 8 chars are available on the card, COBOL left-justifies and space-fills the remainder.
   // source: COBSWAIT.cbl:36
2. `MOVE PARM-VALUE TO MVSWAIT-TIME` — convert the 8 display characters to the binary `9(8) COMP`
   count. This is an alphanumeric→numeric move: COBOL de-edits the digits, right-justifies into the
   numeric receiver, and **truncates** anything that does not fit the 8 digits (no rounding, no sign).
   // source: COBSWAIT.cbl:37
3. `CALL 'MVSWAIT' USING MVSWAIT-TIME` — invoke the external system timing routine, passing the binary
   centisecond count BY REFERENCE; `MVSWAIT` blocks the task for that many centiseconds.
   // source: COBSWAIT.cbl:38
4. `STOP RUN` — terminate the program (return to the operating system / next JCL step).
   // source: COBSWAIT.cbl:40

There are **no COMPUTE statements** and no arithmetic other than the implicit numeric conversion in the
`MOVE` at line 37.

---

## 5. VALIDATION RULES and literal messages

- **None.** The program performs **no validation** of `PARM-VALUE`, emits **no messages**, displays
  nothing, and has no error handling. There are no literal message strings anywhere in the source.
  // source: COBSWAIT.cbl:34-40

---

## 6. FAITHFUL BUGS / quirks to reproduce verbatim (do NOT fix)

1. **No input validation / no numeric check.** `PARM-VALUE` (`X(8)`) is moved straight into a numeric
   `9(8) COMP` field with no `IS NUMERIC` test. If SYSIN supplies non-numeric characters, spaces, or a
   short/blank card, the alphanumeric→numeric `MOVE` yields an undefined/garbage centisecond count
   (and on the mainframe could produce a data exception inside the move depending on compiler options).
   The port must mirror this: **do not add validation, do not reject bad input** — replicate the raw
   de-edit-and-truncate behavior of the COBOL `MOVE`. // source: COBSWAIT.cbl:31, COBSWAIT.cbl:36-37

2. **Reads from SYSIN, not PARM.** The JCL banner and the program FUNCTION comment both speak of a
   "PARM", but the value is actually consumed via `ACCEPT ... FROM SYSIN`. A naive port that wires the
   wait value to a command-line `PARM`/args would change the contract. Keep the SYSIN-stream source.
   // source: COBSWAIT.cbl:36, WAITSTEP.jcl:20-26

3. **Silent overflow / truncation on `MOVE`.** `9(8)` holds at most 99,999,999 centiseconds. There is
   no overflow check; excess high-order digits from the source are silently dropped per COBOL move
   rules. Preserve truncate-toward-zero, no rounding. // source: COBSWAIT.cbl:30, COBSWAIT.cbl:37

4. **Hard dependency on external `MVSWAIT`.** The actual timing is delegated to a non-COBOL system
   routine `MVSWAIT` that is not in this repository (it lives in the LOADLIB). Its exact semantics
   (centisecond granularity, blocking behavior, return code) are assumed, not defined here.
   // source: COBSWAIT.cbl:38, WAITSTEP.jcl:23

---

## 7. PORT NOTES (relational-access translation plan + COBOL semantics)

### Relational access
- **Nothing to translate.** No file, no table, no DbContext, no repository. This program does not
  participate in the SQLite relational schema at all. The FILE/TABLE access table in §2 is intentionally
  empty.

### Where it lives in the target
- Implement as a small utility class in `src/CardDemo.Batch` (e.g. `CobSwait`), runnable as a batch step
  by the batch dispatcher, consistent with "one class per CB* program over repositories" — here with no
  repositories. // ARCHITECTURE.md "src/CardDemo.Batch"

### COBOL semantics to honor faithfully
- **`ACCEPT ... FROM SYSIN`** → read one logical record (the inline card) from the configured SYSIN
  source. Take the first 8 characters; if the line is shorter, left-justify and pad with spaces to width
  8 (COBOL `X(8)` receiving behavior). The SYSIN source must be injectable (a stream/text reader),
  mirroring the JCL `SYSIN DD *` inline data so tests can feed `"00003600"`. // source: COBSWAIT.cbl:36
- **`MOVE PARM-VALUE TO MVSWAIT-TIME`** (X(8) → 9(8) COMP) → de-edit the 8 characters as an unsigned
  integer, right-justified, truncating toward zero into a 0–99,999,999 range. Use the Runtime
  `CobolDecimal`/numeric-move helper (silent overflow, no rounding) so behavior matches COBOL exactly;
  do **not** throw on non-digit input — replicate whatever the COBOL de-edit produces (the faithful-bug
  rule applies). // source: COBSWAIT.cbl:37
- **`CALL 'MVSWAIT' USING MVSWAIT-TIME`** → model the wait as a delay of `MVSWAIT-TIME` centiseconds
  (`centiseconds * 10` milliseconds). Behind an injectable abstraction (e.g. `IClock`/an `IWaiter`) so
  tests can run instantly without actually sleeping. The argument is passed BY REFERENCE in COBOL, but
  `MVSWAIT` only reads it; treat as input. // source: COBSWAIT.cbl:38
- **`STOP RUN`** → return cleanly from the batch step (process/step exit code 0 on normal completion).
  // source: COBSWAIT.cbl:40
- **No paragraphs, no flow control** → single linear method; do not introduce structure the original
  lacks.

---

## 8. OPEN QUESTIONS / risks

1. **`MVSWAIT` source is absent.** Its return code, exact granularity (is it strictly centiseconds?),
   and whether it can fail are not defined in-repo. The port assumes: wait = `centiseconds × 10 ms`,
   blocking, no meaningful return code consumed. Confirm if a captured fixture or external doc pins this.
   // source: COBSWAIT.cbl:38
2. **Behavior on non-numeric / blank SYSIN.** The exact compiler-dependent result of moving non-digits
   from `X(8)` into `9(8) COMP` should be pinned by a characterization test against the chosen numeric
   helper, since the mainframe behavior here is itself implementation-defined.
3. **Whether this step is needed at all in the .NET port.** A real wall-clock wait may be a no-op in the
   converted system (used originally only to throttle/space mainframe steps). Decide whether to keep an
   actual delay or stub it; default plan keeps a faithful (injectable, test-skippable) delay.

---

## 9. Coverage hooks (for the verification matrix)

- Unit: feed SYSIN `"00003600"` → assert `MVSWAIT-TIME == 3600` and waiter invoked with 3600 cs
  (36 000 ms), via injected waiter (no real sleep). // source: COBSWAIT.cbl:36-38, WAITSTEP.jcl:26
- Unit (faithful-bug): feed short/blank/non-numeric SYSIN → assert it matches the COBOL move result
  and does NOT throw a validation error. // source: COBSWAIT.cbl:36-37
- JCL-step mapping: `WAITSTEP.jcl` step `WAIT` → this utility, SYSIN-sourced. // source: WAITSTEP.jcl:22-26
