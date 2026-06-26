# PORT SPEC — CBCUS01C (Customer File Reader / Printer)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBCUS01C.cbl`
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBCUS01C.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA, no online/BMS.

---

## 1. Purpose

CBCUS01C reads the **CUSTOMER master** (a VSAM **KSDS** indexed file, opened SEQUENTIAL and accessed
low-key-to-high-key, here represented by the relational `CUSTOMER` table) record by record and
**DISPLAYs** each customer record to SYSOUT. It performs no writes, updates, deletes, computation,
or transformation — it is a pure read-and-print utility/demonstration job that exercises sequential
KSDS reads, file-status checking, and an extended IO-status display routine. // source: CBCUS01C.cbl:1-6, 70-87

**How invoked:** JCL job `READCUST.jcl`, step **`STEP05  EXEC PGM=CBCUS01C`**.
// source: jcl/READCUST.jcl:21
DD-to-file binding (from JCL): `CUSTFILE` DD → `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` (the customer KSDS,
input, `DISP=SHR`). // source: jcl/READCUST.jcl:24-25
`SYSOUT`/`SYSPRINT` DDs route the `DISPLAY` output. // source: jcl/READCUST.jcl:26-27
Not called as a subprogram; it is a top-level `GOBACK` batch main. // source: CBCUS01C.cbl:87

---

## 2. FILE / TABLE access

| COBOL file (SELECT) | DD | ORG/ACCESS | Relational target (ARCHITECTURE.md) | Operations in this program | SQL mapping |
|---|---|---|---|---|---|
| `CUSTFILE-FILE` | CUSTFILE | **INDEXED**, **ACCESS SEQUENTIAL**, RECORD KEY `FD-CUST-ID`, FILE STATUS `CUSTFILE-STATUS` | **CUSTOMER** table (PK `cust_id` 9(9)) | OPEN INPUT; sequential READ (READNEXT semantics) `INTO CUSTOMER-RECORD`; CLOSE | `SELECT * FROM CUSTOMER ORDER BY cust_id` forward cursor; each READ = next row; EOF when cursor exhausted → file status `'10'`. // source: CBCUS01C.cbl:29-33, 93, 120, 138 |

**Only relational access:** `CUSTFILE` ↔ base table `CUSTOMER`. There are **no output files**, no
other tables, no alt-index reads, no STARTBR/READPREV, no WRITE/REWRITE/DELETE. The sole VSAM
operation set is OPEN INPUT / sequential READ / CLOSE. // source: CBCUS01C.cbl:29-33, 93, 120, 138

### Record layout (CUSTOMER-RECORD, RECLN 500)

`COPY CVCUS01Y` provides `CUSTOMER-RECORD` (the typed record READ INTO). // source: CBCUS01C.cbl:45; cpy/CVCUS01Y.cpy:4-23
Field list (maps 1:1 to the **CUSTOMER** table columns per ARCHITECTURE.md):
- CUST-ID 9(09) → `cust_id` (PK)
- CUST-FIRST-NAME X(25), CUST-MIDDLE-NAME X(25), CUST-LAST-NAME X(25)
- CUST-ADDR-LINE-1/2/3 X(50) each
- CUST-ADDR-STATE-CD X(02), CUST-ADDR-COUNTRY-CD X(03), CUST-ADDR-ZIP X(10)
- CUST-PHONE-NUM-1 X(15), CUST-PHONE-NUM-2 X(15)
- CUST-SSN 9(09), CUST-GOVT-ISSUED-ID X(20)
- CUST-DOB-YYYY-MM-DD X(10), CUST-EFT-ACCOUNT-ID X(10)
- CUST-PRI-CARD-HOLDER-IND X(01), CUST-FICO-CREDIT-SCORE 9(03)
- FILLER X(168) (reconstructed as spaces on fixed-width serialize; no column). // source: cpy/CVCUS01Y.cpy:5-23

