# PORT SPEC — CBACT02C (Card Master Reader / Printer)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT02C.cbl`
Copybook: `app/cpy/CVACT02Y.cpy` (CARD-RECORD, RECLN 150)
JCL: `app/jcl/READCARD.jcl`
Target table (relational, per ARCHITECTURE.md): **CARD**

---

## 1. Purpose

CBACT02C is a stand-alone **BATCH** program whose sole function is to read the card master file
(`CARDFILE`, a VSAM KSDS keyed on the 16-byte card number) **sequentially from beginning to end**
and `DISPLAY` each record to SYSOUT. It opens the file, loops reading the next record until end-of-file,
displays each card record, then closes the file. It performs no updates, no computations on the data,
and no filtering — it is a pure "read and print card data file" report.
// source: CBACT02C.cbl:5 (Function: Read and print card data file)
// source: CBACT02C.cbl:70-87 (mainline open / loop / close / GOBACK)

**How it is invoked:** Submitted as JCL job `READCARD`, step **STEP05**, `EXEC PGM=CBACT02C`.
The `CARDFILE` DD points at `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS`. It is not a called subprogram and not a
CICS transaction; it ends with `GOBACK` to the operating system.
// source: READCARD.jcl:22 (//STEP05 EXEC PGM=CBACT02C)
// source: READCARD.jcl:25-26 (//CARDFILE DD ... CARDDATA.VSAM.KSDS)
// source: CBACT02C.cbl:87 (GOBACK)

This is an **online program: NO**. No BMS map, no COMMAREA, no CICS commands.

---

## 2. FILE / TABLE access

| COBOL file | Org / Access | Key | Relational table | COBOL operations | Maps to (relational repository) |
|---|---|---|---|---|---|
| `CARDFILE-FILE` (DD `CARDFILE`) | INDEXED (KSDS), **ACCESS SEQUENTIAL** | `FD-CARD-NUM` PIC X(16) | **CARD** | OPEN INPUT; sequential READ (READ NEXT semantics); CLOSE | Open read cursor ordered by PK `card_num`; `ReadNext()` advances cursor; Close. |

// source: CBACT02C.cbl:29-33 (SELECT CARDFILE-FILE ... ORGANIZATION INDEXED, ACCESS SEQUENTIAL, RECORD KEY FD-CARD-NUM, FILE STATUS CARDFILE-STATUS)
// source: CBACT02C.cbl:37-40 (FD CARDFILE-FILE / FD-CARDFILE-REC: FD-CARD-NUM X(16), FD-CARD-DATA X(134))

### Operation -> SQL mapping
Because ACCESS MODE IS SEQUENTIAL on an INDEXED file, a bare `READ` returns records **in primary-key
(card_num) ascending order**. There is no random keyed read, no STARTBR with explicit key, no READPREV,
no WRITE/REWRITE/DELETE in this program.

- **OPEN INPUT** -> open a forward, ordered read cursor:
  `SELECT card_num, acct_id, cvv_cd, embossed_name, expiration_date, active_status FROM CARD ORDER BY card_num ASC;`
  FileStatus '00' on success.
  // source: CBACT02C.cbl:120 (OPEN INPUT CARDFILE-FILE)
- **READ ... INTO CARD-RECORD** -> cursor `ReadNext()`:
  fetch next row, materialize into the CARD-RECORD fields. FileStatus '00' on a row, **'10'** at end-of-file
  (no more rows). Any other status is treated as a hard error.
  // source: CBACT02C.cbl:93 (READ CARDFILE-FILE INTO CARD-RECORD)
  // source: CBACT02C.cbl:94-103 (status '00' -> AOK; '10' -> EOF(16); else -> 12)
- **CLOSE** -> dispose cursor. FileStatus '00' expected.
  // source: CBACT02C.cbl:138 (CLOSE CARDFILE-FILE)

> NOTE on the relational pivot: the program reads the file via `READ ... INTO CARD-RECORD`, where
> `CARD-RECORD` (the CVACT02Y copybook, 150 bytes incl. a 59-byte FILLER) is the target. The FD record
> `FD-CARDFILE-REC` is 16 + 134 = 150 bytes. In the relational port, the CARD table holds the six
> elementary CVACT02Y columns; the FILLER and the FD/copybook split are reconstructed only when serializing
> a row back to the 150-byte fixed-width image (e.g. for golden-master diffing). See PORT NOTES §7.

