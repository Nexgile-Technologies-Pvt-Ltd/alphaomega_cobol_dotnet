# PORT SPEC — CBPAUP0C (Delete Expired Pending Authorization Messages — IMS BMP batch)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/cbl/CBPAUP0C.cbl`
Module: **CardDemo Authorization Module (IMS / DB2 / MQ variant)**.
Program kind: **BATCH IMS BMP** (DL/I via `EXEC DLI`, status in `DIBSTAT`). No screen, no CICS, no COMMAREA, no BMS map. (online program: **NO**.)
Target: `New_Dotnet_Code/src/CardDemo.Ims/CBPAUP0C.cs` (one class over the relational PAUT repositories), per `_design/ARCHITECTURE.md` (§`src/CardDemo.Ims`) and `_design/specs/optional/IMS_SCHEMA.md`.

Copybooks COPYd (affect logic):
- `cpy/CIPAUSMY.cpy` → group **PENDING-AUTH-SUMMARY** (`01 PENDING-AUTH-SUMMARY` / `COPY CIPAUSMY`) — the IMS **root** segment `PAUTSUM0`. // source: CBPAUP0C.cbl:116-117; cpy/CIPAUSMY.cpy:19-31
- `cpy/CIPAUDTY.cpy` → group **PENDING-AUTH-DETAILS** (`01 PENDING-AUTH-DETAILS` / `COPY CIPAUDTY`) — the IMS **child** segment `PAUTDTL1`. // source: CBPAUP0C.cbl:120-121; cpy/CIPAUDTY.cpy:19-54

> NOTE: `IMSFUNCS.cpy` and the PCB-mask copybooks (`PAUTBPCB.CPY`, `PADFLPCB.CPY`, `PASFLPCB.CPY`) are
> **NOT** `COPY`d by this program — it uses the **EXEC DLI** macro interface (function in the verb,
> status in `DIBSTAT` / `DIB`), not the `CALL 'CBLTDLI'` assembler interface. The function-code and
> PCB-mask copybooks belong to the load/unload utilities. The only PCB reference here is the symbolic
> `PCB(PAUT-PCB-NUM)` with `PAUT-PCB-NUM = +2`. // source: CBPAUP0C.cbl:79-95, 223-225

---

## 1. Purpose

CBPAUP0C is a stand-alone **IMS BMP (Batch Message Processing)** housekeeping program whose function is
"Delete Expired Pending Authoriation Messages" (sic — header typo "Authoriation"). It walks the IMS
HIDAM Pending-Authorization database `DBPAUTP0` root-by-root: for every **Pending Authorization
Summary** root segment (`PAUTSUM0`, one per account) it iterates that root's **Pending Authorization
Detail** child segments (`PAUTDTL1`); each detail whose authorization age (today minus the detail's
authorization Julian date, decoded from its 9s-complement form) is **≥ the expiry threshold** is
**deleted**, and the parent summary's running approved/declined counts and amounts are decremented
accordingly; after processing all details of a summary, if the summary's approved-auth count is `<= 0`
the summary root itself is **deleted**. Work is committed periodically via IMS `CHKP` checkpoints (for
restart/recovery), and a final checkpoint and totals report are emitted at end-of-database. Run
parameters (expiry days, checkpoint frequency, checkpoint-display frequency, debug flag) are read once
from `SYSIN`. // source: CBPAUP0C.cbl:1-6 (header — Type BATCH COBOL IMS; Function "Delete Expired Pending Authoriation Messages"); CBPAUP0C.cbl:136-180 (MAIN-PARA driver)

**How it is invoked:** by JCL **`CBPAUP0J.jcl`**, single step **`STEP01 EXEC PGM=DFSRRC00,PARM='BMP,CBPAUP0C,PSBPAUTB'`**.
`DFSRRC00` is the IMS region controller; the real program is `CBPAUP0C`, scheduled under PSB
**`PSBPAUTB`** as region type **BMP**. It is **not** a CICS transaction, **not** a called subprogram in
the ordinary sense — it is entered by the IMS region controller with two PCB-mask arguments on the
`PROCEDURE DIVISION USING` (the I/O PCB and the DB PCB). It ends with `GOBACK` (RC 0 on success, RC 16
on abend). // source: CBPAUP0C.cbl:132-133 (PROCEDURE DIVISION USING IO-PCB-MASK PGM-PCB-MASK); CBPAUP0C.cbl:180 (GOBACK); jobs/CBPAUP0J.md:53-69 (STEP01 / PARM 'BMP,CBPAUP0C,PSBPAUTB')

The single application input is the inline `SYSIN` control card. In `CBPAUP0J.jcl` it is `00,00001,00001,Y`
(expiry-days=0, chkp-freq=1, chkp-display-freq=1, debug=Y). // source: jcl/CBPAUP0J.jcl:36-37; jobs/CBPAUP0J.md:98-114

---

## 2. FILE / TABLE access

This program performs **no QSAM/VSAM file I/O**. All persistent access is **DL/I against the IMS DB
`DBPAUTP0`**, which per `_design/specs/optional/IMS_SCHEMA.md` re-hosts to two relational tables.
`SYSIN` is read via `ACCEPT … FROM SYSIN` (a control card, not a table).

| IMS segment (DL/I) | Hierarchy role | Seq/key field | Relational table (IMS_SCHEMA.md) | DL/I ops used | Maps to (relational repository) |
|---|---|---|---|---|---|
| `PAUTSUM0` (`PENDING-AUTH-SUMMARY`) | **ROOT** | `ACCNTID` (= `PA-ACCT-ID` S9(11) COMP-3, 6-byte packed) | **PAUT_SUMMARY** (PK `ACCT_ID`) | **GN** (get-next root, forward scan); **DLET** (delete root) | Forward cursor `SELECT * FROM PAUT_SUMMARY ORDER BY ACCT_ID ASC` → each GN = `MoveNext()`; **DELETE FROM PAUT_SUMMARY WHERE ACCT_ID=:a** (FK `ON DELETE CASCADE`). |
| `PAUTDTL1` (`PENDING-AUTH-DETAILS`) | **CHILD** of `PAUTSUM0` | `PAUT9CTS` (= `PA-AUTHORIZATION-KEY` = `PA-AUTH-DATE-9C` + `PA-AUTH-TIME-9C`, 8-byte char) | **PAUT_DETAIL** (PK `ACCT_ID,AUTH_KEY`; FK→PAUT_SUMMARY) | **GNP** (get-next-within-parent, forward scan); **DLET** (delete child) | Per-parent forward cursor `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=:current ORDER BY AUTH_KEY ASC` → each GNP = `MoveNext()`; **DELETE FROM PAUT_DETAIL WHERE ACCT_ID=:a AND AUTH_KEY=:k**. |
| (commit control) | — | — | (unit of work) | **CHKP** `ID(WK-CHKPT-ID)` | `COMMIT` + persist a restart token `WK-CHKPT-ID` = `'RMAD'+counter`. |

// source: CBPAUP0C.cbl:223-226 (GN PAUTSUM0 INTO PENDING-AUTH-SUMMARY); CBPAUP0C.cbl:255-258 (GNP PAUTDTL1 INTO PENDING-AUTH-DETAILS); CBPAUP0C.cbl:310-313 (DLET PAUTDTL1); CBPAUP0C.cbl:335-338 (DLET PAUTSUM0); CBPAUP0C.cbl:355-356 (CHKP ID(WK-CHKPT-ID)); IMS_SCHEMA.md:99-159, 314-326, 372

