# PORT SPEC — CBACT01C (Account File Reader / Multi-File Writer)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT01C.cbl`
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBACT01C.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA.

---

## 1. Purpose

CBACT01C reads the **ACCOUNT master** (a VSAM KSDS, here represented by the relational `ACCOUNT`
table) sequentially from low key to high key, and for **each** account record:
1. displays every field of the account record to SYSOUT, then
2. produces three derived **sequential output files** — a fixed-record account file (`OUTFILE`,
   LRECL 107 FB), a fixed-record "array" file (`ARRYFILE`, LRECL 110 FB), and a variable-length
   record file (`VBRCFILE`, LRECL 84 VB) — each populated with a mix of the source fields and
   **hard-coded constant values** plus a date reformatted via the assembler subroutine `COBDATFT`.
The program is essentially a demonstration/utility job: it exercises sequential KSDS reads, fixed
and variable record writes, an `OCCURS` array, `COMP-3` packed fields, a `REDEFINES`, reference
modification, and an external date-conversion CALL. // source: CBACT01C.cbl:1-5, 140-160

**How invoked:** JCL job `READACCT.jcl`, step **`STEP05  EXEC PGM=CBACT01C`**.
// source: jcl/READACCT.jcl:32
DD-to-file bindings (from JCL): `ACCTFILE` → account KSDS (input); `OUTFILE` → PSCOMP LRECL=107
FB; `ARRYFILE` → ARRYPS LRECL=110 FB; `VBRCFILE` → VBPS LRECL=84 RECFM=VB.
// source: jcl/READACCT.jcl:35-48
A preceding step `PREDEL EXEC PGM=IEFBR14` deletes the three output datasets (MOD,DELETE) so the
job re-creates them fresh each run. // source: jcl/READACCT.jcl:22-28
Not called as a subprogram; it is a top-level `GOBACK` batch main. // source: CBACT01C.cbl:160

---

## 2. FILE / TABLE access

| COBOL file (SELECT) | DD | ORG/ACCESS | Relational target (ARCHITECTURE.md) | Operations in this program | SQL mapping |
|---|---|---|---|---|---|
| `ACCTFILE-FILE` | ACCTFILE | INDEXED, **ACCESS SEQUENTIAL**, RECORD KEY `FD-ACCT-ID` | **ACCOUNT** table (PK `acct_id`) | OPEN INPUT; sequential READ (READNEXT semantics); CLOSE | `SELECT * FROM ACCOUNT ORDER BY acct_id` forward cursor; each READ = next row; EOF when cursor exhausted (file status `'10'`). // source: CBACT01C.cbl:29-33, 166, 319, 390 |
| `OUT-FILE` | OUTFILE | SEQUENTIAL output | **NOT a base table** — a derived QSAM report file. Emit as a fixed-width (LRECL 107) output dataset/stream, NOT a DB table. | OPEN OUTPUT; WRITE `OUT-ACCT-REC`; (no explicit CLOSE — see Faithful Bugs) | append fixed-width record to OUTFILE output. // source: CBACT01C.cbl:35-38, 243, 336 |
| `ARRY-FILE` | ARRYFILE | SEQUENTIAL output | derived QSAM report file (LRECL 110) | OPEN OUTPUT; WRITE `ARR-ARRAY-REC`; (no explicit CLOSE) | append fixed-width record to ARRYFILE output. // source: CBACT01C.cbl:40-43, 264, 354 |
| `VBRC-FILE` | VBRCFILE | SEQUENTIAL output, **RECORDING MODE V**, RECORD VARYING 10..80 DEPENDING ON `WS-RECD-LEN` | derived QSAM **variable-length** report file (LRECL 84 VB) | OPEN OUTPUT; two WRITEs of `VBR-REC` per account (lengths 12 then 39); (no explicit CLOSE) | append variable-length records to VBRCFILE output. // source: CBACT01C.cbl:45-48, 80-85, 290, 305, 372 |

**Important:** Only `ACCTFILE` corresponds to a base relational table (`ACCOUNT`). The other three
are **output sequential datasets**, not tables. Per ARCHITECTURE.md they are derived QSAM report
files — the .NET port must emit them as byte-faithful fixed/variable-width record streams (these are
exactly what the batch-characterization golden harness diffs). Do **not** model them as DB tables.

### Record layouts of the output files

