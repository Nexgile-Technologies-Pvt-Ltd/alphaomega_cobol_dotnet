# PORT SPEC — CBACT03C (Card Cross-Reference File Reader / Printer)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT03C.cbl`
Copybook: `app/cpy/CVACT03Y.cpy` (CARD-XREF-RECORD, RECLN 50)
JCL: `app/jcl/READXREF.jcl`
Target table (relational, per ARCHITECTURE.md): **CARD_XREF**
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBACT03C.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA. (online program: **NO**.)

---

## 1. Purpose

CBACT03C is a stand-alone **BATCH** utility whose sole function is to read the **card cross-reference
master file** (`XREFFILE`, a VSAM KSDS keyed on the 16-byte card number) **sequentially from low key
to high key** and `DISPLAY` each cross-reference record to SYSOUT. It opens the file, loops reading
the next record until end-of-file, displays each xref record, then closes the file and ends with
`GOBACK`. It performs no updates, no computations, no filtering, no derived output files — it is a
pure "read and print account cross reference data file" report.
// source: CBACT03C.cbl:1-5 (header — "Function: Read and print account cross reference data file")
// source: CBACT03C.cbl:70-87 (mainline: open / loop / close / GOBACK)

**How it is invoked:** Submitted as JCL job `READXREF`, step **`STEP05  EXEC PGM=CBACT03C`**.
The `XREFFILE` DD points at `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` (DISP=SHR). `SYSOUT` and `SYSPRINT`
go to spool. It is **not** a called subprogram and **not** a CICS transaction; it ends with `GOBACK`
to the operating system.
// source: jcl/READXREF.jcl:22 (//STEP05 EXEC PGM=CBACT03C)
// source: jcl/READXREF.jcl:25-26 (//XREFFILE DD ... CARDXREF.VSAM.KSDS)
// source: jcl/READXREF.jcl:27-28 (//SYSOUT, //SYSPRINT DD SYSOUT=*)
// source: CBACT03C.cbl:87 (GOBACK)

This program is structurally a near-clone of CBACT02C (card master reader) and CBACT01C, with the
**XREFFILE** swapped in for the card/account file. One notable difference from those siblings is that
each record is **DISPLAYed twice** (see §4 and §7 Faithful Bugs).

---

## 2. FILE / TABLE access

| COBOL file (DD) | Org / Access | Record key | Relational table (ARCHITECTURE.md) | Operations used | Maps to (relational repository) |
|---|---|---|---|---|---|
| `XREFFILE-FILE` (DD `XREFFILE`) | INDEXED (KSDS), **ACCESS SEQUENTIAL** | `FD-XREF-CARD-NUM` PIC X(16) | **CARD_XREF** (PK `xref_card_num` X16; idx `acct_id`) | OPEN INPUT; sequential READ (READNEXT semantics); CLOSE | Open a forward, ordered read cursor on `CARD_XREF` by PK `xref_card_num` ASC; `ReadNext()` advances; Close/dispose. |

// source: CBACT03C.cbl:29-33 (SELECT XREFFILE-FILE ASSIGN TO XREFFILE / ORGANIZATION INDEXED / ACCESS SEQUENTIAL / RECORD KEY FD-XREF-CARD-NUM / FILE STATUS XREFFILE-STATUS)
// source: CBACT03C.cbl:37-40 (FD XREFFILE-FILE / FD-XREFFILE-REC: FD-XREF-CARD-NUM X(16), FD-XREF-DATA X(34) = 50 bytes)

### Operation -> SQL mapping
Because `ACCESS MODE IS SEQUENTIAL` on an `INDEXED` file, a bare `READ` returns records **in
primary-key (card_num) ascending order**. There is no random keyed read, no STARTBR with explicit
key, no READPREV, and no WRITE/REWRITE/DELETE in this program. Per the ARCHITECTURE.md VSAM→SQL
contract, STARTBR+READNEXT maps to `ORDER BY key, forward cursor`.

