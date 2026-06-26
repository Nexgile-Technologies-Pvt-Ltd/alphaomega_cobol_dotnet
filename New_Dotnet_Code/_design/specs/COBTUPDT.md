# PORT SPEC — COBTUPDT (Batch DB2 Transaction-Type Maintenance)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/cbl/COBTUPDT.cbl`
Target: `New_Dotnet_Code/src/CardDemo.Db2/COBTUPDT.cs` (one class over the EF Core context / repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH + DB2** (optional Transaction-Type module). No screen, no CICS, no COMMAREA, no BMS map.

---

## 1. Purpose

COBTUPDT is a batch maintenance utility for the DB2 reference table `CARDDEMO.TRANSACTION_TYPE`.
It opens a sequential input file (`INPFILE`), reads it record-by-record, and for each record uses
the first byte (`INPUT-REC-TYPE`) as an **action code** to drive an embedded static-SQL operation
against the table: `A` = INSERT, `U` = UPDATE (description only), `D` = DELETE, `*` = ignore
(comment line), anything else = ABEND. The record's columns 2–3 carry the transaction-type code
(`TR_TYPE`, the primary key) and columns 4–53 carry the transaction description (`TR_DESCRIPTION`).
On any SQL error (negative SQLCODE), or update/delete that matches no rows (SQLCODE +100), or an
invalid action code, it builds an error message, displays it, sets RETURN-CODE 4, and continues —
see Faithful Bugs for the "ABEND that does not actually stop" behavior.
// source: COBTUPDT.cbl:2-4, 109-129, 132-226

**How invoked:** JCL job `MNTTRDB2` (member `jcl/MNTTRDB2.jcl`), single step
**`//STEP1 EXEC PGM=IKJEFT01,REGION=0M`**, which runs TSO batch terminal monitor `IKJEFT01`; the
`SYSTSIN` stream issues `DSN SYSTEM(DAZ1)` then `RUN PROGRAM(COBTUPDT) PLAN(CARDDEMO)`. So COBTUPDT
runs under the DB2 attach with plan `CARDDEMO`. // source: jcl/MNTTRDB2.jcl:21-30
The input dataset is supplied via `//INPFILE DD DSN=INPFILE,DISP=SHR`. // source: jcl/MNTTRDB2.jcl:27
It is a top-level batch main (`PROCEDURE DIVISION` with no `USING`); it terminates via `STOP RUN`
(or sets `RETURN-CODE` and falls through). // source: COBTUPDT.cbl:80, 99, 232

**Input record format** (per JCL header comments and the COBOL layout): column 1 = action
(`A`/`D`/`U`/`*`); columns 2–3 = transaction type (the JCL comment says "NUMERIC VALUE" but the
COBOL host variable is `PIC X(2)` and the column is `CHAR(2)` — treat it as a 2-char code, NOT a
parsed integer); columns 4–53 = transaction description (50 chars). // source: jcl/MNTTRDB2.jcl:11-18; COBTUPDT.cbl:71-77

---

## 2. FILE / TABLE access

| COBOL object | DD / Table | ORG / type | Relational target (ARCHITECTURE.md) | Operations in this program | SQL / repository mapping |
|---|---|---|---|---|---|
| `TR-RECORD` (SELECT `TR-RECORD ASSIGN TO INPFILE`) | `INPFILE` | QSAM **SEQUENTIAL**, ACCESS SEQUENTIAL, RECFM F, FILE STATUS `WS-INF-STATUS` | **NOT a base table** — a sequential input dataset (53-byte fixed records). Read it as a fixed-width input stream (one record = action+type+desc). | OPEN INPUT; `READ ... NEXT RECORD INTO WS-INPUT-REC` until AT END; CLOSE | Sequential file read; map to an `IEnumerable<string>`/record reader over the input dataset. EOF sets `LASTREC='Y'`. // source: COBTUPDT.cbl:31-34, 39, 83, 101-103, 235 |
| `CARDDEMO.TRANSACTION_TYPE` (DCLGEN `DCLTRTYP`) | DB2 table | relational table, PK `TR_TYPE` | **TRANSACTION_TYPE** (per ARCHITECTURE.md optional-module table; from `TRNTYPE.ddl`): `TR_TYPE CHAR(2)` PK, `TR_DESCRIPTION VARCHAR(50)`. Unique index `XTRAN_TYPE` on `TR_TYPE`. | static embedded SQL: **INSERT**, **UPDATE** (set desc), **DELETE** | INSERT→`INSERT INTO TRANSACTION_TYPE(...)`; UPDATE→`UPDATE ... SET TR_DESCRIPTION WHERE TR_TYPE`; DELETE→`DELETE ... WHERE TR_TYPE`. See §3 for exact statements + SQLCODE handling. // source: COBTUPDT.cbl:50-54, 137-148, 171-175, 201-204; ddl/TRNTYPE.ddl:1-4; ddl/XTRNTYPE.ddl:1-5 |

**Notes on the input file:** declared `FD TR-RECORD RECORDING MODE F` with a 53-byte `01 WS-INPUT-VARS`
record (X(1)+X(2)+X(50)). But the program does **not** read into `WS-INPUT-VARS`; it reads
`INTO WS-INPUT-REC` (an identically-laid-out working-storage copy: `INPUT-REC-TYPE` X(1),
`INPUT-REC-NUMBER` X(2), `INPUT-REC-DESC` X(50)). The FD record `WS-INPUT-VARS` /
`INPUT-TYPE`/`INPUT-TR-NUMBER`/`INPUT-TR-DESC` is effectively dead (never referenced after the READ).
// source: COBTUPDT.cbl:39-46, 71-77, 101

**DB2 type handling (DCLGEN):** `TR_DESCRIPTION` is `VARCHAR(50)`, generated as a group with a
2-byte length half-word `DCL-TR-DESCRIPTION-LEN PIC S9(4) COMP` + `DCL-TR-DESCRIPTION-TEXT PIC X(50)`.
**However the program never uses the DCLGEN host structure** — it binds the file fields
`:INPUT-REC-NUMBER` (X(2)) and `:INPUT-REC-DESC` (X(50)) directly as host variables. Because
`:INPUT-REC-DESC` is a fixed `PIC X(50)` (not a VARCHAR group), DB2 inserts/updates the full
50-character field **including trailing spaces** padded to the field width. The .NET port must
store the description as the full 50-char fixed-width string (trailing spaces preserved) to match.
// source: COBTUPDT.cbl:54, 76, 145-146, 173; dcl/DCLTRTYP.dcl:28-46

---

## 3. PARAGRAPH-BY-PARAGRAPH outline (each paragraph = one method)

Procedure flow note: the program has **no top-level driver paragraph that PERFORMs the open**. The
first paragraph executed is `0001-OPEN-FILES` (fall-through entry), but nothing PERFORMs
`1001-READ-NEXT-RECORDS`. With COBOL fall-through semantics, after `0001-OPEN-FILES` runs and hits
its `EXIT`, control falls into `1001-READ-NEXT-RECORDS` (paragraphs execute top-to-bottom unless a
PERFORM/GO TO/STOP intervenes). `EXIT` is just a no-op return point, not a stop. So the de-facto
main loop is: open → read-loop → close → STOP RUN. The .NET port should make this explicit as a
`Run()` that calls Open, then ReadNextRecords (which loops), then CloseStop. // source: COBTUPDT.cbl:80-99

1. **`0001-OPEN-FILES`** — `OPEN INPUT TR-RECORD`. If `WS-INF-STATUS = '00'` DISPLAY `'OPEN FILE OK'`
   else DISPLAY `'OPEN FILE NOT OK'`. `EXIT`. (No abend on open failure — it continues regardless;
   see Faithful Bugs.) // source: COBTUPDT.cbl:82-89

2. **`1001-READ-NEXT-RECORDS`** — driver loop: PERFORM `1002-READ-RECORDS` once (priming read), then
   `PERFORM UNTIL LASTREC = 'Y'`: PERFORM `1003-TREAT-RECORD`, PERFORM `1002-READ-RECORDS`,
   END-PERFORM. After loop: PERFORM `2001-CLOSE-STOP`. Then `EXIT.` and `STOP RUN.`
   (The `STOP RUN` is the real program terminator; the `EXIT` before it is a no-op.)
   // source: COBTUPDT.cbl:91-99

3. **`1002-READ-RECORDS`** — `READ TR-RECORD NEXT RECORD INTO WS-INPUT-REC`; `AT END MOVE 'Y' TO
   LASTREC`. If `LASTREC NOT EQUAL TO 'Y'` DISPLAY `'PROCESSING   '` followed by `WS-INPUT-REC`.
   `EXIT`. // source: COBTUPDT.cbl:100-107

4. **`1003-TREAT-RECORD`** — `EVALUATE INPUT-REC-TYPE` (the first byte / action code):
   - `WHEN 'A'` → DISPLAY `'ADDING RECORD'`, PERFORM `10031-INSERT-DB`.
   - `WHEN 'U'` → DISPLAY `'UPDATING RECORD'`, PERFORM `10032-UPDATE-DB`.
   - `WHEN 'D'` → DISPLAY `'DELETING RECORD'`, PERFORM `10033-DELETE-DB`.
   - `WHEN '*'` → DISPLAY `'IGNORING COMMENTED LINE'` (no DB action).
   - `WHEN OTHER` → `STRING 'ERROR: TYPE NOT VALID' DELIMITED BY SIZE INTO WS-RETURN-MSG`, PERFORM
     `9999-ABEND`.
   `END-EVALUATE`. `EXIT`. (Action code is case-sensitive uppercase; lowercase `a`/`u`/`d` fall to
   `WHEN OTHER` and abend.) // source: COBTUPDT.cbl:109-130

5. **`10031-INSERT-DB`** — `EXEC SQL INSERT INTO CARDDEMO.TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION)
   VALUES (:INPUT-REC-NUMBER, :INPUT-REC-DESC) END-EXEC`. `MOVE SQLCODE TO WS-VAR-SQLCODE`.
   `EVALUATE TRUE`: `WHEN SQLCODE = ZERO` → DISPLAY `'RECORD INSERTED SUCCESSFULLY'`;
   `WHEN SQLCODE < 0` → STRING `'Error accessing:'` + `' TRANSACTION_TYPE table. SQLCODE:'` +
   `WS-VAR-SQLCODE` DELIMITED BY SIZE INTO `WS-RETURN-MSG`, PERFORM `9999-ABEND`. `EXIT`.
   (No explicit handling of SQLCODE +803 duplicate-key — a dup PK is `<0`, so it falls into the
   error branch. There is NO COMMIT in this program; commits are governed by the DB2 attach /
   IKJEFT01 implicit commit at normal end — see Port Notes.) // source: COBTUPDT.cbl:132-164

6. **`10032-UPDATE-DB`** — `EXEC SQL UPDATE CARDDEMO.TRANSACTION_TYPE SET TR_DESCRIPTION =
   :INPUT-REC-DESC WHERE TR_TYPE = :INPUT-REC-NUMBER END-EXEC`. `MOVE SQLCODE TO WS-VAR-SQLCODE`.
   `EVALUATE TRUE`: `WHEN SQLCODE = ZERO` → DISPLAY `'RECORD UPDATED SUCCESSFULLY'`;
   `WHEN SQLCODE = +100` → STRING `'No records found.'` INTO `WS-RETURN-MSG`, PERFORM `9999-ABEND`;
   `WHEN SQLCODE < 0` → STRING error message (as in INSERT) INTO `WS-RETURN-MSG`, PERFORM
   `9999-ABEND`. `EXIT`. (Only `TR_DESCRIPTION` is updated; `TR_TYPE` is the WHERE key, never
   changed.) // source: COBTUPDT.cbl:166-195

7. **`10033-DELETE-DB`** — `EXEC SQL DELETE FROM CARDDEMO.TRANSACTION_TYPE WHERE TR_TYPE =
   :INPUT-REC-NUMBER END-EXEC`. `MOVE SQLCODE TO WS-VAR-SQLCODE`. `EVALUATE TRUE`:
   `WHEN SQLCODE = ZERO` → DISPLAY `'RECORD DELETED SUCCESSFULLY'`; `WHEN SQLCODE = +100` →
   STRING `'No records found.'` INTO `WS-RETURN-MSG`, PERFORM `9999-ABEND`; `WHEN SQLCODE < 0` →
   STRING error message INTO `WS-RETURN-MSG`, PERFORM `9999-ABEND`. `EXIT`.
   // source: COBTUPDT.cbl:196-226

8. **`9999-ABEND`** — `DISPLAY WS-RETURN-MSG`; `MOVE 4 TO RETURN-CODE`; `EXIT`. **This does NOT stop
   the run** — it only sets the program return code to 4 and returns to the caller (the EVALUATE
   that PERFORMed it), after which processing continues with the next record. See Faithful Bugs.
   // source: COBTUPDT.cbl:230-233

9. **`2001-CLOSE-STOP`** — `CLOSE TR-RECORD`; `EXIT`. (Despite the name, it does not itself STOP RUN;
   the STOP RUN is in `1001-READ-NEXT-RECORDS` after the close.) // source: COBTUPDT.cbl:234-236

---

## 4. SQL → relational-access translation plan (SQLite / EF Core)

Target table `TRANSACTION_TYPE` (ARCHITECTURE.md): `TR_TYPE` TEXT PK (CHAR(2)), `TR_DESCRIPTION` TEXT (VARCHAR(50)).

| COBOL EXEC SQL | .NET / SQLite equivalent | Result-code mapping |
|---|---|---|
| INSERT INTO TRANSACTION_TYPE(TR_TYPE,TR_DESCRIPTION) VALUES(:n,:d) | `INSERT INTO TRANSACTION_TYPE (TR_TYPE, TR_DESCRIPTION) VALUES (@n, @d)` | success → SQLCODE 0; PK/unique violation → SQLCODE < 0 (DB2 -803); any other error → < 0. |
| UPDATE TRANSACTION_TYPE SET TR_DESCRIPTION=:d WHERE TR_TYPE=:n | `UPDATE TRANSACTION_TYPE SET TR_DESCRIPTION=@d WHERE TR_TYPE=@n` | rows affected > 0 → SQLCODE 0; rows affected = 0 → SQLCODE +100 ("No records found."); error → < 0. |
| DELETE FROM TRANSACTION_TYPE WHERE TR_TYPE=:n | `DELETE FROM TRANSACTION_TYPE WHERE TR_TYPE=@n` | rows affected > 0 → SQLCODE 0; rows affected = 0 → SQLCODE +100; error → < 0. |

**SQLCODE emulation:** the port must synthesize a DB2-style SQLCODE from the SQLite/EF outcome so the
branching matches: 0 = success-with-rows, +100 = statement OK but zero rows affected (UPDATE/DELETE
no-match), negative = exception (constraint violation, etc.). Store the synthesized code in a field
that maps `WS-VAR-SQLCODE` (DB2 `INTEGER SQLCODE`, displayed edited as `PIC ----9`). The `:n` host
var is the 2-char `INPUT-REC-NUMBER`; the `:d` host var is the 50-char `INPUT-REC-DESC` with trailing
spaces. // source: COBTUPDT.cbl:65, 137-148, 149, 171-175, 201-204

---

## 5. VALIDATION RULES and exact literal messages

There is **no field-content validation** beyond the action-code dispatch — the program does not
range-check the type code or the description; it just hands the bytes to DB2. The only "validation"
is the EVALUATE on the action byte.

Exact literal strings emitted (verbatim — preserve spacing exactly):

DISPLAY (informational, SYSOUT):
- `'OPEN FILE OK'` // source: COBTUPDT.cbl:85
- `'OPEN FILE NOT OK'` // source: COBTUPDT.cbl:87
- `'PROCESSING   '` (three trailing spaces) followed by the 53-byte `WS-INPUT-REC` // source: COBTUPDT.cbl:105
- `'ADDING RECORD'` // source: COBTUPDT.cbl:112
- `'UPDATING RECORD'` // source: COBTUPDT.cbl:115
- `'DELETING RECORD'` // source: COBTUPDT.cbl:118
- `'IGNORING COMMENTED LINE'` // source: COBTUPDT.cbl:121
- `'RECORD INSERTED SUCCESSFULLY'` // source: COBTUPDT.cbl:153
- `'RECORD UPDATED SUCCESSFULLY'` // source: COBTUPDT.cbl:179
- `'RECORD DELETED SUCCESSFULLY'` // source: COBTUPDT.cbl:209

Error messages built into `WS-RETURN-MSG` (PIC X(80)) then DISPLAYed by `9999-ABEND`:
- `'ERROR: TYPE NOT VALID'` (invalid action code) // source: COBTUPDT.cbl:124
- `'Error accessing:'` + `' TRANSACTION_TYPE table. SQLCODE:'` + edited `WS-VAR-SQLCODE`
  (concatenated DELIMITED BY SIZE → `Error accessing: TRANSACTION_TYPE table. SQLCODE:<code>`).
  Used by INSERT, UPDATE, DELETE on SQLCODE < 0. // source: COBTUPDT.cbl:155-161, 186-192, 217-223
- `'No records found.'` (UPDATE/DELETE with SQLCODE +100) // source: COBTUPDT.cbl:181-182, 211-213

**`WS-VAR-SQLCODE` edit picture is `PIC ----9`** — a floating-minus numeric edit, 5 positions: a
non-negative code renders with leading spaces (e.g. `    0`, `  100`); a negative code shows the
floating minus immediately left of the magnitude (e.g. `  -803`). The concatenation appends this
5-char edited field directly after `SQLCODE:` with no extra space. The .NET port must reproduce this
edited format (Runtime `CobolEditedNumeric` with mask `----9`). // source: COBTUPDT.cbl:65, 158, 189, 220

---

## 6. FAITHFUL BUGS to reproduce (do NOT fix)

1. **`9999-ABEND` is a misnomer — it does not abend or stop.** It only DISPLAYs the message and
   `MOVE 4 TO RETURN-CODE`, then `EXIT` (returns to caller). After an "ABEND" on one bad record, the
   loop **continues to the next record**; the program does not terminate early. The final return code
   will be 4 if any error occurred, but all subsequent records are still processed. Reproduce: errors
   set RC=4 and keep going. // source: COBTUPDT.cbl:230-233

2. **No COMMIT / ROLLBACK anywhere.** The program issues no `EXEC SQL COMMIT`/`ROLLBACK`. On the
   mainframe, IKJEFT01/DB2 performs an implicit commit at normal program end and an implicit rollback
   on abnormal end; but since `9999-ABEND` never abends, a unit of work containing a failed statement
   followed by successful statements is committed together at STOP RUN. The port should mirror this:
   do not commit per-record; commit once at end-of-run (and note there is no per-record rollback of
   prior successful rows even when a later record errors). // source: COBTUPDT.cbl:132-226, 99

3. **Open-failure is ignored.** `0001-OPEN-FILES` checks `WS-INF-STATUS` only to choose a DISPLAY
   message; on a non-`'00'` status it still falls through into the read loop and attempts to READ a
   file that may not be open (undefined/implementation-defined behavior; typically AT END or a file
   status error). No abend, no RC set on open failure. Reproduce: open failure → 'OPEN FILE NOT OK'
   then proceed. // source: COBTUPDT.cbl:82-89, 91-92

4. **Dead FD record / dead host structure.** The FD record `WS-INPUT-VARS`
   (`INPUT-TYPE`/`INPUT-TR-NUMBER`/`INPUT-TR-DESC`) is never used (READ is `INTO WS-INPUT-REC`), and
   the DCLGEN host struct `DCLTRANSACTION-TYPE` from `DCLTRTYP` is never referenced. Harmless but
   preserve the fact that binding uses the file-image fields, not the DCLGEN VARCHAR group — this is
   why descriptions are stored as fixed 50-char (space-padded), not trimmed VARCHARs. // source: COBTUPDT.cbl:40-46, 71-77, 101; dcl/DCLTRTYP.dcl:35-46

5. **Case-sensitive action codes.** Only uppercase `A`/`U`/`D`/`*` are recognized; any other byte
   (including lowercase variants, blanks, or digits) goes to `WHEN OTHER` → `'ERROR: TYPE NOT VALID'`
   → "abend" (RC=4) and continues. // source: COBTUPDT.cbl:110-129

6. **JCL says columns 2–3 are a "NUMERIC VALUE" but the host variable is `PIC X(2)` / column is
   `CHAR(2)`.** No numeric validation or conversion happens; non-numeric type codes are accepted and
   stored verbatim. Treat `TR_TYPE` as an opaque 2-char string. // source: jcl/MNTTRDB2.jcl:16; COBTUPDT.cbl:74, 145; dcl/DCLTRTYP.dcl:29

---

## 7. PORT NOTES (semantics, gotchas)

- **Module placement:** per ARCHITECTURE.md this is the optional DB2 Transaction-Type module → target
  `src/CardDemo.Db2/`, using the same EF Core context with the `TRANSACTION_TYPE` table
  (`TR_TYPE` TEXT PK, `TR_DESCRIPTION` TEXT). // source: ARCHITECTURE.md (optional-module tables, TRNTYPE.ddl)
- **Fall-through control flow:** there is no PERFORM that starts the run from `1001-READ-NEXT-RECORDS`;
  COBOL executes paragraphs in source order, so `0001-OPEN-FILES` then falls into the read loop. Make
  the .NET `Run()` explicitly call Open → ReadLoop → CloseStop to preserve the effective order. Do not
  introduce a paragraph that would change which records are processed. // source: COBTUPDT.cbl:80-99
- **Priming read pattern:** standard read-ahead — one read before the loop, then process+read inside
  the loop, terminating when `LASTREC='Y'`. EOF detection maps to enumerator-exhausted on the input
  stream. // source: COBTUPDT.cbl:91-96, 101-103
- **Host-variable widths:** `:INPUT-REC-NUMBER` = exactly 2 chars; `:INPUT-REC-DESC` = exactly 50
  chars including trailing spaces. Bind/store the full fixed widths; do NOT trim. The `TR_DESCRIPTION`
  column is `VARCHAR(50)`, but because the host var is `PIC X(50)`, DB2 stores 50 chars (right-padded
  with spaces) — preserve trailing spaces in the SQLite TEXT value. // source: COBTUPDT.cbl:145-146, 173; dcl/DCLTRTYP.dcl:45-46
- **SQLCODE branching is the only logic that matters** (0 / +100 / <0). Synthesize SQLCODE from the
  EF Core outcome: rows-affected and exception type. INSERT has no +100 branch (insert of 0 rows is
  not a normal DB2 outcome); UPDATE and DELETE both treat 0 rows-affected as +100. // source: COBTUPDT.cbl:151-163, 177-194, 207-225
- **Edited numeric `PIC ----9`:** render `WS-VAR-SQLCODE` with the Runtime `CobolEditedNumeric` floating-minus
  mask to embed in the error message; width 5 (leading spaces for non-negative). // source: COBTUPDT.cbl:65, 158
- **No STRING/UNSTRING subtlety beyond concatenation:** all `STRING ... DELIMITED BY SIZE` calls just
  concatenate fixed literals (and one edited number) into `WS-RETURN-MSG` (X(80)); `WS-RETURN-MSG` is
  NOT reset to spaces between uses, but since these messages are only built immediately before being
  DISPLAYed by `9999-ABEND`, and each STRING starts at position 1, residual tail bytes from a previous
  longer message could linger — but in practice messages are short and the field is X(80); reproduce by
  STRING-ing from position 1 without clearing (faithful), padding the remainder with whatever was there.
  Simplest faithful approach: build into an 80-char buffer initialized to spaces only at program start
  (matches `VALUE SPACES`), then overwrite from position 1 on each STRING and leave the rest. // source: COBTUPDT.cbl:61-62, 123-127, 155-161
- **RETURN-CODE:** map to the process/job step return code; 0 normally, 4 if any record errored. // source: COBTUPDT.cbl:232
- **Transaction boundary:** see Faithful Bug #2 — single unit of work, committed at end of run, no
  per-record commit or rollback.

---

## 8. OPEN QUESTIONS / risks

- **DB2 -803 (duplicate key) on INSERT** is not specially handled; it falls into the generic
  `SQLCODE < 0` error branch. The port's SQLite unique-constraint violation must therefore surface as a
  negative SQLCODE (not +100) so it routes to the "Error accessing" message, not "No records found."
  // source: COBTUPDT.cbl:154-162
- **Implicit commit semantics under IKJEFT01:** the exact commit/rollback timing on the mainframe
  depends on the DB2 attach (TSO/DSN RUN). We model it as commit-once-at-normal-end. If golden
  fixtures show partial-failure rollback behavior, revisit. // source: jcl/MNTTRDB2.jcl:29-30
- **Input record length / DD:** JCL `//INPFILE DD DSN=INPFILE,DISP=SHR` does not state LRECL; the
  COBOL FD is RECFM F with a 53-byte record. Assume 53-byte (or ≥53) fixed records; columns beyond 53
  (if any) are ignored. // source: jcl/MNTTRDB2.jcl:27; COBTUPDT.cbl:39-46
- **Sibling programs** in this module (`COTRTUPC`/`COTRTLIC` online, plus the `TRANSACTION_TYPE_CATEGORY`
  table via `DCLTRCAT`/`TRNTYCAT.ddl`) are out of scope for COBTUPDT; COBTUPDT touches only
  `TRANSACTION_TYPE`. // source: README.md:36-47