### CARD-RECORD layout (CVACT02Y.cpy -> CARD table columns)
| COBOL field | PIC | CARD column | C# type |
|---|---|---|---|
| CARD-NUM | X(16) | card_num (PK) | string(16) |
| CARD-ACCT-ID | 9(11) | acct_id | long |
| CARD-CVV-CD | 9(03) | cvv_cd | int |
| CARD-EMBOSSED-NAME | X(50) | embossed_name | string(50) |
| CARD-EXPIRAION-DATE | X(10) | expiration_date | string(10) |
| CARD-ACTIVE-STATUS | X(01) | active_status | string(1) |
| FILLER | X(59) | (dropped; spaces on serialize) | — |

// source: CVACT02Y.cpy:4-11 (CARD-RECORD elementary items + FILLER X(59))
// source: ARCHITECTURE.md:53-54 (CARD table column contract)

---

## 3. WORKING-STORAGE of interest

- `CARDFILE-STATUS` (group of CARDFILE-STAT1 + CARDFILE-STAT2, 2 bytes) = the file-status field for CARDFILE.
  // source: CBACT02C.cbl:46-48
- `IO-STATUS` (IO-STAT1 + IO-STAT2, 2 bytes) = working copy of the status used by the display routine.
  // source: CBACT02C.cbl:50-52
- `TWO-BYTES-BINARY` PIC 9(4) BINARY, REDEFINEd by `TWO-BYTES-ALPHA` (TWO-BYTES-LEFT X, TWO-BYTES-RIGHT X).
  Used to convert a single status byte into a numeric for the "NNNN" display. // source: CBACT02C.cbl:53-56
- `IO-STATUS-04` = IO-STATUS-0401 PIC 9 + IO-STATUS-0403 PIC 999, both VALUE 0; the 4-char status display.
  // source: CBACT02C.cbl:57-59
- `APPL-RESULT` PIC S9(9) COMP with 88-levels: `APPL-AOK` (VALUE 0), `APPL-EOF` (VALUE 16).
  Result-code convention: 0=ok, 8=pre-op default, 12=hard error, 16=EOF. // source: CBACT02C.cbl:61-63