- **OPEN INPUT XREFFILE-FILE** → open a forward, ordered read cursor:
  `SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF ORDER BY xref_card_num ASC;`
  FileStatus `'00'` on success; anything else → APPL-RESULT 12 → abend.
  // source: CBACT03C.cbl:120 (OPEN INPUT XREFFILE-FILE), 121-125
- **READ XREFFILE-FILE INTO CARD-XREF-RECORD** → cursor `ReadNext()`:
  fetch next row, materialize into `CARD-XREF-RECORD` fields. FileStatus `'00'` on a row,
  **`'10'`** at end-of-file (no more rows) → APPL-EOF. Any other status is a hard error → abend.
  // source: CBACT03C.cbl:93 (READ ... INTO CARD-XREF-RECORD), 94-103 (status handling)
- **CLOSE XREFFILE-FILE** → dispose cursor. FileStatus `'00'` expected; else abend.
  // source: CBACT03C.cbl:138 (CLOSE XREFFILE-FILE), 139-143

> NOTE on the relational pivot: the program reads the file via `READ ... INTO CARD-XREF-RECORD`,
> where `CARD-XREF-RECORD` is the CVACT03Y copybook (50 bytes incl. a trailing 14-byte FILLER). The
> FD record `FD-XREFFILE-REC` is `16 + 34 = 50` bytes. In the relational port, the `CARD_XREF` table
> holds only the three elementary CVACT03Y columns (`xref_card_num`, `cust_id`, `acct_id`); the
> 14-byte FILLER and the FD/copybook split are reconstructed only when re-serializing the row to its
> canonical 50-byte fixed-width image for the verification harness.

---

## 3. WORKING-STORAGE structures that affect logic

- `COPY CVACT03Y` → **`CARD-XREF-RECORD`** (the typed xref record that `READ ... INTO` targets and
  that `DISPLAY` prints). Layout (RECLN 50): // source: cpy/CVACT03Y.cpy:4-8
  - `XREF-CARD-NUM`  PIC X(16)  → CARD_XREF.`xref_card_num` (PK)
  - `XREF-CUST-ID`   PIC 9(09)  → CARD_XREF.`cust_id`  (unsigned, `int`/`long`; ARCHITECTURE maps 9(9) → long via CUSTOMER, but as a 9-digit value `int` overflows at 9 digits — store as `long`/`int` per table def; column type INTEGER)
  - `XREF-ACCT-ID`   PIC 9(11)  → CARD_XREF.`acct_id` (unsigned 11-digit → `long`, INTEGER)
  - `FILLER`         PIC X(14)  → dropped (reconstructed as 14 spaces on fixed-width serialize)
- **`XREFFILE-STATUS`** (group of `XREFFILE-STAT1` X(1) + `XREFFILE-STAT2` X(1)) — the 2-char file
  status compared against `'00'` / `'10'`. // source: CBACT03C.cbl:46-48
- **`IO-STATUS`** (`IO-STAT1` X(1) + `IO-STAT2` X(1)) — receives the file status for the display
  routine. // source: CBACT03C.cbl:50-52
- **`TWO-BYTES-BINARY`** PIC 9(4) BINARY, REDEFINED by **`TWO-BYTES-ALPHA`** (`TWO-BYTES-LEFT` X(1) +
  `TWO-BYTES-RIGHT` X(1)) — used to convert the second status byte into a number in
  `9910-DISPLAY-IO-STATUS`. // source: CBACT03C.cbl:53-56
- **`IO-STATUS-04`** (`IO-STATUS-0401` PIC 9 VALUE 0 + `IO-STATUS-0403` PIC 999 VALUE 0) — the 4-digit
  rendered status used in the `'FILE STATUS IS: NNNN'` line. // source: CBACT03C.cbl:57-59
- **`APPL-RESULT`** PIC S9(9) COMP with 88-levels **`APPL-AOK VALUE 0`**, **`APPL-EOF VALUE 16`**.
  // source: CBACT03C.cbl:61-63
- **`END-OF-FILE`** PIC X(01) VALUE `'N'` — loop sentinel. // source: CBACT03C.cbl:65
- **`ABCODE`** PIC S9(9) BINARY, **`TIMING`** PIC S9(9) BINARY — args to the `CEE3ABD` abend call.
  // source: CBACT03C.cbl:66-67

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM flow are preserved.

