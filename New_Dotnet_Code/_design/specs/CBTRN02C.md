# PORT SPEC — CBTRN02C (Daily Transaction Posting, BATCH)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBTRN02C.cbl`
> JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/POSTTRAN.jcl`
> Target: `src/CardDemo.Batch` over the relational SQLite schema defined in `_design/ARCHITECTURE.md`.
> All line citations below refer to `CBTRN02C.cbl` unless otherwise stated.

---

## 1. Purpose & Invocation

**Purpose.** CBTRN02C is the daily **transaction posting** batch program. It reads the *Daily Transaction* file (DALYTRAN, a sequential PS dataset) record-by-record. For each daily transaction it (a) validates it — looks up the card number in the card cross-reference, then loads the matching account master and performs credit-limit and account-expiration checks; (b) if valid, **posts** it: updates the transaction-category-balance file (creating the balance row if it does not yet exist), updates the account master's current balance and cycle credit/debit, and writes the transaction into the transaction master file; (c) if invalid, increments a reject counter and writes the original record plus an 80-byte validation trailer (fail reason code + description) to the daily rejects file. At end of run it displays processed/rejected counts and, if any record was rejected, sets the job return code to 4. // source: CBTRN02C.cbl:1-6, 193-234

**Invocation.** Standalone batch program, run by JCL job `POSTTRAN`, step **`STEP15`**: `EXEC PGM=CBTRN02C`. No PARM is passed (no LINKAGE SECTION); the program is driven entirely by its files. // source: POSTTRAN.jcl:23; CBTRN02C.cbl:22-24, 193

**DD → table/file mapping (from JCL):**
| DD name | DSN (VSAM/QSAM) | Logical file | Relational target |
|---|---|---|---|
| DALYTRAN | ...DALYTRAN.PS (input, sequential) | DALYTRAN-FILE | `DAILY_TRANSACTION` |
| TRANFILE | ...TRANSACT.VSAM.KSDS (**OUTPUT**) | TRANSACT-FILE | `TRANSACTION` |
| XREFFILE | ...CARDXREF.VSAM.KSDS (input, random) | XREF-FILE | `CARD_XREF` |
| DALYREJS | ...DALYREJS(+1) GDG, RECFM=F LRECL=430 (**NEW**) | DALYREJS-FILE | reject output dataset (no base table; sequential reject file) |
| ACCTFILE | ...ACCTDATA.VSAM.KSDS (**I-O**) | ACCOUNT-FILE | `ACCOUNT` |
| TCATBALF | ...TCATBALF.VSAM.KSDS (**I-O**) | TCATBAL-FILE | `TRAN_CAT_BAL` |
// source: POSTTRAN.jcl:28-42; CBTRN02C.cbl:29-61

> Note: `TRANFILE` is opened **OUTPUT** (not I-O), so the transaction master is logically rewritten/loaded each run — the relational port should treat it as truncate-then-insert per run (see §2 / §6). The JCL `DISP=SHR` is cosmetic here; the COBOL `OPEN OUTPUT` is authoritative. // source: CBTRN02C.cbl:256; POSTTRAN.jcl:28-29

---

## 2. FILE / TABLE ACCESS TABLE

| COBOL file | Org / Access | Record key | Relational table | Ops used | SQL mapping |
|---|---|---|---|---|---|
| `DALYTRAN-FILE` (DALYTRAN) | SEQUENTIAL | (none) | `DAILY_TRANSACTION` | OPEN INPUT; READ … INTO (sequential next) | `SELECT * FROM DAILY_TRANSACTION ORDER BY <load order / tran_id>` — forward cursor; each READ = next row; EOF → FileStatus '10'. // source: CBTRN02C.cbl:29-32, 238, 346 |
| `XREF-FILE` (XREFFILE) | INDEXED, RANDOM | `FD-XREF-CARD-NUM` X(16) | `CARD_XREF` | OPEN INPUT; READ … INTO (by PK, INVALID KEY) | `SELECT * FROM CARD_XREF WHERE xref_card_num = @cardnum` — PK read; not found → FileStatus '23' → INVALID KEY branch. // source: CBTRN02C.cbl:40-44, 275, 382-391 |
| `ACCOUNT-FILE` (ACCTFILE) | INDEXED, RANDOM | `FD-ACCT-ID` 9(11) | `ACCOUNT` | OPEN **I-O**; READ … INTO (by PK, INVALID KEY); **REWRITE** (INVALID KEY) | READ: `SELECT * FROM ACCOUNT WHERE acct_id = @id`; REWRITE: `UPDATE ACCOUNT SET ... WHERE acct_id = @id` (missing → '23' → INVALID KEY). // source: CBTRN02C.cbl:51-55, 311, 394-421, 554-559 |
| `TCATBAL-FILE` (TCATBALF) | INDEXED, RANDOM | `FD-TRAN-CAT-KEY` = acct_id 9(11) + type_cd X(2) + cat_cd 9(4) (17 bytes) | `TRAN_CAT_BAL` | OPEN **I-O**; READ … INTO (by composite PK, INVALID KEY); **WRITE**; **REWRITE** | READ: `SELECT * FROM TRAN_CAT_BAL WHERE acct_id=@a AND type_cd=@t AND cat_cd=@c` (missing → '23' → INVALID KEY → create); WRITE: `INSERT`; REWRITE: `UPDATE … WHERE` composite PK. // source: CBTRN02C.cbl:57-61, 329, 467-542 |
| `TRANSACT-FILE` (TRANFILE) | INDEXED, RANDOM | `FD-TRANS-ID` X(16) | `TRANSACTION` | OPEN **OUTPUT**; **WRITE** (from TRAN-RECORD) | Each WRITE = `INSERT INTO TRANSACTION (...)` keyed on tran_id. OPEN OUTPUT ⇒ logical load-from-empty per run (truncate `TRANSACTION` then insert). Duplicate tran_id → '22' → ABEND. // source: CBTRN02C.cbl:34-38, 256, 562-579 |
| `DALYREJS-FILE` (DALYREJS) | SEQUENTIAL | (none — output) | (reject dataset; not a base table) | OPEN OUTPUT; **WRITE** (430-byte fixed: 350-byte tran image + 80-byte trailer) | Append-into-empty reject dataset per run. Port: write to a `DAILY_REJECT` sink table (`reject_tran_data X(350)`, `validation_fail_reason 9(4)`, `validation_fail_reason_desc X(76)`) or to a fixed-width reject file matching RECFM=F LRECL=430. // source: CBTRN02C.cbl:46-49, 293, 446-465; POSTTRAN.jcl:34-38 |

**Repository contract (per ARCHITECTURE.md §VSAM→SQL):** READ key → SELECT by PK ('00'/'23'); sequential READ → ORDER BY key forward cursor (EOF→'10'); WRITE → INSERT (dup → '22'); REWRITE → UPDATE (missing → '23'); DELETE → DELETE. The `'00' OR '23'`-tolerant read + "create on '23'" idiom in `2700-UPDATE-TCATBAL` is preserved at the program layer (see §4). // source: ARCHITECTURE.md:80-89; CBTRN02C.cbl:474-499

---

## 3. WORKING-STORAGE / record layouts (typed)

Record copybooks (one column per elementary field per ARCHITECTURE.md):

- `DALYTRAN-RECORD` (CVTRA06Y, RECLN 350): `DALYTRAN-ID X(16)`, `DALYTRAN-TYPE-CD X(2)`, `DALYTRAN-CAT-CD 9(4)`, `DALYTRAN-SOURCE X(10)`, `DALYTRAN-DESC X(100)`, `DALYTRAN-AMT S9(9)V99`, `DALYTRAN-MERCHANT-ID 9(9)`, `DALYTRAN-MERCHANT-NAME X(50)`, `DALYTRAN-MERCHANT-CITY X(50)`, `DALYTRAN-MERCHANT-ZIP X(10)`, `DALYTRAN-CARD-NUM X(16)`, `DALYTRAN-ORIG-TS X(26)`, `DALYTRAN-PROC-TS X(26)`, FILLER X(20). → `DAILY_TRANSACTION`. // source: CVTRA06Y.cpy:4-18
- `TRAN-RECORD` (CVTRA05Y, RECLN 350): `TRAN-ID X(16)`, `TRAN-TYPE-CD X(2)`, `TRAN-CAT-CD 9(4)`, `TRAN-SOURCE X(10)`, `TRAN-DESC X(100)`, `TRAN-AMT S9(9)V99`, `TRAN-MERCHANT-ID 9(9)`, `TRAN-MERCHANT-NAME X(50)`, `TRAN-MERCHANT-CITY X(50)`, `TRAN-MERCHANT-ZIP X(10)`, `TRAN-CARD-NUM X(16)`, `TRAN-ORIG-TS X(26)`, `TRAN-PROC-TS X(26)`, FILLER X(20). → `TRANSACTION`. // source: CVTRA05Y.cpy:4-18
- `CARD-XREF-RECORD` (CVACT03Y, RECLN 50): `XREF-CARD-NUM X(16)`, `XREF-CUST-ID 9(9)`, `XREF-ACCT-ID 9(11)`, FILLER X(14). → `CARD_XREF`. // source: CVACT03Y.cpy:4-8
- `ACCOUNT-RECORD` (CVACT01Y, RECLN 300): `ACCT-ID 9(11)`, `ACCT-ACTIVE-STATUS X(1)`, `ACCT-CURR-BAL S9(10)V99`, `ACCT-CREDIT-LIMIT S9(10)V99`, `ACCT-CASH-CREDIT-LIMIT S9(10)V99`, `ACCT-OPEN-DATE X(10)`, `ACCT-EXPIRAION-DATE X(10)`, `ACCT-REISSUE-DATE X(10)`, `ACCT-CURR-CYC-CREDIT S9(10)V99`, `ACCT-CURR-CYC-DEBIT S9(10)V99`, `ACCT-ADDR-ZIP X(10)`, `ACCT-GROUP-ID X(10)`, FILLER X(178). → `ACCOUNT`. // source: CVACT01Y.cpy:4-17
- `TRAN-CAT-BAL-RECORD` (CVTRA01Y, RECLN 50): `TRAN-CAT-KEY` { `TRANCAT-ACCT-ID 9(11)`, `TRANCAT-TYPE-CD X(2)`, `TRANCAT-CD 9(4)` }, `TRAN-CAT-BAL S9(9)V99`, FILLER X(22). → `TRAN_CAT_BAL`. // source: CVTRA01Y.cpy:4-10

**FD record layouts (used only as the I/O buffers; READ/WRITE move INTO/FROM the WS copybook records):**
- `FD-TRAN-RECORD`: FD-TRAN-ID X(16) + FD-CUST-DATA X(334). (DALYTRAN buffer; READ INTO DALYTRAN-RECORD) // source: CBTRN02C.cbl:66-69
- `FD-TRANFILE-REC`: FD-TRANS-ID X(16) + FD-ACCT-DATA X(334). (TRANSACT buffer; key FD-TRANS-ID; WRITE FROM TRAN-RECORD) // source: CBTRN02C.cbl:71-74
- `FD-XREFFILE-REC`: FD-XREF-CARD-NUM X(16) + FD-XREF-DATA X(34). (key FD-XREF-CARD-NUM; READ INTO CARD-XREF-RECORD) // source: CBTRN02C.cbl:76-79
- `FD-REJS-RECORD`: FD-REJECT-RECORD X(350) + FD-VALIDATION-TRAILER X(80) (= 430 bytes). // source: CBTRN02C.cbl:81-84
- `FD-ACCTFILE-REC`: FD-ACCT-ID 9(11) + FD-ACCT-DATA X(289). (key FD-ACCT-ID; READ/REWRITE INTO/FROM ACCOUNT-RECORD) // source: CBTRN02C.cbl:86-89
- `FD-TRAN-CAT-BAL-RECORD`: FD-TRAN-CAT-KEY { FD-TRANCAT-ACCT-ID 9(11), FD-TRANCAT-TYPE-CD X(2), FD-TRANCAT-CD 9(4) } + FD-FD-TRAN-CAT-DATA X(33). // source: CBTRN02C.cbl:91-97

**Key working vars / flags:**
- File-status pairs: `DALYTRAN-STATUS`, `TRANFILE-STATUS`, `XREFFILE-STATUS`, `DALYREJS-STATUS`, `ACCTFILE-STATUS`, `TCATBALF-STATUS` (each = 2× PIC X). // source: CBTRN02C.cbl:103-129
- `IO-STATUS` (IO-STAT1/IO-STAT2), `TWO-BYTES-BINARY 9(4) BINARY` redefined as TWO-BYTES-LEFT/RIGHT, `IO-STATUS-04` (IO-STATUS-0401 9, IO-STATUS-0403 999). // source: CBTRN02C.cbl:131-140
- `APPL-RESULT S9(9) COMP` with 88s `APPL-AOK`=0, `APPL-EOF`=16. // source: CBTRN02C.cbl:142-144
- `END-OF-FILE X(1) VALUE 'N'`. // source: CBTRN02C.cbl:146
- `ABCODE S9(9) BINARY`, `TIMING S9(9) BINARY`. // source: CBTRN02C.cbl:147-148
- `COBOL-TS` group (COB-YYYY/MM/DD/HH/MIN/SS/MIL/REST) and `DB2-FORMAT-TS X(26)` redefined into DB2-YYYY, separators, etc. — the DB2-style processing timestamp. // source: CBTRN02C.cbl:150-174
- `REJECT-RECORD` (REJECT-TRAN-DATA X(350) + VALIDATION-TRAILER X(80)). // source: CBTRN02C.cbl:176-178
- `WS-VALIDATION-TRAILER` (WS-VALIDATION-FAIL-REASON 9(4) + WS-VALIDATION-FAIL-REASON-DESC X(76)). // source: CBTRN02C.cbl:180-182
- `WS-COUNTERS`: `WS-TRANSACTION-COUNT 9(9)`, `WS-REJECT-COUNT 9(9)`, `WS-TEMP-BAL S9(9)V99`. // source: CBTRN02C.cbl:184-187
- `WS-FLAGS`: `WS-CREATE-TRANCAT-REC X(1) VALUE 'N'`. // source: CBTRN02C.cbl:189-190

---

## 4. PARAGRAPH-BY-PARAGRAPH OUTLINE (every paragraph = one method)

**MAIN (unnamed, top of PROCEDURE DIVISION).** // source: CBTRN02C.cbl:193-234
1. DISPLAY start banner; PERFORM the six OPEN paragraphs in order: 0000-DALYTRAN-OPEN, 0100-TRANFILE-OPEN, 0200-XREFFILE-OPEN, 0300-DALYREJS-OPEN, 0400-ACCTFILE-OPEN, 0500-TCATBALF-OPEN. // source: CBTRN02C.cbl:194-200
2. PERFORM UNTIL END-OF-FILE='Y': if END-OF-FILE='N' → 1000-DALYTRAN-GET-NEXT; if still 'N' → ADD 1 to WS-TRANSACTION-COUNT, reset WS-VALIDATION-FAIL-REASON=0 and desc=SPACES, PERFORM 1500-VALIDATE-TRAN. // source: CBTRN02C.cbl:202-210
3. If WS-VALIDATION-FAIL-REASON = 0 → PERFORM 2000-POST-TRANSACTION; ELSE ADD 1 to WS-REJECT-COUNT and PERFORM 2500-WRITE-REJECT-REC. // source: CBTRN02C.cbl:211-219
4. After loop: PERFORM the six CLOSE paragraphs (9000…9500); DISPLAY processed and rejected counts; IF WS-REJECT-COUNT > 0 → MOVE 4 TO RETURN-CODE; DISPLAY end banner; GOBACK. // source: CBTRN02C.cbl:221-234

**0000-DALYTRAN-OPEN.** OPEN INPUT DALYTRAN-FILE; if status='00' APPL-RESULT=0 else 12; if not AOK → DISPLAY 'ERROR OPENING DALYTRAN', set IO-STATUS, 9910-DISPLAY-IO-STATUS, 9999-ABEND-PROGRAM. // source: CBTRN02C.cbl:236-252

**0100-TRANFILE-OPEN.** OPEN **OUTPUT** TRANSACT-FILE; same status/AOK/abend pattern; error text 'ERROR OPENING TRANSACTION FILE'. // source: CBTRN02C.cbl:254-270

**0200-XREFFILE-OPEN.** OPEN INPUT XREF-FILE; same pattern; error text 'ERROR OPENING CROSS REF FILE'. // source: CBTRN02C.cbl:272-289

**0300-DALYREJS-OPEN.** OPEN OUTPUT DALYREJS-FILE; same pattern; error text 'ERROR OPENING DALY REJECTS FILE'. // source: CBTRN02C.cbl:291-307

**0400-ACCTFILE-OPEN.** OPEN **I-O** ACCOUNT-FILE; same pattern; error text 'ERROR OPENING ACCOUNT MASTER FILE'. // source: CBTRN02C.cbl:309-325

**0500-TCATBALF-OPEN.** OPEN **I-O** TCATBAL-FILE; same pattern; error text 'ERROR OPENING TRANSACTION BALANCE FILE'. // source: CBTRN02C.cbl:327-343

**1000-DALYTRAN-GET-NEXT.** READ DALYTRAN-FILE INTO DALYTRAN-RECORD. If status='00' → APPL-RESULT=0; elif status='10' → APPL-RESULT=16 (EOF) else 12. If AOK CONTINUE; elif APPL-EOF → MOVE 'Y' TO END-OF-FILE; else DISPLAY 'ERROR READING DALYTRAN FILE' + IO-STATUS + abend. // source: CBTRN02C.cbl:345-369
> Port: sequential cursor `MoveNext()`; '00'→record loaded, '10'→EOF sets END-OF-FILE='Y'; any other status aborts.

**1500-VALIDATE-TRAN.** PERFORM 1500-A-LOOKUP-XREF. IF WS-VALIDATION-FAIL-REASON = 0 → PERFORM 1500-B-LOOKUP-ACCT; ELSE CONTINUE. Comment "ADD MORE VALIDATIONS HERE". // source: CBTRN02C.cbl:370-378

**1500-A-LOOKUP-XREF.** MOVE DALYTRAN-CARD-NUM TO FD-XREF-CARD-NUM; READ XREF-FILE INTO CARD-XREF-RECORD. INVALID KEY → MOVE 100 TO WS-VALIDATION-FAIL-REASON, MOVE 'INVALID CARD NUMBER FOUND' TO desc. NOT INVALID KEY → CONTINUE. // source: CBTRN02C.cbl:380-392

**1500-B-LOOKUP-ACCT.** MOVE XREF-ACCT-ID TO FD-ACCT-ID; READ ACCOUNT-FILE INTO ACCOUNT-RECORD. // source: CBTRN02C.cbl:393-395
- INVALID KEY → MOVE 101, MOVE 'ACCOUNT RECORD NOT FOUND' TO desc. // source: CBTRN02C.cbl:396-399
- NOT INVALID KEY:
  - `COMPUTE WS-TEMP-BAL = ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT`. WS-TEMP-BAL is S9(9)V99 (9 integer + 2 frac); operands are S9(10)V99 / S9(9)V99 — **result truncated to S9(9)V99** on store (drop fractional digits beyond 2 toward zero; integer overflow silently truncated). // source: CBTRN02C.cbl:403-405
  - IF ACCT-CREDIT-LIMIT >= WS-TEMP-BAL → CONTINUE; ELSE MOVE 102, MOVE 'OVERLIMIT TRANSACTION'. // source: CBTRN02C.cbl:407-413
  - IF ACCT-EXPIRAION-DATE >= DALYTRAN-ORIG-TS (1:10) → CONTINUE; ELSE MOVE 103, MOVE 'TRANSACTION RECEIVED AFTER ACCT EXPIRATION'. (string >= compare: ACCT-EXPIRAION-DATE X(10) vs first 10 chars of DALYTRAN-ORIG-TS X(26).) // source: CBTRN02C.cbl:414-420
> Note: the credit-limit check and the expiration check are **independent sequential IFs** — the second always runs and can overwrite reason 102 with 103 (last-writer-wins). See §6 / Faithful bug. // source: CBTRN02C.cbl:407-420

**2000-POST-TRANSACTION.** Build TRAN-RECORD from DALYTRAN-RECORD field-by-field: TRAN-ID←DALYTRAN-ID, TYPE-CD, CAT-CD, SOURCE, DESC, AMT, MERCHANT-ID, MERCHANT-NAME, MERCHANT-CITY, MERCHANT-ZIP, CARD-NUM, ORIG-TS. Then PERFORM Z-GET-DB2-FORMAT-TIMESTAMP; MOVE DB2-FORMAT-TS TO TRAN-PROC-TS. Then PERFORM 2700-UPDATE-TCATBAL, 2800-UPDATE-ACCOUNT-REC, 2900-WRITE-TRANSACTION-FILE (in that order). // source: CBTRN02C.cbl:424-444
> Note: TRAN-PROC-TS is set from the *current* run clock (Z-GET-DB2-FORMAT-TIMESTAMP), NOT from DALYTRAN-PROC-TS. // source: CBTRN02C.cbl:437-438

**2500-WRITE-REJECT-REC.** MOVE DALYTRAN-RECORD TO REJECT-TRAN-DATA; MOVE WS-VALIDATION-TRAILER TO VALIDATION-TRAILER; MOVE 8 TO APPL-RESULT; WRITE FD-REJS-RECORD FROM REJECT-RECORD. If DALYREJS-STATUS='00' APPL-RESULT=0 else 12; if not AOK → DISPLAY 'ERROR WRITING TO REJECTS FILE' + IO-STATUS + abend. // source: CBTRN02C.cbl:446-465
> The 430-byte reject record = 350-byte original daily-tran image + 80-byte trailer; trailer = WS-VALIDATION-FAIL-REASON (4-digit) + desc X(76). // source: CBTRN02C.cbl:176-182, 447-451

**2700-UPDATE-TCATBAL.** MOVE XREF-ACCT-ID, DALYTRAN-TYPE-CD, DALYTRAN-CAT-CD into FD-TRAN-CAT-KEY (acct/type/cat); MOVE 'N' TO WS-CREATE-TRANCAT-REC; READ TCATBAL-FILE INTO TRAN-CAT-BAL-RECORD; INVALID KEY → DISPLAY 'TCATBAL record not found for key : ' FD-TRAN-CAT-KEY '.. Creating.' and MOVE 'Y' TO WS-CREATE-TRANCAT-REC. Then **IF TCATBALF-STATUS = '00' OR '23' → APPL-RESULT=0 ELSE 12** (so '23' is tolerated); if not AOK → DISPLAY 'ERROR READING TRANSACTION BALANCE FILE' + abend. Finally: IF WS-CREATE-TRANCAT-REC='Y' → 2700-A-CREATE-TCATBAL-REC ELSE 2700-B-UPDATE-TCATBAL-REC. // source: CBTRN02C.cbl:467-501

**2700-A-CREATE-TCATBAL-REC.** INITIALIZE TRAN-CAT-BAL-RECORD (all elementary fields to zero/spaces); MOVE XREF-ACCT-ID TO TRANCAT-ACCT-ID; MOVE DALYTRAN-TYPE-CD TO TRANCAT-TYPE-CD; MOVE DALYTRAN-CAT-CD TO TRANCAT-CD; `ADD DALYTRAN-AMT TO TRAN-CAT-BAL` (from 0 → = DALYTRAN-AMT, S9(9)V99). WRITE FD-TRAN-CAT-BAL-RECORD FROM TRAN-CAT-BAL-RECORD; if status='00' AOK else 12; on error DISPLAY 'ERROR WRITING TRANSACTION BALANCE FILE' + abend. // source: CBTRN02C.cbl:503-524

**2700-B-UPDATE-TCATBAL-REC.** `ADD DALYTRAN-AMT TO TRAN-CAT-BAL` (accumulate into existing S9(9)V99 balance; silent overflow on carry beyond 9 integer digits). REWRITE FD-TRAN-CAT-BAL-RECORD FROM TRAN-CAT-BAL-RECORD; if status='00' AOK else 12; on error DISPLAY 'ERROR REWRITING TRANSACTION BALANCE FILE' + abend. // source: CBTRN02C.cbl:526-542

**2800-UPDATE-ACCOUNT-REC.** `ADD DALYTRAN-AMT TO ACCT-CURR-BAL`; IF DALYTRAN-AMT >= 0 → `ADD DALYTRAN-AMT TO ACCT-CURR-CYC-CREDIT` ELSE `ADD DALYTRAN-AMT TO ACCT-CURR-CYC-DEBIT` (negative amount accumulated into debit — note debit grows more negative). REWRITE FD-ACCTFILE-REC FROM ACCOUNT-RECORD; INVALID KEY → MOVE 109 TO WS-VALIDATION-FAIL-REASON, MOVE 'ACCOUNT RECORD NOT FOUND' TO desc. // source: CBTRN02C.cbl:545-560
> Note: account/cycle fields are S9(10)V99; DALYTRAN-AMT is S9(9)V99 — added at full precision, stored truncated-to-2-frac. The INVALID-KEY here sets reason 109 *after* posting already updated TCATBAL — see §6. // source: CBTRN02C.cbl:547-559

**2900-WRITE-TRANSACTION-FILE.** MOVE 8 TO APPL-RESULT; WRITE FD-TRANFILE-REC FROM TRAN-RECORD; if TRANFILE-STATUS='00' AOK else 12; on error DISPLAY 'ERROR WRITING TO TRANSACTION FILE' + IO-STATUS + abend. // source: CBTRN02C.cbl:562-579

**9000-DALYTRAN-CLOSE** … **9500-TCATBALF-CLOSE.** Each: MOVE 8 TO APPL-RESULT; CLOSE the file; if status='00' AOK else 12; on error DISPLAY a file-specific 'ERROR CLOSING …' + IO-STATUS + abend. Error texts: 'ERROR CLOSING DALYTRAN FILE', 'ERROR CLOSING TRANSACTION FILE', 'ERROR CLOSING CROSS REF FILE', 'ERROR CLOSING DAILY REJECTS FILE', 'ERROR CLOSING ACCOUNT FILE', 'ERROR CLOSING TRANSACTION BALANCE FILE'. // source: CBTRN02C.cbl:582-690

**Z-GET-DB2-FORMAT-TIMESTAMP.** MOVE FUNCTION CURRENT-DATE TO COBOL-TS; copy COB-YYYY/MM/DD/HH/MIN/SS/MIL into DB2-YYYY/MM/DD/HH/MIN/SS/MIL; MOVE '0000' TO DB2-REST; MOVE '-' TO the three DB2-STREEP separators; MOVE '.' TO the three DB2-DOT separators. Produces DB2 timestamp `YYYY-MM-DD-HH.MM.SS.mmmm0000` form in DB2-FORMAT-TS (X(26)). // source: CBTRN02C.cbl:692-705

**9999-ABEND-PROGRAM.** DISPLAY 'ABENDING PROGRAM'; MOVE 0 TO TIMING; MOVE 999 TO ABCODE; CALL 'CEE3ABD' USING ABCODE, TIMING (Language Environment abend). // source: CBTRN02C.cbl:707-711
> Port: map to a runtime `Abend(999)` that terminates the batch with a non-zero exit, after emitting the same DISPLAYs.

**9910-DISPLAY-IO-STATUS.** If IO-STATUS NOT NUMERIC OR IO-STAT1='9': MOVE IO-STAT1 TO IO-STATUS-04(1:1); zero TWO-BYTES-BINARY; MOVE IO-STAT2 TO TWO-BYTES-RIGHT; MOVE TWO-BYTES-BINARY TO IO-STATUS-0403; DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04. ELSE MOVE '0000' TO IO-STATUS-04; MOVE IO-STATUS TO IO-STATUS-04(3:2); DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04. (Renders 2-byte file status as a 4-digit number; the '9x' branch decodes the binary second byte.) // source: CBTRN02C.cbl:714-727

---

## 5. VALIDATION RULES & exact literal messages

All literal messages move into `WS-VALIDATION-FAIL-REASON-DESC` (X(76)) with a numeric reason in `WS-VALIDATION-FAIL-REASON` (9(4)). A non-zero reason ⇒ record is rejected (written to DALYREJS), NOT posted. // source: CBTRN02C.cbl:208-216

| Reason | Trigger | Exact message text | Source |
|---|---|---|---|
| 100 | Card number not in CARD_XREF (INVALID KEY on XREF read) | `INVALID CARD NUMBER FOUND` | CBTRN02C.cbl:385-387 |
| 101 | Account not found in ACCOUNT (INVALID KEY on account read) | `ACCOUNT RECORD NOT FOUND` | CBTRN02C.cbl:397-399 |
| 102 | `ACCT-CREDIT-LIMIT < WS-TEMP-BAL` (over credit limit) | `OVERLIMIT TRANSACTION` | CBTRN02C.cbl:410-412 |
| 103 | `ACCT-EXPIRAION-DATE < DALYTRAN-ORIG-TS(1:10)` (tran after acct expiration) | `TRANSACTION RECEIVED AFTER ACCT EXPIRATION` | CBTRN02C.cbl:417-419 |
| 109 | REWRITE of ACCOUNT fails with INVALID KEY in 2800 | `ACCOUNT RECORD NOT FOUND` | CBTRN02C.cbl:556-558 |

Computation governing reason 102: `WS-TEMP-BAL = ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT`, then over-limit when `ACCT-CREDIT-LIMIT < WS-TEMP-BAL`. // source: CBTRN02C.cbl:403-413

---

## 6. FAITHFUL BUGS to reproduce verbatim (do NOT fix)

1. **Validation reason can be overwritten (102 → 103).** The credit-limit check (reason 102) and the expiration check (reason 103) are two independent sequential `IF`s with no `ELSE`/short-circuit; both always execute when the account is found. If a transaction is *both* over-limit and after expiration, reason 102 is set then unconditionally overwritten by 103. Only the last failure is reported. // source: CBTRN02C.cbl:407-420

2. **Posting side effects occur before the account-REWRITE INVALID-KEY check (orphaned/partial post + lost reject).** In 2000-POST-TRANSACTION the order is 2700-UPDATE-TCATBAL → 2800-UPDATE-ACCOUNT-REC → 2900-WRITE-TRANSACTION-FILE. The TCATBAL update is committed before 2800 attempts the account REWRITE. If that REWRITE hits INVALID KEY it sets reason 109, **but** (a) the program never re-checks WS-VALIDATION-FAIL-REASON after posting, so no reject record is written and the count is not bumped; and (b) 2900 still WRITEs the transaction. Net: TCATBAL/TRANSACT updated for an account that REWRITE could not find. (In practice the account was just read OK in 1500-B so this is latent, but it must be reproduced.) // source: CBTRN02C.cbl:424-444, 545-559

3. **Negative amount accumulated into ACCT-CURR-CYC-DEBIT as-is.** For `DALYTRAN-AMT < 0`, the code does `ADD DALYTRAN-AMT TO ACCT-CURR-CYC-DEBIT` (adds the negative value), so the debit bucket moves *negative* rather than accumulating the absolute debit magnitude. Preserve the literal `ADD` (do not negate). // source: CBTRN02C.cbl:548-552

4. **Credit-limit uses cycle credit/debit, not current balance.** WS-TEMP-BAL is built from `ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT` (cycle figures), not `ACCT-CURR-BAL`. Keep this exact formula even though it may look inconsistent with the balance updated in 2800. // source: CBTRN02C.cbl:403-405

5. **WS-TEMP-BAL truncation.** WS-TEMP-BAL is `S9(9)V99` while account cycle fields are `S9(10)V99`; the COMPUTE result is truncated to 9 integer digits (toward zero, silent overflow). Reproduce via CobolDecimal truncate-toward-zero semantics. // source: CBTRN02C.cbl:187, 403-405

6. **9300-DALYREJS-CLOSE displays the wrong file status on error.** On a close error it does `MOVE XREFFILE-STATUS TO IO-STATUS` (copy/paste from the XREF close) instead of `DALYREJS-STATUS`, so the diagnostic shows the cross-ref file's status. Reproduce verbatim. // source: CBTRN02C.cbl:637-652

7. **TRAN-PROC-TS overwritten with run clock.** Even though DALYTRAN carries DALYTRAN-PROC-TS, the posted TRAN-PROC-TS is always the current-run DB2 timestamp, discarding the daily file's value. (Intended, but pin it so the golden-master timestamp masking is correct.) // source: CBTRN02C.cbl:437-438

---

## 7. PORT NOTES (relational-access translation plan + tricky semantics)

- **Loop / cursor.** DALYTRAN is a forward-only sequential scan. Implement as a repository cursor over `DAILY_TRANSACTION` (`ORDER BY tran_id`, or input/load order if golden masters expect file order). EOF maps to FileStatus '10' → END-OF-FILE='Y'. // source: CBTRN02C.cbl:202-219, 345-369; ARCHITECTURE.md:80-89
- **XREF read** = PK SELECT on `xref_card_num`; not-found → '23' → INVALID-KEY branch (reason 100). // source: CBTRN02C.cbl:380-391
- **ACCOUNT read/rewrite** = PK SELECT then `UPDATE … WHERE acct_id`; OPEN I-O. Missing on UPDATE → '23' → INVALID KEY (reason 109). // source: CBTRN02C.cbl:393-421, 545-559
- **TCATBAL upsert** = the canonical "create on '23'" idiom. Read by composite PK (acct_id,type_cd,cat_cd); the program treats `'00' OR '23'` as success, then branches to INSERT (2700-A) vs UPDATE (2700-B). Preserve the upsert at the program layer (do NOT push it into the repository as a transparent UPSERT — the COBOL distinguishes WRITE vs REWRITE and INITIALIZEs the record on create). // source: CBTRN02C.cbl:467-542; ARCHITECTURE.md:89
- **INITIALIZE TRAN-CAT-BAL-RECORD** sets TRANCAT-* to zeros/spaces and TRAN-CAT-BAL to 0 before the ADD — so the created row's balance equals exactly DALYTRAN-AMT. The FILLER X(22) becomes spaces on re-serialize. // source: CBTRN02C.cbl:504-508
- **TRANSACT WRITE** = INSERT into `TRANSACTION` keyed on tran_id; OPEN OUTPUT means truncate-then-load per run. A duplicate tran_id yields '22' → not '00' → ABEND (12). // source: CBTRN02C.cbl:256, 562-579
- **DALYREJS** has no base table in ARCHITECTURE.md; it is a 430-byte fixed-width reject dataset (350 tran image + 80 trailer). Port to either a fixed-width output file (RECFM=F LRECL=430) or a `DAILY_REJECT` sink table; the trailer formatting (4-digit reason + 76-char desc) must be byte-faithful for golden-master diff. The leading 350 bytes are the *raw DALYTRAN record image* (re-serialized fixed-width), not the typed columns. // source: CBTRN02C.cbl:81-84, 176-182, 446-451; POSTTRAN.jcl:34-38
- **Arithmetic / signed decimal.** All money is COMP/DISPLAY signed decimal; use CardDemo.Runtime `CobolDecimal` (truncate toward zero, silent overflow, never float). Watch: WS-TEMP-BAL S9(9)V99 truncation; cycle-credit/debit S9(10)V99; TRAN-CAT-BAL S9(9)V99 accumulation. // source: CBTRN02C.cbl:403-405, 508, 527, 547-551; ARCHITECTURE.md:38
- **Reference modification** `DALYTRAN-ORIG-TS (1:10)` = first 10 chars of the 26-char origin timestamp, compared as a string against `ACCT-EXPIRAION-DATE X(10)` (both CCYY-MM-DD form). Use ordinal string compare. // source: CBTRN02C.cbl:414
- **Timestamp.** Z-GET-DB2-FORMAT-TIMESTAMP builds `YYYY-MM-DD-HH.MM.SS.mmmm0000` from FUNCTION CURRENT-DATE via the IClock; mask this field in golden-master comparisons. Note: `DB2-MIL` is `9(02)` (only 2 digits of milliseconds carried), `DB2-REST` is hardcoded `'0000'`. // source: CBTRN02C.cbl:159-174, 692-705
- **REDEFINES** of TWO-BYTES-BINARY (9(4) BINARY) and DB2-FORMAT-TS are byte/overlay tricks for status display and timestamp assembly; reimplement the 9910 status decode logically (the '9x' branch reads the binary second byte of the file status as a number). // source: CBTRN02C.cbl:134-137, 160-174, 714-726
- **RETURN-CODE = 4** when any record rejected; otherwise 0 (or the ABEND path). Map to process exit code. // source: CBTRN02C.cbl:229-231
- **ABEND** via CALL 'CEE3ABD' on any unexpected file status — map to runtime Abend that halts with the displayed diagnostics (and after 9910 status print). // source: CBTRN02C.cbl:707-711

---

## 8. OPEN QUESTIONS / RISKS

1. **DALYTRAN ordering.** The golden master must fix the row order of `DAILY_TRANSACTION` reads (PS file is positional). Confirm the import seeds rows in dataset order and the cursor returns them in that order (not PK order) if file order ≠ tran_id order. // source: CBTRN02C.cbl:345-346
2. **DALYREJS target.** ARCHITECTURE.md lists no reject table; decide table vs fixed-width file. The 350-byte leading image must be the re-serialized DALYTRAN record (faithful fixed-width), so the import/serialize path for `DAILY_TRANSACTION` must be reusable here. // source: CBTRN02C.cbl:447-451
3. **TRANSACT OPEN OUTPUT semantics.** Truncate-then-load vs append must match how the upstream CardDemo pipeline (e.g. COMBTRAN / TRANBKP) feeds the KSDS; verify with golden fixtures. // source: CBTRN02C.cbl:256
4. **Faithful bug #2 (orphaned post on REWRITE failure)** is latent because the account was just read OK; pin it with a test only if a fixture can force the REWRITE miss (otherwise document-only). // source: CBTRN02C.cbl:545-559