- `END-OF-FILE` PIC X(01) VALUE 'N' — loop sentinel. // source: CBACT02C.cbl:65
- `ABCODE` PIC S9(9) BINARY, `TIMING` PIC S9(9) BINARY — abend call args. // source: CBACT02C.cbl:66-67

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (each -> one C# method)

### MAIN (unnamed PROCEDURE DIVISION mainline) — `Run()`
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBACT02C'`. // source: CBACT02C.cbl:71
2. `PERFORM 0000-CARDFILE-OPEN`. // source: CBACT02C.cbl:72
3. `PERFORM UNTIL END-OF-FILE = 'Y'`: if `END-OF-FILE = 'N'`, `PERFORM 1000-CARDFILE-GET-NEXT`; then if
   `END-OF-FILE = 'N'` (still), `DISPLAY CARD-RECORD`. (Inner re-check ensures the EOF read is not printed.)
   // source: CBACT02C.cbl:74-81
4. `PERFORM 9000-CARDFILE-CLOSE`. // source: CBACT02C.cbl:83
5. `DISPLAY 'END OF EXECUTION OF PROGRAM CBACT02C'`. // source: CBACT02C.cbl:85
6. `GOBACK`. // source: CBACT02C.cbl:87

> `DISPLAY CARD-RECORD` prints the full 150-byte logical record (six fields concatenated, FILLER as spaces).
> In the port this is the fixed-width serialization of the CARD row (see §7). // source: CBACT02C.cbl:78

### 1000-CARDFILE-GET-NEXT — `CardfileGetNext()`
1. `READ CARDFILE-FILE INTO CARD-RECORD`. // source: CBACT02C.cbl:93
2. If `CARDFILE-STATUS = '00'` -> `MOVE 0 TO APPL-RESULT`; else if `= '10'` -> `MOVE 16 TO APPL-RESULT`;
   else -> `MOVE 12 TO APPL-RESULT`. // source: CBACT02C.cbl:94-103
3. If `APPL-AOK` (result 0) -> CONTINUE. Else if `APPL-EOF` (result 16) -> `MOVE 'Y' TO END-OF-FILE`.
   Else (result 12 hard error) -> `DISPLAY 'ERROR READING CARDFILE'`, `MOVE CARDFILE-STATUS TO IO-STATUS`,
   `PERFORM 9910-DISPLAY-IO-STATUS`, `PERFORM 9999-ABEND-PROGRAM`. // source: CBACT02C.cbl:104-115
4. `EXIT`. // source: CBACT02C.cbl:116

> No COMPUTE/arithmetic here beyond literal MOVEs into APPL-RESULT. Result is set by status, not computed.

### 0000-CARDFILE-OPEN — `CardfileOpen()`
1. `MOVE 8 TO APPL-RESULT` (pre-op default). // source: CBACT02C.cbl:119
2. `OPEN INPUT CARDFILE-FILE`. // source: CBACT02C.cbl:120
3. If `CARDFILE-STATUS = '00'` -> `MOVE 0 TO APPL-RESULT`; else `MOVE 12 TO APPL-RESULT`.
   // source: CBACT02C.cbl:121-125
4. If `APPL-AOK` -> CONTINUE; else `DISPLAY 'ERROR OPENING CARDFILE'`, `MOVE CARDFILE-STATUS TO IO-STATUS`,
   `PERFORM 9910-DISPLAY-IO-STATUS`, `PERFORM 9999-ABEND-PROGRAM`. // source: CBACT02C.cbl:126-133
5. `EXIT`. // source: CBACT02C.cbl:134

### 9000-CARDFILE-CLOSE — `CardfileClose()`
1. `ADD 8 TO ZERO GIVING APPL-RESULT` (i.e. APPL-RESULT := 8, pre-op default via add). // source: CBACT02C.cbl:137
2. `CLOSE CARDFILE-FILE`. // source: CBACT02C.cbl:138
3. If `CARDFILE-STATUS = '00'` -> `SUBTRACT APPL-RESULT FROM APPL-RESULT` (APPL-RESULT := 0);
   else `ADD 12 TO ZERO GIVING APPL-RESULT` (APPL-RESULT := 12). // source: CBACT02C.cbl:139-143
4. If `APPL-AOK` -> CONTINUE; else `DISPLAY 'ERROR CLOSING CARDFILE'`, `MOVE CARDFILE-STATUS TO IO-STATUS`,
   `PERFORM 9910-DISPLAY-IO-STATUS`, `PERFORM 9999-ABEND-PROGRAM`. // source: CBACT02C.cbl:144-151
5. `EXIT`. // source: CBACT02C.cbl:152

> Arithmetic note: steps 1 and 3 use `ADD ... TO ZERO GIVING` and `SUBTRACT x FROM x` purely as obfuscated
> ways to assign constants 8/12/0 to APPL-RESULT. No truncation/sign concern (S9(9) COMP holds 8/12/0).

### 9999-ABEND-PROGRAM — `AbendProgram()`
1. `DISPLAY 'ABENDING PROGRAM'`. // source: CBACT02C.cbl:155
2. `MOVE 0 TO TIMING`; `MOVE 999 TO ABCODE`. // source: CBACT02C.cbl:156-157
3. `CALL 'CEE3ABD' USING ABCODE, TIMING` — Language Environment abend, user code 999, immediate.
   // source: CBACT02C.cbl:158

> Port: throw a fatal `AbendException`/`Abend(999)` (CardDemo.Runtime.Abend) that terminates the batch with a
> nonzero code; do not return.

### 9910-DISPLAY-IO-STATUS — `DisplayIoStatus()`
Formats the 2-byte file status into a 4-char "NNNN" string and DISPLAYs `'FILE STATUS IS: NNNN' IO-STATUS-04`.
1. If `IO-STATUS NOT NUMERIC` OR `IO-STAT1 = '9'`:
   - `MOVE IO-STAT1 TO IO-STATUS-04(1:1)` (first char = stat1 byte as-is). // source: CBACT02C.cbl:164
   - `MOVE 0 TO TWO-BYTES-BINARY`; `MOVE IO-STAT2 TO TWO-BYTES-RIGHT`; `MOVE TWO-BYTES-BINARY TO IO-STATUS-0403`
     — i.e. take the second status byte, place it in the low byte of a halfword, read that halfword as a
     number, and store it right-justified into the 3-digit IO-STATUS-0403. // source: CBACT02C.cbl:165-167
   - `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBACT02C.cbl:168
2. Else (status is numeric and stat1 != '9'):
   - `MOVE '0000' TO IO-STATUS-04`; `MOVE IO-STATUS TO IO-STATUS-04(3:2)` (status digits in positions 3-4).
   - `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBACT02C.cbl:170-172
3. `EXIT`. // source: CBACT02C.cbl:174

> Port detail: `TWO-BYTES-BINARY` is `PIC 9(4) BINARY` (a halfword) redefined as two bytes; `TWO-BYTES-RIGHT`
> is the **right (low-order)** byte. On the mainframe this is **big-endian**, so moving the status character
> into the low byte and reading the halfword yields the EBCDIC numeric value of that character (e.g. EBCDIC
> '0'..'9' = 0xF0..0xF9 -> 240..249). The .NET port must reproduce the same mapping the verification harness
> expects — this branch fires only for non-numeric / '9'-class statuses, which never occur on the normal
> '00'/'10' paths here. See FAITHFUL BUGS §6 and PORT NOTES §7.

---

## 5. VALIDATION RULES and exact literal messages

This program performs **no data validation** of card fields. The only "rules" are file-status checks.
Exact literal strings emitted (preserve verbatim, including spacing/case):

- `'START OF EXECUTION OF PROGRAM CBACT02C'` // source: CBACT02C.cbl:71
- `'END OF EXECUTION OF PROGRAM CBACT02C'` // source: CBACT02C.cbl:85
- `'ERROR READING CARDFILE'` // source: CBACT02C.cbl:110
- `'ERROR OPENING CARDFILE'` // source: CBACT02C.cbl:129
- `'ERROR CLOSING CARDFILE'` // source: CBACT02C.cbl:147
- `'ABENDING PROGRAM'` // source: CBACT02C.cbl:155
- `'FILE STATUS IS: NNNN'` followed by the 4-char IO-STATUS-04 value // source: CBACT02C.cbl:168, 172
- Each card record is displayed via `DISPLAY CARD-RECORD` (the 150-byte image). // source: CBACT02C.cbl:78

File-status decision table:
| Operation | status '00' | status '10' | any other |
|---|---|---|---|
| OPEN | APPL-RESULT=0 (ok) | (n/a) | APPL-RESULT=12 -> abend | // source: CBACT02C.cbl:121-133 |
| READ | APPL-RESULT=0 (ok) | APPL-RESULT=16 -> set EOF | APPL-RESULT=12 -> abend | // source: CBACT02C.cbl:94-115 |
| CLOSE | APPL-RESULT=0 (ok) | (n/a) | APPL-RESULT=12 -> abend | // source: CBACT02C.cbl:139-151 |

---

## 6. FAITHFUL BUGS / quirks to reproduce verbatim (do NOT fix)

1. **`'FILE STATUS IS: NNNN'` literal with placeholder text printed alongside the value.** The DISPLAY emits
   the literal string `NNNN` immediately followed by the actual 4-digit `IO-STATUS-04`. The mainframe prints
   `FILE STATUS IS: NNNN0010` (literal NNNN + value). Reproduce the literal `NNNN` in the output, do not
   substitute. // source: CBACT02C.cbl:168, 172

2. **Misspelled copybook field name `CARD-EXPIRAION-DATE`** (missing 'T', should be EXPIRATION). The CARD
   column is named `expiration_date` per ARCHITECTURE.md but the COBOL field is `CARD-EXPIRAION-DATE`; keep
   the column name as specified, but note the source typo so mapping stays traceable.
   // source: CVACT02Y.cpy:9 ; ARCHITECTURE.md:54 (field name EXPIRAION noted there too)

3. **Status-byte-to-number conversion relies on big-endian EBCDIC halfword aliasing.** In 9910, a single
   status character is moved into the low byte of a `PIC 9(4) BINARY` and read back as a number. This is
   architecture/endianness/codepage dependent and yields the character's EBCDIC code point as a value (e.g.
   '0'->240). It only executes for non-numeric or '9'-class statuses. Reproduce the same numeric mapping the
   golden fixtures encode; do not "clean it up" to parse the character as its decimal digit.
   // source: CBACT02C.cbl:53-56, 162-167