### MAIN (unnamed PROCEDURE DIVISION body) // source: CBACT03C.cbl:70-87
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBACT03C'`. // source: CBACT03C.cbl:71
2. PERFORM `0000-XREFFILE-OPEN` (open input; abend on non-`'00'`). // source: CBACT03C.cbl:72
3. `PERFORM UNTIL END-OF-FILE = 'Y'`: // source: CBACT03C.cbl:74-81
   - IF `END-OF-FILE = 'N'`: PERFORM `1000-XREFFILE-GET-NEXT`; then IF still `END-OF-FILE = 'N'`,
     `DISPLAY CARD-XREF-RECORD`. (The inner `IF END-OF-FILE = 'N'` guards are redundant with the loop
     condition but must be reproduced. NOTE: this is the **second** DISPLAY of the record — the first
     happens inside `1000-XREFFILE-GET-NEXT`; see §7.) // source: CBACT03C.cbl:75-80
4. PERFORM `9000-XREFFILE-CLOSE`. // source: CBACT03C.cbl:83
5. `DISPLAY 'END OF EXECUTION OF PROGRAM CBACT03C'`. // source: CBACT03C.cbl:85
6. `GOBACK`. // source: CBACT03C.cbl:87

### 1000-XREFFILE-GET-NEXT // source: CBACT03C.cbl:92-116
1. `READ XREFFILE-FILE INTO CARD-XREF-RECORD` (sequential next; copies the 50-byte FD record into the
   copybook record). // source: CBACT03C.cbl:93
2. IF `XREFFILE-STATUS = '00'`: MOVE 0 → APPL-RESULT; **`DISPLAY CARD-XREF-RECORD`** (first display).
   // source: CBACT03C.cbl:94-96
3. ELSE IF `XREFFILE-STATUS = '10'` (EOF): MOVE 16 → APPL-RESULT; ELSE MOVE 12 → APPL-RESULT.
   // source: CBACT03C.cbl:97-103
4. IF `APPL-AOK` (=0) → CONTINUE; // source: CBACT03C.cbl:104-105
   ELSE IF `APPL-EOF` (=16) → MOVE `'Y'` → END-OF-FILE; // source: CBACT03C.cbl:107-108
   ELSE → `DISPLAY 'ERROR READING XREFFILE'`, MOVE `XREFFILE-STATUS` → IO-STATUS,
   PERFORM `9910-DISPLAY-IO-STATUS`, PERFORM `9999-ABEND-PROGRAM`. // source: CBACT03C.cbl:109-114
5. `EXIT`. // source: CBACT03C.cbl:116

### 0000-XREFFILE-OPEN // source: CBACT03C.cbl:118-134
1. MOVE 8 → APPL-RESULT (priming value so AOK is false unless OPEN succeeds). // source: CBACT03C.cbl:119
2. `OPEN INPUT XREFFILE-FILE`. // source: CBACT03C.cbl:120
3. IF status `'00'` → MOVE 0 → APPL-RESULT; ELSE → MOVE 12 → APPL-RESULT. // source: CBACT03C.cbl:121-125
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR OPENING XREFFILE'`, MOVE status → IO-STATUS,
   PERFORM `9910-DISPLAY-IO-STATUS`, PERFORM `9999-ABEND-PROGRAM`. // source: CBACT03C.cbl:126-133
5. `EXIT`. // source: CBACT03C.cbl:134

### 9000-XREFFILE-CLOSE // source: CBACT03C.cbl:136-152
1. `ADD 8 TO ZERO GIVING APPL-RESULT` (→ APPL-RESULT = 8; priming). // source: CBACT03C.cbl:137
2. `CLOSE XREFFILE-FILE`. // source: CBACT03C.cbl:138
3. IF status `'00'` → `SUBTRACT APPL-RESULT FROM APPL-RESULT` (→ 0, i.e. AOK);
   ELSE → `ADD 12 TO ZERO GIVING APPL-RESULT` (→ 12). // source: CBACT03C.cbl:139-143
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR CLOSING XREFFILE'`, MOVE status → IO-STATUS,
   PERFORM `9910-DISPLAY-IO-STATUS`, PERFORM `9999-ABEND-PROGRAM`. // source: CBACT03C.cbl:144-151