**FD record vs copybook mismatch (note, not a bug):** The FD declares `FD-CUSTFILE-REC` as
`FD-CUST-ID PIC 9(09)` + `FD-CUST-DATA PIC X(491)` = **500 bytes**; the `READ ... INTO
CUSTOMER-RECORD` copies the 500-byte FD area into the typed `CUSTOMER-RECORD` (also 500 = 9 + 491
elementary fields + 168 FILLER). The RECORD KEY for the KSDS is `FD-CUST-ID` (the first 9 digits =
`cust_id`). // source: CBCUS01C.cbl:32, 37-40; cpy/CVCUS01Y.cpy:4-23

---

## 3. WORKING-STORAGE structures that affect logic

- `COPY CVCUS01Y` → `CUSTOMER-RECORD` (see §2). // source: CBCUS01C.cbl:45
- `CUSTFILE-STATUS` (2 chars: `CUSTFILE-STAT1` X, `CUSTFILE-STAT2` X) — VSAM file status pair.
  // source: CBCUS01C.cbl:46-48
- `IO-STATUS` (2 chars: `IO-STAT1` X, `IO-STAT2` X) — copy of file status for the display routine.
  // source: CBCUS01C.cbl:50-52
- `TWO-BYTES-BINARY` PIC 9(4) BINARY, REDEFINED by `TWO-BYTES-ALPHA` (`TWO-BYTES-LEFT` X, `TWO-BYTES-RIGHT` X) — used to convert the 2nd status byte into a number. // source: CBCUS01C.cbl:53-56
- `IO-STATUS-04` (`IO-STATUS-0401` PIC 9 VALUE 0; `IO-STATUS-0403` PIC 999 VALUE 0) — the 4-char
  formatted status display buffer. // source: CBCUS01C.cbl:57-59
- `APPL-RESULT` PIC S9(9) COMP with 88s `APPL-AOK VALUE 0`, `APPL-EOF VALUE 16`. // source: CBCUS01C.cbl:61-63
- `END-OF-FILE` X(01) VALUE 'N'. // source: CBCUS01C.cbl:65
- `ABCODE` PIC S9(9) BINARY, `TIMING` PIC S9(9) BINARY — abend-call parameters. // source: CBCUS01C.cbl:66-67

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM flow preserved.

### MAIN (unnamed PROCEDURE DIVISION body) // source: CBCUS01C.cbl:70-87
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBCUS01C'`. // source: CBCUS01C.cbl:71
2. PERFORM `0000-CUSTFILE-OPEN` (open, abend on non-`'00'`). // source: CBCUS01C.cbl:72
3. `PERFORM UNTIL END-OF-FILE = 'Y'`: inside, `IF END-OF-FILE = 'N'` → PERFORM
   `1000-CUSTFILE-GET-NEXT`; then `IF END-OF-FILE = 'N'` → `DISPLAY CUSTOMER-RECORD`. (The inner
   `IF`s are redundant with the loop condition but must be reproduced.) // source: CBCUS01C.cbl:74-81
4. PERFORM `9000-CUSTFILE-CLOSE`. // source: CBCUS01C.cbl:83
5. `DISPLAY 'END OF EXECUTION OF PROGRAM CBCUS01C'`. // source: CBCUS01C.cbl:85
6. `GOBACK`. // source: CBCUS01C.cbl:87

   **Note — double DISPLAY of each record:** Each successful read DISPLAYs `CUSTOMER-RECORD` **twice**
   — once inside `1000-CUSTFILE-GET-NEXT` (line 96) and once in the MAIN loop (line 78). See Faithful
   Bugs #1. // source: CBCUS01C.cbl:78, 96

### 1000-CUSTFILE-GET-NEXT // source: CBCUS01C.cbl:92-116
1. `READ CUSTFILE-FILE INTO CUSTOMER-RECORD` (sequential next; copies the FD area into the typed
   record). // source: CBCUS01C.cbl:93
2. IF `CUSTFILE-STATUS = '00'`: MOVE 0 → APPL-RESULT; `DISPLAY CUSTOMER-RECORD`. // source: CBCUS01C.cbl:94-96
3. ELSE IF `CUSTFILE-STATUS = '10'` (EOF): MOVE 16 → APPL-RESULT; ELSE MOVE 12 → APPL-RESULT.
   // source: CBCUS01C.cbl:97-103