**OUTFILE — `OUT-ACCT-REC`** (declared length 11+1+9*4 wait recalc; see field list).
Fields (PIC): OUT-ACCT-ID 9(11); OUT-ACCT-ACTIVE-STATUS X(1); OUT-ACCT-CURR-BAL S9(10)V99;
OUT-ACCT-CREDIT-LIMIT S9(10)V99; OUT-ACCT-CASH-CREDIT-LIMIT S9(10)V99; OUT-ACCT-OPEN-DATE X(10);
OUT-ACCT-EXPIRAION-DATE X(10); OUT-ACCT-REISSUE-DATE X(10); OUT-ACCT-CURR-CYC-CREDIT S9(10)V99;
**OUT-ACCT-CURR-CYC-DEBIT S9(10)V99 USAGE COMP-3** (packed, 7 bytes on disk); OUT-ACCT-GROUP-ID
X(10). // source: CBACT01C.cbl:56-69
On-disk LRECL per JCL = **107**. Note the COMP-3 field occupies 7 bytes on disk (12 digits → ceil((12+1)/2)=7); all other numeric DISPLAY fields are zoned (1 byte/digit). The .NET fixed-width
serializer (Runtime) must pack OUT-ACCT-CURR-CYC-DEBIT as COMP-3 to reproduce LRECL 107 exactly.
// source: jcl/READACCT.jcl:38-40

**ARRYFILE — `ARR-ARRAY-REC`**: ARR-ACCT-ID 9(11); **ARR-ACCT-BAL OCCURS 5 TIMES** of
{ ARR-ACCT-CURR-BAL S9(10)V99 (zoned 12 bytes) + ARR-ACCT-CURR-CYC-DEBIT S9(10)V99 **COMP-3**
(7 bytes) }; ARR-FILLER X(4). // source: CBACT01C.cbl:71-78
On-disk: 11 + 5*(12+7) + 4 = 11 + 95 + 4 = **110** = JCL LRECL. // source: jcl/READACCT.jcl:42-43

**VBRCFILE — `VBR-REC` PIC X(80)** written with variable length `WS-RECD-LEN` (12 then 39).
RECFM VB, LRECL 84 (80 data + 4 RDW). Two physical records per account.
// source: CBACT01C.cbl:80-85, jcl/READACCT.jcl:45-47

---

## 3. WORKING-STORAGE structures that affect logic

- `COPY CVACT01Y` → `ACCOUNT-RECORD` (the typed account record READ INTO; 11 elementary fields +
  FILLER X(178), RECLN 300). Maps to the **ACCOUNT** table columns.
  Field note: `ACCT-EXPIRAION-DATE` is misspelled in the copybook (carried through faithfully).
  // source: cpy/CVACT01Y.cpy:4-17
- `COPY CODATECN` → `CODATECN-REC`, the parameter block for the `COBDATFT` date subroutine.
  88-levels: **`YYYY-MM-DD-IN VALUE "2"`** (input type), **`YYYYMMDD-OP VALUE "2"`** (output type).
  Layout: CODATECN-TYPE X(1); CODATECN-INP-DATE X(20); CODATECN-OUTTYPE X(1);
  CODATECN-0UT-DATE X(20); CODATECN-ERROR-MSG X(38). // source: cpy/CODATECN.cpy:17-53
- File-status pairs: `ACCTFILE-STATUS`, `OUTFILE-STATUS`, `ARRYFILE-STATUS`, `VBRCFILE-STATUS`
  (each 2 chars). // source: CBACT01C.cbl:91-102
- `IO-STATUS` (2 chars) + `TWO-BYTES-BINARY` PIC 9(4) BINARY REDEFINED as `TWO-BYTES-LEFT/RIGHT`
  X(1) each — used by the IO-status display routine. // source: CBACT01C.cbl:104-113
- `APPL-RESULT` S9(9) COMP with 88s `APPL-AOK VALUE 0`, `APPL-EOF VALUE 16`. // source: CBACT01C.cbl:115-117
- `END-OF-FILE` X(1) VALUE 'N'. // source: CBACT01C.cbl:119
- `WS-RECD-LEN` 9(4) — drives VB record length. // source: CBACT01C.cbl:122
- `VBRC-REC1` (VB1-ACCT-ID 9(11) + VB1-ACCT-ACTIVE-STATUS X(1) = 12 bytes). // source: CBACT01C.cbl:123-125
- `VBRC-REC2` (VB2-ACCT-ID 9(11) + VB2-ACCT-CURR-BAL S9(10)V99 + VB2-ACCT-CREDIT-LIMIT S9(10)V99 +
  VB2-ACCT-REISSUE-YYYY X(4) = 11+12+12+4 = 39 bytes). // source: CBACT01C.cbl:126-130