4. **Obfuscated constant assignment via arithmetic in 9000-CARDFILE-CLOSE.** `ADD 8 TO ZERO GIVING
   APPL-RESULT`, `SUBTRACT APPL-RESULT FROM APPL-RESULT`, and `ADD 12 TO ZERO GIVING APPL-RESULT` are just
   ways to set 8/0/12. Keep the equivalent assignments; behavior is identical but preserve intent.
   // source: CBACT02C.cbl:137, 140, 142

5. **OPEN does not handle status '10'.** Only '00' is success; everything else (including the impossible '10'
   on OPEN) goes to the 12/abend path. Faithful: do not add EOF handling to open. // source: CBACT02C.cbl:121-125

6. **Dead `EXIT` statements / no explicit paragraph-EXIT-section.** Each I/O paragraph ends with a bare
   `EXIT` (no-op fall-through, not `EXIT PROGRAM`). Harmless; methods just return. // source: CBACT02C.cbl:116, 134, 152, 174

---

## 7. PORT NOTES (relational-access translation plan + tricky COBOL semantics)

- **Sequential keyed read -> ordered cursor.** `OPEN INPUT` + repeated `READ` on an INDEXED/SEQUENTIAL file
  is a primary-key-ordered forward scan. Implement via the CARD repository as
  `SELECT ... FROM CARD ORDER BY card_num ASC` and iterate. EBCDIC-vs-ASCII collation: card_num is X(16) of
  ASCII digits in CardDemo data, so ordinal/ASCII ordering matches EBCDIC ordering for this key (digits are
  monotonic in both). Pin with a guard test. // source: ARCHITECTURE.md:84-85 ; CBACT02C.cbl:30-32