4. IF `APPL-AOK` (=0) → CONTINUE; ELSE IF `APPL-EOF` (=16) → MOVE 'Y' → END-OF-FILE; ELSE →
   `DISPLAY 'ERROR READING CUSTOMER FILE'`, MOVE CUSTFILE-STATUS → IO-STATUS, PERFORM
   `Z-DISPLAY-IO-STATUS`, PERFORM `Z-ABEND-PROGRAM`. // source: CBCUS01C.cbl:104-115
5. `EXIT`. // source: CBCUS01C.cbl:116

### 0000-CUSTFILE-OPEN // source: CBCUS01C.cbl:118-134
1. MOVE 8 → APPL-RESULT. // source: CBCUS01C.cbl:119
2. `OPEN INPUT CUSTFILE-FILE`. // source: CBCUS01C.cbl:120
3. IF `CUSTFILE-STATUS = '00'` → MOVE 0 → APPL-RESULT; ELSE MOVE 12 → APPL-RESULT. // source: CBCUS01C.cbl:121-125
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR OPENING CUSTFILE'`, MOVE CUSTFILE-STATUS →
   IO-STATUS, PERFORM `Z-DISPLAY-IO-STATUS`, PERFORM `Z-ABEND-PROGRAM`. // source: CBCUS01C.cbl:126-133
5. `EXIT`. // source: CBCUS01C.cbl:134

### 9000-CUSTFILE-CLOSE // source: CBCUS01C.cbl:136-152
1. `ADD 8 TO ZERO GIVING APPL-RESULT` (→ APPL-RESULT = 8). // source: CBCUS01C.cbl:137
2. `CLOSE CUSTFILE-FILE`. // source: CBCUS01C.cbl:138
3. IF `CUSTFILE-STATUS = '00'` → `SUBTRACT APPL-RESULT FROM APPL-RESULT` (→ 0); ELSE
   `ADD 12 TO ZERO GIVING APPL-RESULT` (→ 12). // source: CBCUS01C.cbl:139-143
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR CLOSING CUSTOMER FILE'`, MOVE CUSTFILE-STATUS →
   IO-STATUS, PERFORM `Z-DISPLAY-IO-STATUS`, PERFORM `Z-ABEND-PROGRAM`. // source: CBCUS01C.cbl:144-151
5. `EXIT`. // source: CBCUS01C.cbl:152

   Arithmetic note: `ADD 8 TO ZERO GIVING APPL-RESULT`, `SUBTRACT APPL-RESULT FROM APPL-RESULT`, and
   `ADD 12 TO ZERO GIVING APPL-RESULT` are roundabout ways to set APPL-RESULT to 8, 0, and 12
   respectively (S9(9) COMP, no truncation/overflow possible for these small values). Reproduce as
   plain integer assignments. // source: CBCUS01C.cbl:137, 140, 142

### Z-ABEND-PROGRAM // source: CBCUS01C.cbl:154-158
`DISPLAY 'ABENDING PROGRAM'`; MOVE 0 → TIMING; MOVE 999 → ABCODE; `CALL 'CEE3ABD' USING ABCODE,
TIMING`. Port: throw an `Abend(999)` (Runtime.Abend), terminating the batch with code 999.
(No `EXIT` — this paragraph never returns.) // source: CBCUS01C.cbl:154-158

### Z-DISPLAY-IO-STATUS // source: CBCUS01C.cbl:160-174
Formats the 2-char file status into a 4-digit `'FILE STATUS IS: NNNN'` line:
- IF `IO-STATUS NOT NUMERIC` **OR** `IO-STAT1 = '9'`: MOVE IO-STAT1 → IO-STATUS-04(1:1); MOVE 0 →
  TWO-BYTES-BINARY; MOVE IO-STAT2 → TWO-BYTES-RIGHT (low byte of the 9(4) BINARY); MOVE
  TWO-BYTES-BINARY → IO-STATUS-0403 (PIC 999); `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. This
  converts the binary value of the second status byte into a 3-digit number (the classic Micro Focus
  extended-status display idiom). // source: CBCUS01C.cbl:162-168
- ELSE: MOVE '0000' → IO-STATUS-04; MOVE IO-STATUS → IO-STATUS-04(3:2) (status in positions 3-4);
  `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBCUS01C.cbl:169-173
- `EXIT`. // source: CBCUS01C.cbl:174

---

