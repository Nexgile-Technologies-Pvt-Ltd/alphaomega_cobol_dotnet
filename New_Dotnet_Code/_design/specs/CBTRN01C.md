# PORT SPEC — CBTRN01C (Daily-Transaction File Reader / Card-XREF & Account Validator)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBTRN01C.cbl`
Copybooks used: `app/cpy/CVTRA06Y.cpy` (DALYTRAN-RECORD, RECLN 350), `app/cpy/CVACT03Y.cpy` (CARD-XREF-RECORD, RECLN 50), `app/cpy/CVACT01Y.cpy` (ACCOUNT-RECORD, RECLN 300), `app/cpy/CVCUS01Y.cpy` (CUSTOMER-RECORD, opened but logic-unused), `app/cpy/CVACT02Y.cpy` (CARD-RECORD, opened but logic-unused), `app/cpy/CVTRA05Y.cpy` (TRAN-RECORD, opened but logic-unused).
JCL: **none** — see §1 (no JCL in the repo runs `PGM=CBTRN01C`; the production posting job `POSTTRAN.jcl` runs **CBTRN02C**).
Target tables (relational, per ARCHITECTURE.md): reads **DAILY_TRANSACTION**, **CARD_XREF**, **ACCOUNT**; opens (no logic) **CUSTOMER**, **CARD**, **TRANSACTION**.
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBTRN01C.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA, no BMS map. (online program: **NO**.)

---

## 1. Purpose

CBTRN01C is a stand-alone **BATCH** program whose stated function is "Post the records from daily
transaction file." In practice this version is a **read-and-validate demo / driver**: it opens six
files (the sequential daily-transaction input plus five indexed master files), then loops over the
daily-transaction file record by record. For each daily transaction it (a) `DISPLAY`s the raw
record, (b) takes the transaction's card number and looks it up in the **card cross-reference** file
(random keyed read), and (c) if the xref is found, uses the cross-referenced **account id** to do a
random keyed read of the **account** master. It emits `DISPLAY` diagnostics on success/failure of
each lookup but performs **no updates, no writes, no posting, no balance arithmetic, and no category
balance maintenance** — despite the header comment. (The real posting/balance logic lives in
CBTRN02C.) After end-of-file it closes all six files and `GOBACK`s.
// source: CBTRN01C.cbl:1-6 (header — Type BATCH; Function: "Post the records from daily transaction file.")
// source: CBTRN01C.cbl:155-197 (mainline: opens / per-record loop / closes / GOBACK)
// source: CBTRN01C.cbl:164-186 (loop body: DISPLAY record, lookup XREF, conditionally read ACCOUNT)

**How it is invoked:** There is **no JCL in this repository that executes `PGM=CBTRN01C`** (grep for
`PGM=CBTRN01C` returns nothing; `POSTTRAN.jcl` step `STEP15 EXEC PGM=CBTRN02C` is the posting job).
CBTRN01C is therefore a developer/demo batch utility, run on its own by a JCL step that would supply
the DD names `DALYTRAN`, `CUSTFILE`, `XREFFILE`, `CARDFILE`, `ACCTFILE`, `TRANFILE` (the ASSIGN
targets) and `SYSOUT`/`SYSPRINT`. It is **not** a called subprogram and **not** a CICS transaction;
it ends with `GOBACK` to the operating system. // source: CBTRN01C.cbl:29-62 (SELECT … ASSIGN TO DALYTRAN / CUSTFILE / XREFFILE / CARDFILE / ACCTFILE / TRANFILE); CBTRN01C.cbl:197 (GOBACK)

> NOTE for the port: because no JCL drives this program, the verification harness must invent a step
> name (suggested `RUNTRN01`) and seed the six tables. Its DD→table bindings mirror the masters used
> by POSTTRAN: `DALYTRAN`=DAILY_TRANSACTION (the `.PS` sequential file), `XREFFILE`=CARD_XREF,
> `ACCTFILE`=ACCOUNT, `CUSTFILE`=CUSTOMER, `CARDFILE`=CARD, `TRANFILE`=TRANSACTION. See §9.

---

## 2. FILE / TABLE access