- `WS-ACCT-REISSUE-DATE` (YYYY X4 / FILLER X1 / MM X2 / FILLER X1 / DD X2 = 10 bytes), REDEFINED by
  `WS-REISSUE-DATE PIC X(10)`. // source: CBACT01C.cbl:131-137

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM flow preserved.

### MAIN (unnamed PROCEDURE DIVISION body) // source: CBACT01C.cbl:140-160
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBACT01C'`.
2. PERFORM `0000-ACCTFILE-OPEN`, `2000-OUTFILE-OPEN`, `3000-ARRFILE-OPEN`, `4000-VBRFILE-OPEN`
   (open all four files, abend on any non-`'00'`).
3. `PERFORM UNTIL END-OF-FILE = 'Y'`: if `END-OF-FILE = 'N'` then PERFORM `1000-ACCTFILE-GET-NEXT`;
   if still `'N'` after the read, `DISPLAY ACCOUNT-RECORD`. (The inner `IF END-OF-FILE='N'` guards
   are redundant with the loop condition but must be reproduced.) // source: CBACT01C.cbl:147-154
4. PERFORM `9000-ACCTFILE-CLOSE`.
5. `DISPLAY 'END OF EXECUTION OF PROGRAM CBACT01C'`.
6. `GOBACK`. **No CLOSE for OUTFILE/ARRYFILE/VBRCFILE** (see Faithful Bugs). // source: CBACT01C.cbl:156-160

### 1000-ACCTFILE-GET-NEXT // source: CBACT01C.cbl:165-198
1. `READ ACCTFILE-FILE INTO ACCOUNT-RECORD` (sequential next).
2. IF status `'00'`: MOVE 0 TO APPL-RESULT; `INITIALIZE ARR-ARRAY-REC`; then PERFORM, in order:
   `1100-DISPLAY-ACCT-RECORD`, `1300-POPUL-ACCT-RECORD`, `1350-WRITE-ACCT-RECORD`,
   `1400-POPUL-ARRAY-RECORD`, `1450-WRITE-ARRY-RECORD`; `INITIALIZE VBRC-REC1`;
   `1500-POPUL-VBRC-RECORD`, `1550-WRITE-VB1-RECORD`, `1575-WRITE-VB2-RECORD`. // source: CBACT01C.cbl:167-178
3. ELSE IF status `'10'` (EOF): MOVE 16 TO APPL-RESULT; ELSE MOVE 12. // source: CBACT01C.cbl:179-185
4. IF `APPL-AOK` CONTINUE; ELSE IF `APPL-EOF` MOVE 'Y' TO END-OF-FILE; ELSE display
   `'ERROR READING ACCOUNT FILE'`, MOVE status to IO-STATUS, PERFORM `9910-DISPLAY-IO-STATUS`,
   PERFORM `9999-ABEND-PROGRAM`. // source: CBACT01C.cbl:186-197

### 1100-DISPLAY-ACCT-RECORD // source: CBACT01C.cbl:200-213
DISPLAY each of the 11 account fields with fixed labels (`'ACCT-ID :' ACCT-ID`, etc.) then a dashed
separator line. **Pure SYSOUT side-effect; no data change.** Labels are reproduced verbatim
(including 2-space label widths and the misspelled `'ACCT-EXPIRAION-DATE'`). The numeric fields are
DISPLAYed using COBOL default (signed-zoned, sign overpunch on last digit) — reproduce that exact
text only if SYSOUT is part of the golden diff; otherwise treat as informational.

### 1300-POPUL-ACCT-RECORD (builds OUT-ACCT-REC) // source: CBACT01C.cbl:215-240
1. MOVE ACCT-ID → OUT-ACCT-ID; ACCT-ACTIVE-STATUS → OUT-ACCT-ACTIVE-STATUS;
   ACCT-CURR-BAL → OUT-ACCT-CURR-BAL; ACCT-CREDIT-LIMIT → OUT-ACCT-CREDIT-LIMIT;
   ACCT-CASH-CREDIT-LIMIT → OUT-ACCT-CASH-CREDIT-LIMIT; ACCT-OPEN-DATE → OUT-ACCT-OPEN-DATE;
   ACCT-EXPIRAION-DATE → OUT-ACCT-EXPIRAION-DATE. // source: CBACT01C.cbl:216-222