## 5. VALIDATION RULES & exact literal messages

There is **no** business-field validation; the only "validation" is file-status checking. File-status
accept rules: open/read/close accept `'00'`; reads also treat `'10'` as EOF; any other status →
abend. // source: CBCUS01C.cbl:94, 98, 121, 139

Exact literal strings to reproduce verbatim (SYSOUT / abend):
- `'START OF EXECUTION OF PROGRAM CBCUS01C'` // source: CBCUS01C.cbl:71
- `'END OF EXECUTION OF PROGRAM CBCUS01C'` // source: CBCUS01C.cbl:85
- `'ERROR READING CUSTOMER FILE'` // source: CBCUS01C.cbl:110
- `'ERROR OPENING CUSTFILE'` // source: CBCUS01C.cbl:129
- `'ERROR CLOSING CUSTOMER FILE'` // source: CBCUS01C.cbl:147
- `'ABENDING PROGRAM'` // source: CBCUS01C.cbl:155
- `'FILE STATUS IS: NNNN'` (literal prefix; the trailing `NNNN` is literal text in the DISPLAY,
  immediately followed by the 4-char formatted `IO-STATUS-04` value). // source: CBCUS01C.cbl:168, 172

---

## 6. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **Each customer record is DISPLAYed twice.** `1000-CUSTFILE-GET-NEXT` DISPLAYs `CUSTOMER-RECORD`
   on a `'00'` read (line 96), then the MAIN loop DISPLAYs the same `CUSTOMER-RECORD` again
   (line 78). Every record therefore appears twice in SYSOUT. Reproduce both DISPLAYs in the same
   order. // source: CBCUS01C.cbl:96, 78
2. **`'FILE STATUS IS: NNNN'` literal includes a literal `NNNN`.** The DISPLAY emits the placeholder
   text `NNNN` followed by the actual formatted value, so the line reads e.g.
   `FILE STATUS IS: NNNN0010`. This is the COBOL source as written — keep the literal `NNNN` in the
   output. // source: CBCUS01C.cbl:168, 172
3. **Misleading `IO-STAT1`-only branch in Z-DISPLAY-IO-STATUS.** When the status is non-numeric or
   begins with `'9'`, only `IO-STAT1` is placed in position 1 and the second byte is rendered as a
   raw binary-to-3-digit number; this is an intentional Micro Focus extended-status idiom but yields
   non-obvious output for ordinary statuses. Reproduce the exact byte-juggling, not a "clean"
   formatting. // source: CBCUS01C.cbl:162-168

(No data-corruption, stale-buffer, or arithmetic bugs exist in this program — it only reads and
displays.)

---

## 7. PORT NOTES (relational-access + tricky COBOL semantics)

**CUSTOMER read (the only access):** OPEN INPUT + sequential READ over an INDEXED KSDS = forward
ordered cursor over the `CUSTOMER` table: `SELECT ... FROM CUSTOMER ORDER BY cust_id` (PK ascending).
Each `1000-CUSTFILE-GET-NEXT` = `MoveNext()` on that cursor; exhausted cursor = file status `'10'` →
APPL-EOF → END-OF-FILE='Y'. Map repository `ReadNext` per the VSAM→SQL contract (STARTBR+READNEXT →
ORDER BY key forward). No writes/updates/deletes. // source: ARCHITECTURE.md §"VSAM-semantics"; CBCUS01C.cbl:93, 120, 138

**File-status modeling:** the repository must surface `'00'` (ok), `'10'` (EOF) on the read path;
OPEN/CLOSE return `'00'` on success. Any other status drives the abend path. Per ARCHITECTURE.md the
SQLite `IVsamFile` returns these FileStatus codes. // source: ARCHITECTURE.md §"VSAM-semantics -> SQL mapping"

**`READ ... INTO`:** copies the FD record area into `CUSTOMER-RECORD`. In the relational port the
repository materializes a typed `Customer` POCO directly; the `INTO` is just "current row → record
object". No byte-image copy is needed at runtime (string fields carry their full fixed width per the
type map). // source: CBCUS01C.cbl:93