| COBOL file (DD) | Org / Access | Record key | Relational table (ARCHITECTURE.md) | Operations used | Maps to (relational repository) |
|---|---|---|---|---|---|
| `DALYTRAN-FILE` (DD `DALYTRAN`) | **SEQUENTIAL** (QSAM `.PS`) | — (no key) | **DAILY_TRANSACTION** (cols = DALYTRAN-*; PK tran_id) | OPEN INPUT; sequential READ (READNEXT); CLOSE | Forward ordered read cursor over DAILY_TRANSACTION (input order; PK `tran_id` if ordering is needed). `ReadNext()`; status `'00'`/`'10'`(EOF). |
| `CUSTOMER-FILE` (DD `CUSTFILE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-CUST-ID` PIC 9(09) | **CUSTOMER** (PK cust_id) | OPEN INPUT; CLOSE **only** (no READ anywhere) | Open/close a CUSTOMER handle; **no query is issued** — see §7 Faithful Bug #1. |
| `XREF-FILE` (DD `XREFFILE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-XREF-CARD-NUM` PIC X(16) | **CARD_XREF** (PK xref_card_num X16; idx acct_id) | OPEN INPUT; **random keyed READ … KEY IS FD-XREF-CARD-NUM**; CLOSE | `SELECT xref_card_num,cust_id,acct_id FROM CARD_XREF WHERE xref_card_num=@k` → FileStatus `'00'`/`'23'` (INVALID KEY). |
| `CARD-FILE` (DD `CARDFILE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-CARD-NUM` PIC X(16) | **CARD** (PK card_num X16; idx acct_id) | OPEN INPUT; CLOSE **only** (no READ anywhere) | Open/close a CARD handle; **no query is issued** — see §7 Faithful Bug #1. |
| `ACCOUNT-FILE` (DD `ACCTFILE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-ACCT-ID` PIC 9(11) | **ACCOUNT** (PK acct_id 9(11)) | OPEN INPUT; **random keyed READ … KEY IS FD-ACCT-ID**; CLOSE | `SELECT … FROM ACCOUNT WHERE acct_id=@k` → FileStatus `'00'`/`'23'` (INVALID KEY). |
| `TRANSACT-FILE` (DD `TRANFILE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-TRANS-ID` PIC X(16) | **TRANSACTION** (PK tran_id X16) | OPEN INPUT; CLOSE **only** (no READ/WRITE anywhere) | Open/close a TRANSACTION handle; **no query is issued** — see §7 Faithful Bug #1. |

// source: CBTRN01C.cbl:29-32 (DALYTRAN-FILE: ORGANIZATION SEQUENTIAL / ACCESS SEQUENTIAL / FILE STATUS DALYTRAN-STATUS)
// source: CBTRN01C.cbl:34-38 (CUSTOMER-FILE: INDEXED / RANDOM / RECORD KEY FD-CUST-ID)
// source: CBTRN01C.cbl:40-44 (XREF-FILE: INDEXED / RANDOM / RECORD KEY FD-XREF-CARD-NUM)
// source: CBTRN01C.cbl:46-50 (CARD-FILE: INDEXED / RANDOM / RECORD KEY FD-CARD-NUM)
// source: CBTRN01C.cbl:52-56 (ACCOUNT-FILE: INDEXED / RANDOM / RECORD KEY FD-ACCT-ID)
// source: CBTRN01C.cbl:58-62 (TRANSACT-FILE: INDEXED / RANDOM / RECORD KEY FD-TRANS-ID)
// source: CBTRN01C.cbl:66-94 (FD record layouts for all six files)

### Operation → SQL mapping (only two keyed reads actually fire)

- **DALYTRAN sequential READ** (`1000-DALYTRAN-GET-NEXT`): `READ DALYTRAN-FILE INTO DALYTRAN-RECORD`
  → cursor `ReadNext()` over DAILY_TRANSACTION. FileStatus `'00'` = row; **`'10'` = EOF** → APPL-EOF
  → `END-OF-DAILY-TRANS-FILE='Y'`; any other status → APPL-RESULT 12 → display error → abend.
  // source: CBTRN01C.cbl:203-225
- **XREF random keyed READ** (`2000-LOOKUP-XREF`): `READ XREF-FILE RECORD INTO CARD-XREF-RECORD
  KEY IS FD-XREF-CARD-NUM` with INVALID KEY / NOT INVALID KEY. →
  `SELECT xref_card_num,cust_id,acct_id FROM CARD_XREF WHERE xref_card_num=@k`.
  Row present → NOT INVALID KEY path (status `'00'`); no row → INVALID KEY path (status `'23'`),
  `WS-XREF-READ-STATUS` set to 4. // source: CBTRN01C.cbl:227-239
- **ACCOUNT random keyed READ** (`3000-READ-ACCOUNT`): `READ ACCOUNT-FILE RECORD INTO ACCOUNT-RECORD
  KEY IS FD-ACCT-ID` with INVALID KEY / NOT INVALID KEY. →
  `SELECT … FROM ACCOUNT WHERE acct_id=@k`. Row → NOT INVALID KEY; no row → INVALID KEY,
  `WS-ACCT-READ-STATUS` set to 4. // source: CBTRN01C.cbl:241-250
- **CUSTOMER / CARD / TRANSACT**: opened and closed only; **no READ/WRITE/REWRITE/DELETE**. In the
  relational port these are open/close no-ops on the repository (or skipped entirely — see §7/§9).
  // source: CBTRN01C.cbl:158,160,162 (opens); 189,191,193 (closes); no READ on these files anywhere in PROCEDURE DIVISION

There are **no WRITE / REWRITE / DELETE / STARTBR / READPREV** operations in this program. All six
files are `OPEN INPUT`. The only ordered/sequential traversal is the DALYTRAN read; XREF and ACCOUNT
are single-row keyed (random) reads.

---

## 3. WORKING-STORAGE / record structures that affect logic

### 3.1 Record copybooks (from `COPY`)
- `COPY CVTRA06Y` → **`DALYTRAN-RECORD`** (RECLN 350) — the daily-transaction record `READ INTO`d and
  `DISPLAY`ed. Only two of its fields drive logic: `DALYTRAN-CARD-NUM` and `DALYTRAN-ID`.
  // source: CBTRN01C.cbl:99; cpy/CVTRA06Y.cpy:4-18
  - `DALYTRAN-ID`            PIC X(16)      → DAILY_TRANSACTION.`tran_id` (PK)
  - `DALYTRAN-TYPE-CD`       PIC X(02)      → `type_cd`
  - `DALYTRAN-CAT-CD`        PIC 9(04)      → `cat_cd`
  - `DALYTRAN-SOURCE`        PIC X(10)      → `source`
  - `DALYTRAN-DESC`          PIC X(100)     → `desc`
  - `DALYTRAN-AMT`           PIC S9(09)V99  → `amt` (decimal, signed; **not used in logic here**)
  - `DALYTRAN-MERCHANT-ID`   PIC 9(09)      → `merchant_id`
  - `DALYTRAN-MERCHANT-NAME` PIC X(50)      → `merchant_name`
  - `DALYTRAN-MERCHANT-CITY` PIC X(50)      → `merchant_city`
  - `DALYTRAN-MERCHANT-ZIP`  PIC X(10)      → `merchant_zip`
  - `DALYTRAN-CARD-NUM`      PIC X(16)      → `card_num`  **(the only key used: moved to XREF-CARD-NUM)**
  - `DALYTRAN-ORIG-TS`       PIC X(26)      → `orig_ts`
  - `DALYTRAN-PROC-TS`       PIC X(26)      → `proc_ts`
  - `FILLER`                 PIC X(20)      → dropped (20 spaces on serialize)
- `COPY CVACT03Y` → **`CARD-XREF-RECORD`** (RECLN 50) — target of the XREF keyed read; supplies
  `XREF-ACCT-ID` (→ ACCT-ID) and provides `XREF-CARD-NUM`, `XREF-CUST-ID` for DISPLAY.
  // source: CBTRN01C.cbl:109; cpy/CVACT03Y.cpy:4-8
  - `XREF-CARD-NUM` X(16) (PK) ; `XREF-CUST-ID` 9(09) ; `XREF-ACCT-ID` 9(11) ; FILLER X(14).
- `COPY CVACT01Y` → **`ACCOUNT-RECORD`** (RECLN 300) — target of the ACCOUNT keyed read; only its
  existence (status) is used (record contents are **not** referenced after the read).
  // source: CBTRN01C.cbl:119; cpy/CVACT01Y.cpy:4-17
  - `ACCT-ID` 9(11) (PK) and 11 more elementary fields + FILLER X(178); none are read by logic.
- `COPY CVCUS01Y` → **`CUSTOMER-RECORD`**; `COPY CVACT02Y` → **`CARD-RECORD`**; `COPY CVTRA05Y` →
  **`TRAN-RECORD`** — declared so the files can be opened; **no field is referenced** by procedure
  logic. // source: CBTRN01C.cbl:104,114,124

### 3.2 File-status & control fields
- Six 2-byte status groups, one per file: `DALYTRAN-STATUS`, `CUSTFILE-STATUS`, `XREFFILE-STATUS`,
  `CARDFILE-STATUS`, `ACCTFILE-STATUS`, `TRANFILE-STATUS` (each STAT1 X + STAT2 X).
  // source: CBTRN01C.cbl:100-127
- `IO-STATUS` (`IO-STAT1` X + `IO-STAT2` X) — receives a status for the display helper.
  // source: CBTRN01C.cbl:129-131
- `TWO-BYTES-BINARY` PIC 9(4) BINARY, REDEFINED by `TWO-BYTES-ALPHA` (`TWO-BYTES-LEFT` X + `TWO-BYTES-RIGHT` X)
  — halfword used to numericize the 2nd status byte in `Z-DISPLAY-IO-STATUS`. // source: CBTRN01C.cbl:133-136
- `IO-STATUS-04` (`IO-STATUS-0401` PIC 9 VALUE 0 + `IO-STATUS-0403` PIC 999 VALUE 0) — the 4-digit
  rendered status. // source: CBTRN01C.cbl:138-140
- `APPL-RESULT` PIC S9(9) COMP, 88s: **`APPL-AOK VALUE 0`**, **`APPL-EOF VALUE 16`**. // source: CBTRN01C.cbl:142-144
- `END-OF-DAILY-TRANS-FILE` PIC X(01) VALUE `'N'` — loop sentinel. // source: CBTRN01C.cbl:146
- `ABCODE` PIC S9(9) BINARY, `TIMING` PIC S9(9) BINARY — args to `CEE3ABD`. // source: CBTRN01C.cbl:147-148
- `WS-MISC-VARIABLES`: **`WS-XREF-READ-STATUS`** PIC 9(04), **`WS-ACCT-READ-STATUS`** PIC 9(04) —
  per-record lookup result flags (0 = found/ok, 4 = not found). // source: CBTRN01C.cbl:149-151
- `IO-STATUS-04` redefine-free; `IO-STATUS-0401`/`-0403` are display-only. (Note: there is **no
  `END-OF-FILE` variable** here; the sentinel is `END-OF-DAILY-TRANS-FILE`.)

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM flow preserved.

### MAIN-PARA // source: CBTRN01C.cbl:155-197
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBTRN01C'`. // source: CBTRN01C.cbl:156
2. PERFORM the six open paragraphs in order: `0000-DALYTRAN-OPEN`, `0100-CUSTFILE-OPEN`,
   `0200-XREFFILE-OPEN`, `0300-CARDFILE-OPEN`, `0400-ACCTFILE-OPEN`, `0500-TRANFILE-OPEN`
   (each abends on non-`'00'`). // source: CBTRN01C.cbl:157-162
3. `PERFORM UNTIL END-OF-DAILY-TRANS-FILE = 'Y'`: // source: CBTRN01C.cbl:164-186
   a. IF `END-OF-DAILY-TRANS-FILE = 'N'` (redundant inner guard): // source: CBTRN01C.cbl:165
      - PERFORM `1000-DALYTRAN-GET-NEXT`. // source: CBTRN01C.cbl:166
      - IF `END-OF-DAILY-TRANS-FILE = 'N'` (i.e. a record was read, not EOF): `DISPLAY DALYTRAN-RECORD`.
        // source: CBTRN01C.cbl:167-169
      - `MOVE 0 TO WS-XREF-READ-STATUS`. // source: CBTRN01C.cbl:170
      - `MOVE DALYTRAN-CARD-NUM TO XREF-CARD-NUM`. // source: CBTRN01C.cbl:171
      - PERFORM `2000-LOOKUP-XREF`. // source: CBTRN01C.cbl:172
      - IF `WS-XREF-READ-STATUS = 0` (xref found): // source: CBTRN01C.cbl:173
        - `MOVE 0 TO WS-ACCT-READ-STATUS`. // source: CBTRN01C.cbl:174
        - `MOVE XREF-ACCT-ID TO ACCT-ID`. // source: CBTRN01C.cbl:175
        - PERFORM `3000-READ-ACCOUNT`. // source: CBTRN01C.cbl:176
        - IF `WS-ACCT-READ-STATUS NOT = 0`: `DISPLAY 'ACCOUNT ' ACCT-ID ' NOT FOUND'`.
          // source: CBTRN01C.cbl:177-179
      - ELSE (xref not found): `DISPLAY 'CARD NUMBER ' DALYTRAN-CARD-NUM ' COULD NOT BE VERIFIED.
        SKIPPING TRANSACTION ID-' DALYTRAN-ID` (one continued DISPLAY). // source: CBTRN01C.cbl:180-184
   > **IMPORTANT control-flow subtlety:** the loop body's XREF/ACCOUNT lookups run *unconditionally*
   > after `1000-DALYTRAN-GET-NEXT`, i.e. even on the final iteration where EOF was just hit. See
   > §7 Faithful Bug #2 (a stale last record is re-validated at EOF).
4. PERFORM the six close paragraphs in order: `9000-DALYTRAN-CLOSE`, `9100-CUSTFILE-CLOSE`,
   `9200-XREFFILE-CLOSE`, `9300-CARDFILE-CLOSE`, `9400-ACCTFILE-CLOSE`, `9500-TRANFILE-CLOSE`.
   // source: CBTRN01C.cbl:188-193
5. `DISPLAY 'END OF EXECUTION OF PROGRAM CBTRN01C'`. // source: CBTRN01C.cbl:195
6. `GOBACK`. // source: CBTRN01C.cbl:197

### 1000-DALYTRAN-GET-NEXT // source: CBTRN01C.cbl:202-225
1. `READ DALYTRAN-FILE INTO DALYTRAN-RECORD` (sequential next; copies the 350-byte FD record into the
   copybook record). // source: CBTRN01C.cbl:203
2. IF `DALYTRAN-STATUS = '00'` → MOVE 0 → APPL-RESULT; ELSE IF `'10'` → MOVE 16 → APPL-RESULT;
   ELSE → MOVE 12 → APPL-RESULT. // source: CBTRN01C.cbl:204-212
3. IF `APPL-AOK` (=0) → CONTINUE; ELSE IF `APPL-EOF` (=16) → `MOVE 'Y' TO END-OF-DAILY-TRANS-FILE`;
   ELSE → `DISPLAY 'ERROR READING DAILY TRANSACTION FILE'`, MOVE `DALYTRAN-STATUS` → IO-STATUS,
   PERFORM `Z-DISPLAY-IO-STATUS`, PERFORM `Z-ABEND-PROGRAM`. // source: CBTRN01C.cbl:213-224
4. `EXIT`. // source: CBTRN01C.cbl:225

### 2000-LOOKUP-XREF // source: CBTRN01C.cbl:227-239
1. `MOVE XREF-CARD-NUM TO FD-XREF-CARD-NUM` (the working-storage card number set in MAIN copied to the
   FD key field). // source: CBTRN01C.cbl:228
2. `READ XREF-FILE RECORD INTO CARD-XREF-RECORD KEY IS FD-XREF-CARD-NUM`: // source: CBTRN01C.cbl:229-230
   - **INVALID KEY** (no row): `DISPLAY 'INVALID CARD NUMBER FOR XREF'`; `MOVE 4 TO WS-XREF-READ-STATUS`.
     // source: CBTRN01C.cbl:231-233
   - **NOT INVALID KEY** (row found): `DISPLAY 'SUCCESSFUL READ OF XREF'`,
     `DISPLAY 'CARD NUMBER: ' XREF-CARD-NUM`, `DISPLAY 'ACCOUNT ID : ' XREF-ACCT-ID`,
     `DISPLAY 'CUSTOMER ID: ' XREF-CUST-ID`. (WS-XREF-READ-STATUS left at 0.) // source: CBTRN01C.cbl:234-238
   - `END-READ`. // source: CBTRN01C.cbl:239
   > Note: `READ … INTO CARD-XREF-RECORD` overwrites the record, so `XREF-CARD-NUM` used as the source
   > of `FD-XREF-CARD-NUM` at line 228 is the value MAIN moved in at line 171 *before* the read; after
   > a successful read the displayed `XREF-CARD-NUM` is the record's own value (same key on success).
   > On INVALID KEY the contents of `CARD-XREF-RECORD` are **not** updated (stale). (No paragraph EXIT
   > statement; control falls through to the implicit return.)

### 3000-READ-ACCOUNT // source: CBTRN01C.cbl:241-250
1. `MOVE ACCT-ID TO FD-ACCT-ID` (the account id set by MAIN from XREF-ACCT-ID). // source: CBTRN01C.cbl:242
2. `READ ACCOUNT-FILE RECORD INTO ACCOUNT-RECORD KEY IS FD-ACCT-ID`: // source: CBTRN01C.cbl:243-244
   - **INVALID KEY**: `DISPLAY 'INVALID ACCOUNT NUMBER FOUND'`; `MOVE 4 TO WS-ACCT-READ-STATUS`.
     // source: CBTRN01C.cbl:245-247
   - **NOT INVALID KEY**: `DISPLAY 'SUCCESSFUL READ OF ACCOUNT FILE'`. (WS-ACCT-READ-STATUS stays 0.)
     // source: CBTRN01C.cbl:248-249
   - `END-READ`. // source: CBTRN01C.cbl:250 (no explicit EXIT)

### 0000-DALYTRAN-OPEN // source: CBTRN01C.cbl:252-268
1. `MOVE 8 TO APPL-RESULT` (priming so AOK is false unless OPEN succeeds). // source: CBTRN01C.cbl:253
2. `OPEN INPUT DALYTRAN-FILE`. // source: CBTRN01C.cbl:254
3. IF `DALYTRAN-STATUS = '00'` → MOVE 0 → APPL-RESULT; ELSE → MOVE 12 → APPL-RESULT. // source: CBTRN01C.cbl:255-259
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR OPENING DAILY TRANSACTION FILE'`,
   MOVE `DALYTRAN-STATUS` → IO-STATUS, PERFORM `Z-DISPLAY-IO-STATUS`, PERFORM `Z-ABEND-PROGRAM`.
   // source: CBTRN01C.cbl:260-267
5. `EXIT`. // source: CBTRN01C.cbl:268

### 0100-CUSTFILE-OPEN // source: CBTRN01C.cbl:271-287
Identical pattern to 0000: MOVE 8 → APPL-RESULT; `OPEN INPUT CUSTOMER-FILE`; status `'00'`→0 else 12;
on non-AOK → `DISPLAY 'ERROR OPENING CUSTOMER FILE'`, MOVE `CUSTFILE-STATUS`→IO-STATUS,
`Z-DISPLAY-IO-STATUS`, `Z-ABEND-PROGRAM`; EXIT. // source: CBTRN01C.cbl:272-287

### 0200-XREFFILE-OPEN // source: CBTRN01C.cbl:289-305
Same pattern: `OPEN INPUT XREF-FILE`; on error `DISPLAY 'ERROR OPENING CROSS REF FILE'`, MOVE
`XREFFILE-STATUS`→IO-STATUS, helper, abend; EXIT. // source: CBTRN01C.cbl:290-305

### 0300-CARDFILE-OPEN // source: CBTRN01C.cbl:307-323
Same pattern: `OPEN INPUT CARD-FILE`; on error `DISPLAY 'ERROR OPENING CARD FILE'`, MOVE
`CARDFILE-STATUS`→IO-STATUS, helper, abend; EXIT. // source: CBTRN01C.cbl:308-323

### 0400-ACCTFILE-OPEN // source: CBTRN01C.cbl:325-341
Same pattern: `OPEN INPUT ACCOUNT-FILE`; on error `DISPLAY 'ERROR OPENING ACCOUNT FILE'`, MOVE
`ACCTFILE-STATUS`→IO-STATUS, helper, abend; EXIT. // source: CBTRN01C.cbl:326-341

### 0500-TRANFILE-OPEN // source: CBTRN01C.cbl:343-359
Same pattern: `OPEN INPUT TRANSACT-FILE`; on error `DISPLAY 'ERROR OPENING TRANSACTION FILE'`, MOVE
`TRANFILE-STATUS`→IO-STATUS, helper, abend; EXIT. // source: CBTRN01C.cbl:344-359

### 9000-DALYTRAN-CLOSE // source: CBTRN01C.cbl:361-377
1. `ADD 8 TO ZERO GIVING APPL-RESULT` (→ 8; priming). // source: CBTRN01C.cbl:362
2. `CLOSE DALYTRAN-FILE`. // source: CBTRN01C.cbl:363
3. IF `DALYTRAN-STATUS = '00'` → MOVE 0 → APPL-RESULT; ELSE → MOVE 12 → APPL-RESULT. // source: CBTRN01C.cbl:364-368
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR CLOSING CUSTOMER FILE'` **(wrong literal — see §7
   Faithful Bug #3)**, MOVE **`CUSTFILE-STATUS`** → IO-STATUS **(wrong status field — see §7 Faithful
   Bug #4)**, `Z-DISPLAY-IO-STATUS`, `Z-ABEND-PROGRAM`. // source: CBTRN01C.cbl:369-376
5. `EXIT`. // source: CBTRN01C.cbl:377

### 9100-CUSTFILE-CLOSE // source: CBTRN01C.cbl:379-395
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE CUSTOMER-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING CUSTOMER FILE'`, MOVE `CUSTFILE-STATUS`→IO-STATUS, helper, abend; EXIT.
// source: CBTRN01C.cbl:380-395

### 9200-XREFFILE-CLOSE // source: CBTRN01C.cbl:397-413
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE XREF-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING CROSS REF FILE'`, MOVE `XREFFILE-STATUS`→IO-STATUS, helper, abend; EXIT.
// source: CBTRN01C.cbl:398-413

### 9300-CARDFILE-CLOSE // source: CBTRN01C.cbl:415-431
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE CARD-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING CARD FILE'`, MOVE `CARDFILE-STATUS`→IO-STATUS, helper, abend; EXIT.
// source: CBTRN01C.cbl:416-431

### 9400-ACCTFILE-CLOSE // source: CBTRN01C.cbl:433-449
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE ACCOUNT-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING ACCOUNT FILE'`, MOVE `ACCTFILE-STATUS`→IO-STATUS, helper, abend; EXIT.
// source: CBTRN01C.cbl:434-449

### 9500-TRANFILE-CLOSE // source: CBTRN01C.cbl:451-467
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE TRANSACT-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING TRANSACTION FILE'`, MOVE `TRANFILE-STATUS`→IO-STATUS, helper, abend; EXIT.
// source: CBTRN01C.cbl:452-467

### Z-ABEND-PROGRAM // source: CBTRN01C.cbl:469-473
`DISPLAY 'ABENDING PROGRAM'`; `MOVE 0 TO TIMING`; `MOVE 999 TO ABCODE`;
`CALL 'CEE3ABD' USING ABCODE, TIMING`. Port: throw `Runtime.Abend(999)` (terminates the batch run,
no return). // source: CBTRN01C.cbl:470-473

### Z-DISPLAY-IO-STATUS // source: CBTRN01C.cbl:476-489
Formats the 2-char status into `'FILE STATUS IS: NNNN'`:
- IF `IO-STATUS NOT NUMERIC` **OR** `IO-STAT1 = '9'`: // source: CBTRN01C.cbl:477-478
  - `MOVE IO-STAT1 TO IO-STATUS-04(1:1)`. // source: CBTRN01C.cbl:479
  - `MOVE 0 TO TWO-BYTES-BINARY`; `MOVE IO-STAT2 TO TWO-BYTES-RIGHT`; `MOVE TWO-BYTES-BINARY TO
    IO-STATUS-0403` (renders the raw byte value of the 2nd status char as a 3-digit number — the
    extended-status idiom; big-endian halfword). // source: CBTRN01C.cbl:480-482
  - `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBTRN01C.cbl:483
- ELSE (normal numeric status): `MOVE '0000' TO IO-STATUS-04`; `MOVE IO-STATUS TO IO-STATUS-04(3:2)`;
  `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBTRN01C.cbl:485-487
- `EXIT`. // source: CBTRN01C.cbl:489

**Byte-order caveat:** `TWO-BYTES-RIGHT` is the rightmost (low-order) byte of the big-endian halfword
`TWO-BYTES-BINARY`, so the rendered number = `(int)(unsigned byte)IO-STAT2`. The .NET port must
reproduce the big-endian semantics (character code 0..255 of `IO-STAT2`, rendered `%03d`) regardless
of host endianness. // source: CBTRN01C.cbl:133-136, 480-482

---

## 5. VALIDATION RULES & exact literal messages

This program has **no business-field validation**. Its "validation" is (a) file-status checks
(`'00'` ok, `'10'` EOF on the DALYTRAN read, anything else → abend) and (b) presence checks via
INVALID KEY on the two keyed reads. Exact literal strings to reproduce verbatim on SYSOUT / at abend:

- `'START OF EXECUTION OF PROGRAM CBTRN01C'` // source: CBTRN01C.cbl:156
- `'END OF EXECUTION OF PROGRAM CBTRN01C'` // source: CBTRN01C.cbl:195
- `'ACCOUNT ' ACCT-ID ' NOT FOUND'` (account-not-found, ACCT-ID = the 11-digit account id). // source: CBTRN01C.cbl:178
- `'CARD NUMBER ' DALYTRAN-CARD-NUM ' COULD NOT BE VERIFIED. SKIPPING TRANSACTION ID-' DALYTRAN-ID`
  (one continued DISPLAY across lines 181-183). // source: CBTRN01C.cbl:181-183
- `'INVALID CARD NUMBER FOR XREF'` (XREF INVALID KEY). // source: CBTRN01C.cbl:232
- `'SUCCESSFUL READ OF XREF'` ; `'CARD NUMBER: ' XREF-CARD-NUM` ; `'ACCOUNT ID : ' XREF-ACCT-ID` ;
  `'CUSTOMER ID: ' XREF-CUST-ID` (XREF success block). // source: CBTRN01C.cbl:235-238
- `'INVALID ACCOUNT NUMBER FOUND'` (ACCOUNT INVALID KEY). // source: CBTRN01C.cbl:246
- `'SUCCESSFUL READ OF ACCOUNT FILE'` (ACCOUNT success). // source: CBTRN01C.cbl:249
- `'ERROR READING DAILY TRANSACTION FILE'` // source: CBTRN01C.cbl:219
- Open errors: `'ERROR OPENING DAILY TRANSACTION FILE'` (263), `'ERROR OPENING CUSTOMER FILE'` (282),
  `'ERROR OPENING CROSS REF FILE'` (300), `'ERROR OPENING CARD FILE'` (318),
  `'ERROR OPENING ACCOUNT FILE'` (336), `'ERROR OPENING TRANSACTION FILE'` (354).
- Close errors: `'ERROR CLOSING CUSTOMER FILE'` (**emitted by BOTH 9000-DALYTRAN-CLOSE@372 and
  9100-CUSTFILE-CLOSE@390** — see §7 #3), `'ERROR CLOSING CROSS REF FILE'` (408),
  `'ERROR CLOSING CARD FILE'` (426), `'ERROR CLOSING ACCOUNT FILE'` (444),
  `'ERROR CLOSING TRANSACTION FILE'` (462).
- `'ABENDING PROGRAM'` // source: CBTRN01C.cbl:470
- `'FILE STATUS IS: NNNN'` (literal prefix; followed by the 4-char formatted `IO-STATUS-04`). // source: CBTRN01C.cbl:483,487
- Plus the raw `DISPLAY DALYTRAN-RECORD` line (the 350-byte record image). // source: CBTRN01C.cbl:168

File-status accept rules: OPEN/CLOSE accept `'00'` (else 12 → abend); the DALYTRAN READ accepts `'00'`
and treats `'10'` as EOF; XREF/ACCOUNT keyed reads use INVALID KEY (status `'23'`) to set the
read-status flag to 4 (not an abend). // source: CBTRN01C.cbl:204-212, 231-233, 245-247

---

## 6. ARITHMETIC / COMPUTE notes

There are **no COMPUTE statements** and **no business arithmetic** in CBTRN01C. All arithmetic is on
the integer control flag `APPL-RESULT` (PIC S9(9) COMP), used only as a success/EOF/error code:
- `MOVE 0 / 8 / 12 / 16 → APPL-RESULT` (flag assignments). // source: CBTRN01C.cbl:205,208,210,253,256,258,272,…
- `ADD 8 TO ZERO GIVING APPL-RESULT` → 8 (priming at the start of each close paragraph). // source: CBTRN01C.cbl:362,380,398,416,434,452
- The per-record flags `WS-XREF-READ-STATUS` / `WS-ACCT-READ-STATUS` (PIC 9(04)) are only ever
  assigned constant 0 or 4 and compared. // source: CBTRN01C.cbl:170,174,233,247,173,177
None of these can truncate or overflow (values 0,4,8,12,16 fit their PICs); no sign or scaling
concerns. The only PIC-driven numeric formatting is in `Z-DISPLAY-IO-STATUS` (`IO-STATUS-0403` PIC 999
zero-pads the byte value to 3 digits; `IO-STATUS-04(3:2)` places the 2-char status). No rounding,
no truncation of money fields (`DALYTRAN-AMT` is never arithmetic-used here). // source: CBTRN01C.cbl:479-487

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **CUSTOMER, CARD, and TRANSACT files are opened and closed but never read or written.** The header
   claims the program "posts" transactions, but `CUSTOMER-FILE`, `CARD-FILE`, and `TRANSACT-FILE`
   have no READ/WRITE/REWRITE/DELETE anywhere in the PROCEDURE DIVISION — they are pure OPEN/CLOSE
   dead weight. Reproduce by opening/closing (or no-op'ing) these handles **without** issuing any
   query; do not "use" them. // source: CBTRN01C.cbl:158,160,162 (opens) and 189,191,193 (closes); no READ on these files exists (grep PROCEDURE DIVISION).

2. **The XREF/ACCOUNT lookup runs one extra time on the EOF iteration using the STALE last record.**
   The `PERFORM UNTIL` loop calls `1000-DALYTRAN-GET-NEXT`, which on EOF sets
   `END-OF-DAILY-TRANS-FILE='Y'` **but the loop body then unconditionally falls through** to
   `MOVE DALYTRAN-CARD-NUM TO XREF-CARD-NUM`, `PERFORM 2000-LOOKUP-XREF`, and (if found)
   `3000-READ-ACCOUNT` — using whatever `DALYTRAN-RECORD` held from the **previous** successful read
   (READ INTO does not clear the record on `'10'`). Only the `DISPLAY DALYTRAN-RECORD` is guarded by
   the inner `IF END-OF-DAILY-TRANS-FILE = 'N'`; the lookups are not. So the last real transaction's
   card number is validated **twice** (once normally, once on the EOF pass), producing an extra set
   of `'SUCCESSFUL READ OF XREF'`/account DISPLAY lines (or extra not-found lines). The loop then
   re-tests the UNTIL condition and exits. **Reproduce this extra trailing lookup verbatim** — do not
   add an EOF guard around the lookups. // source: CBTRN01C.cbl:164-186 (loop), 167-169 (DISPLAY guarded), 170-184 (lookups NOT guarded), 203 (READ INTO leaves record stale at EOF)

3. **Wrong DISPLAY literal in 9000-DALYTRAN-CLOSE.** When the DALYTRAN close fails, the program
   displays `'ERROR CLOSING CUSTOMER FILE'` instead of an "ERROR CLOSING DAILY TRANSACTION FILE"
   message (copy-paste bug). Reproduce the literal exactly as written. // source: CBTRN01C.cbl:372

4. **Wrong status field moved to IO-STATUS in 9000-DALYTRAN-CLOSE.** On a DALYTRAN-close error the
   program does `MOVE CUSTFILE-STATUS TO IO-STATUS` (line 373) — it copies the **CUSTOMER** file
   status, not `DALYTRAN-STATUS`, into the display helper. So the `'FILE STATUS IS: NNNN'` line on a
   daily-transaction close failure reports the customer file's status. Reproduce verbatim (move
   CUSTFILE-STATUS, not DALYTRAN-STATUS). // source: CBTRN01C.cbl:373

5. **Inconsistent close-priming style vs open-priming style (cosmetic).** Opens use `MOVE 8 TO
   APPL-RESULT`; closes use `ADD 8 TO ZERO GIVING APPL-RESULT`. Both yield 8; reproduce as-is (no
   functional effect, but keep the statements). // source: CBTRN01C.cbl:253 vs 362

6. **Redundant inner `IF END-OF-DAILY-TRANS-FILE = 'N'` guard** at line 165 duplicates the
   `PERFORM UNTIL … = 'Y'` condition. Harmless; reproduce the control structure as-is. // source: CBTRN01C.cbl:164-165

7. **`Z-DISPLAY-IO-STATUS` second-byte rendering depends on raw byte value / big-endian halfword.**
   On the non-numeric / `IO-STAT1='9'` branch the 2nd status char is reinterpreted as the low byte of
   a halfword binary and printed as 0..255. Intentional mainframe behavior; reproduce the big-endian
   result (value = character code of `IO-STAT2`), not a little-endian misread. // source: CBTRN01C.cbl:133-136, 477-482

> No data-mutation or money-arithmetic bugs exist (no posting is performed). Bugs #1, #2, #3, #4 are
> the salient faithful bugs; #2 (stale-record extra lookup at EOF) materially affects SYSOUT line
> counts and must be pinned by a characterization test.

---

## 8. PORT NOTES (relational-access + tricky COBOL semantics)

**DAILY_TRANSACTION read (sequential):** `OPEN INPUT` + `READ DALYTRAN-FILE INTO DALYTRAN-RECORD`
over a QSAM `.PS` sequential file = a forward read cursor over the DAILY_TRANSACTION table in input
order. Each `1000-DALYTRAN-GET-NEXT` = `ReadNext()`; exhausted cursor → file status `'10'` →
APPL-EOF → `END-OF-DAILY-TRANS-FILE='Y'`. Because the file is plain sequential (not keyed), preserve
the seeded/import order; if the harness needs a deterministic order, order by PK `tran_id` and pin it.
// source: ARCHITECTURE.md §"VSAM-semantics" (sequential = forward cursor); CBTRN01C.cbl:203

**CARD_XREF keyed read (random):** `READ XREF-FILE … KEY IS FD-XREF-CARD-NUM` →
`SELECT xref_card_num,cust_id,acct_id FROM CARD_XREF WHERE xref_card_num=@k`. Row present → NOT
INVALID KEY (status `'00'`); absent → INVALID KEY (status `'23'`) and `WS-XREF-READ-STATUS=4`. Key
`FD-XREF-CARD-NUM` X(16): compare with an ordinal (culture-invariant) string equality after copying
`DALYTRAN-CARD-NUM` (which preserves trailing spaces — keep the full 16-char fixed width). // source:
ARCHITECTURE.md §"VSAM-semantics" (READ key → SELECT by PK; '00'/'23'); CBTRN01C.cbl:228-239

**ACCOUNT keyed read (random):** `READ ACCOUNT-FILE … KEY IS FD-ACCT-ID` →
`SELECT … FROM ACCOUNT WHERE acct_id=@k`. `FD-ACCT-ID` is PIC 9(11) numeric; `ACCT-ID` likewise.
**Width caveat:** PIC 9(11) does not fit a 32-bit `int` (max 99,999,999,999) — use `long`/INTEGER per
the ACCOUNT table definition. The keyed read uses only existence (status), not record contents.
// source: ARCHITECTURE.md §schema (ACCOUNT PK acct_id 9(11) → long); CBTRN01C.cbl:242-249

**MOVE numeric→numeric for the key:** `MOVE XREF-ACCT-ID TO ACCT-ID` (both 9(11)) and
`MOVE ACCT-ID TO FD-ACCT-ID` (both 9(11)) are straight numeric copies; in the port these are `long`
assignments. `MOVE DALYTRAN-CARD-NUM TO XREF-CARD-NUM` and `MOVE XREF-CARD-NUM TO FD-XREF-CARD-NUM`
are X(16)→X(16) string copies — keep exact 16-char width (trailing spaces preserved). // source:
CBTRN01C.cbl:171,175,228,242

**CUSTOMER / CARD / TRANSACT open/close only:** model as open/close no-ops on the repositories (or
omit entirely, but the abend-on-open-failure behavior should be preserved if the harness simulates a
missing/locked file). **No SELECT is issued** against these tables. // source: CBTRN01C.cbl:158,160,162,189,191,193

**READ INTO + stale record at EOF (Faithful Bug #2):** the port's `ReadNext()` must NOT clear the
`DALYTRAN-RECORD` working fields on EOF — COBOL `READ … INTO` leaves the destination unchanged on
status `'10'`. The mainline then re-uses the last record's `DALYTRAN-CARD-NUM`/`DALYTRAN-ID` for one
extra XREF/ACCOUNT lookup. Implement the loop exactly: read; (guarded) display; then *unconditionally*
do the lookups. // source: CBTRN01C.cbl:164-186, 203

**Abend mapping:** `CALL 'CEE3ABD' USING ABCODE, TIMING` with ABCODE=999, TIMING=0 → terminate with
abend code 999 via `Runtime.Abend`. No graceful return. // source: CBTRN01C.cbl:469-473

**`Z-DISPLAY-IO-STATUS` port:** on the non-numeric/'9' branch render `IO-STAT1` as digit 1 and
`(int)(byte)IO-STAT2` as a 3-digit number (`%03d`); on the numeric branch zero-pad the 2-char status
into positions 3-4 of `'0000'`. Reproduce big-endian halfword semantics. // source: CBTRN01C.cbl:476-489

**REDEFINES:** `TWO-BYTES-ALPHA` over `TWO-BYTES-BINARY` — model as a 2-byte backing buffer with a
numeric (halfword, big-endian) view and a 2-char view; only the right byte is written. Internal to the
status helper; no table impact. // source: CBTRN01C.cbl:133-136

**DISPLAY DALYTRAN-RECORD:** displays the whole 350-byte group. Numeric subfields `DALYTRAN-CAT-CD`
9(4), `DALYTRAN-MERCHANT-ID` 9(9) render as zero-padded unsigned digit strings; `DALYTRAN-AMT`
S9(9)V99 (signed, implied decimal, DISPLAY usage) renders as 11 zoned-decimal digits with the sign
overpunched on the last digit and **no** decimal point (it is `V99`, not an edited PIC). If SYSOUT is
part of the golden fixture, reproduce the exact 350-character image; otherwise treat as informational.
Likewise `DISPLAY 'ACCOUNT ' ACCT-ID …` renders ACCT-ID as 11 unsigned digits, and `'… ID-' DALYTRAN-ID`
renders the 16-char card/tran id literally. // source: CBTRN01C.cbl:168,178,181-183; cpy/CVTRA06Y.cpy:4-18

**No INITIALIZE, OCCURS, STRING/UNSTRING, or edited PIC** appear. The only signed field
(`DALYTRAN-AMT`) is never computed, only (optionally) displayed as part of the raw record.

---

## 9. OPEN QUESTIONS / RISKS

1. **No JCL invokes CBTRN01C.** Confirmed by grep (`PGM=CBTRN01C` absent; POSTTRAN runs CBTRN02C).
   The port must synthesize a runner/step (suggest `RUNTRN01`) and DD→table bindings. Decide whether
   CBTRN01C is included in the batch suite at all, or kept only as a characterization target. // source:
   jcl/POSTTRAN.jcl:23 (STEP15 EXEC PGM=CBTRN02C); repo-wide grep for PGM=CBTRN01C = no match.
2. **Is SYSOUT part of the golden fixture?** If the characterization harness diffs SYSOUT, then (a)
   the exact 350-char `DALYTRAN-RECORD` rendering, (b) the per-record `SUCCESSFUL READ OF XREF`/account
   DISPLAY lines, and critically (c) the **extra trailing lookup at EOF** (Faithful Bug #2) and the
   **wrong CLOSE literal/status** (Bugs #3/#4, only on a close failure) must all be reproduced
   precisely. Pin Bug #2's extra output with a fixture that has ≥1 valid trailing transaction. // source:
   CBTRN01C.cbl:164-186
3. **DAILY_TRANSACTION read order.** The sequential `.PS` file has no key; the relational port needs a
   deterministic order to be byte-faithful. Default to import/seed order; if that is non-deterministic,
   order by PK `tran_id` and add a guard test. // source: CBTRN01C.cbl:29-32, 203
4. **INVALID KEY status code.** This port maps "no row" to FileStatus `'23'` per the ARCHITECTURE
   contract; CBTRN01C never inspects the literal status on the keyed reads (only the INVALID KEY
   branch), so the exact `'23'` vs `'00'` mapping only matters if a future test asserts on it. // source:
   ARCHITECTURE.md §"VSAM-semantics" (READ key → '00'/'23'); CBTRN01C.cbl:231-233,245-247
5. **`Z-DISPLAY-IO-STATUS` abnormal branch** only triggers on non-numeric / `'9'`-prefixed statuses,
   which never occur on the happy path. If a test injects such a status, the big-endian byte rendering
   must match (Faithful Bug #7). Pin with a unit test on the formatter. // source: CBTRN01C.cbl:477-482