2. MOVE ACCT-REISSUE-DATE → **both** `CODATECN-INP-DATE` and `WS-REISSUE-DATE` (the X(10) redefine).
   ACCT-REISSUE-DATE is X(10); CODATECN-INP-DATE is X(20) → right-padded with spaces.
   // source: CBACT01C.cbl:223-224
3. MOVE '2' TO CODATECN-TYPE (input format = `YYYY-MM-DD`); MOVE '2' TO CODATECN-OUTTYPE
   (output format = `YYYYMMDD`). // source: CBACT01C.cbl:225-226
4. `CALL 'COBDATFT' USING CODATECN-REC` — external date reformat (see §5). // source: CBACT01C.cbl:231
5. MOVE CODATECN-0UT-DATE (X20) → OUT-ACCT-REISSUE-DATE (X10) → takes first 10 chars
   (`YYYYMMDD` + 2 spaces). // source: CBACT01C.cbl:233
6. MOVE ACCT-CURR-CYC-CREDIT → OUT-ACCT-CURR-CYC-CREDIT. // source: CBACT01C.cbl:235
7. **IF ACCT-CURR-CYC-DEBIT = ZERO → MOVE 2525.00 TO OUT-ACCT-CURR-CYC-DEBIT** (else the field is
   left UNTOUCHED — never assigned from the source! see Faithful Bugs). // source: CBACT01C.cbl:236-238
8. MOVE ACCT-GROUP-ID → OUT-ACCT-GROUP-ID. // source: CBACT01C.cbl:239

   Arithmetic/precision notes: all `S9(10)V99` MOVEs are decimal copies (12 int digits + 2 frac);
   no scaling/truncation since source and target PICs are identical. `2525.00` literal fits.
   OUT-ACCT-CURR-CYC-DEBIT is COMP-3 in the output record but the value is a plain decimal here.

### 1350-WRITE-ACCT-RECORD // source: CBACT01C.cbl:242-251
WRITE OUT-ACCT-REC. IF OUTFILE-STATUS NOT '00' AND NOT '10' → display
`'ACCOUNT FILE WRITE STATUS IS:' OUTFILE-STATUS`, MOVE→IO-STATUS, `9910`, `9999-ABEND`. // source: CBACT01C.cbl:243-250

### 1400-POPUL-ARRAY-RECORD (builds ARR-ARRAY-REC) // source: CBACT01C.cbl:253-261
1. MOVE ACCT-ID → ARR-ACCT-ID.
2. ARR-ACCT-CURR-BAL(1) = ACCT-CURR-BAL; ARR-ACCT-CURR-CYC-DEBIT(1) = **1005.00** (const).
3. ARR-ACCT-CURR-BAL(2) = ACCT-CURR-BAL; ARR-ACCT-CURR-CYC-DEBIT(2) = **1525.00** (const).
4. ARR-ACCT-CURR-BAL(3) = **-1025.00**; ARR-ACCT-CURR-CYC-DEBIT(3) = **-2500.00**.
5. **Occurrences (4) and (5) are never populated** here. But `INITIALIZE ARR-ARRAY-REC` ran in
   1000 before this, so (4) and (5) = numeric ZERO. Negative literals require the signed PIC (it is
   S9(10)V99) — sign is preserved (zoned overpunch for CURR-BAL, packed sign nibble for the COMP-3
   CURR-CYC-DEBIT). // source: CBACT01C.cbl:254-260, 169

### 1450-WRITE-ARRY-RECORD // source: CBACT01C.cbl:263-274
WRITE ARR-ARRAY-REC. IF ARRYFILE-STATUS NOT '00' AND NOT '10' → display
`'ACCOUNT FILE WRITE STATUS IS:' ARRYFILE-STATUS`, IO-STATUS, `9910`, `9999-ABEND`.
(Label says "ACCOUNT FILE" though it is the array file — reproduce verbatim.) // source: CBACT01C.cbl:264-273

### 1500-POPUL-VBRC-RECORD (builds VBRC-REC1 & VBRC-REC2) // source: CBACT01C.cbl:276-285
1. MOVE ACCT-ID → VB1-ACCT-ID and VB2-ACCT-ID.
2. MOVE ACCT-ACTIVE-STATUS → VB1-ACCT-ACTIVE-STATUS.
3. MOVE ACCT-CURR-BAL → VB2-ACCT-CURR-BAL; ACCT-CREDIT-LIMIT → VB2-ACCT-CREDIT-LIMIT.
4. MOVE WS-ACCT-REISSUE-YYYY → VB2-ACCT-REISSUE-YYYY. (`WS-ACCT-REISSUE-YYYY` = first 4 chars of the
   reissue date loaded in 1300 step 2.) // source: CBACT01C.cbl:277-282