**`DISPLAY CUSTOMER-RECORD`:** DISPLAYs the entire 500-byte record as a single concatenated string
(all elementary fields rendered back-to-back: zoned-display for numeric `CUST-ID`/`CUST-SSN`/
`CUST-FICO-CREDIT-SCORE`, raw text for X-fields, plus the trailing 168-byte FILLER as spaces). If
SYSOUT is part of the golden fixture, the port must reproduce this exact fixed-width concatenation
(signed-zoned numeric rendering — though these three numeric fields are *unsigned* `9(n)`, so they
render as plain digits with no overpunch). // source: CBCUS01C.cbl:78, 96; cpy/CVCUS01Y.cpy:5-23

**`REDEFINES` (TWO-BYTES-BINARY / TWO-BYTES-ALPHA):** model as a 2-byte buffer (PIC 9(4) BINARY =
16-bit halfword) with a low-byte view (`TWO-BYTES-RIGHT`). `MOVE 0 TO TWO-BYTES-BINARY` then
`MOVE IO-STAT2 TO TWO-BYTES-RIGHT` puts the ASCII/EBCDIC byte of the status char into the low byte;
`MOVE TWO-BYTES-BINARY TO IO-STATUS-0403` renders it as a 0..255 number in PIC 999. Endianness:
mainframe is big-endian, so `TWO-BYTES-RIGHT` (the second/right byte) is the **low-order** byte → the
numeric value equals the raw byte value of `IO-STAT2`. Port as `(int)(byte)IO-STAT2` rendered "%03d".
// source: CBCUS01C.cbl:53-56, 165-167

**`Z-DISPLAY-IO-STATUS` two branches:** on the non-numeric/'9' branch emit
`"FILE STATUS IS: NNNN" + IO-STAT1 + (byteValue of IO-STAT2 as 3 digits)` (4 chars after the
literal); on the numeric branch emit `"FILE STATUS IS: NNNN" + "00" + IO-STATUS` (the 2-char status
in positions 3-4 of `IO-STATUS-04`). // source: CBCUS01C.cbl:162-173

**`INITIALIZE`:** none used in this program.

**`CALL 'CEE3ABD'`:** LE abend service → map to `Runtime.Abend(999)` (code 999, timing 0). // source: CBCUS01C.cbl:156-158

**Roundabout arithmetic in 9000-CUSTFILE-CLOSE:** `ADD 8 TO ZERO GIVING`, `SUBTRACT X FROM X`,
`ADD 12 TO ZERO GIVING` are just constant assignments (8 / 0 / 12) — port as direct assignments;
no truncation or sign concern (small positive ints into S9(9) COMP). // source: CBCUS01C.cbl:137-143

---

## 8. OPEN QUESTIONS / RISKS

1. **Is SYSOUT part of the golden fixture?** The program's only output is the `DISPLAY` of each
   record (twice — bug #1). If the batch-characterization harness diffs SYSOUT, the exact fixed-width
   concatenation of `CUSTOMER-RECORD` (including trailing FILLER spaces and the doubled lines) must
   be reproduced byte-for-byte; otherwise SYSOUT can be best-effort. Needs confirmation against
   `tests/golden/` for this job. // source: CBCUS01C.cbl:78, 96
2. **`Z-DISPLAY-IO-STATUS` is unreachable on the happy path.** It runs only on a non-`'00'`/non-`'10'`
   status (open/read/close error). Its exact output format (especially the binary-byte branch) is
   hard to golden without an induced error; pin it with a unit test that feeds a synthetic status
   (e.g. `'9I'` or `'23'`). // source: CBCUS01C.cbl:160-174
3. **No CLOSE/abend ordering issue.** Unlike CBACT01C, this program opens and closes exactly one
   file, with the close in `9000-CUSTFILE-CLOSE`; no missing-CLOSE bug. (Stated for contrast.)
   // source: CBCUS01C.cbl:118-152
4. **`CUSTOMER-RECORD` numeric fields are unsigned `9(n)`** (`CUST-ID`, `CUST-SSN`,
   `CUST-FICO-CREDIT-SCORE`) — no sign overpunch on DISPLAY, plain digits, zero-padded to PIC width.
   Confirm the import/seed preserves leading zeros so the DISPLAY concatenation width is exact.
   // source: cpy/CVCUS01Y.cpy:5, 17, 22