- **File status semantics.** Repository must surface FileStatus: '00' (row read), '10' (EOF), and a generic
  hard-error path. Map READ EOF -> '10' -> APPL-RESULT 16 -> set END-OF-FILE='Y' and stop the loop without
  printing. OPEN/CLOSE expect '00'; anything else -> abend(999). // source: CBACT02C.cbl:94-115, 121-133, 139-151

- **`READ ... INTO CARD-RECORD` + 150-byte image / FILLER.** CARD-RECORD = 91 meaningful bytes
  (16+11+3+50+10+1) + 59-byte FILLER = 150. The relational CARD row stores the six columns; to reproduce
  `DISPLAY CARD-RECORD` and golden-master output, serialize the row to a fixed-width 150-char image:
  card_num(16) + acct_id as 9(11) zero-padded numeric + cvv_cd as 9(3) zero-padded + embossed_name(50, space
  padded) + expiration_date(10) + active_status(1) + 59 spaces. Use CardDemo.Runtime fixed-width formatting.
  // source: CVACT02Y.cpy:4-11

- **Numeric display fields.** CARD-ACCT-ID PIC 9(11) and CARD-CVV-CD PIC 9(03) are USAGE DISPLAY (zoned),
  so in the 150-byte image they are zero-padded ASCII/EBCDIC digits, not packed. No sign. The port's column
  types are `long`/`int` per ARCHITECTURE.md:37/49-54; re-pad to fixed width on serialize.

- **REDEFINES + halfword endianness (9910 routine).** `TWO-BYTES-BINARY` PIC 9(4) BINARY redefined as two
  X bytes; only the right byte is set. Reproduce the big-endian EBCDIC-code-point result (see FAITHFUL BUG
  #3). This path is effectively unreachable for the '00'/'10' statuses this program normally sees.
  // source: CBACT02C.cbl:53-56, 162-167

- **Abend.** `CALL 'CEE3ABD'` -> CardDemo.Runtime.Abend(999): terminate batch with a fatal exception /
  nonzero exit; never return to the loop. // source: CBACT02C.cbl:154-158

- **DISPLAY destination.** All DISPLAYs go to SYSOUT (JCL `//SYSOUT DD SYSOUT=*`). In the port these are
  console/log lines; the per-record card image lines are the program's primary "report" output that the
  characterization test diffs. // source: READCARD.jcl:27 ; CBACT02C.cbl:78

---

## 8. OPEN QUESTIONS / risks

1. **9910 NNNN formatting for non-'00'/'10' statuses.** The big-endian halfword aliasing produces a value
   from the status byte's code point. Since no golden fixture is likely to exercise a non-numeric status
   (CARDFILE under normal seeding never errors), this branch is hard to characterize. Risk is low (dead path
   in practice) but the exact NNNN output for an error status should be pinned if any fixture forces it.
   // source: CBACT02C.cbl:162-167

2. **Empty CARDFILE.** If the table is empty, the first READ returns '10' immediately, loop never prints a
   record, program closes and ends normally. Confirm repository returns '10' (not an exception) on an empty
   ordered cursor. // source: CBACT02C.cbl:74-81, 94-103

3. **Record-image byte-exactness** depends on faithful re-padding of zoned numerics and the 59-byte FILLER;
   verified by the schema round-trip net in ARCHITECTURE.md §Verification(1). // source: ARCHITECTURE.md:92-94