5. DISPLAY `'VBRC-REC1:' VBRC-REC1` and `'VBRC-REC2:' VBRC-REC2`. // source: CBACT01C.cbl:283-284

### 1550-WRITE-VB1-RECORD // source: CBACT01C.cbl:287-300
1. MOVE 12 TO WS-RECD-LEN.
2. MOVE VBRC-REC1 TO `VBR-REC(1:WS-RECD-LEN)` (reference-modified first 12 bytes). VBR-REC residual
   bytes 13..80 retain prior content (see Faithful Bugs re: stale bytes).
3. WRITE VBR-REC → physical record length = WS-RECD-LEN (12). IF VBRCFILE-STATUS NOT '00'/'10' →
   error path (same as others). // source: CBACT01C.cbl:288-299

### 1575-WRITE-VB2-RECORD // source: CBACT01C.cbl:302-315
1. MOVE 39 TO WS-RECD-LEN.
2. MOVE VBRC-REC2 TO `VBR-REC(1:39)`.
3. WRITE VBR-REC → record length 39. Same error path. // source: CBACT01C.cbl:303-314

### 0000-ACCTFILE-OPEN // source: CBACT01C.cbl:317-333
MOVE 8→APPL-RESULT; `OPEN INPUT ACCTFILE-FILE`; if status '00' set 0 else 12; if not AOK display
`'ERROR OPENING ACCTFILE'`, IO-STATUS, `9910`, `9999-ABEND`.

### 2000-OUTFILE-OPEN // source: CBACT01C.cbl:334-350
`OPEN OUTPUT OUT-FILE`; analogous; error msg `'ERROR OPENING OUTFILE' OUTFILE-STATUS`.

### 3000-ARRFILE-OPEN // source: CBACT01C.cbl:352-368
`OPEN OUTPUT ARRY-FILE`; error msg `'ERROR OPENING ARRAYFILE' ARRYFILE-STATUS`.

### 4000-VBRFILE-OPEN // source: CBACT01C.cbl:370-386
`OPEN OUTPUT VBRC-FILE`; error msg `'ERROR OPENING VBRC FILE' VBRCFILE-STATUS`.