### DL/I status (`DIBSTAT`) → relational meaning

The program reads the DL/I status from **`DIBSTAT`** (the EXEC-DLI interface block field). It uses the
literal status values directly (not the `IMS-RETURN-CODE` 88-levels in WS — those 88s are declared but
**never referenced** in PROCEDURE DIVISION; see §8 / faithful-bug #4). // source: CBPAUP0C.cbl:83-95 (88s in WS, unused); CBPAUP0C.cbl:228,259,315,340,358 (DIBSTAT tests)

| `DIBSTAT` value | Meaning here | Relational equivalent |
|---|---|---|
| `'  '` (two spaces) | OK / segment returned / op succeeded | row returned, or DELETE affected 1 row |
| `'GB'` | end of database (no more roots) | root cursor exhausted → `END-OF-AUTHDB` |
| `'GE'` | segment not found (no more children under parent) | child cursor exhausted → `NO-MORE-AUTHS` |
| any other | unexpected DL/I error | abend (RC 16) |

### Operation → SQL mapping (detail)

- **GN root** (`2000-FIND-NEXT-AUTH-SUMMARY`): `EXEC DLI GN … SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY)`.
  `'  '`→ next summary loaded, `NOT-END-OF-AUTHDB`, counters bumped, `PA-ACCT-ID`→`WS-CURR-APP-ID`;
  `'GB'`→ `END-OF-AUTHDB`; OTHER→ display + abend. Establishes parentage for the following GNPs (the
  resolved `ACCT_ID` becomes `:current_parent_acct_id`). // source: CBPAUP0C.cbl:223-241; IMS_SCHEMA.md:226-241
- **GNP child** (`3000-FIND-NEXT-AUTH-DTL`): `EXEC DLI GNP … SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS)`.
  `'  '`→ `MORE-AUTHS`, count bumped; `'GE'` **or** `'GB'`→ `NO-MORE-AUTHS`; OTHER→ display + abend.
  Cursor over children of the parent set by the last GN. // source: CBPAUP0C.cbl:255-271; IMS_SCHEMA.md:243-258
- **DLET child** (`5000-DELETE-AUTH-DTL`): `EXEC DLI DLET … SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS)`.
  `DIBSTAT = SPACES`→ deleted-count bumped; else display + abend. → `DELETE FROM PAUT_DETAIL WHERE
  ACCT_ID=:a AND AUTH_KEY=:k`. // source: CBPAUP0C.cbl:310-321; IMS_SCHEMA.md:314-322
- **DLET root** (`6000-DELETE-AUTH-SUMMARY`): `EXEC DLI DLET … SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY)`.
  `DIBSTAT = SPACES`→ deleted-count bumped; else display + abend. → `DELETE FROM PAUT_SUMMARY WHERE
  ACCT_ID=:a` (FK `ON DELETE CASCADE` mirrors IMS root-delete cascading to children). // source: CBPAUP0C.cbl:335-346; IMS_SCHEMA.md:324-329
- **CHKP** (`9000-TAKE-CHECKPOINT`): `EXEC DLI CHKP ID(WK-CHKPT-ID)`. `DIBSTAT = SPACES`→ chkp-count
  bumped, progress display every `P-CHKP-DIS-FREQ`; else display + abend. → `COMMIT` + restart token.
  // source: CBPAUP0C.cbl:355-370; IMS_SCHEMA.md:372

There are **no GU / REPL / ISRT / GHU / GHN / GHNP** operations in this program. The only data
mutations are the two DLETs; the summary running-totals are decremented **in the io-area in memory**
(see §5/§6) but are **never written back** to PAUT_SUMMARY (no REPL) — see faithful-bug #1.

---

## 3. WORKING-STORAGE / record structures that affect logic

### 3.1 PENDING-AUTH-SUMMARY (root `PAUTSUM0`, copybook CIPAUSMY) — RECLN 100

// source: cpy/CIPAUSMY.cpy:19-31. Maps to **PAUT_SUMMARY** (IMS_SCHEMA.md:66-97).
- `PA-ACCT-ID`            S9(11) COMP-3  → `ACCT_ID` (root key `ACCNTID`; → `WS-CURR-APP-ID`).
- `PA-CUST-ID`            9(09)          → `CUST_ID`.
- `PA-AUTH-STATUS`        X(01)          → `AUTH_STATUS`.
- `PA-ACCOUNT-STATUS`     X(02) OCCURS 5 → `ACCOUNT_STATUS_1..5` (flattened; not used by this program).
- `PA-CREDIT-LIMIT`       S9(09)V99 COMP-3 → `CREDIT_LIMIT` (not used here).
- `PA-CASH-LIMIT`         S9(09)V99 COMP-3 → `CASH_LIMIT` (not used here).
- `PA-CREDIT-BALANCE`     S9(09)V99 COMP-3 → `CREDIT_BALANCE` (not used here).
- `PA-CASH-BALANCE`       S9(09)V99 COMP-3 → `CASH_BALANCE` (not used here).
- `PA-APPROVED-AUTH-CNT`  S9(04) COMP    → `APPROVED_AUTH_CNT` (**decremented** when an approved detail expires; tested in 6000 gate). // source: CBPAUP0C.cbl:288,156
- `PA-DECLINED-AUTH-CNT`  S9(04) COMP    → `DECLINED_AUTH_CNT` (**decremented** when a declined detail expires). // source: CBPAUP0C.cbl:291
- `PA-APPROVED-AUTH-AMT`  S9(09)V99 COMP-3 → `APPROVED_AUTH_AMT` (**decremented** by `PA-APPROVED-AMT`). // source: CBPAUP0C.cbl:289
- `PA-DECLINED-AUTH-AMT`  S9(09)V99 COMP-3 → `DECLINED_AUTH_AMT` (**decremented** by `PA-TRANSACTION-AMT`). // source: CBPAUP0C.cbl:292
- `FILLER` X(34) → pad to 100 bytes (dropped; not a column).

### 3.2 PENDING-AUTH-DETAILS (child `PAUTDTL1`, copybook CIPAUDTY) — RECLN 200

// source: cpy/CIPAUDTY.cpy:19-54. Maps to **PAUT_DETAIL** (IMS_SCHEMA.md:99-159).
- `PA-AUTHORIZATION-KEY` group → child seq key `PAUT9CTS` / `AUTH_KEY`:
  - `PA-AUTH-DATE-9C`  S9(05) COMP-3 → `AUTH_DATE_9C`. **9s-complement Julian**: real `yyddd = 99999 − value`. **Drives expiry test.** // source: cpy/CIPAUDTY.cpy:20; CBPAUP0C.cbl:280
  - `PA-AUTH-TIME-9C`  S9(09) COMP-3 → `AUTH_TIME_9C` (9s-complement time; not used arithmetically here, part of key/order).
- `PA-AUTH-ORIG-DATE`  X(06) → `AUTH_ORIG_DATE`; `PA-AUTH-ORIG-TIME` X(06) → `AUTH_ORIG_TIME` (not used).
- `PA-CARD-NUM` X(16) → `CARD_NUM`; `PA-AUTH-TYPE` X(04) → `AUTH_TYPE`; `PA-CARD-EXPIRY-DATE` X(04) → `CARD_EXPIRY_DATE` (not used).
- `PA-MESSAGE-TYPE`/`PA-MESSAGE-SOURCE`/`PA-AUTH-ID-CODE` (not used).
- `PA-AUTH-RESP-CODE` X(02), 88 `PA-AUTH-APPROVED VALUE '00'` → `AUTH_RESP_CODE`. **Approved-vs-declined branch** in 4000 tests `PA-AUTH-RESP-CODE = '00'` (NB: uses the **literal `'00'` test, not the 88-name `PA-AUTH-APPROVED`**; equivalent result). // source: cpy/CIPAUDTY.cpy:30-31; CBPAUP0C.cbl:287
- `PA-AUTH-RESP-REASON` X(04); `PA-PROCESSING-CODE` 9(06) (not used).
- `PA-TRANSACTION-AMT` S9(10)V99 COMP-3 → `TRANSACTION_AMT` (**subtracted from `PA-DECLINED-AUTH-AMT`** on a declined expiry). // source: cpy/CIPAUDTY.cpy:34; CBPAUP0C.cbl:292
- `PA-APPROVED-AMT`     S9(10)V99 COMP-3 → `APPROVED_AMT` (**subtracted from `PA-APPROVED-AUTH-AMT`** on an approved expiry). // source: cpy/CIPAUDTY.cpy:35; CBPAUP0C.cbl:289
- remaining merchant / match-status / fraud / filler fields → corresponding PAUT_DETAIL columns; **not used** by this program.

### 3.3 Control / parameter WORKING-STORAGE (`01 WS-VARIABLES`, `01 PRM-INFO`, `01 WS-IMS-VARIABLES`)

- `WS-PGMNAME`        X(08) VALUE `'CBPAUP0C'`. // source: CBPAUP0C.cbl:42
- `CURRENT-DATE`      9(06) — receives `ACCEPT … FROM DATE` (YYMMDD); **not used after** the accept (only `CURRENT-YYDDD` drives logic). // source: CBPAUP0C.cbl:43, 186
- `CURRENT-YYDDD`     9(05) — receives `ACCEPT … FROM DAY` (Julian YYDDD); **today's date** used in expiry math. // source: CBPAUP0C.cbl:44, 187, 282
- `WS-AUTH-DATE`      9(05) — decoded real Julian of the current detail (`99999 − PA-AUTH-DATE-9C`). // source: CBPAUP0C.cbl:45, 280
- `WS-EXPIRY-DAYS`    S9(4) COMP — the active expiry threshold (from `P-EXPIRY-DAYS` or default 5). // source: CBPAUP0C.cbl:46, 197-199
- `WS-DAY-DIFF`       S9(4) COMP — `CURRENT-YYDDD − WS-AUTH-DATE` (age in "days"; see §6/§9 caveat). // source: CBPAUP0C.cbl:47, 282
- `IDX`              S9(4) COMP — declared, **unused**. // source: CBPAUP0C.cbl:48
- `WS-CURR-APP-ID`    9(11) — last summary's account id (for checkpoint progress display). // source: CBPAUP0C.cbl:49, 233, 363, 368
- `WS-NO-CHKP`        9(8) VALUE 0 — checkpoints since last progress display. // source: CBPAUP0C.cbl:51, 359-361
- `WS-AUTH-SMRY-PROC-CNT` 9(8) VALUE 0 — summaries processed since last checkpoint (drives chkp trigger). // source: CBPAUP0C.cbl:52, 232, 160, 163
- `WS-TOT-REC-WRITTEN`  S9(8) COMP VALUE 0 — declared, **unused**. // source: CBPAUP0C.cbl:53
- `WS-NO-SUMRY-READ`    S9(8) COMP VALUE 0 — count of summary roots read. // source: CBPAUP0C.cbl:54, 231
- `WS-NO-SUMRY-DELETED` S9(8) COMP VALUE 0 — count of summary roots deleted. // source: CBPAUP0C.cbl:55, 341
- `WS-NO-DTL-READ`      S9(8) COMP VALUE 0 — count of detail children read. // source: CBPAUP0C.cbl:56, 262
- `WS-NO-DTL-DELETED`   S9(8) COMP VALUE 0 — count of detail children deleted. // source: CBPAUP0C.cbl:57, 316
- `WS-ERR-FLG` X(01) VALUE `'N'`, 88 `ERR-FLG-ON`='Y' / `ERR-FLG-OFF`='N' — loop sentinel. **Note:** ERR-FLG is **never SET to 'Y'** anywhere; the abend path GOBACKs instead, so this flag is effectively always 'N' (the outer `PERFORM UNTIL ERR-FLG-ON OR END-OF-AUTHDB` exits only on `END-OF-AUTHDB`). // source: CBPAUP0C.cbl:59-61, 142
- `WS-END-OF-AUTHDB-FLAG` X(01), 88 `END-OF-AUTHDB`='Y' / `NOT-END-OF-AUTHDB`='N' — root-scan EOF. // source: CBPAUP0C.cbl:62-64
- `WS-MORE-AUTHS-FLAG` X(01), 88 `MORE-AUTHS`='Y' / `NO-MORE-AUTHS`='N' — child-scan sentinel. // source: CBPAUP0C.cbl:65-67
- `WS-QUALIFY-DELETE-FLAG` X(01), 88 `QUALIFIED-FOR-DELETE`='Y' / `NOT-QUALIFIED-FOR-DELETE`='N'. // source: CBPAUP0C.cbl:68-70
- `WS-INFILE-STATUS` X(02) VALUE SPACES; `WS-CUSTID-STATUS` X(02) VALUE SPACES, 88 `END-OF-FILE`='10' — declared, **unused** (no QSAM files). // source: CBPAUP0C.cbl:71-73
- `WK-CHKPT-ID` group = `FILLER X(04) VALUE 'RMAD'` + `WK-CHKPT-ID-CTR 9(04) VALUE ZEROES` — IMS checkpoint id (8 bytes). **Note:** `WK-CHKPT-ID-CTR` is **never incremented** in PROCEDURE DIVISION, so the checkpoint id stays `'RMAD0000'` on every CHKP. // source: CBPAUP0C.cbl:75-77, 355
- `WS-IMS-VARIABLES`: `PSB-NAME` X(8) VALUE `'PSBPAUTB'`; `PCB-OFFSET.PAUT-PCB-NUM` S9(4) COMP VALUE **+2** (the DB PCB number used in every `PCB(PAUT-PCB-NUM)`); `IMS-RETURN-CODE` X(02) with status 88s (**all unused** — see faithful-bug #4); `WS-IMS-PSB-SCHD-FLG` X(1) with 88s (**unused**). // source: CBPAUP0C.cbl:79-95
- `PRM-INFO` (read from SYSIN): `P-EXPIRY-DAYS` 9(02) + FILLER X(01) + `P-CHKP-FREQ` X(05) + FILLER X(01) + `P-CHKP-DIS-FREQ` X(05) + FILLER X(01) + `P-DEBUG-FLAG` X(01) (88 `DEBUG-ON`='Y'/`DEBUG-OFF`='N') + FILLER X(01). // source: CBPAUP0C.cbl:98-108

### 3.4 LINKAGE / PROCEDURE DIVISION USING

`01 IO-PCB-MASK PIC X` and `01 PGM-PCB-MASK PIC X` are the two PCB masks passed by the IMS region
controller; `PROCEDURE DIVISION USING IO-PCB-MASK PGM-PCB-MASK`. The program does not dereference these
masks directly (it uses `PCB(PAUT-PCB-NUM)` symbolically); they exist so IMS can bind PCBs at entry.
// source: CBPAUP0C.cbl:125-133

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM/flag flow preserved.
Note: every paragraph is performed with `THRU nnnn-EXIT`; the `nnnn-EXIT` paragraphs are bare `EXIT`.

### MAIN-PARA (mainline driver) // source: CBPAUP0C.cbl:136-180
1. PERFORM `1000-INITIALIZE THRU 1000-EXIT` (accept date/parms, apply defaults). // source: CBPAUP0C.cbl:138
2. PERFORM `2000-FIND-NEXT-AUTH-SUMMARY THRU 2000-EXIT` (prime: read first root). // source: CBPAUP0C.cbl:140
3. **Outer loop** `PERFORM UNTIL ERR-FLG-ON OR END-OF-AUTHDB`: // source: CBPAUP0C.cbl:142-167
   a. PERFORM `3000-FIND-NEXT-AUTH-DTL THRU 3000-EXIT` (prime first child of this root). // source: CBPAUP0C.cbl:144
   b. **Inner loop** `PERFORM UNTIL NO-MORE-AUTHS`: // source: CBPAUP0C.cbl:146-154
      - PERFORM `4000-CHECK-IF-EXPIRED THRU 4000-EXIT` (sets QUALIFIED-FOR-DELETE + decrements summary totals in memory). // source: CBPAUP0C.cbl:147
      - IF `QUALIFIED-FOR-DELETE` → PERFORM `5000-DELETE-AUTH-DTL THRU 5000-EXIT`. // source: CBPAUP0C.cbl:149-151
      - PERFORM `3000-FIND-NEXT-AUTH-DTL THRU 3000-EXIT` (advance to next child). // source: CBPAUP0C.cbl:153
   c. IF `PA-APPROVED-AUTH-CNT <= 0 AND PA-APPROVED-AUTH-CNT <= 0` → PERFORM `6000-DELETE-AUTH-SUMMARY THRU 6000-EXIT`. **(Duplicated condition — both conjuncts test `PA-APPROVED-AUTH-CNT`; `PA-DECLINED-AUTH-CNT` is NOT tested. See faithful-bug #2.)** // source: CBPAUP0C.cbl:156-158
   d. IF `WS-AUTH-SMRY-PROC-CNT > P-CHKP-FREQ` → PERFORM `9000-TAKE-CHECKPOINT THRU 9000-EXIT`; MOVE 0 TO `WS-AUTH-SMRY-PROC-CNT`. **(Compares a numeric COMP/`9(8)` against the X(05) display field `P-CHKP-FREQ`; see §6 / faithful-bug #3.)** // source: CBPAUP0C.cbl:160-164
   e. PERFORM `2000-FIND-NEXT-AUTH-SUMMARY THRU 2000-EXIT` (read next root; may set END-OF-AUTHDB). // source: CBPAUP0C.cbl:165
4. PERFORM `9000-TAKE-CHECKPOINT THRU 9000-EXIT` (final checkpoint). // source: CBPAUP0C.cbl:169
5. DISPLAY a blank line, a rule line, then four totals lines (`# TOTAL SUMMARY READ`, `# SUMMARY REC DELETED`, `# TOTAL DETAILS READ`, `# DETAILS REC DELETED`), a rule, a blank. // source: CBPAUP0C.cbl:171-178
6. `GOBACK`. // source: CBPAUP0C.cbl:180

### 1000-INITIALIZE // source: CBPAUP0C.cbl:183-210
1. `ACCEPT CURRENT-DATE FROM DATE`; `ACCEPT CURRENT-YYDDD FROM DAY`. // source: CBPAUP0C.cbl:186-187
2. `ACCEPT PRM-INFO FROM SYSIN`. // source: CBPAUP0C.cbl:189
3. DISPLAY start banner, rule, `'CBPAUP0C PARM RECEIVED :' PRM-INFO`, `'TODAYS DATE :' CURRENT-YYDDD`, blank. // source: CBPAUP0C.cbl:190-194
4. IF `P-EXPIRY-DAYS IS NUMERIC` → MOVE `P-EXPIRY-DAYS`→`WS-EXPIRY-DAYS`; ELSE → MOVE 5→`WS-EXPIRY-DAYS`. // source: CBPAUP0C.cbl:196-200
5. IF `P-CHKP-FREQ = SPACES OR 0 OR LOW-VALUES` → MOVE 5→`P-CHKP-FREQ`. // source: CBPAUP0C.cbl:201-203
6. IF `P-CHKP-DIS-FREQ = SPACES OR 0 OR LOW-VALUES` → MOVE 10→`P-CHKP-DIS-FREQ`. // source: CBPAUP0C.cbl:204-206
7. IF `P-DEBUG-FLAG NOT = 'Y'` → MOVE `'N'`→`P-DEBUG-FLAG`. // source: CBPAUP0C.cbl:207-209
   > NOTE: `P-CHKP-FREQ`/`P-CHKP-DIS-FREQ` are X(05) alphanumeric. `MOVE 5 TO P-CHKP-FREQ` moves a
   > numeric literal into an alphanumeric field → result is right-justified-as-display? No: MOVE of a
   > numeric literal to an alphanumeric receiving field stores it **left-justified, space-filled**, i.e.
   > `'5    '` for `MOVE 5` and `'10   '` for `MOVE 10`. This matters for the later numeric comparison;
   > reproduce exactly (see §6 / faithful-bug #3). // source: CBPAUP0C.cbl:202,205,101,103

### 2000-FIND-NEXT-AUTH-SUMMARY // source: CBPAUP0C.cbl:216-244
1. IF `DEBUG-ON` → DISPLAY `'DEBUG: AUTH SMRY READ : ' WS-NO-SUMRY-READ`. // source: CBPAUP0C.cbl:219-221
2. `EXEC DLI GN USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTSUM0) INTO(PENDING-AUTH-SUMMARY) END-EXEC`. → root cursor `MoveNext()`. // source: CBPAUP0C.cbl:223-226
3. EVALUATE `DIBSTAT`:
   - `'  '` → SET `NOT-END-OF-AUTHDB`; ADD 1→`WS-NO-SUMRY-READ`; ADD 1→`WS-AUTH-SMRY-PROC-CNT`; MOVE `PA-ACCT-ID`→`WS-CURR-APP-ID`. // source: CBPAUP0C.cbl:229-233
   - `'GB'` → SET `END-OF-AUTHDB`. // source: CBPAUP0C.cbl:234-235
   - OTHER → DISPLAY `'AUTH SUMMARY READ FAILED  :' DIBSTAT`, `'SUMMARY READ BEFORE ABEND :' WS-NO-SUMRY-READ`; PERFORM `9999-ABEND`. // source: CBPAUP0C.cbl:236-240

### 3000-FIND-NEXT-AUTH-DTL // source: CBPAUP0C.cbl:248-274
1. IF `DEBUG-ON` → DISPLAY `'DEBUG: AUTH DTL READ : ' WS-NO-DTL-READ`. // source: CBPAUP0C.cbl:251-253
2. `EXEC DLI GNP USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTDTL1) INTO(PENDING-AUTH-DETAILS) END-EXEC`. → child cursor `MoveNext()` within current parent. // source: CBPAUP0C.cbl:255-258
3. EVALUATE `DIBSTAT`:
   - `'  '` → SET `MORE-AUTHS`; ADD 1→`WS-NO-DTL-READ`. // source: CBPAUP0C.cbl:260-262
   - `'GE'` / `'GB'` (both WHENs fall to same action) → SET `NO-MORE-AUTHS`. // source: CBPAUP0C.cbl:263-265
   - OTHER → DISPLAY `'AUTH DETAIL READ FAILED  :' DIBSTAT`, `'SUMMARY AUTH APP ID      :' PA-ACCT-ID`, `'DETAIL READ BEFORE ABEND :' WS-NO-DTL-READ`; PERFORM `9999-ABEND`. // source: CBPAUP0C.cbl:266-270

### 4000-CHECK-IF-EXPIRED // source: CBPAUP0C.cbl:277-300
1. `COMPUTE WS-AUTH-DATE = 99999 - PA-AUTH-DATE-9C` (decode 9s-complement to real Julian YYDDD). // source: CBPAUP0C.cbl:280
2. `COMPUTE WS-DAY-DIFF = CURRENT-YYDDD - WS-AUTH-DATE` (age, S9(4) COMP). // source: CBPAUP0C.cbl:282
3. IF `WS-DAY-DIFF >= WS-EXPIRY-DAYS`: // source: CBPAUP0C.cbl:284
   - SET `QUALIFIED-FOR-DELETE`. // source: CBPAUP0C.cbl:285
   - IF `PA-AUTH-RESP-CODE = '00'` (approved): `SUBTRACT 1 FROM PA-APPROVED-AUTH-CNT`; `SUBTRACT PA-APPROVED-AMT FROM PA-APPROVED-AUTH-AMT`. // source: CBPAUP0C.cbl:287-289
   - ELSE (declined): `SUBTRACT 1 FROM PA-DECLINED-AUTH-CNT`; `SUBTRACT PA-TRANSACTION-AMT FROM PA-DECLINED-AUTH-AMT`. // source: CBPAUP0C.cbl:290-292
4. ELSE → SET `NOT-QUALIFIED-FOR-DELETE`. // source: CBPAUP0C.cbl:294-295
   > Arithmetic notes: both COMPUTEs into S9(4) COMP (binary halfword, signed). 9s-complement decode is
   > exact; the day-diff can be **negative** (future-dated) → `>= WS-EXPIRY-DAYS` is false. Decrements
   > are plain SUBTRACT into the in-memory io-area (COMP counts / COMP-3 amounts); **truncate toward
   > zero, no rounding** per ARCHITECTURE.md money rule. These mutated summary fields are NEVER written
   > back (no REPL) — see §7 faithful-bug #1. // source: CBPAUP0C.cbl:280-292; ARCHITECTURE.md:38

### 5000-DELETE-AUTH-DTL // source: CBPAUP0C.cbl:303-325
1. IF `DEBUG-ON` → DISPLAY `'DEBUG: AUTH DTL DLET : ' PA-ACCT-ID`. // source: CBPAUP0C.cbl:306-308
2. `EXEC DLI DLET USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTDTL1) FROM(PENDING-AUTH-DETAILS) END-EXEC`. → `DELETE FROM PAUT_DETAIL WHERE ACCT_ID=:a AND AUTH_KEY=:k`. // source: CBPAUP0C.cbl:310-313
3. IF `DIBSTAT = SPACES` → ADD 1→`WS-NO-DTL-DELETED`; ELSE → DISPLAY `'AUTH DETAIL DELETE FAILED :' DIBSTAT`, `'AUTH APP ID               :' PA-ACCT-ID`; PERFORM `9999-ABEND`. // source: CBPAUP0C.cbl:315-321

### 6000-DELETE-AUTH-SUMMARY // source: CBPAUP0C.cbl:328-349
1. IF `DEBUG-ON` → DISPLAY `'DEBUG: AUTH SMRY DLET : ' PA-ACCT-ID`. // source: CBPAUP0C.cbl:331-333
2. `EXEC DLI DLET USING PCB(PAUT-PCB-NUM) SEGMENT(PAUTSUM0) FROM(PENDING-AUTH-SUMMARY) END-EXEC`. → `DELETE FROM PAUT_SUMMARY WHERE ACCT_ID=:a` (FK ON DELETE CASCADE removes any leftover children). // source: CBPAUP0C.cbl:335-338
3. IF `DIBSTAT = SPACES` → ADD 1→`WS-NO-SUMRY-DELETED`; ELSE → DISPLAY `'AUTH SUMMARY DELETE FAILED :' DIBSTAT`, `'AUTH APP ID                :' PA-ACCT-ID`; PERFORM `9999-ABEND`. // source: CBPAUP0C.cbl:340-346

### 9000-TAKE-CHECKPOINT // source: CBPAUP0C.cbl:352-374
1. `EXEC DLI CHKP ID(WK-CHKPT-ID) END-EXEC`. → `COMMIT` + persist restart token `WK-CHKPT-ID`. // source: CBPAUP0C.cbl:355-356
2. IF `DIBSTAT = SPACES`: ADD 1→`WS-NO-CHKP`; IF `WS-NO-CHKP >= P-CHKP-DIS-FREQ` → MOVE 0→`WS-NO-CHKP`; DISPLAY `'CHKP SUCCESS: AUTH COUNT - ' WS-NO-SUMRY-READ ', APP ID - ' WS-CURR-APP-ID`. // source: CBPAUP0C.cbl:358-364
3. ELSE → DISPLAY `'CHKP FAILED: DIBSTAT - ' DIBSTAT ', REC COUNT - ' WS-NO-SUMRY-READ ', APP ID - ' WS-CURR-APP-ID`; PERFORM `9999-ABEND`. // source: CBPAUP0C.cbl:365-369
   > Same X(05)-vs-numeric comparison caveat: `WS-NO-CHKP` (9(8)) `>= P-CHKP-DIS-FREQ` (X(05)). See §6.

### 9999-ABEND // source: CBPAUP0C.cbl:377-383
1. DISPLAY `'CBPAUP0C ABENDING ...'`. // source: CBPAUP0C.cbl:380
2. `MOVE 16 TO RETURN-CODE`. // source: CBPAUP0C.cbl:382
3. `GOBACK`. // source: CBPAUP0C.cbl:383
   > Port: terminate the batch step with RC 16 (e.g. `Runtime.Abend(16)` / set process exit code 16).
   > Note this is a hard `GOBACK` (returns to IMS region controller), NOT a `CALL CEE3ABD`. It does NOT
   > unwind the outer PERFORM via ERR-FLG; it exits the whole program.

### EXIT paragraphs
`1000-EXIT`, `2000-EXIT`, `3000-EXIT`, `4000-EXIT`, `5000-EXIT`, `6000-EXIT`, `9000-EXIT`, `9999-EXIT`
— each is a single `EXIT` (no-op landing label for the `THRU`). // source: CBPAUP0C.cbl:212-213,243-244,273-274,299-300,324-325,348-349,373-374,385-386

---

## 5. VALIDATION RULES & exact literal messages

Business "validation" is minimal: a single **expiry test** plus DL/I status checks (abend on
anything unexpected). There are no screen-field validations (batch program).

- **Parameter parsing/defaults** (1000-INITIALIZE):
  - `P-EXPIRY-DAYS` used as-is **only if `IS NUMERIC`**, else defaulted to **5**. // source: CBPAUP0C.cbl:196-200
  - `P-CHKP-FREQ` defaulted to `5` if `= SPACES OR 0 OR LOW-VALUES`. // source: CBPAUP0C.cbl:201-203
  - `P-CHKP-DIS-FREQ` defaulted to `10` if `= SPACES OR 0 OR LOW-VALUES`. // source: CBPAUP0C.cbl:204-206
  - `P-DEBUG-FLAG` forced to `'N'` unless exactly `'Y'`. // source: CBPAUP0C.cbl:207-209
- **Expiry test** (4000): a detail qualifies for delete iff `WS-DAY-DIFF >= WS-EXPIRY-DAYS`, where
  `WS-DAY-DIFF = CURRENT-YYDDD − (99999 − PA-AUTH-DATE-9C)`. // source: CBPAUP0C.cbl:280-284
- **Summary delete gate** (MAIN): delete the root only if `PA-APPROVED-AUTH-CNT <= 0` (tested twice;
  declined-count NOT tested — bug #2). // source: CBPAUP0C.cbl:156

**Exact literal strings (SYSOUT) to reproduce verbatim** (preserve trailing/embedded spacing exactly):
- `'STARTING PROGRAM CBPAUP0C::'` // source: CBPAUP0C.cbl:190
- `'*-------------------------------------*'` (rule line; used at 191, 172, 177) // source: CBPAUP0C.cbl:191,172,177
- `'CBPAUP0C PARM RECEIVED :' PRM-INFO` // source: CBPAUP0C.cbl:192
- `'TODAYS DATE            :' CURRENT-YYDDD` // source: CBPAUP0C.cbl:193
- `'DEBUG: AUTH SMRY READ : ' WS-NO-SUMRY-READ` // source: CBPAUP0C.cbl:220
- `'DEBUG: AUTH DTL READ : ' WS-NO-DTL-READ` // source: CBPAUP0C.cbl:252
- `'DEBUG: AUTH DTL DLET : ' PA-ACCT-ID` // source: CBPAUP0C.cbl:307
- `'DEBUG: AUTH SMRY DLET : ' PA-ACCT-ID` // source: CBPAUP0C.cbl:332
- `'AUTH SUMMARY READ FAILED  :' DIBSTAT` // source: CBPAUP0C.cbl:237
- `'SUMMARY READ BEFORE ABEND :' WS-NO-SUMRY-READ` // source: CBPAUP0C.cbl:238-239
- `'AUTH DETAIL READ FAILED  :' DIBSTAT` // source: CBPAUP0C.cbl:267
- `'SUMMARY AUTH APP ID      :' PA-ACCT-ID` // source: CBPAUP0C.cbl:268
- `'DETAIL READ BEFORE ABEND :' WS-NO-DTL-READ` // source: CBPAUP0C.cbl:269
- `'AUTH DETAIL DELETE FAILED :' DIBSTAT` // source: CBPAUP0C.cbl:318
- `'AUTH APP ID               :' PA-ACCT-ID` // source: CBPAUP0C.cbl:319
- `'AUTH SUMMARY DELETE FAILED :' DIBSTAT` // source: CBPAUP0C.cbl:343
- `'AUTH APP ID                :' PA-ACCT-ID` // source: CBPAUP0C.cbl:344
- `'CHKP SUCCESS: AUTH COUNT - ' WS-NO-SUMRY-READ ', APP ID - ' WS-CURR-APP-ID` // source: CBPAUP0C.cbl:362-363
- `'CHKP FAILED: DIBSTAT - ' DIBSTAT ', REC COUNT - ' WS-NO-SUMRY-READ ', APP ID - ' WS-CURR-APP-ID` // source: CBPAUP0C.cbl:366-368
- `'# TOTAL SUMMARY READ  :' WS-NO-SUMRY-READ` // source: CBPAUP0C.cbl:173
- `'# SUMMARY REC DELETED :' WS-NO-SUMRY-DELETED` // source: CBPAUP0C.cbl:174
- `'# TOTAL DETAILS READ  :' WS-NO-DTL-READ` // source: CBPAUP0C.cbl:175
- `'# DETAILS REC DELETED :' WS-NO-DTL-DELETED` // source: CBPAUP0C.cbl:176
- `'CBPAUP0C ABENDING ...'` // source: CBPAUP0C.cbl:380

---

## 6. ARITHMETIC / COMPUTE / counter notes

- **`COMPUTE WS-AUTH-DATE = 99999 - PA-AUTH-DATE-9C`** — `PA-AUTH-DATE-9C` is S9(05) COMP-3 (packed)
  holding the **9s-complement** of the real Julian (`stored = 99999 − yyddd`); so this restores
  `yyddd`. Receiver `WS-AUTH-DATE` 9(05) unsigned. Result fits in 5 digits; no truncation for valid
  data. // source: CBPAUP0C.cbl:280; cpy/CIPAUDTY.cpy:20
- **`COMPUTE WS-DAY-DIFF = CURRENT-YYDDD - WS-AUTH-DATE`** — receiver S9(4) COMP (signed binary
  halfword). Can be **negative** (future auth) or positive. **Caveat:** both operands are Julian
  `YYDDD` (year*1000 + day-of-year); subtracting them is **NOT a true calendar day-difference across
  year boundaries** (e.g. `23001 − 22365 = 636`, not 1). This is the program's actual (faithful)
  behavior — reproduce as a plain integer subtraction of the two YYDDD values, no calendar math. See
  §9 risk. // source: CBPAUP0C.cbl:282; jobs/CBPAUP0J.md:148-149
- **`SUBTRACT 1 FROM PA-APPROVED-AUTH-CNT` / `… PA-DECLINED-AUTH-CNT`** — S9(04) COMP halfword counts;
  may go **negative** (no floor) when the stored count is already 0. // source: CBPAUP0C.cbl:288,291
- **`SUBTRACT PA-APPROVED-AMT FROM PA-APPROVED-AUTH-AMT`** — S9(10)V99 − into S9(09)V99: **the summary
  amount has fewer integer digits (9) than the detail amount (10)**. A detail approved-amt ≥ 1,000,000,000.00
  would **overflow on subtract → silent high-order truncation** (no ON SIZE ERROR clause). Reproduce
  with CobolDecimal truncate-toward-zero + silent overflow per ARCHITECTURE.md. Same width mismatch for
  `SUBTRACT PA-TRANSACTION-AMT (S9(10)V99) FROM PA-DECLINED-AUTH-AMT (S9(09)V99)`. // source: CBPAUP0C.cbl:289,292; cpy/CIPAUSMY.cpy:29-30; cpy/CIPAUDTY.cpy:34-35; ARCHITECTURE.md:38
- **Checkpoint trigger `WS-AUTH-SMRY-PROC-CNT > P-CHKP-FREQ`** and **progress `WS-NO-CHKP >= P-CHKP-DIS-FREQ`**:
  the right operands are **X(05) alphanumeric** display fields, the left are numeric (`9(8)`). COBOL
  compares numeric-vs-alphanumeric by treating the numeric as its character (zoned) form, OR by numeric
  conversion of the alphanumeric — under IBM COBOL a numeric/alphanumeric comparison converts the
  alphanumeric operand using its content as digits when it is all-numeric characters. With the JCL card
  value `P-CHKP-FREQ='00001'`, this compares as `1`; with a defaulted `MOVE 5` it becomes `'5    '`
  (left-justified, trailing spaces) which is **not all-numeric** → the comparison semantics differ from
  `5`. The faithful port must reproduce IBM's class/zoned comparison of these exact bytes; for the
  CardDemo run card (`00001`) the effective frequency is 1. See §7 faithful-bug #3 and §9. // source: CBPAUP0C.cbl:160,360,101,103,202,205
- **Counters:** `WS-NO-SUMRY-READ/DELETED`, `WS-NO-DTL-READ/DELETED`, `WS-AUTH-SMRY-PROC-CNT`,
  `WS-NO-CHKP` are simple `ADD 1`. `WK-CHKPT-ID-CTR` is **never incremented** (stays 0000). // source: CBPAUP0C.cbl:231-232,262,316,341,359,77

---

## 7. FAITHFUL BUGS to reproduce verbatim (do NOT fix)

**#1 — Decremented summary totals are never written back (no REPL).** `4000-CHECK-IF-EXPIRED`
decrements `PA-APPROVED-AUTH-CNT/AMT` or `PA-DECLINED-AUTH-CNT/AMT` in the **in-memory** io-area, but
the program issues **no `REPL` on `PAUTSUM0`** — only `DLET`. So while a summary survives (is not
deleted), its persisted counts/amounts are **NOT updated** by this run; the decremented values are used
only transiently for the `6000` delete gate within the same root iteration and are lost when the next
`GN` overwrites the io-area. The port must replicate this: mutate the in-memory summary object, use it
for the gate test, but do **NOT** UPDATE PAUT_SUMMARY. // source: CBPAUP0C.cbl:287-292 (SUBTRACTs), 156-158 (gate uses them), entire program has no REPL — only DLET at 310-313 / 335-338

**#2 — Summary-delete gate has a duplicated/incorrect condition.** Line 156:
`IF PA-APPROVED-AUTH-CNT <= 0 AND PA-APPROVED-AUTH-CNT <= 0` — both conjuncts test the **approved**
count; the **declined** count (`PA-DECLINED-AUTH-CNT`) is **never** tested. The almost-certain intent
was `PA-APPROVED-AUTH-CNT <= 0 AND PA-DECLINED-AUTH-CNT <= 0`. Reproduce the duplicated test exactly:
a summary is deleted whenever its approved-count ≤ 0, **regardless of remaining declined auths**.
// source: CBPAUP0C.cbl:156

**#3 — Checkpoint-frequency fields are alphanumeric, compared against numeric counters, and defaulted
with `MOVE <numeric-literal>`.** `P-CHKP-FREQ` and `P-CHKP-DIS-FREQ` are `PIC X(05)`. They are
(a) compared `IF P-CHKP-FREQ = SPACES OR 0 OR LOW-VALUES` (mixed class), (b) defaulted via
`MOVE 5`/`MOVE 10` into the X(05) field (yielding `'5    '`/`'10   '`, left-justified space-filled),
then (c) used as the right operand of numeric comparisons (`WS-AUTH-SMRY-PROC-CNT > P-CHKP-FREQ`,
`WS-NO-CHKP >= P-CHKP-DIS-FREQ`). The combination is non-idiomatic and the defaulted (space-padded)
values do not compare numerically as 5/10. Reproduce the exact byte content and IBM comparison
semantics rather than "fixing" the types. (For the shipped CardDemo card `00001`, no default fires and
the value compares as 1.) // source: CBPAUP0C.cbl:101,103,201-206,160,360

**#4 — Dead WS status definitions.** `IMS-RETURN-CODE` and its 88-levels (STATUS-OK, SEGMENT-NOT-FOUND,
END-OF-DB, etc.), `WS-IMS-PSB-SCHD-FLG`, `WS-INFILE-STATUS`, `WS-CUSTID-STATUS`/`END-OF-FILE`, `IDX`,
`WS-TOT-REC-WRITTEN`, and `WS-ERR-FLG`/`ERR-FLG-ON` are declared but the status 88s are **never
populated/tested**; the program tests the literal `DIBSTAT` directly. `ERR-FLG-ON` appears in the outer
`PERFORM UNTIL` but is never SET — so it is dead in the loop condition. Carry these as inert fields;
do not synthesize logic for them. // source: CBPAUP0C.cbl:53,71-73,83-95,142

**#5 — Header/Comment typo "Authoriation".** The program header function text reads
"Delete Expired Pending Authoriation Messages" (missing 'z'). Preserve verbatim wherever the spec/text
is echoed; it has no runtime effect. // source: CBPAUP0C.cbl:5

**#6 — `CURRENT-DATE` accepted but unused.** `ACCEPT CURRENT-DATE FROM DATE` populates a 9(06) field
that is never referenced afterward; only `CURRENT-YYDDD` is used. Keep the accept (side-effect-free).
// source: CBPAUP0C.cbl:43,186

**#7 — `WK-CHKPT-ID-CTR` never incremented.** Every `CHKP` uses the same id `'RMAD0000'`; the counter
suffix never advances despite being designed to. Reproduce a constant checkpoint id. // source: CBPAUP0C.cbl:75-77,355

---

## 8. PORT NOTES (relational-access translation plan + tricky COBOL semantics)

**Target placement & shape.** Implement as `CardDemo.Ims/CBPAUP0C.cs`, a single batch class driven by a
`Run(parms)` entry that takes the parsed `SYSIN` card (or reads it from the step's parameter input).
Use the two PAUT repositories from the IMS relational re-host (`PAUT_SUMMARY`, `PAUT_DETAIL`,
IMS_SCHEMA.md §2).

**Cursor / parentage model (the crux of IMS→SQL here).** There is **no GU**; the program drives purely
off forward cursors:
- The root scan (`GN`) = a single forward iterator `SELECT * FROM PAUT_SUMMARY ORDER BY ACCT_ID ASC`.
  `MoveNext()` returns a row → status `'  '`; exhausted → `'GB'`. // IMS_SCHEMA.md:226-241
- After each root row, open/position a child iterator `SELECT * FROM PAUT_DETAIL WHERE ACCT_ID=:current
  ORDER BY AUTH_KEY ASC` (`:current` = the `ACCT_ID` of the just-read summary). `GNP` `MoveNext()` →
  `'  '`; exhausted → `'GE'` (this program treats `'GE'` and `'GB'` identically as NO-MORE-AUTHS).
  // IMS_SCHEMA.md:243-258
- **Deletion-during-iteration hazard:** the program deletes detail rows while iterating the same parent
  and may delete the root after. To match IMS twin-chain semantics safely in SQL, **materialize the
  child key list per parent first** (snapshot `AUTH_KEY`s in ascending order), then iterate that
  snapshot issuing `DELETE`s — so deletes don't disturb the live cursor. Then evaluate the
  delete-summary gate, then advance the root cursor. This preserves observable order/counts. // source: CBPAUP0C.cbl:144-165
- **`ORDER BY AUTH_KEY ASC`** = newest-first because keys are 9s-complement (ARCHITECTURE.md ordinal
  string compare; IMS_SCHEMA.md:161-166). Use ordinal compare on the 8-byte key (or equivalently
  `ORDER BY AUTH_DATE_9C ASC, AUTH_TIME_9C ASC`). Iteration order does not affect this program's
  correctness (it visits all children), but keep it for parity of counters/displays.

**DLET semantics → SQL.**
- Detail DLET → `DELETE FROM PAUT_DETAIL WHERE ACCT_ID=:a AND AUTH_KEY=:k`; expect 1 row → `'  '`.
- Root DLET → `DELETE FROM PAUT_SUMMARY WHERE ACCT_ID=:a`; FK `ON DELETE CASCADE` removes any
  surviving children (IMS root delete cascades). In this program the children are normally already
  gone, but cascade keeps it safe. // IMS_SCHEMA.md:324-329, 436-437

**CHKP → commit.** `9000-TAKE-CHECKPOINT` = `COMMIT` of the current unit of work + (optionally) persist
a restart token equal to `WK-CHKPT-ID` (constant `'RMAD0000'`). Frequency is `P-CHKP-FREQ` summaries
(plus the mandatory final checkpoint at end-of-DB). For a faithful run, commit at the same cadence.
// source: CBPAUP0C.cbl:160-164,169,355; IMS_SCHEMA.md:372

**9s-complement & packed decimal.** `PA-ACCT-ID` S9(11) COMP-3, `PA-AUTH-DATE-9C` S9(05) COMP-3, etc.,
are *file-format* concerns only at the import boundary (Runtime PackedDecimal codec). At the relational
layer they are `DECIMAL`. Decode the auth date with `99999 − AUTH_DATE_9C`. Do **not** "fix" the
descending encoding. // source: cpy/CIPAUSMY.cpy:19; cpy/CIPAUDTY.cpy:20; ARCHITECTURE.md:42; IMS_SCHEMA.md:433-435

**OCCURS.** `PA-ACCOUNT-STATUS X(02) OCCURS 5` flattens to 5 columns `ACCOUNT_STATUS_1..5`; unused
here, carry through unchanged. // source: cpy/CIPAUSMY.cpy:22; IMS_SCHEMA.md:73

**Edited PIC / numeric display.** No edited PICs. `DISPLAY` of S9(8) COMP and packed fields renders the
signed numeric (a leading sign position for signed items under IBM COBOL DISPLAY). Reproduce the
DISPLAY text of counters/amounts faithfully for SYSOUT characterization (sign handling on `WS-NO-*`
COMP fields and `PA-ACCT-ID`). // source: CBPAUP0C.cbl:173-176,237-239,362-368

**No STRING/UNSTRING/INITIALIZE/REDEFINES** in this program. `ACCEPT … FROM SYSIN` reads the control
card into `PRM-INFO` positionally (the comma `FILLER`s are literal commas in the card). The
alphanumeric-vs-numeric comparisons (bug #3) and the width-mismatch SUBTRACTs (bug #6 numeric note) are
the only delicate COBOL semantics; pin both with characterization tests.

**ABEND.** `9999-ABEND` → `MOVE 16 TO RETURN-CODE; GOBACK`. Port: set the batch step's exit/return code
to 16 and terminate the run (no partial continue). This is a clean program-return to IMS, not a system
abend dump. // source: CBPAUP0C.cbl:377-383

**Verification (per ARCHITECTURE.md §Verification + IMS_SCHEMA.md).** Seed PAUT_SUMMARY/PAUT_DETAIL
from `data/EBCDIC/AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0.dat` (the unloaded IMS image) via the import path;
run CBPAUP0C with the JCL card `00,00001,00001,Y`; characterize (a) the post-run table contents
(which roots/details survive) and (b) the SYSOUT totals/DEBUG lines against captured golden fixtures.
Pin: the bug-#2 gate (root deleted on approved-count≤0 ignoring declined), the YYDDD subtraction
behavior, and the no-REPL persistence of summary totals. // source: ARCHITECTURE.md:91-102; IMS_SCHEMA.md; data/EBCDIC/AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0.dat

---

## 9. OPEN QUESTIONS / risks

1. **YYDDD subtraction is not a real day-count across year boundaries.** `WS-DAY-DIFF =
   CURRENT-YYDDD − (99999 − PA-AUTH-DATE-9C)` subtracts two `YYDDD` integers. Within the same year this
   equals the day difference; across a year boundary it jumps by ~635 per year. With the shipped expiry
   of `00` (or default 5), virtually every non-future detail qualifies anyway, so the anomaly is masked
   for the standard run — but it is a latent behavior. **Decision: reproduce the raw integer
   subtraction faithfully** (do not substitute calendar-day math). Flag for the faithful-bugs log.
   // source: CBPAUP0C.cbl:280-284
2. **Mixed-class comparisons of `P-CHKP-FREQ`/`P-CHKP-DIS-FREQ` (X(05))** against numeric counters and
   against `SPACES OR 0 OR LOW-VALUES`, plus `MOVE <numeric> TO X(05)`. The exact IBM Enterprise COBOL
   class/zoned comparison result for non-all-numeric content (e.g. defaulted `'5    '`) must be pinned
   with a unit test; the relational port should not silently re-type these to integers. For the
   CardDemo card (`00001`, `00001`) both behave as 1. // source: CBPAUP0C.cbl:101,103,160,201-206,360
3. **`'GE'` vs `'GB'` on GNP.** The program treats end-of-children (`GE`) and end-of-DB (`GB`)
   identically as `NO-MORE-AUTHS`; the SQL child cursor only ever signals "exhausted". Ensure the
   re-host's GNP-exhaustion maps to NO-MORE-AUTHS regardless of which IMS code it would have been.
   // source: CBPAUP0C.cbl:263-265
4. **No REPL means summary balances/counts drift relative to surviving details.** Confirm with product
   owners this is intended (it is the observed source behavior) before relying on PAUT_SUMMARY totals
   downstream after a purge run; the port reproduces it as-is (bug #1). // source: CBPAUP0C.cbl:287-292
5. **Restart/checkpoint token.** IMS `CHKP ID(WK-CHKPT-ID)` provides restart positioning; the
   relational port models it as a `COMMIT`. True restart-from-checkpoint repositioning (resume the root
   scan after the last committed `ACCT_ID`) is not implemented by this program's logic itself (IMS does
   it); decide whether the .NET step-runner needs equivalent restartability or just transactional
   batching. The constant `'RMAD0000'` id (bug #7) makes IMS-style multi-checkpoint restart ambiguous
   in the original anyway. // source: CBPAUP0C.cbl:355-370