5. `EXIT`. // source: CBACT03C.cbl:152

### 9999-ABEND-PROGRAM // source: CBACT03C.cbl:154-158
`DISPLAY 'ABENDING PROGRAM'`; MOVE 0 → TIMING; MOVE 999 → ABCODE;
`CALL 'CEE3ABD' USING ABCODE, TIMING`.
Port: throw an `Abend(999)` (Runtime.Abend) that terminates the batch run with code 999 (no return).
// source: CBACT03C.cbl:155-158

### 9910-DISPLAY-IO-STATUS // source: CBACT03C.cbl:161-174
Formats the 2-char file status into the 4-digit `'FILE STATUS IS: NNNN'` line:
- IF `IO-STATUS NOT NUMERIC` **OR** `IO-STAT1 = '9'`: // source: CBACT03C.cbl:162-163
  - MOVE `IO-STAT1` → `IO-STATUS-04(1:1)` (first rendered digit = first status char). // source: CBACT03C.cbl:164
  - MOVE 0 → `TWO-BYTES-BINARY`; MOVE `IO-STAT2` → `TWO-BYTES-RIGHT` (places the second status byte
    into the LOW byte of the 9(4) BINARY); MOVE `TWO-BYTES-BINARY` → `IO-STATUS-0403` (PIC 999). This
    converts the raw byte value of the second status char into a 3-digit number — the classic
    Micro Focus / extended-status display idiom. // source: CBACT03C.cbl:165-167
  - `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBACT03C.cbl:168
- ELSE (normal 2-digit numeric status): MOVE `'0000'` → IO-STATUS-04; MOVE `IO-STATUS` →
  `IO-STATUS-04(3:2)` (status in positions 3-4); `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`.
  // source: CBACT03C.cbl:169-173
- `EXIT`. // source: CBACT03C.cbl:174

**Byte-order caveat:** `TWO-BYTES-RIGHT` is the **second** (rightmost) byte of the 2-byte BINARY.
On a big-endian (mainframe) 9(4) BINARY (halfword), the rightmost byte is the low-order byte, so the
rendered number = `(int)(unsigned byte)IO-STAT2`. The .NET port must reproduce big-endian semantics
(treat IO-STAT2's character code as the numeric value, 0..255, rendered "%03d") regardless of host
endianness. // source: CBACT03C.cbl:53-56, 165-167

---

## 5. VALIDATION RULES & exact literal messages

There is **no business-field validation** in this program; the only "validation" is file-status
checking. Exact literal strings to reproduce verbatim on SYSOUT / at abend:

- `'START OF EXECUTION OF PROGRAM CBACT03C'` // source: CBACT03C.cbl:71
- `'END OF EXECUTION OF PROGRAM CBACT03C'` // source: CBACT03C.cbl:85
- `'ERROR READING XREFFILE'` // source: CBACT03C.cbl:110
- `'ERROR OPENING XREFFILE'` // source: CBACT03C.cbl:129
- `'ERROR CLOSING XREFFILE'` // source: CBACT03C.cbl:147
- `'ABENDING PROGRAM'` // source: CBACT03C.cbl:155
- `'FILE STATUS IS: NNNN'` (literal prefix; followed by the 4-char formatted `IO-STATUS-04`).
  // source: CBACT03C.cbl:168, 172
- Plus the raw `DISPLAY CARD-XREF-RECORD` lines (the 50-byte record image; see §7/§8 for rendering).
  // source: CBACT03C.cbl:78, 96

File-status accept rules: OPEN/CLOSE/READ accept `'00'`; READ additionally treats `'10'` as EOF
(→ APPL-EOF → END-OF-FILE='Y'); any other status on any operation → APPL-RESULT 12 → abend.
// source: CBACT03C.cbl:94-103, 121-125, 139-143

---

## 6. ARITHMETIC / COMPUTE notes

There are **no COMPUTE statements** and **no business arithmetic** in CBACT03C. The only arithmetic
is on the integer control variable `APPL-RESULT` (PIC S9(9) COMP) and is used purely as a
success/EOF/error flag:
- `MOVE 0 / 16 / 12 / 8 → APPL-RESULT` (flag assignments). // source: CBACT03C.cbl:95,99,101,119,122,124
- `ADD 8 TO ZERO GIVING APPL-RESULT` → 8. // source: CBACT03C.cbl:137
- `SUBTRACT APPL-RESULT FROM APPL-RESULT` → 0 (deliberately zero on close-success). // source: CBACT03C.cbl:140
- `ADD 12 TO ZERO GIVING APPL-RESULT` → 12. // source: CBACT03C.cbl:142
None of these can truncate or overflow (values 0,8,12,16 fit S9(9)); no sign or scaling concerns.
The only PIC-driven numeric formatting is in `9910-DISPLAY-IO-STATUS` (§4) — `IO-STATUS-0403` PIC 999
zero-pads the byte value to 3 digits; `IO-STATUS-04(3:2)` places the 2-char status. No rounding.

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **Each record is DISPLAYed TWICE.** `1000-XREFFILE-GET-NEXT` does `DISPLAY CARD-XREF-RECORD` when
   status = '00' (line 96), and then the mainline loop ALSO does `DISPLAY CARD-XREF-RECORD` after the
   PERFORM (line 78). So every cross-reference record appears two consecutive times on SYSOUT. This
   is a genuine duplicate-output bug present in CBACT03C (the sibling CBACT02C displays only once).
   The .NET port must emit each record's display **twice, back-to-back**, to be byte-faithful if
   SYSOUT is part of the golden fixture. // source: CBACT03C.cbl:96 and CBACT03C.cbl:78

2. **`9910-DISPLAY-IO-STATUS` second-byte rendering depends on raw byte value / endianness.** On the
   non-numeric / `IO-STAT1='9'` branch, the second status char is reinterpreted as the low byte of a
   halfword binary and printed as a 0..255 number. This is intentional mainframe behavior but is a
   portability hazard: the result is correct only with big-endian halfword semantics. Reproduce the
   big-endian result (value = character code of IO-STAT2), NOT a little-endian misread. (Not a "fix"
   target — preserve the mainframe-equivalent output.) // source: CBACT03C.cbl:53-56, 162-167

3. **Redundant inner `IF END-OF-FILE = 'N'` guards** in the mainline loop (lines 75 and 77) duplicate
   the `PERFORM UNTIL END-OF-FILE = 'Y'` condition. Harmless but reproduce the control structure
   as-is (do not "simplify" it away). // source: CBACT03C.cbl:74-80

> No data-mutation, stale-buffer, or magic-constant bugs exist here (unlike CBACT01C) — CBACT03C is a
> pure reader. The duplicate DISPLAY (#1) is the salient faithful bug.

---

## 8. PORT NOTES (relational-access + tricky COBOL semantics)

**CARD_XREF read (the only relational access):** `OPEN INPUT` + sequential `READ` over an INDEXED
KSDS = a forward ordered cursor over the `CARD_XREF` table:
`SELECT xref_card_num, cust_id, acct_id FROM CARD_XREF ORDER BY xref_card_num ASC`. Each
`1000-XREFFILE-GET-NEXT` = `MoveNext()`/`ReadNext()` on that cursor; an exhausted cursor → file
status `'10'` → APPL-EOF → `END-OF-FILE = 'Y'`. Map the repository `ReadNext` per the VSAM→SQL
contract (STARTBR+READNEXT → ORDER BY key forward). There are **no** writes/updates/deletes and no
alt-index access in this program. // source: ARCHITECTURE.md §"VSAM-semantics"; CBACT03C.cbl:93, 120

**Key ordering / collation:** the PK is `XREF-CARD-NUM` X(16). Card numbers in CardDemo are ASCII
digits/spaces, where ordinal (byte) ordering coincides with EBCDIC ordering for the subset used, so
`ORDER BY xref_card_num ASC` (ordinal string compare) reproduces the KSDS sequence. Use an ordinal
(case-sensitive, culture-invariant) comparison, not a locale collation. // source: ARCHITECTURE.md §"VSAM-semantics"; CBACT03C.cbl:32, 39

**Record materialization (READ INTO):** the FD record `FD-XREFFILE-REC` (FD-XREF-CARD-NUM X16 +
FD-XREF-DATA X34) is moved into `CARD-XREF-RECORD` (the CVACT03Y copybook). In the relational port,
materialize the three typed columns; the 14-byte trailing FILLER and the FD/copybook split are not
stored, only reconstructed on fixed-width serialize for the verification harness (50-byte image:
16 + 9 + 11 + 14). // source: CBACT03C.cbl:38-40, 93; cpy/CVACT03Y.cpy:4-8

**`DISPLAY CARD-XREF-RECORD`:** displays the whole 50-byte group. For numeric subfields `XREF-CUST-ID`
9(9) and `XREF-ACCT-ID` 9(11) (unsigned DISPLAY usage), COBOL renders them as plain right-justified
zero-padded digit strings (no sign, no edit). `XREF-CARD-NUM` X16 and the 14-byte FILLER render as
their literal characters. If SYSOUT is part of the golden fixture, reproduce the exact 50-character
record image (and remember to emit it twice per record — Faithful Bug #1). If SYSOUT is not captured,
treat the DISPLAYs as informational. // source: CBACT03C.cbl:78, 96; cpy/CVACT03Y.cpy:5-8

**Abend mapping:** `CALL 'CEE3ABD' USING ABCODE, TIMING` with ABCODE=999, TIMING=0 → terminate the
batch with abend code 999 via `Runtime.Abend`. No graceful return; the loop is unreachable past an
abend. // source: CBACT03C.cbl:154-158

**`9910-DISPLAY-IO-STATUS` port:** on the non-numeric/'9' branch render `IO-STAT1` as digit 1 and
`(int)(byte)IO-STAT2` as a 3-digit number (`%03d`); on the numeric branch zero-pad the 2-char status
into positions 3-4 of `'0000'`. See §4 byte-order caveat (big-endian halfword). // source: CBACT03C.cbl:161-173

**REDEFINES:** `TWO-BYTES-ALPHA` over `TWO-BYTES-BINARY` — model as a single 2-byte backing buffer
with a numeric view (halfword, big-endian) and a 2-char view; only the right byte is written. This is
internal to the status-display helper and has no relational/table impact. // source: CBACT03C.cbl:53-56

**No INITIALIZE, OCCURS, STRING/UNSTRING, edited PIC, or signed-zoned business fields** appear in this
program (the numeric fields are unsigned DISPLAY and only printed, never computed).

---

## 9. OPEN QUESTIONS / RISKS

1. **Is SYSOUT part of the golden fixture for this job?** If the characterization harness diffs
   SYSOUT, the exact 50-char `CARD-XREF-RECORD` rendering AND the **double DISPLAY** (Faithful Bug #1)
   must be reproduced precisely. If SYSOUT is not captured for READXREF, the displays can be
   best-effort. Confirm against `tests/golden/` for this job. // source: CBACT03C.cbl:78, 96
2. **Second-byte rendering on the abnormal status branch.** The `'FILE STATUS IS: NNNN'` extended
   branch (`9910`) only triggers on non-numeric or `'9'`-prefixed statuses, which on the happy path
   never occur. If a test deliberately injects such a status, the big-endian byte rendering must be
   matched (§4 caveat, Faithful Bug #2). Pin with a unit test on the status formatter. // source: CBACT03C.cbl:162-167
3. **`XREF-CUST-ID` width vs C# int.** PIC 9(09) holds up to 999,999,999 which fits a signed 32-bit
   `int`; PIC 9(11) `XREF-ACCT-ID` does NOT fit `int` and must be `long`. Ensure the `CARD_XREF`
   entity uses `long` for `acct_id` (and `int`/`long` for `cust_id` consistently with the CUSTOMER
   table) so no overflow on materialize/serialize. // source: cpy/CVACT03Y.cpy:6-7; ARCHITECTURE.md §schema (CARD_XREF)