### 9000-ACCTFILE-CLOSE // source: CBACT01C.cbl:388-404
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE ACCTFILE-FILE`; if status '00'
`SUBTRACT APPL-RESULT FROM APPL-RESULT` (→ 0) else `ADD 12 TO ZERO GIVING APPL-RESULT`; if not AOK
display `'ERROR CLOSING ACCOUNT FILE'`, IO-STATUS, `9910`, `9999-ABEND`. (Only ACCTFILE is closed.)

### 9999-ABEND-PROGRAM // source: CBACT01C.cbl:406-410
DISPLAY `'ABENDING PROGRAM'`; MOVE 0→TIMING; MOVE 999→ABCODE; `CALL 'CEE3ABD' USING ABCODE,TIMING`.
Port: throw an `Abend(999)` (Runtime.Abend), terminating the batch with code 999.

### 9910-DISPLAY-IO-STATUS // source: CBACT01C.cbl:413-426
Formats the 2-char file status into a 4-digit `'FILE STATUS IS: NNNN'` line:
- IF IO-STATUS not numeric OR IO-STAT1 = '9': put IO-STAT1 into digit 1 of IO-STATUS-04; clear
  TWO-BYTES-BINARY; MOVE IO-STAT2 into TWO-BYTES-RIGHT (low byte of the 9(4) BINARY); MOVE the
  binary value into IO-STATUS-0403 (PIC 999); DISPLAY. This converts the binary value of the second
  status byte into a 3-digit number (the classic Micro Focus extended-status display idiom).
- ELSE: '0000' with the 2-digit status in positions 3-4; DISPLAY. // source: CBACT01C.cbl:414-425

---

## 5. External CALL — `COBDATFT` (date reformat) // source: CBACT01C.cbl:231; asm/COBDATFT.asm

`COBDATFT` is an HLASM subroutine operating on the `CODATECN-REC` (DSECT `COCDATFT`:
COINTYPE CL1, COINPDT CL20, COOUTYPE CL1, COOUTDT CL20, COERMSG CL38). // source: maclib/COCDATFT.mac:17-23
Behavior (must be reproduced exactly as a pure C# helper, NOT a real assembler):
- If COINTYPE = '1' → branch VALIDIN1; if '2' → VALIDIN2; else → GOTOERR. // source: asm/COBDATFT.asm:30-34
- **VALIDIN1** (input `YYYYMMDD`, expand to `YYYY-MM-DD`): if `COINPDT+4 = '-'` → error; if
  COOUTYPE='2' → error; else COOUTDT = COINPDT[0:4] + '-' + COINPDT[4:2] + '-' + COINPDT[6:2].
  // source: asm/COBDATFT.asm:35-45
- **VALIDIN2** (input `YYYY-MM-DD`, compress to `YYYYMMDD`): if COOUTYPE='1' → error; else
  COOUTDT = COINPDT[0:4] + COINPDT[5:2] + COINPDT[8:2]  (drops the two `-` separators).
  // source: asm/COBDATFT.asm:46-54
- GOTOERR: `MVC COERMSG,=C'INVALID INPUT'`. // source: asm/COBDATFT.asm:55-56
- COOUTDT is **only partially overwritten** (8 or 10 chars of the 20-char field); the remaining
  bytes keep whatever was there before (spaces in this program's flow, since CODATECN-REC is not
  re-cleared). The subroutine never clears COOUTDT before writing.

**In CBACT01C's call:** COINTYPE='2', COOUTYPE='2' → path **VALIDIN2**, COOUTYPE!='1' so no error,
so for reissue date `"YYYY-MM-DD"` the output is `"YYYYMMDD"` (8 chars) + trailing spaces. Then
OUT-ACCT-REISSUE-DATE (X10) = `"YYYYMMDD  "` (8 digits + 2 spaces). // source: CBACT01C.cbl:225-233

**Port plan:** implement `CobDatFt(ref CodatecnRec)` as a straight transliteration of the byte
moves above (string slicing on fixed offsets, no validation beyond the type/separator checks). Do
**not** parse/validate the date semantically.

---

## 6. VALIDATION RULES & exact literal messages

There is no business-field validation; the only "validation" is file-status checking. Exact literal
strings to reproduce verbatim (SYSOUT / abend):

- `'START OF EXECUTION OF PROGRAM CBACT01C'` // source: CBACT01C.cbl:141
- `'END OF EXECUTION OF PROGRAM CBACT01C'` // source: CBACT01C.cbl:158
- Per-field labels in 1100 (e.g. `'ACCT-ID                 :'`, `'ACCT-EXPIRAION-DATE     :'`, the
  dashed line `'-------------------------------------------------'`). // source: CBACT01C.cbl:201-212
- `'ERROR READING ACCOUNT FILE'` // source: CBACT01C.cbl:192
- `'ACCOUNT FILE WRITE STATUS IS:'` (used for OUTFILE, ARRYFILE, **and** VBRCFILE writes — same
  literal even for non-account files). // source: CBACT01C.cbl:246, 268, 294, 309
- `'ERROR OPENING ACCTFILE'` / `'ERROR OPENING OUTFILE'` / `'ERROR OPENING ARRAYFILE'` /
  `'ERROR OPENING VBRC FILE'`. // source: CBACT01C.cbl:328, 345, 363, 381
- `'ERROR CLOSING ACCOUNT FILE'` // source: CBACT01C.cbl:399
- `'VBRC-REC1:'` / `'VBRC-REC2:'` // source: CBACT01C.cbl:283-284
- `'ABENDING PROGRAM'` // source: CBACT01C.cbl:407
- `'FILE STATUS IS: NNNN'` (literal prefix; followed by the 4-char formatted status). // source: CBACT01C.cbl:420, 424
- COBDATFT error string `'INVALID INPUT'` (not reachable in this program's path, but include in the helper). // source: asm/COBDATFT.asm:56

File-status accept rules: open/close/read accept `'00'`; reads also treat `'10'` as EOF; writes
accept `'00'` and `'10'`; anything else abends. // source: CBACT01C.cbl:167,180,245,266,292,307,320,337,355,373,391

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **OUT-ACCT-CURR-CYC-DEBIT only set when source is zero.** In 1300, the field is assigned
   `2525.00` ONLY when `ACCT-CURR-CYC-DEBIT = ZERO`; when the source is non-zero the output field is
   **never assigned**, so it keeps whatever value it had from the prior iteration (or zero on the
   first record — OUT-ACCT-REC is not re-initialized per record). This is a genuine stale-data bug.
   // source: CBACT01C.cbl:236-238
2. **Output files OUTFILE/ARRYFILE/VBRCFILE are never CLOSEd.** Only `9000-ACCTFILE-CLOSE` runs.
   On the mainframe the runtime closes them at GOBACK; the .NET port must flush/close them at end of
   run but must NOT add any per-iteration close. (Behaviorally harmless but structurally a bug.)
   // source: CBACT01C.cbl:156-160, 388-404
3. **Wrong DISPLAY label for the array file write error** — `'ACCOUNT FILE WRITE STATUS IS:'` is
   shown for the ARRYFILE and VBRCFILE write failures too. Reproduce the misleading label.
   // source: CBACT01C.cbl:268, 294, 309
4. **`ARR-ARRAY-REC` occurrences (4) and (5) are never populated** — only indices 1,2,3 are set in
   1400; 4 and 5 stay at the `INITIALIZE`-zeroed value. (Intended? unclear — reproduce as-is: zeros.)
   // source: CBACT01C.cbl:254-260
5. **Misspelled data name `ACCT-EXPIRAION-DATE`** (should be EXPIRATION) carried from the copybook
   into both the record and the DISPLAY label. Keep the misspelling in column/field names and SYSOUT.
   // source: cpy/CVACT01Y.cpy:11; CBACT01C.cbl:64, 207, 222
6. **VBR-REC stale tail bytes.** `1550` writes only the first 12 bytes of the 80-byte `VBR-REC` and
   `1575` only the first 39; bytes beyond the reference-modified prefix are not cleared between
   writes. For VB records only `WS-RECD-LEN` bytes are written, so the stale tail does not reach the
   file — but if the .NET port serializes the whole 80-byte buffer it would diverge. Port must honor
   the variable length and emit exactly WS-RECD-LEN bytes. // source: CBACT01C.cbl:288-290, 303-305
7. **Hard-coded "magic" array/debit constants** (`2525.00`, `1005.00`, `1525.00`, `-1025.00`,
   `-2500.00`). These are not derived from data; reproduce the exact literals. // source: CBACT01C.cbl:237, 256, 258, 259, 260
8. **`MOVE ... TO WS-REISSUE-DATE` then uses WS-ACCT-REISSUE-YYYY** — WS-ACCT-REISSUE-DATE has
   embedded FILLER bytes at positions 5 and 8; the redefine `WS-REISSUE-DATE X(10)` receives the raw
   `YYYY-MM-DD`, so WS-ACCT-REISSUE-YYYY = first 4 chars = the year. Correct by accident, but the
   structure-with-fillers vs flat-redefine duality is fragile; reproduce the byte layout.
   // source: CBACT01C.cbl:131-137, 223-224, 282

---

## 8. PORT NOTES (relational-access + tricky COBOL semantics)

**ACCOUNT read (the only relational access):** OPEN INPUT + sequential READ over an INDEXED KSDS =
forward ordered cursor over the `ACCOUNT` table: `SELECT ... FROM ACCOUNT ORDER BY acct_id` (PK
ascending). Each `1000-ACCTFILE-GET-NEXT` = `MoveNext()` on that cursor; exhausted cursor = file
status `'10'` → APPL-EOF → END-OF-FILE='Y'. Map repository `ReadNext` per the VSAM→SQL contract
(STARTBR+READNEXT → ORDER BY key forward). No writes/updates/deletes to ACCOUNT.
// source: ARCHITECTURE.md §"VSAM-semantics"; CBACT01C.cbl:166

**Output files are QSAM, not tables.** Emit OUTFILE (FB 107), ARRYFILE (FB 110), VBRCFILE (VB 84)
as fixed/variable-width byte streams via the Runtime fixed-width serializer. These are precisely
what the batch-characterization golden harness diffs (timestamps not present here, so a plain byte
diff applies). Crucial serializer requirements:
- **COMP-3 packing:** `OUT-ACCT-CURR-CYC-DEBIT` and every `ARR-ACCT-CURR-CYC-DEBIT(n)` are
  `USAGE COMP-3` (packed decimal). The serializer must pack `S9(10)V99` (12 digits) into 7 bytes,
  sign nibble in the low nibble (C=+, D=-). All other numerics are signed-zoned DISPLAY (1 byte/
  digit, sign overpunch on the units digit). Getting these widths right is what makes LRECL 107/110
  reproduce. // source: CBACT01C.cbl:67-68, 76-77
- **OCCURS 5:** ARR-ACCT-BAL is a 5-element group array; serialize all 5 (even the zero 4th/5th).
  // source: CBACT01C.cbl:74-78
- **REDEFINES:** `WS-REISSUE-DATE` over `WS-ACCT-REISSUE-DATE`, and the input/output redefines in
  CODATECN — model as a single backing byte buffer with overlapping typed views (or just do the
  string slicing the assembler/MOVEs imply). // source: CBACT01C.cbl:131-137; cpy/CODATECN.cpy:23-51
- **Reference modification `VBR-REC(1:WS-RECD-LEN)`:** write exactly WS-RECD-LEN bytes; the VB
  record's logical length = WS-RECD-LEN (RDW = len+4). Two records per account (12, then 39).
  // source: CBACT01C.cbl:289, 304
- **INITIALIZE:** `INITIALIZE ARR-ARRAY-REC` zeroes numerics and spaces alphanumerics per COBOL
  rules (ARR-ACCT-ID 9(11)→0, balances→0, ARR-FILLER X4→spaces). `INITIALIZE VBRC-REC1` likewise.
  Reproduce these resets at the start of each account iteration. // source: CBACT01C.cbl:169, 175
- **Signed decimals / negative literals:** `-1025.00`, `-2500.00` go into `S9(10)V99` fields — keep
  the sign (truncate-toward-zero / silent-overflow rules of CobolDecimal apply, though no overflow
  occurs here since all literals fit 12 digits). // source: CBACT01C.cbl:259-260
- **`9910-DISPLAY-IO-STATUS` binary trick:** `TWO-BYTES-BINARY` PIC 9(4) BINARY with the second
  status char placed in its low byte (`TWO-BYTES-RIGHT`), then rendered as a 3-digit number. Port as
  `(int)(byte)IO-STAT2` rendered "%03d" only on the non-numeric/'9' branch; on the numeric branch
  just zero-pad the 2-char status to NNNN with leading zeros in positions 3-4. // source: CBACT01C.cbl:414-424
- **DISPLAY of ACCOUNT-RECORD and per-field labels:** treat SYSOUT as non-golden unless the harness
  captures it; if captured, reproduce signed-zoned numeric rendering exactly. // source: CBACT01C.cbl:151, 200-212

**COBDATFT:** implement as a pure C# string helper (see §5); no external assembler, no real date
validation.

---

## 9. OPEN QUESTIONS / RISKS

1. **Is SYSOUT part of the golden fixture?** The program DISPLAYs heavily (every field of every
   record). If the characterization harness diffs SYSOUT, the exact signed-zoned numeric text and
   label spacing must be reproduced; if not, SYSOUT can be best-effort. Needs confirmation against
   `tests/golden/` for this job.
2. **OUT-ACCT-CURR-CYC-DEBIT stale value on first record (bug #1).** On the very first account, if
   ACCT-CURR-CYC-DEBIT is non-zero, the output field has never been assigned. Mainframe behavior:
   the WORKING/FD record area initial content (OPEN OUTPUT does not clear the record area) — likely
   binary low-values/zeros → as COMP-3 this is an invalid packed value or zero. The .NET port should
   default the OUT-ACCT-REC buffer to packed-zero at OPEN to match the most likely mainframe result;
   pin this with a golden fixture. // source: CBACT01C.cbl:236-238
3. **COMP-3 byte count for LRECL:** confirm 7 bytes for S9(10)V99 COMP-3 (12 digits → 7 bytes) so
   OUTFILE=107 and ARRYFILE=110 reconcile with the JCL DCB. (107 = 11+1+12+12+12+10+10+10+12+7+10;
   110 = 11 + 5*(12+7) + 4.) Verified against JCL. // source: jcl/READACCT.jcl:38-43
4. **VBRCFILE max length:** FD declares VARYING 10..80 but VB1 writes 12 and VB2 writes 39; JCL
   LRECL=84 (80+4 RDW). No conflict. // source: CBACT01C.cbl:80-85; jcl/READACCT.jcl:47
5. **COBDATFT does not clear COOUTDT** before writing — in this program flow CODATECN-0UT-DATE is
   space-padded once via the MOVE chain, so result is `YYYYMMDD` + spaces; if any later refactor
   reuses the rec across calls without re-clearing, stale tail bytes could appear. Pin the 8-char +
   spaces expectation. // source: asm/COBDATFT.asm:51-53
