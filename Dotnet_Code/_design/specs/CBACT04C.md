# PORT SPEC — CBACT04C (Interest Calculator, BATCH)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBACT04C.cbl`
> JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/INTCALC.jcl`
> Target: `src/CardDemo.Batch` over the relational SQLite schema defined in `_design/ARCHITECTURE.md`.
> All line citations below refer to `CBACT04C.cbl` unless otherwise stated.

---

## 1. Purpose & Invocation

**Purpose.** CBACT04C is the monthly **interest calculator**. It reads the *Transaction Category Balance* file (TCATBAL) **sequentially in key order**. For each TCATBAL record it: (a) detects an account-number break, and on each break rewrites the *previous* account's balance with accumulated interest and resets its cycle credit/debit; (b) for the new account, loads the account master and the card cross-reference; (c) for the current `(group-id, type, cat)` combination, looks up the disclosure-group interest rate (falling back to a `DEFAULT` group when the specific group is missing); and (d) when the rate is non-zero, computes monthly interest `bal * rate / 1200`, accumulates it, and **writes one interest TRANSACTION record** to a new sequential output file. Fees are stubbed (`1400-COMPUTE-FEES` is "To be implemented"). // source: CBACT04C.cbl:5, 350-356, 462-468, 518-520

**Invocation.** Standalone batch program, run by JCL job `INTCALC`, step **`STEP15`**: `EXEC PGM=CBACT04C,PARM='2022071800'`. It is invoked with a 10-char parameter date passed through the LINKAGE SECTION `EXTERNAL-PARMS` (`PARM-LENGTH S9(4) COMP`, `PARM-DATE X(10)`). // source: INTCALC.jcl:22; CBACT04C.cbl:175-180

> Note: JCL passes `'2022071800'` (10 chars) as PARM; the COBOL receives `PARM-LENGTH` + `PARM-DATE X(10)`. The `.NET` driver must supply the same 10-char date string and use it verbatim in the generated transaction id (see `1300-B-WRITE-TX`). // source: CBACT04C.cbl:476-480

**DD → table/file mapping (from JCL):**
| DD name | DSN (VSAM/QSAM) | Logical file | Relational target |
|---|---|---|---|
| TCATBALF | ...TCATBALF.VSAM.KSDS | TCATBAL-FILE (input, KSDS, seq read) | `TRAN_CAT_BAL` |
| XREFFILE / XREFFIL1 | ...CARDXREF.VSAM.KSDS (+ AIX path) | XREF-FILE (random, alt key) | `CARD_XREF` |
| ACCTFILE | ...ACCTDATA.VSAM.KSDS | ACCOUNT-FILE (I-O) | `ACCOUNT` |
| DISCGRP | ...DISCGRP.VSAM.KSDS | DISCGRP-FILE (random) | `DISCLOSURE_GROUP` |
| TRANSACT | ...SYSTRAN(+1), RECFM=F LRECL=350 (NEW) | TRANSACT-FILE (output seq) | `TRANSACTION` (sequential insert) |
// source: INTCALC.jcl:27-41

---

## 2. FILE / TABLE ACCESS TABLE

| COBOL file | Org / Access | Record key | Relational table | Ops used | SQL mapping |
|---|---|---|---|---|---|
| `TCATBAL-FILE` (TCATBALF) | INDEXED, **SEQUENTIAL** | `FD-TRAN-CAT-KEY` = acct_id 9(11) + type_cd X(2) + cat_cd 9(4) | `TRAN_CAT_BAL` | OPEN INPUT; READ (sequential next) | `SELECT ... FROM TRAN_CAT_BAL ORDER BY acct_id, type_cd, cat_cd` — forward cursor; each READ = next row; EOF → FileStatus '10'. // source: CBACT04C.cbl:28-32, 236, 326-348 |
| `XREF-FILE` (XREFFILE) | INDEXED, RANDOM | RECORD KEY `FD-XREF-CARD-NUM` X(16); **ALTERNATE** `FD-XREF-ACCT-ID` 9(11) | `CARD_XREF` | OPEN INPUT; READ **KEY IS FD-XREF-ACCT-ID** (alt-key read) | `SELECT * FROM CARD_XREF WHERE acct_id = @id` — first/only match by indexed alt column. // source: CBACT04C.cbl:34-39, 254, 394-398 |
| `ACCOUNT-FILE` (ACCTFILE) | INDEXED, RANDOM | `FD-ACCT-ID` 9(11) | `ACCOUNT` | OPEN **I-O**; READ (by PK); **REWRITE** | READ: `SELECT * FROM ACCOUNT WHERE acct_id=@id`; REWRITE: `UPDATE ACCOUNT SET ... WHERE acct_id=@id`. // source: CBACT04C.cbl:41-45, 291, 356, 373-374 |
| `DISCGRP-FILE` (DISCGRP) | INDEXED, RANDOM | `FD-DISCGRP-KEY` = group_id X(10) + type_cd X(2) + cat_cd 9(4) | `DISCLOSURE_GROUP` | OPEN INPUT; READ (by PK) twice (specific then DEFAULT) | `SELECT * FROM DISCLOSURE_GROUP WHERE acct_group_id=@g AND tran_type_cd=@t AND tran_cat_cd=@c`. // source: CBACT04C.cbl:47-51, 272, 416-444 |
| `TRANSACT-FILE` (TRANSACT) | SEQUENTIAL | (none — output) | `TRANSACTION` | OPEN OUTPUT; **WRITE** | Each WRITE = `INSERT INTO TRANSACTION (...)` (PK tran_id). Output is a *new* dataset (DISP=NEW); the relational port should treat this as append-into-empty / truncate-then-insert per run. // source: CBACT04C.cbl:53-56, 309, 500 |

**Repository contract (per ARCHITECTURE.md §VSAM→SQL):** READ key → SELECT by PK ('00'/'23'); READ alt key → SELECT by indexed col; sequential READ → ORDER BY key forward cursor (EOF→'10'); WRITE → INSERT (dup → '22'); REWRITE → UPDATE (missing → '23'). The `'23'`-fallback idiom in `1200-GET-INTEREST-RATE` is preserved at the program layer.

---

## 3. WORKING-STORAGE / record layouts (typed)

Record copybooks (one column per elementary field per ARCHITECTURE.md):

- `TRAN-CAT-BAL-RECORD` (CVTRA01Y, RECLN 50): `TRANCAT-ACCT-ID 9(11)`, `TRANCAT-TYPE-CD X(2)`, `TRANCAT-CD 9(4)`, `TRAN-CAT-BAL S9(9)V99`, FILLER X(22). → `TRAN_CAT_BAL`. // source: CVTRA01Y.cpy:4-10
- `DIS-GROUP-RECORD` (CVTRA02Y, RECLN 50): `DIS-ACCT-GROUP-ID X(10)`, `DIS-TRAN-TYPE-CD X(2)`, `DIS-TRAN-CAT-CD 9(4)`, `DIS-INT-RATE S9(4)V99`, FILLER X(28). → `DISCLOSURE_GROUP`. // source: CVTRA02Y.cpy:4-10
- `TRAN-RECORD` (CVTRA05Y, RECLN 350): `TRAN-ID X(16)`, `TRAN-TYPE-CD X(2)`, `TRAN-CAT-CD 9(4)`, `TRAN-SOURCE X(10)`, `TRAN-DESC X(100)`, `TRAN-AMT S9(9)V99`, `TRAN-MERCHANT-ID 9(9)`, `TRAN-MERCHANT-NAME X(50)`, `TRAN-MERCHANT-CITY X(50)`, `TRAN-MERCHANT-ZIP X(10)`, `TRAN-CARD-NUM X(16)`, `TRAN-ORIG-TS X(26)`, `TRAN-PROC-TS X(26)`, FILLER X(20). → `TRANSACTION`. // source: CVTRA05Y.cpy:4-18
- `ACCOUNT-RECORD` (CVACT01Y, RECLN 300): `ACCT-ID 9(11)`, `ACCT-ACTIVE-STATUS X(1)`, `ACCT-CURR-BAL S9(10)V99`, `ACCT-CREDIT-LIMIT`, `ACCT-CASH-CREDIT-LIMIT`, `ACCT-OPEN-DATE X(10)`, `ACCT-EXPIRAION-DATE X(10)`, `ACCT-REISSUE-DATE X(10)`, `ACCT-CURR-CYC-CREDIT S9(10)V99`, `ACCT-CURR-CYC-DEBIT S9(10)V99`, `ACCT-ADDR-ZIP X(10)`, `ACCT-GROUP-ID X(10)`, FILLER X(178). → `ACCOUNT`. // source: CVACT01Y.cpy:4-17
- `CARD-XREF-RECORD` (CVACT03Y, RECLN 50): `XREF-CARD-NUM X(16)`, `XREF-CUST-ID 9(9)`, `XREF-ACCT-ID 9(11)`, FILLER X(14). → `CARD_XREF`. // source: CVACT03Y.cpy:4-8

Key working vars:
- `WS-LAST-ACCT-NUM PIC X(11) VALUE SPACES` — last account seen for the break. // source: CBACT04C.cbl:167
- `WS-MONTHLY-INT S9(9)V99`, `WS-TOTAL-INT S9(9)V99`. // source: CBACT04C.cbl:168-169
- `WS-FIRST-TIME PIC X(1) VALUE 'Y'`. // source: CBACT04C.cbl:170
- `WS-RECORD-COUNT 9(9) VALUE 0`, `WS-TRANID-SUFFIX 9(6) VALUE 0`. // source: CBACT04C.cbl:172-173
- `END-OF-FILE PIC X(1) VALUE 'N'`. // source: CBACT04C.cbl:137
- `APPL-RESULT S9(9) COMP` with 88s `APPL-AOK`=0, `APPL-EOF`=16. // source: CBACT04C.cbl:133-135
- DB2 timestamp formatter group `COBOL-TS` / `DB2-FORMAT-TS X(26)` with REDEFINES splitting into `YYYY-MM-DD-HH.MM.SS.MILxxxx`. // source: CBACT04C.cbl:141-165

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = a method)

### MAIN (PROCEDURE DIVISION USING EXTERNAL-PARMS) — `Run` // source: CBACT04C.cbl:180-232
1. DISPLAY start banner. Call OPEN paragraphs in order: `0000-TCATBALF-OPEN`, `0100-XREFFILE-OPEN`, `0200-DISCGRP-OPEN`, `0300-ACCTFILE-OPEN`, `0400-TRANFILE-OPEN`. // source: 181-186
2. Main loop `PERFORM UNTIL END-OF-FILE = 'Y'`: // source: 188-222
   - `IF END-OF-FILE = 'N'` → `PERFORM 1000-TCATBALF-GET-NEXT`; then `IF END-OF-FILE = 'N'` (still not EOF after the read): // source: 189-191
     - `ADD 1 TO WS-RECORD-COUNT`; `DISPLAY TRAN-CAT-BAL-RECORD`. // source: 192-193
     - **Account break:** `IF TRANCAT-ACCT-ID NOT= WS-LAST-ACCT-NUM`: // source: 194
       - `IF WS-FIRST-TIME NOT = 'Y'` → `PERFORM 1050-UPDATE-ACCOUNT` (rewrite the *previous* account) `ELSE MOVE 'N' TO WS-FIRST-TIME`. // source: 195-199
       - `MOVE 0 TO WS-TOTAL-INT`; `MOVE TRANCAT-ACCT-ID TO WS-LAST-ACCT-NUM`; `MOVE TRANCAT-ACCT-ID TO FD-ACCT-ID`; `PERFORM 1100-GET-ACCT-DATA`; `MOVE TRANCAT-ACCT-ID TO FD-XREF-ACCT-ID`; `PERFORM 1110-GET-XREF-DATA`. // source: 200-205
     - (every record, inside or outside a break) `MOVE ACCT-GROUP-ID TO FD-DIS-ACCT-GROUP-ID`; `MOVE TRANCAT-CD TO FD-DIS-TRAN-CAT-CD`; `MOVE TRANCAT-TYPE-CD TO FD-DIS-TRAN-TYPE-CD`; `PERFORM 1200-GET-INTEREST-RATE`. // source: 210-213
     - `IF DIS-INT-RATE NOT = 0` → `PERFORM 1300-COMPUTE-INTEREST`; `PERFORM 1400-COMPUTE-FEES`. // source: 214-217
   - `ELSE` (END-OF-FILE became 'Y' at the top of this iteration's outer IF) → `PERFORM 1050-UPDATE-ACCOUNT` (final flush of the last account). // source: 219-220
3. After loop: CLOSE all five files (`9000`..`9400`), DISPLAY end banner, `GOBACK`. // source: 224-232

> **Flow nuance (port carefully):** the outer `IF END-OF-FILE='N' ... ELSE PERFORM 1050-UPDATE-ACCOUNT` means the *last* account's update happens when the loop re-enters after EOF was set by the prior `1000-TCATBALF-GET-NEXT`. Because `END-OF-FILE` is only set inside `1000-...-GET-NEXT`, the ELSE branch (line 220) runs exactly once, on the iteration immediately following the read that hit EOF. The `.NET` loop must replicate this: when the get-next sets EOF, do **not** process the (empty) record; on the next loop pass take the ELSE and flush the final account. // source: CBACT04C.cbl:188-221

### `0000-TCATBALF-OPEN` // source: 234-250
OPEN INPUT TCATBAL-FILE; status '00' → AOK else result 12; on not-AOK DISPLAY `'ERROR OPENING TRANSACTION CATEGORY BALANCE'`, display IO status, ABEND.

### `0100-XREFFILE-OPEN` // source: 252-268
OPEN INPUT XREF-FILE; on error DISPLAY `'ERROR OPENING CROSS REF FILE'` + status, then ABEND.

### `0200-DISCGRP-OPEN` // source: 270-286
OPEN INPUT DISCGRP-FILE; on error DISPLAY `'ERROR OPENING DALY REJECTS FILE'`, ABEND. (Message text is mislabeled — faithful bug.)

### `0300-ACCTFILE-OPEN` // source: 288-305
OPEN **I-O** ACCOUNT-FILE; on error DISPLAY `'ERROR OPENING ACCOUNT MASTER FILE'`, ABEND.

### `0400-TRANFILE-OPEN` // source: 306-323
OPEN **OUTPUT** TRANSACT-FILE; on error DISPLAY `'ERROR OPENING TRANSACTION FILE'`, ABEND.

### `1000-TCATBALF-GET-NEXT` // source: 324-348
`READ TCATBAL-FILE INTO TRAN-CAT-BAL-RECORD`. Status '00' → AOK; '10' → result 16 (EOF); else 12. If AOK continue; else if `APPL-EOF` (16) `MOVE 'Y' TO END-OF-FILE`; else DISPLAY `'ERROR READING TRANSACTION CATEGORY FILE'`, ABEND. → In .NET: advance the ordered cursor; no row → set EOF flag.

### `1050-UPDATE-ACCOUNT` // source: 349-370
`ADD WS-TOTAL-INT TO ACCT-CURR-BAL`; `MOVE 0 TO ACCT-CURR-CYC-CREDIT`; `MOVE 0 TO ACCT-CURR-CYC-DEBIT`; `REWRITE FD-ACCTFILE-REC FROM ACCOUNT-RECORD`. Status '00' → AOK; else DISPLAY `'ERROR RE-WRITING ACCOUNT FILE'`, ABEND. → `UPDATE ACCOUNT SET curr_bal = curr_bal + @totInt, curr_cyc_credit=0, curr_cyc_debit=0 WHERE acct_id=@id` (use the in-memory ACCOUNT-RECORD that was last read; the add is on the COBOL field then written back). **Arithmetic:** `ADD` of `WS-TOTAL-INT S9(9)V99` into `ACCT-CURR-BAL S9(10)V99` — result truncated/stored as S9(10)V99 (2 decimals), truncate toward zero, silent overflow beyond 10 integer digits. // source: 352-354

### `1100-GET-ACCT-DATA` // source: 371-391
`READ ACCOUNT-FILE INTO ACCOUNT-RECORD` (by `FD-ACCT-ID`) with `INVALID KEY DISPLAY 'ACCOUNT NOT FOUND: ' FD-ACCT-ID`. Then: status '00' → AOK; else 12 → DISPLAY `'ERROR READING ACCOUNT FILE'`, ABEND. → SELECT by PK; **note:** on not-found, the INVALID KEY clause prints the message but the subsequent status check sees status ≠ '00' (likely '23') → result 12 → ABEND. So a missing account aborts the run (after printing both messages). // source: 373-389

### `1110-GET-XREF-DATA` // source: 392-413
`READ XREF-FILE INTO CARD-XREF-RECORD KEY IS FD-XREF-ACCT-ID` (alt-key) with `INVALID KEY DISPLAY 'ACCOUNT NOT FOUND: ' FD-XREF-ACCT-ID`. Then status '00' → AOK else 12 → DISPLAY `'ERROR READING XREF FILE'`, ABEND. → SELECT by indexed `acct_id`; missing → ABEND (after message). Result populates `XREF-CARD-NUM` used by the write. // source: 394-410, 495

### `1200-GET-INTEREST-RATE` // source: 414-440
`READ DISCGRP-FILE INTO DIS-GROUP-RECORD` (by `FD-DISCGRP-KEY` = group/type/cat) with `INVALID KEY DISPLAY 'DISCLOSURE GROUP RECORD MISSING'` + `'TRY WITH DEFAULT GROUP CODE'`. Acceptance: **status '00' OR '23'** → AOK (result 0); else 12 → DISPLAY `'ERROR READING DISCLOSURE GROUP FILE'`, ABEND. Then `IF DISCGRP-STATUS = '23'` → `MOVE 'DEFAULT' TO FD-DIS-ACCT-GROUP-ID` and `PERFORM 1200-A-GET-DEFAULT-INT-RATE`. // source: 416-439
→ SELECT by composite PK. Not-found ('23') is tolerated and triggers a DEFAULT-group retry.

### `1200-A-GET-DEFAULT-INT-RATE` // source: 442-460
`READ DISCGRP-FILE INTO DIS-GROUP-RECORD` again — now the key has group-id = `'DEFAULT'` (with the same type/cat already moved in). Status '00' → AOK; else 12 → DISPLAY `'ERROR READING DEFAULT DISCLOSURE GROUP'`, ABEND. → SELECT `WHERE acct_group_id='DEFAULT' AND tran_type_cd=@t AND tran_cat_cd=@c`; missing → ABEND. // source: 444-458

### `1300-COMPUTE-INTEREST` // source: 461-470
`COMPUTE WS-MONTHLY-INT = ( TRAN-CAT-BAL * DIS-INT-RATE) / 1200`; `ADD WS-MONTHLY-INT TO WS-TOTAL-INT`; `PERFORM 1300-B-WRITE-TX`. **Arithmetic:** intermediate `bal(S9(9)V99) * rate(S9(4)V99)` then `/1200`, result stored to `WS-MONTHLY-INT S9(9)V99` (2 dp). COBOL truncates toward zero to 2 decimals (no ROUNDED clause). The accumulate then truncates again into `WS-TOTAL-INT S9(9)V99`. // source: 464-467

### `1300-B-WRITE-TX` // source: 472-515
1. `ADD 1 TO WS-TRANID-SUFFIX`. // source: 474
2. `STRING PARM-DATE, WS-TRANID-SUFFIX DELIMITED BY SIZE INTO TRAN-ID` → tran_id = 10-char PARM-DATE concatenated with the 6-digit zero-padded suffix (16 chars total = X(16)). // source: 476-480
3. `MOVE '01' TO TRAN-TYPE-CD`; `MOVE '05' TO TRAN-CAT-CD`; `MOVE 'System' TO TRAN-SOURCE`. // source: 482-484
4. `STRING 'Int. for a/c ', ACCT-ID DELIMITED BY SIZE INTO TRAN-DESC` → desc = `"Int. for a/c " + ACCT-ID(11 digits)` left-justified in X(100). // source: 485-489
5. `MOVE WS-MONTHLY-INT TO TRAN-AMT`; `MOVE 0 TO TRAN-MERCHANT-ID`; merchant name/city/zip = SPACES; `MOVE XREF-CARD-NUM TO TRAN-CARD-NUM`. // source: 490-495
6. `PERFORM Z-GET-DB2-FORMAT-TIMESTAMP`; `MOVE DB2-FORMAT-TS TO TRAN-ORIG-TS` and `TRAN-PROC-TS`. // source: 496-498
7. `WRITE FD-TRANFILE-REC FROM TRAN-RECORD`; status '00' → AOK else 12 → DISPLAY `'ERROR WRITING TRANSACTION RECORD'`, ABEND. // source: 500-513
→ `INSERT INTO TRANSACTION (...)` with the fields above.

### `1400-COMPUTE-FEES` // source: 517-520
Empty stub (`* To be implemented`, EXIT). No-op method.

### `9000-9400` CLOSE paragraphs // source: 521-611
CLOSE each file; status '00' → AOK else 12 → DISPLAY the file-specific close error, ABEND. Messages: `'ERROR CLOSING TRANSACTION BALANCE FILE'` (9000), `'ERROR CLOSING CROSS REF FILE'` (9100), `'ERROR CLOSING DISCLOSURE GROUP FILE'` (9200), `'ERROR CLOSING ACCOUNT FILE'` (9300), `'ERROR CLOSING TRANSACTION FILE'` (9400). In .NET these map to repository dispose/commit; keep as no-ops/commit but preserve banner ordering if logging.

### `Z-GET-DB2-FORMAT-TIMESTAMP` // source: 613-626
`MOVE FUNCTION CURRENT-DATE TO COBOL-TS`; copy YYYY/MM/DD/HH/MIN/SS/MIL into the DB2 layout; `MOVE '0000' TO DB2-REST`; set the three `-` separators (`DB2-STREEP-1/2/3`) and three `.` separators (`DB2-DOT-1/2/3`). Result `DB2-FORMAT-TS X(26)` = `YYYY-MM-DD-HH.MM.SS.MIL0000`. → C#: `IClock.Now` formatted `yyyy-MM-dd-HH.mm.ss.ffNNNN`... see PORT NOTES for the exact width handling of `DB2-MIL` (2 digits) + `DB2-REST` `'0000'`. // source: 613-624

### `9999-ABEND-PROGRAM` // source: 627-632
DISPLAY `'ABENDING PROGRAM'`; `MOVE 0 TO TIMING`; `MOVE 999 TO ABCODE`; `CALL 'CEE3ABD' USING ABCODE, TIMING`. → throw `AbendException(999)` via Runtime.Abend.

### `9910-DISPLAY-IO-STATUS` // source: 634-648
Formats the 2-byte file status into a 4-digit `IO-STATUS-04`. If status non-numeric OR first byte = '9': put stat1 in pos 1, take binary value of stat2 byte into a 3-digit field, DISPLAY `'FILE STATUS IS: NNNN'` + value. Else: `'0000'`, overlay the 2-char status in positions 3-4, DISPLAY. → diagnostic logging only; reproduce literal `FILE STATUS IS: NNNN` prefix.

---

## 5. VALIDATION RULES & EXACT LITERAL MESSAGES

This batch program has **no interactive validation**; all "rules" are I/O status checks that ABEND on failure, plus two tolerated not-found cases. Exact literals (reproduce verbatim):

| Trigger | Message | Line |
|---|---|---|
| Start | `START OF EXECUTION OF PROGRAM CBACT04C` | 181 |
| Per record | (DISPLAY of `TRAN-CAT-BAL-RECORD`) | 193 |
| Open TCATBAL err | `ERROR OPENING TRANSACTION CATEGORY BALANCE` | 245 |
| Open XREF err | `ERROR OPENING CROSS REF FILE` (+ status) | 263 |
| Open DISCGRP err | `ERROR OPENING DALY REJECTS FILE` *(mislabeled)* | 281 |
| Open ACCT err | `ERROR OPENING ACCOUNT MASTER FILE` | 300 |
| Open TRAN err | `ERROR OPENING TRANSACTION FILE` | 318 |
| Read TCATBAL err | `ERROR READING TRANSACTION CATEGORY FILE` | 342 |
| Rewrite ACCT err | `ERROR RE-WRITING ACCOUNT FILE` | 365 |
| ACCT not found | `ACCOUNT NOT FOUND: ` + FD-ACCT-ID | 375 |
| Read ACCT err | `ERROR READING ACCOUNT FILE` | 386 |
| XREF not found | `ACCOUNT NOT FOUND: ` + FD-XREF-ACCT-ID | 397 |
| Read XREF err | `ERROR READING XREF FILE` | 408 |
| DISCGRP missing | `DISCLOSURE GROUP RECORD MISSING` / `TRY WITH DEFAULT GROUP CODE` | 418-419 |
| Read DISCGRP err | `ERROR READING DISCLOSURE GROUP FILE` | 431 |
| Read default DISCGRP err | `ERROR READING DEFAULT DISCLOSURE GROUP` | 455 |
| Write TRAN err | `ERROR WRITING TRANSACTION RECORD` | 510 |
| Close errors (5) | see §4 9000-9400 | 533,552,570,588,606 |
| File status | `FILE STATUS IS: NNNN` + IO-STATUS-04 | 642,646 |
| Abend | `ABENDING PROGRAM` | 629 |
| End | `END OF EXECUTION OF PROGRAM CBACT04C` | 230 |

**Tolerated (non-abend) conditions:** DISCGRP specific-key not found (status '23') → retry with `'DEFAULT'` group. // source: 422, 436-438

---

## 6. FAITHFUL BUGS (reproduce verbatim — DO NOT FIX)

1. **Mislabeled DISCGRP open error message.** `0200-DISCGRP-OPEN` prints `'ERROR OPENING DALY REJECTS FILE'` though it is opening the disclosure-group file. // source: CBACT04C.cbl:281

2. **`INVALID KEY` masks a hard error in 1100/1110.** In `1100-GET-ACCT-DATA` and `1110-GET-XREF-DATA`, a not-found triggers `INVALID KEY DISPLAY 'ACCOUNT NOT FOUND: ...'`, but the very next status check (`IF status='00' ... ELSE 12 → ABEND`) still aborts the program. So a missing account/xref prints "ACCOUNT NOT FOUND" **and then abends** — the displayed message implies graceful handling that does not occur. Reproduce: print the not-found message, then abend. // source: CBACT04C.cbl:373-389, 394-410

3. **Interest TRANSACTION written even when account/xref data is stale across non-break records.** `ACCT-GROUP-ID`, `ACCT-ID`, and `XREF-CARD-NUM` are only refreshed on an account break (lines 200-205), but interest is computed for *every* TCATBAL record (lines 210-217). Within one account this is intended; but the transaction `desc`/`card-num` always use the last-read account/xref — correct only because TCATBAL is grouped by account. No bug if input is key-ordered (it is, sequential KSDS), but the port must keep the account/xref load strictly on the break to be faithful, not per record. // source: CBACT04C.cbl:194-217

4. **`TRAN-CAT-CD` literal mismatch.** `MOVE '05' TO TRAN-CAT-CD` where `TRAN-CAT-CD` is `PIC 9(4)`. Moving the 2-char literal `'05'` into a 9(4) numeric field yields `0005`. Reproduce the value `0005` (not `05`). Likewise `MOVE '01' TO TRAN-TYPE-CD` (X(2)) → `01`. // source: CBACT04C.cbl:482-483

5. **`DB2-MIL` is only 2 digits + fixed `'0000'` REST.** `FUNCTION CURRENT-DATE` returns hundredths in `COB-MIL X(2)`; the DB2 timestamp puts those 2 digits in `DB2-MIL PIC 9(2)` followed by literal `'0000'` in `DB2-REST X(4)`. The microsecond portion is therefore always `HH0000` (hundredths then four zeros), never true microseconds. Reproduce: `...SS.<hundredths>0000`. // source: CBACT04C.cbl:621-622, 164-165

6. **`1400-COMPUTE-FEES` is a no-op.** Fees are never computed despite being called every interest-bearing record. // source: CBACT04C.cbl:516-520

7. **Interest truncation, not rounding.** `COMPUTE WS-MONTHLY-INT = (TRAN-CAT-BAL * DIS-INT-RATE)/1200` with no `ROUNDED` → truncate toward zero at 2 decimals. Must NOT round-half-up. // source: CBACT04C.cbl:464-465

---

## 7. PORT NOTES (relational translation + tricky COBOL semantics)

- **Sequential driver over `TRAN_CAT_BAL`:** open an ordered forward cursor `ORDER BY acct_id, type_cd, cat_cd`. EOF → set the `END-OF-FILE` flag; this is what arms the final `1050-UPDATE-ACCOUNT` flush. Keep the exact loop structure (read, then process only if still not EOF; the ELSE flush fires on the next pass). // source: CBACT04C.cbl:188-221, 326-348

- **Account break + final flush:** maintain `WS-LAST-ACCT-NUM` (X(11), init spaces) and `WS-FIRST-TIME='Y'`. On a new acct id: if not first time, REWRITE (`UPDATE`) the previously-loaded account; then reset `WS-TOTAL-INT=0`, set last-acct, reload ACCOUNT + XREF. The last account is flushed by the post-EOF ELSE branch. // source: CBACT04C.cbl:194-205, 219-220

- **`WS-LAST-ACCT-NUM` is X(11) compared against `TRANCAT-ACCT-ID` 9(11).** COBOL compares the numeric to the alphanumeric by aligning; the initial `SPACES` value guarantees the first record is always a break. In .NET compare as the canonical 11-char zero-padded string (TRANCAT-ACCT-ID rendered as 11 digits) vs the stored last value; seed last with a non-matching sentinel so record 1 breaks. // source: CBACT04C.cbl:167, 194, 201

- **ACCOUNT REWRITE arithmetic:** add `WS-TOTAL-INT (S9(9)V99)` to `ACCT-CURR-BAL (S9(10)V99)`, store as S9(10)V99 (use `CobolDecimal` truncate-toward-zero, silent overflow). Then set `curr_cyc_credit=0`, `curr_cyc_debit=0`. The REWRITE uses the in-memory `ACCOUNT-RECORD` last loaded for that account — so the UPDATE writes all account columns back (or at minimum the three mutated columns; balance is the only changed one besides the two zeroed cycle fields). // source: CBACT04C.cbl:352-356

- **Interest COMPUTE:** `WS-MONTHLY-INT = TRAN-CAT-BAL * DIS-INT-RATE / 1200`. Use `decimal` exact arithmetic in the order `(bal * rate)` then `/1200`, then truncate to 2 dp toward zero (no rounding). Accumulate into `WS-TOTAL-INT` truncating to 2 dp. `DIS-INT-RATE S9(4)V99` (e.g. 19.99). The `IF DIS-INT-RATE NOT = 0` guard at line 214 skips both interest and the fees stub. // source: CBACT04C.cbl:214-217, 464-467

- **DISCGRP DEFAULT fallback:** SELECT specific `(group,type,cat)`; if missing ('23'), set group-id to literal `'DEFAULT'` (10-char, space-padded) and re-SELECT. A missing DEFAULT row abends. Preserve the `'00' OR '23'` acceptance at line 422 — a genuine I/O error (other status) abends. // source: CBACT04C.cbl:422, 436-438, 444-458

- **TRAN-ID build (STRING DELIMITED BY SIZE):** concatenate `PARM-DATE` (10) + `WS-TRANID-SUFFIX` rendered as its display image. `WS-TRANID-SUFFIX PIC 9(6)` → 6-digit zero-padded → total 16 chars exactly filling `TRAN-ID X(16)`. The suffix increments per written transaction across the whole run (not reset per account). // source: CBACT04C.cbl:474-480, 173

- **TRAN-DESC build:** `"Int. for a/c " + ACCT-ID`. `'Int. for a/c '` is 13 chars; `ACCT-ID` 9(11) → 11 digit chars; result 24 chars left-justified, space-padded to X(100). Use the ACCT-ID from the last-loaded ACCOUNT-RECORD. // source: CBACT04C.cbl:485-489

- **Timestamp (`DB2-FORMAT-TS`):** format `IClock.Now` as `yyyy-MM-dd-HH.mm.ss.` + 2-digit hundredths + `0000` → 26 chars. For golden-master tests the orig/proc timestamps are masked (per ARCHITECTURE.md verification §2). // source: CBACT04C.cbl:613-624

- **Edited / signed-zoned fields:** numeric record fields (`TRAN-CAT-BAL`, `DIS-INT-RATE`, `ACCT-CURR-BAL`, amounts) map to `decimal`; on re-serialize to the canonical fixed-width image use the Runtime signed-zoned/COMP-3 serializer only at the file boundary (the relational store keeps typed values). `TRAN-MERCHANT-ID 9(9)` set to 0; merchant name/city/zip = spaces (X-fields keep full width). // source: CBACT04C.cbl:490-494; CVTRA05Y.cpy:11-14

- **REDEFINES `TWO-BYTES-BINARY`/`TWO-BYTES-ALPHA` and `IO-STATUS-04`** are pure diagnostic formatting in `9910`; reproduce only the displayed string `FILE STATUS IS: NNNN` if logging file-status diagnostics. // source: CBACT04C.cbl:125-131, 635-647

- **Output file is DISP=(NEW,...):** each run produces a *fresh* TRANSACTION dataset. For the relational port, treat the run as: clear/replace the run's interest transactions (or insert into an empty target per characterization fixture). The program itself never reads TRANSACT back. // source: INTCALC.jcl:37-41; CBACT04C.cbl:309, 500

---

## 8. OPEN QUESTIONS / RISKS

1. **TRANSACTION output semantics in relational mode.** The COBOL writes to a brand-new sequential dataset every run (DISP=NEW). In the shared `TRANSACTION` table, repeated runs would collide on `tran_id` (PARM-DATE + suffix). Decision needed: truncate-then-insert per run, or run into an isolated/empty table for characterization. Recommend: characterization run inserts into an empty `TRANSACTION` (or a dedicated interest-output view) and the golden diff masks timestamps. // source: CBACT04C.cbl:309, 500; INTCALC.jcl:37-41

2. **`tran_id` uniqueness vs suffix width.** `WS-TRANID-SUFFIX PIC 9(6)` wraps after 999999 interest transactions in a single run; faithful behavior is silent wrap (overflow drops high digit) — unlikely in practice but pin a test only if fixtures approach the limit. // source: CBACT04C.cbl:474, 173

3. **Per-record vs per-break interest.** Confirmed: interest is computed for **every** TCATBAL record (lines 210-217), account/xref refreshed only on break. Faithful only if TCATBAL is strictly key-ordered by account (guaranteed by sequential KSDS read). The port's ordered cursor preserves this. // source: CBACT04C.cbl:194-217

4. **Hundredths source from `FUNCTION CURRENT-DATE`.** `COB-MIL X(2)` is hundredths-of-second; reproduced as 2 digits + `0000`. If the .NET clock provides milliseconds, take hundredths (`ms/10`, 2 digits) to stay faithful. // source: CBACT04C.cbl:621-622
