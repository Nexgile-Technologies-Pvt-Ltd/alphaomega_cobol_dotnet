# PORT SPEC — CBTRN03C (Daily Transaction Detail Report writer)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBTRN03C.cbl`
Copybooks used:
- `app/cpy/CVTRA05Y.cpy` → **TRAN-RECORD** (RECLN 350) — the posted/daily transaction record read sequentially. // source: CBTRN03C.cbl:93
- `app/cpy/CVACT03Y.cpy` → **CARD-XREF-RECORD** (RECLN 50) — card cross-reference, keyed lookup. // source: CBTRN03C.cbl:98
- `app/cpy/CVTRA03Y.cpy` → **TRAN-TYPE-RECORD** (RECLN 60) — transaction-type description, keyed lookup. // source: CBTRN03C.cbl:103
- `app/cpy/CVTRA04Y.cpy` → **TRAN-CAT-RECORD** (RECLN 60) — transaction-category description, keyed lookup. // source: CBTRN03C.cbl:108
- `app/cpy/CVTRA07Y.cpy` → report layout group items (REPORT-NAME-HEADER, TRANSACTION-DETAIL-REPORT, TRANSACTION-HEADER-1/2, REPORT-PAGE-TOTALS, REPORT-ACCOUNT-TOTALS, REPORT-GRAND-TOTALS). // source: CBTRN03C.cbl:113

JCL: **`app/jcl/TRANREPT.jcl`**, step **`STEP10R EXEC PGM=CBTRN03C`** (the prior SORT step `STEP05R` filters the TRANSACT backup by the parm date range and sorts ascending by `TRAN-CARD-NUM` into `…TRANSACT.DALY(+1)`, which is then DD `TRANFILE` here). // source: jcl/TRANREPT.jcl:59-80
Target tables (relational, per ARCHITECTURE.md): reads **TRANSACTION** (via DD `TRANFILE`), **CARD_XREF**, **TRAN_TYPE**, **TRAN_CATEGORY**; reads a sequential **DATEPARM** parameter record; writes a sequential **report dataset** (DD `TRANREPT`, LRECL 133).
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBTRN03C.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA, no BMS map. (online program: **NO**.)

---

## 1. Purpose

CBTRN03C is a stand-alone **BATCH** report writer whose function is "Print the transaction detail
report." It opens five input sources (the sequential transaction file `TRANFILE`, three indexed
master files `CARDXREF`/`TRANTYPE`/`TRANCATG`, and the sequential `DATEPARM` parameter file) and one
sequential output report file `TRANREPT` (LRECL 133). It first reads the single `DATEPARM` record to
obtain a start-date and end-date window. It then reads the transaction file record by record. For
each transaction whose **processing-timestamp date (first 10 chars of `TRAN-PROC-TS`)** falls within
the inclusive `[WS-START-DATE, WS-END-DATE]` window, it: (a) on a **card-number break** writes the
prior card's account total and looks up the new card in the cross-reference file to obtain the
account id; (b) looks up the transaction-type description; (c) looks up the transaction-category
description; and (d) writes a formatted detail line, accumulating page/account totals. Page breaks
happen every `WS-PAGE-SIZE` (20) detail lines, emitting page totals and re-printing headers. At
end-of-file it adds the last transaction's amount into the page/account totals, writes final page
total and grand total, closes all files and `GOBACK`s. Because the upstream SORT delivers the file
ordered by card number, the per-card account-total grouping works as a control break.
// source: CBTRN03C.cbl:1-6 (header — Type BATCH; Function "Print the transaction detail report.")
// source: CBTRN03C.cbl:160-217 (mainline: opens / dateparm read / per-record loop / closes / GOBACK)
// source: jcl/TRANREPT.jcl:37-55 (upstream SORT: INCLUDE by proc-date range, SORT by TRAN-CARD-NUM ascending)

**How it is invoked:** by JCL **`TRANREPT.jcl`** step **`STEP10R EXEC PGM=CBTRN03C`**. DD bindings:
`TRANFILE`=`AWS.M2.CARDDEMO.TRANSACT.DALY(+1)` (the date-filtered, card-sorted transaction file
produced by the prior SORT step), `CARDXREF`=`…CARDXREF.VSAM.KSDS`, `TRANTYPE`=`…TRANTYPE.VSAM.KSDS`,
`TRANCATG`=`…TRANCATG.VSAM.KSDS`, `DATEPARM`=`…DATEPARM`, output `TRANREPT`=`…TRANREPT(+1)` (LRECL
133, RECFM FB). It is **not** a called subprogram and **not** a CICS transaction; it ends with
`GOBACK`. // source: CBTRN03C.cbl:29-57 (SELECT … ASSIGN TO TRANFILE/CARDXREF/TRANTYPE/TRANCATG/TRANREPT/DATEPARM); CBTRN03C.cbl:217 (GOBACK); jcl/TRANREPT.jcl:59-80 (DD assignments)

> NOTE: although the SORT step's `SYMNAMES` references `TRAN-PROC-DT` at offset 305 length 10 (the
> proc-timestamp date), the date filter is applied **both** in the SORT INCLUDE *and* re-applied
> inside CBTRN03C against `WS-START-DATE`/`WS-END-DATE` from the `DATEPARM` record. The SORT's literal
> parm dates (`2022-01-01` … `2022-07-06`) and the DATEPARM file contents are independent inputs; in
> the port, only the program-internal `DATEPARM`-driven filter (§4 mainline, §5) is part of CBTRN03C.
> // source: jcl/TRANREPT.jcl:41-48; CBTRN03C.cbl:173-174

---

## 2. FILE / TABLE access

| COBOL file (DD) | Org / Access | Record key | Relational table (ARCHITECTURE.md) | Operations used | Maps to (relational repository) |
|---|---|---|---|---|---|
| `TRANSACT-FILE` (DD `TRANFILE`) | **SEQUENTIAL** (QSAM) | — (no key; arrives card-sorted) | **TRANSACTION** (PK tran_id X16) | OPEN INPUT; sequential READ (READNEXT); CLOSE | Forward ordered read cursor over TRANSACTION **in the upstream card-sorted order**. `ReadNext()`; status `'00'`/`'10'`(EOF). |
| `XREF-FILE` (DD `CARDXREF`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-XREF-CARD-NUM` PIC X(16) | **CARD_XREF** (PK xref_card_num X16; idx acct_id) | OPEN INPUT; **random keyed READ** (RECORD KEY = FD-XREF-CARD-NUM) with INVALID KEY; CLOSE | `SELECT xref_card_num,cust_id,acct_id FROM CARD_XREF WHERE xref_card_num=@k` → FileStatus `'00'`/`'23'` (INVALID KEY → abend, see §5). |
| `TRANTYPE-FILE` (DD `TRANTYPE`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-TRAN-TYPE` PIC X(02) | **TRAN_TYPE** (PK tran_type X2) | OPEN INPUT; **random keyed READ** (RECORD KEY = FD-TRAN-TYPE) with INVALID KEY; CLOSE | `SELECT tran_type,tran_type_desc FROM TRAN_TYPE WHERE tran_type=@k` → `'00'`/`'23'` (INVALID KEY → abend). |
| `TRANCATG-FILE` (DD `TRANCATG`) | INDEXED (KSDS), **ACCESS RANDOM** | `FD-TRAN-CAT-KEY` (FD-TRAN-TYPE-CD X2 + FD-TRAN-CAT-CD 9(4)) = 6 bytes | **TRAN_CATEGORY** (composite PK tran_type_cd X2, tran_cat_cd 9(4)) | OPEN INPUT; **random keyed READ** (RECORD KEY = FD-TRAN-CAT-KEY) with INVALID KEY; CLOSE | `SELECT … FROM TRAN_CATEGORY WHERE tran_type_cd=@t AND tran_cat_cd=@c` → `'00'`/`'23'` (INVALID KEY → abend). |
| `DATE-PARMS-FILE` (DD `DATEPARM`) | **SEQUENTIAL** (QSAM) | — | (no base table — a 1-record parameter dataset) | OPEN INPUT; single sequential READ INTO WS-DATEPARM-RECORD; CLOSE | Read one parameter row (start/end date). Model as a tiny config table or a single-row read; FileStatus `'00'` (ok)/`'10'` (EOF→end-of-file). |
| `REPORT-FILE` (DD `TRANREPT`) | **SEQUENTIAL** (QSAM), **OPEN OUTPUT** | — | (no base table — an output report dataset, LRECL 133) | OPEN OUTPUT; sequential **WRITE** of FD-REPTFILE-REC; CLOSE | Append fixed-width 133-char lines to the report output. WRITE → status `'00'` else 12 → abend. |

// source: CBTRN03C.cbl:29-31 (TRANSACT-FILE: ORGANIZATION SEQUENTIAL / FILE STATUS TRANFILE-STATUS)
// source: CBTRN03C.cbl:33-37 (XREF-FILE: INDEXED / RANDOM / RECORD KEY FD-XREF-CARD-NUM)
// source: CBTRN03C.cbl:39-43 (TRANTYPE-FILE: INDEXED / RANDOM / RECORD KEY FD-TRAN-TYPE)
// source: CBTRN03C.cbl:45-49 (TRANCATG-FILE: INDEXED / RANDOM / RECORD KEY FD-TRAN-CAT-KEY)
// source: CBTRN03C.cbl:51-53 (REPORT-FILE: ORGANIZATION SEQUENTIAL / FILE STATUS TRANREPT-STATUS)
// source: CBTRN03C.cbl:55-57 (DATE-PARMS-FILE: ORGANIZATION SEQUENTIAL / FILE STATUS DATEPARM-STATUS)
// source: CBTRN03C.cbl:61-88 (FD record layouts for all six files)

### Operation → SQL mapping

- **DATEPARM sequential READ** (`0550-DATEPARM-READ`): `READ DATE-PARMS-FILE INTO WS-DATEPARM-RECORD`.
  Status `'00'`→APPL-RESULT 0; `'10'`→16 (APPL-EOF → END-OF-FILE='Y'); any other →12 → display
  `'ERROR READING DATEPARM FILE'`, helper, abend. On `'00'` it DISPLAYs `'Reporting from ' WS-START-DATE
  ' to ' WS-END-DATE`. // source: CBTRN03C.cbl:220-243
- **TRANSACT sequential READ** (`1000-TRANFILE-GET-NEXT`): `READ TRANSACT-FILE INTO TRAN-RECORD`
  → cursor `ReadNext()`. `'00'`→0; `'10'`→16 (EOF→END-OF-FILE='Y'); other→12 → display
  `'ERROR READING TRANSACTION FILE'`, helper, abend. // source: CBTRN03C.cbl:248-272
- **XREF random keyed READ** (`1500-A-LOOKUP-XREF`): `READ XREF-FILE INTO CARD-XREF-RECORD` with
  INVALID KEY (key = FD-XREF-CARD-NUM, set from TRAN-CARD-NUM at break) →
  `SELECT … FROM CARD_XREF WHERE xref_card_num=@k`. Row → continue (XREF-ACCT-ID used later);
  no row → INVALID KEY → display `'INVALID CARD NUMBER : ' FD-XREF-CARD-NUM`, MOVE 23 → IO-STATUS,
  helper, **abend**. // source: CBTRN03C.cbl:484-492
- **TRANTYPE random keyed READ** (`1500-B-LOOKUP-TRANTYPE`): `READ TRANTYPE-FILE INTO TRAN-TYPE-RECORD`
  with INVALID KEY (key = FD-TRAN-TYPE from TRAN-TYPE-CD) → `SELECT … FROM TRAN_TYPE WHERE tran_type=@k`.
  No row → `'INVALID TRANSACTION TYPE : ' FD-TRAN-TYPE`, MOVE 23, helper, **abend**. // source: CBTRN03C.cbl:494-502
- **TRANCATG random keyed READ** (`1500-C-LOOKUP-TRANCATG`): `READ TRANCATG-FILE INTO TRAN-CAT-RECORD`
  with INVALID KEY (key = FD-TRAN-CAT-KEY = TRAN-TYPE-CD + TRAN-CAT-CD) →
  `SELECT … FROM TRAN_CATEGORY WHERE tran_type_cd=@t AND tran_cat_cd=@c`. No row →
  `'INVALID TRAN CATG KEY : ' FD-TRAN-CAT-KEY`, MOVE 23, helper, **abend**. // source: CBTRN03C.cbl:504-512
- **REPORT WRITE** (`1111-WRITE-REPORT-REC`): `WRITE FD-REPTFILE-REC` (133 bytes). Status `'00'`→0 else
  12 → display `'ERROR WRITING REPTFILE'`, helper, abend. // source: CBTRN03C.cbl:343-359

There are **no REWRITE / DELETE / STARTBR / READPREV** operations. The only ordered traversal is the
sequential TRANSACT read; the three master files are single-row keyed (random) reads. The DATEPARM
read fires exactly once; the REPORT file is write-only.

---

## 3. WORKING-STORAGE / record structures that affect logic

### 3.1 Record copybooks (from `COPY`)

- `COPY CVTRA05Y` → **`TRAN-RECORD`** (RECLN 350) — the transaction `READ INTO`d from TRANFILE and
  `DISPLAY`ed. Fields driving logic: `TRAN-ID`, `TRAN-TYPE-CD`, `TRAN-CAT-CD`, `TRAN-SOURCE`,
  `TRAN-AMT`, `TRAN-CARD-NUM`, `TRAN-PROC-TS`. // source: CBTRN03C.cbl:93; cpy/CVTRA05Y.cpy:4-18
  - `TRAN-ID`            PIC X(16)      → TRANSACTION.`tran_id`   (→ TRAN-REPORT-TRANS-ID)
  - `TRAN-TYPE-CD`       PIC X(02)      → `type_cd`              (→ FD-TRAN-TYPE, FD-TRAN-TYPE-CD, report TYPE-CD)
  - `TRAN-CAT-CD`        PIC 9(04)      → `cat_cd`               (→ FD-TRAN-CAT-CD, report CAT-CD)
  - `TRAN-SOURCE`        PIC X(10)      → `source`               (→ TRAN-REPORT-SOURCE)
  - `TRAN-DESC`          PIC X(100)     → `desc`                 (not used in report)
  - `TRAN-AMT`           PIC S9(09)V99  → `amt`                  (accumulated into totals; → TRAN-REPORT-AMT)
  - `TRAN-MERCHANT-ID`   PIC 9(09)      → `merchant_id`          (not used)
  - `TRAN-MERCHANT-NAME` PIC X(50)      → `merchant_name`        (not used)
  - `TRAN-MERCHANT-CITY` PIC X(50)      → `merchant_city`        (not used)
  - `TRAN-MERCHANT-ZIP`  PIC X(10)      → `merchant_zip`         (not used)
  - `TRAN-CARD-NUM`      PIC X(16)      → `card_num`             (control-break key; → WS-CURR-CARD-NUM, FD-XREF-CARD-NUM)
  - `TRAN-ORIG-TS`       PIC X(26)      → `orig_ts`              (not used)
  - `TRAN-PROC-TS`       PIC X(26)      → `proc_ts`              (**date filter: substring (1:10)**)
  - `FILLER`             PIC X(20)      → dropped (20 spaces on serialize)
  > **NOTE:** the FD `FD-TRANFILE-REC` (line 62-65) splits the 350-byte record as `FD-TRANS-DATA X(304)`
  > + `FD-TRAN-PROC-TS X(26)` + `FD-FILLER X(20)`, but the program always `READ … INTO TRAN-RECORD`, so
  > the copybook layout (CVTRA05Y) is the authoritative field map. // source: CBTRN03C.cbl:61-65, 249

- `COPY CVACT03Y` → **`CARD-XREF-RECORD`** (RECLN 50) — target of the XREF keyed read; supplies
  `XREF-ACCT-ID` PIC 9(11) (→ TRAN-REPORT-ACCOUNT-ID). // source: CBTRN03C.cbl:98; cpy/CVACT03Y.cpy:4-8
  - `XREF-CARD-NUM` X(16) (PK) ; `XREF-CUST-ID` 9(09) ; `XREF-ACCT-ID` 9(11) ; FILLER X(14).

- `COPY CVTRA03Y` → **`TRAN-TYPE-RECORD`** (RECLN 60) — target of the TRANTYPE keyed read; supplies
  `TRAN-TYPE-DESC` PIC X(50) (→ TRAN-REPORT-TYPE-DESC). // source: CBTRN03C.cbl:103; cpy/CVTRA03Y.cpy:4-7
  - `TRAN-TYPE` X(02) (PK) ; `TRAN-TYPE-DESC` X(50) ; FILLER X(08).

- `COPY CVTRA04Y` → **`TRAN-CAT-RECORD`** (RECLN 60) — target of the TRANCATG keyed read; supplies
  `TRAN-CAT-TYPE-DESC` PIC X(50) (→ TRAN-REPORT-CAT-DESC). // source: CBTRN03C.cbl:108; cpy/CVTRA04Y.cpy:4-9
  - `TRAN-CAT-KEY` = `TRAN-TYPE-CD` X(02) + `TRAN-CAT-CD` 9(04) ; `TRAN-CAT-TYPE-DESC` X(50) ; FILLER X(04).
  > **Name-collision caveat:** both TRAN-RECORD (CVTRA05Y) and TRAN-CAT-RECORD (CVTRA04Y) declare
  > fields named `TRAN-TYPE-CD` and `TRAN-CAT-CD`. The program therefore qualifies with `OF TRAN-RECORD`
  > (e.g. line 189, 191, 193, 365, 367) when it means the transaction record. The port must keep these
  > as distinct typed properties. // source: CBTRN03C.cbl:189-194, 365-367; cpy/CVTRA04Y.cpy:6-7; cpy/CVTRA05Y.cpy:6-7

- `COPY CVTRA07Y` → the **report layout** group items (no I/O record; pure WORKING-STORAGE templates
  moved into FD-REPTFILE-REC before each WRITE). See §3.3. // source: CBTRN03C.cbl:113; cpy/CVTRA07Y.cpy:1-66

### 3.2 File-status, parameter, and control fields

- Six 2-byte status groups, one per file: `TRANFILE-STATUS`, `CARDXREF-STATUS`, `TRANTYPE-STATUS`,
  `TRANCATG-STATUS`, `TRANREPT-STATUS` (declared under the `COPY CVTRA07Y` block), `DATEPARM-STATUS`
  (each STAT1 X + STAT2 X). // source: CBTRN03C.cbl:94-120
- `WS-DATEPARM-RECORD` (X(10) `WS-START-DATE` + X(01) FILLER + X(10) `WS-END-DATE`) — the parsed
  parameter record; total 21 bytes (the DATEPARM FD record is X(80), so only the first 21 bytes are
  consumed; the FILLER byte at position 11 is the delimiter between the two dates). // source: CBTRN03C.cbl:122-125, 88
- `WS-REPORT-VARS`:
  - `WS-FIRST-TIME`     PIC X      VALUE `'Y'` — first-detail flag (drives one-time header + date stamp). // source: CBTRN03C.cbl:128
  - `WS-LINE-COUNTER`   PIC 9(09) COMP-3 VALUE 0 — running count of lines written (drives MOD page break). // source: CBTRN03C.cbl:129-130
  - `WS-PAGE-SIZE`      PIC 9(03) COMP-3 VALUE 20 — lines per page. // source: CBTRN03C.cbl:131-132
  - `WS-BLANK-LINE`     PIC X(133) VALUE SPACES — a blank report line. // source: CBTRN03C.cbl:133
  - `WS-PAGE-TOTAL`     PIC S9(09)V99 VALUE 0 — accumulated amount for current page. // source: CBTRN03C.cbl:134
  - `WS-ACCOUNT-TOTAL`  PIC S9(09)V99 VALUE 0 — accumulated amount for current card/account. // source: CBTRN03C.cbl:135
  - `WS-GRAND-TOTAL`    PIC S9(09)V99 VALUE 0 — accumulated amount overall. // source: CBTRN03C.cbl:136
  - `WS-CURR-CARD-NUM`  PIC X(16) VALUE SPACES — current control-break card number. // source: CBTRN03C.cbl:137
- `IO-STATUS` (`IO-STAT1` X + `IO-STAT2` X) — receives a status for the display helper. // source: CBTRN03C.cbl:139-141
- `TWO-BYTES-BINARY` PIC 9(4) BINARY, REDEFINED by `TWO-BYTES-ALPHA` (`TWO-BYTES-LEFT` X + `TWO-BYTES-RIGHT` X)
  — halfword used to numericize the 2nd status byte in `9910-DISPLAY-IO-STATUS`. // source: CBTRN03C.cbl:142-145
- `IO-STATUS-04` (`IO-STATUS-0401` PIC 9 VALUE 0 + `IO-STATUS-0403` PIC 999 VALUE 0). // source: CBTRN03C.cbl:146-148
- `APPL-RESULT` PIC S9(9) COMP, 88s: **`APPL-AOK VALUE 0`**, **`APPL-EOF VALUE 16`**. // source: CBTRN03C.cbl:150-152
- `END-OF-FILE` PIC X(01) VALUE `'N'` — loop sentinel (set 'Y' by DATEPARM EOF or TRANSACT EOF). // source: CBTRN03C.cbl:154
- `ABCODE` PIC S9(9) BINARY, `TIMING` PIC S9(9) BINARY — args to `CEE3ABD`. // source: CBTRN03C.cbl:155-156

### 3.3 Report layout templates (CVTRA07Y) — exact byte composition

These group items are MOVEd into `FD-REPTFILE-REC` (X(133)) before each WRITE. The port must
reproduce them byte-for-byte (fixed widths, literal fillers, edited PICs). // source: cpy/CVTRA07Y.cpy:1-66

- **REPORT-NAME-HEADER** (line 1 of headers): `REPT-SHORT-NAME` X(38) VALUE `'DALYREPT'` (left-just,
  spaces) + `REPT-LONG-NAME` X(41) VALUE `'Daily Transaction Report'` + `REPT-DATE-HEADER` X(12)
  VALUE `'Date Range: '` + `REPT-START-DATE` X(10) + FILLER X(4) VALUE `' to '` + `REPT-END-DATE` X(10).
  Total = 38+41+12+10+4+10 = 115 chars; padded to 133 on WRITE. // source: cpy/CVTRA07Y.cpy:4-13
- **TRANSACTION-DETAIL-REPORT** (a detail line): `TRAN-REPORT-TRANS-ID` X(16) + FILLER X(1) sp +
  `TRAN-REPORT-ACCOUNT-ID` X(11) + FILLER X(1) sp + `TRAN-REPORT-TYPE-CD` X(2) + FILLER X(1) `'-'` +
  `TRAN-REPORT-TYPE-DESC` X(15) + FILLER X(1) sp + `TRAN-REPORT-CAT-CD` 9(4) + FILLER X(1) `'-'` +
  `TRAN-REPORT-CAT-DESC` X(29) + FILLER X(1) sp + `TRAN-REPORT-SOURCE` X(10) + FILLER X(4) sp +
  `TRAN-REPORT-AMT` PIC `-ZZZ,ZZZ,ZZZ.ZZ` (14 chars) + FILLER X(2) sp. Total = 16+1+11+1+2+1+15+1+4+1+29+1+10+4+14+2 = 113. // source: cpy/CVTRA07Y.cpy:15-31
- **TRANSACTION-HEADER-1** (column-titles line): FILLER X(17) `'Transaction ID'` + FILLER X(12)
  `'Account ID'` + FILLER X(19) `'Transaction Type'` + FILLER X(35) `'Tran Category'` + FILLER X(14)
  `'Tran Source'` + FILLER X(1) sp + FILLER X(16) `'        Amount'`. Total = 17+12+19+35+14+1+16 = 114. // source: cpy/CVTRA07Y.cpy:33-46
- **TRANSACTION-HEADER-2**: PIC X(133) VALUE ALL `'-'` (a full 133-dash rule line). // source: cpy/CVTRA07Y.cpy:48
- **REPORT-PAGE-TOTALS**: FILLER X(11) `'Page Total'` + FILLER X(86) ALL `'.'` + `REPT-PAGE-TOTAL`
  PIC `+ZZZ,ZZZ,ZZZ.ZZ` (14). Total = 11+86+14 = 111. // source: cpy/CVTRA07Y.cpy:50-54
- **REPORT-ACCOUNT-TOTALS**: FILLER X(13) `'Account Total'` + FILLER X(84) ALL `'.'` +
  `REPT-ACCOUNT-TOTAL` PIC `+ZZZ,ZZZ,ZZZ.ZZ` (14). Total = 13+84+14 = 111. // source: cpy/CVTRA07Y.cpy:56-60
- **REPORT-GRAND-TOTALS**: FILLER X(11) `'Grand Total'` + FILLER X(86) ALL `'.'` + `REPT-GRAND-TOTAL`
  PIC `+ZZZ,ZZZ,ZZZ.ZZ` (14). Total = 11+86+14 = 111. // source: cpy/CVTRA07Y.cpy:62-66

> **Edited PIC notes:** `-ZZZ,ZZZ,ZZZ.ZZ` (detail amount): leading floating minus, zero-suppressed
> with comma grouping and 2 decimals — a **negative** amount shows `-`, a **positive or zero** amount
> shows a leading space (the `-` position becomes a space). `+ZZZ,ZZZ,ZZZ.ZZ` (the three totals):
> floating-plus, so positives/zero show `+`, negatives show `-`. The fields hold S9(9)V99 source
> values; the maximum magnitude `999,999,999.99` exactly fills the 9 Z + comma positions. Reproduce
> with `CobolEditedNumeric` (`-ZZZ,ZZZ,ZZZ.ZZ` and `+ZZZ,ZZZ,ZZZ.ZZ`). // source: cpy/CVTRA07Y.cpy:30,54,60,66; ARCHITECTURE.md src/CardDemo.Runtime (CobolEditedNumeric)

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (method-per-paragraph)

Each PROCEDURE-DIVISION paragraph becomes a method. Statement order and PERFORM flow preserved.

### MAIN (unnamed mainline) // source: CBTRN03C.cbl:159-217
1. `DISPLAY 'START OF EXECUTION OF PROGRAM CBTRN03C'`. // source: CBTRN03C.cbl:160
2. PERFORM opens in order: `0000-TRANFILE-OPEN`, `0100-REPTFILE-OPEN`, `0200-CARDXREF-OPEN`,
   `0300-TRANTYPE-OPEN`, `0400-TRANCATG-OPEN`, `0500-DATEPARM-OPEN` (each abends on non-`'00'`). // source: CBTRN03C.cbl:161-166
3. PERFORM `0550-DATEPARM-READ` (reads start/end dates; EOF here → END-OF-FILE='Y'). // source: CBTRN03C.cbl:168
4. `PERFORM UNTIL END-OF-FILE = 'Y'`: // source: CBTRN03C.cbl:170-206
   a. IF `END-OF-FILE = 'N'` (redundant inner guard vs the UNTIL): // source: CBTRN03C.cbl:171
      - PERFORM `1000-TRANFILE-GET-NEXT` (reads next transaction; may set END-OF-FILE='Y'). // source: CBTRN03C.cbl:172
      - **Date filter:** IF `TRAN-PROC-TS (1:10) >= WS-START-DATE AND TRAN-PROC-TS (1:10) <= WS-END-DATE`
        THEN `CONTINUE` ELSE `NEXT SENTENCE`. // source: CBTRN03C.cbl:173-178
        > **Critical control-flow subtlety (see §7 Faithful Bug #1):** `NEXT SENTENCE` jumps to the end
        > of the *whole sentence* (the period after `END-PERFORM` at line 206), i.e. it **skips the
        > rest of the loop body AND the UNTIL re-test is reached normally**. In COBOL `NEXT SENTENCE`
        > transfers control to the statement following the next period; here the enclosing sentence is
        > the entire `PERFORM UNTIL … END-PERFORM.` so it effectively continues the loop without
        > processing this (out-of-range) record. Meanwhile `CONTINUE` (the in-range branch) is a no-op
        > that falls through to the next statement. **NOTE the inverted-looking structure:** the
        > in-range case does `CONTINUE` (fall through to processing) and the out-of-range case does
        > `NEXT SENTENCE` (skip processing). Reproduce exactly. // source: CBTRN03C.cbl:173-178, 206
      - IF `END-OF-FILE = 'N'` (a record was actually read, not EOF): // source: CBTRN03C.cbl:179
        - `DISPLAY TRAN-RECORD`. // source: CBTRN03C.cbl:180
        - **Card control break:** IF `WS-CURR-CARD-NUM NOT= TRAN-CARD-NUM`: // source: CBTRN03C.cbl:181
          - IF `WS-FIRST-TIME = 'N'`: PERFORM `1120-WRITE-ACCOUNT-TOTALS` (flush prior card's account total). // source: CBTRN03C.cbl:182-184
          - `MOVE TRAN-CARD-NUM TO WS-CURR-CARD-NUM`; `MOVE TRAN-CARD-NUM TO FD-XREF-CARD-NUM`. // source: CBTRN03C.cbl:185-186
          - PERFORM `1500-A-LOOKUP-XREF` (sets XREF-ACCT-ID; abends if card not in xref). // source: CBTRN03C.cbl:187
        - `MOVE TRAN-TYPE-CD OF TRAN-RECORD TO FD-TRAN-TYPE`; PERFORM `1500-B-LOOKUP-TRANTYPE`. // source: CBTRN03C.cbl:189-190
        - `MOVE TRAN-TYPE-CD OF TRAN-RECORD TO FD-TRAN-TYPE-CD`; `MOVE TRAN-CAT-CD OF TRAN-RECORD TO
          FD-TRAN-CAT-CD`; PERFORM `1500-C-LOOKUP-TRANCATG`. // source: CBTRN03C.cbl:191-195
        - PERFORM `1100-WRITE-TRANSACTION-REPORT` (header-on-first, page-break, accumulate, write detail). // source: CBTRN03C.cbl:196
      - ELSE (END-OF-FILE = 'Y', i.e. this iteration hit EOF): // source: CBTRN03C.cbl:197
        - `DISPLAY 'TRAN-AMT ' TRAN-AMT`; `DISPLAY 'WS-PAGE-TOTAL' WS-PAGE-TOTAL`. // source: CBTRN03C.cbl:198-199
        - `ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL` (adds the **stale last record's** amount; see §7 #2). // source: CBTRN03C.cbl:200-201
        - PERFORM `1110-WRITE-PAGE-TOTALS`. // source: CBTRN03C.cbl:202
        - PERFORM `1110-WRITE-GRAND-TOTALS`. // source: CBTRN03C.cbl:203
   b. (END-IF for the inner `END-OF-FILE='N'` guard at line 205; END-PERFORM at line 206.)
   > **NOTE: the account total for the *final* card is never flushed.** At EOF the program writes page
   > total + grand total but does **not** call `1120-WRITE-ACCOUNT-TOTALS` for the last card. See §7 #3.
5. PERFORM closes in order: `9000-TRANFILE-CLOSE`, `9100-REPTFILE-CLOSE`, `9200-CARDXREF-CLOSE`,
   `9300-TRANTYPE-CLOSE`, `9400-TRANCATG-CLOSE`, `9500-DATEPARM-CLOSE`. // source: CBTRN03C.cbl:208-213
6. `DISPLAY 'END OF EXECUTION OF PROGRAM CBTRN03C'`. // source: CBTRN03C.cbl:215
7. `GOBACK`. // source: CBTRN03C.cbl:217

### 0550-DATEPARM-READ // source: CBTRN03C.cbl:220-243
1. `READ DATE-PARMS-FILE INTO WS-DATEPARM-RECORD`. // source: CBTRN03C.cbl:221
2. EVALUATE DATEPARM-STATUS: `'00'`→APPL-RESULT 0; `'10'`→16; OTHER→12. // source: CBTRN03C.cbl:222-229
3. IF `APPL-AOK` → `DISPLAY 'Reporting from ' WS-START-DATE ' to ' WS-END-DATE`;
   ELSE IF `APPL-EOF` → `MOVE 'Y' TO END-OF-FILE`;
   ELSE → `DISPLAY 'ERROR READING DATEPARM FILE'`, MOVE DATEPARM-STATUS→IO-STATUS, `9910-DISPLAY-IO-STATUS`,
   `9999-ABEND-PROGRAM`. // source: CBTRN03C.cbl:231-243
   > NOTE: no `EXIT`; paragraph ends at the period on line 243.

### 1000-TRANFILE-GET-NEXT // source: CBTRN03C.cbl:248-272
1. `READ TRANSACT-FILE INTO TRAN-RECORD`. // source: CBTRN03C.cbl:249
2. EVALUATE TRANFILE-STATUS: `'00'`→0; `'10'`→16; OTHER→12. // source: CBTRN03C.cbl:251-258
3. IF `APPL-AOK` → CONTINUE; ELSE IF `APPL-EOF` → `MOVE 'Y' TO END-OF-FILE`;
   ELSE → `DISPLAY 'ERROR READING TRANSACTION FILE'`, MOVE TRANFILE-STATUS→IO-STATUS, helper, abend. // source: CBTRN03C.cbl:260-271
4. `EXIT`. // source: CBTRN03C.cbl:272

### 1100-WRITE-TRANSACTION-REPORT // source: CBTRN03C.cbl:274-290
1. IF `WS-FIRST-TIME = 'Y'`: MOVE 'N'→WS-FIRST-TIME; MOVE WS-START-DATE→REPT-START-DATE; MOVE
   WS-END-DATE→REPT-END-DATE; PERFORM `1120-WRITE-HEADERS`. // source: CBTRN03C.cbl:275-280
2. IF `FUNCTION MOD(WS-LINE-COUNTER, WS-PAGE-SIZE) = 0`: PERFORM `1110-WRITE-PAGE-TOTALS`; PERFORM
   `1120-WRITE-HEADERS`. // source: CBTRN03C.cbl:282-285
   > **NOTE:** on the very first detail, `WS-LINE-COUNTER` is already 4 (the first header block added 4),
   > so MOD(4,20)≠0 and no page-total fires until line counter hits a multiple of 20. See §6 for the
   > exact counter progression and §7 #4.
3. `ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL` (accumulate this in-range record's amount). // source: CBTRN03C.cbl:287-288
4. PERFORM `1120-WRITE-DETAIL`. // source: CBTRN03C.cbl:289
5. `EXIT`. // source: CBTRN03C.cbl:290

### 1110-WRITE-PAGE-TOTALS // source: CBTRN03C.cbl:293-304
1. `MOVE WS-PAGE-TOTAL TO REPT-PAGE-TOTAL`; `MOVE REPORT-PAGE-TOTALS TO FD-REPTFILE-REC`; PERFORM
   `1111-WRITE-REPORT-REC`. // source: CBTRN03C.cbl:294-296
2. `ADD WS-PAGE-TOTAL TO WS-GRAND-TOTAL`; `MOVE 0 TO WS-PAGE-TOTAL`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:297-299
3. `MOVE TRANSACTION-HEADER-2 TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:300-302
4. `EXIT`. // source: CBTRN03C.cbl:304

### 1120-WRITE-ACCOUNT-TOTALS // source: CBTRN03C.cbl:306-316
1. `MOVE WS-ACCOUNT-TOTAL TO REPT-ACCOUNT-TOTAL`; `MOVE REPORT-ACCOUNT-TOTALS TO FD-REPTFILE-REC`;
   PERFORM `1111-WRITE-REPORT-REC`. // source: CBTRN03C.cbl:307-309
2. `MOVE 0 TO WS-ACCOUNT-TOTAL`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:310-311
3. `MOVE TRANSACTION-HEADER-2 TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:312-314
4. `EXIT`. // source: CBTRN03C.cbl:316
   > **NOTE the misleading paragraph number:** `1120-WRITE-ACCOUNT-TOTALS` shares the `1120-` prefix
   > with `1120-WRITE-HEADERS` and `1120-WRITE-DETAIL` but is a distinct paragraph. Paragraph names are
   > non-unique-looking but each is independent. // source: CBTRN03C.cbl:306,324,361

### 1110-WRITE-GRAND-TOTALS // source: CBTRN03C.cbl:318-322
1. `MOVE WS-GRAND-TOTAL TO REPT-GRAND-TOTAL`; `MOVE REPORT-GRAND-TOTALS TO FD-REPTFILE-REC`;
   PERFORM `1111-WRITE-REPORT-REC`. // source: CBTRN03C.cbl:319-321
2. `EXIT`. // source: CBTRN03C.cbl:322
   > NOTE: does **not** increment WS-LINE-COUNTER (the only write paragraph that doesn't). // source: CBTRN03C.cbl:318-322

### 1120-WRITE-HEADERS // source: CBTRN03C.cbl:324-341
1. `MOVE REPORT-NAME-HEADER TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:325-327
2. `MOVE WS-BLANK-LINE TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:329-331
3. `MOVE TRANSACTION-HEADER-1 TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:333-335
4. `MOVE TRANSACTION-HEADER-2 TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:337-339
5. `EXIT`. (Header block = **4 lines**, +4 to WS-LINE-COUNTER.) // source: CBTRN03C.cbl:341

### 1111-WRITE-REPORT-REC // source: CBTRN03C.cbl:343-359
1. `WRITE FD-REPTFILE-REC`. // source: CBTRN03C.cbl:345
2. IF `TRANREPT-STATUS = '00'` → MOVE 0→APPL-RESULT; ELSE → MOVE 12→APPL-RESULT. // source: CBTRN03C.cbl:346-350
3. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR WRITING REPTFILE'`, MOVE TRANREPT-STATUS→IO-STATUS,
   `9910-DISPLAY-IO-STATUS`, `9999-ABEND-PROGRAM`. // source: CBTRN03C.cbl:351-358
4. `EXIT`. // source: CBTRN03C.cbl:359

### 1120-WRITE-DETAIL // source: CBTRN03C.cbl:361-374
1. `INITIALIZE TRANSACTION-DETAIL-REPORT` (resets all elementary items: alphanumerics→spaces,
   numerics→zeros; FILLER **not** reset by INITIALIZE — see §8). // source: CBTRN03C.cbl:362
2. `MOVE TRAN-ID TO TRAN-REPORT-TRANS-ID`. // source: CBTRN03C.cbl:363
3. `MOVE XREF-ACCT-ID TO TRAN-REPORT-ACCOUNT-ID` (9(11) numeric → X(11) alphanumeric report field). // source: CBTRN03C.cbl:364
4. `MOVE TRAN-TYPE-CD OF TRAN-RECORD TO TRAN-REPORT-TYPE-CD`. // source: CBTRN03C.cbl:365
5. `MOVE TRAN-TYPE-DESC TO TRAN-REPORT-TYPE-DESC` (X(50) source → X(15) report; **truncated to 15**). // source: CBTRN03C.cbl:366
6. `MOVE TRAN-CAT-CD OF TRAN-RECORD TO TRAN-REPORT-CAT-CD` (9(4) → 9(4)). // source: CBTRN03C.cbl:367
7. `MOVE TRAN-CAT-TYPE-DESC TO TRAN-REPORT-CAT-DESC` (X(50) source → X(29) report; **truncated to 29**). // source: CBTRN03C.cbl:368
8. `MOVE TRAN-SOURCE TO TRAN-REPORT-SOURCE`. // source: CBTRN03C.cbl:369
9. `MOVE TRAN-AMT TO TRAN-REPORT-AMT` (S9(9)V99 → edited `-ZZZ,ZZZ,ZZZ.ZZ`). // source: CBTRN03C.cbl:370
10. `MOVE TRANSACTION-DETAIL-REPORT TO FD-REPTFILE-REC`; PERFORM `1111-WRITE-REPORT-REC`; `ADD 1 TO WS-LINE-COUNTER`. // source: CBTRN03C.cbl:371-373
11. `EXIT`. // source: CBTRN03C.cbl:374

### 0000-TRANFILE-OPEN // source: CBTRN03C.cbl:376-392
`MOVE 8 TO APPL-RESULT`; `OPEN INPUT TRANSACT-FILE`; status `'00'`→0 else 12; on non-AOK →
`DISPLAY 'ERROR OPENING TRANFILE'`, MOVE TRANFILE-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:377-392

### 0100-REPTFILE-OPEN // source: CBTRN03C.cbl:394-410
`MOVE 8 TO APPL-RESULT`; **`OPEN OUTPUT REPORT-FILE`**; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR OPENING REPTFILE'`, MOVE TRANREPT-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:395-410

### 0200-CARDXREF-OPEN // source: CBTRN03C.cbl:412-428
`MOVE 8 TO APPL-RESULT`; `OPEN INPUT XREF-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR OPENING CROSS REF FILE'`, MOVE CARDXREF-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:413-428

### 0300-TRANTYPE-OPEN // source: CBTRN03C.cbl:430-446
`MOVE 8 TO APPL-RESULT`; `OPEN INPUT TRANTYPE-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR OPENING TRANSACTION TYPE FILE'`, MOVE TRANTYPE-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:431-446

### 0400-TRANCATG-OPEN // source: CBTRN03C.cbl:448-464
`MOVE 8 TO APPL-RESULT`; `OPEN INPUT TRANCATG-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR OPENING TRANSACTION CATG FILE'`, MOVE TRANCATG-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:449-464

### 0500-DATEPARM-OPEN // source: CBTRN03C.cbl:466-482
`MOVE 8 TO APPL-RESULT`; `OPEN INPUT DATE-PARMS-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR OPENING DATE PARM FILE'`, MOVE DATEPARM-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:467-482

### 1500-A-LOOKUP-XREF // source: CBTRN03C.cbl:484-492
1. `READ XREF-FILE INTO CARD-XREF-RECORD` with: // source: CBTRN03C.cbl:485
   - INVALID KEY → `DISPLAY 'INVALID CARD NUMBER : ' FD-XREF-CARD-NUM`; `MOVE 23 TO IO-STATUS`;
     `9910-DISPLAY-IO-STATUS`; `9999-ABEND-PROGRAM`. // source: CBTRN03C.cbl:486-490
2. `EXIT`. // source: CBTRN03C.cbl:492
   > On success (row found) XREF-ACCT-ID is now set for use in `1120-WRITE-DETAIL`. No NOT-INVALID-KEY
   > block; control just falls through to EXIT. // source: CBTRN03C.cbl:485-492

### 1500-B-LOOKUP-TRANTYPE // source: CBTRN03C.cbl:494-502
`READ TRANTYPE-FILE INTO TRAN-TYPE-RECORD` with INVALID KEY →
`DISPLAY 'INVALID TRANSACTION TYPE : ' FD-TRAN-TYPE`; MOVE 23→IO-STATUS; helper; abend; EXIT.
On success TRAN-TYPE-DESC is set. // source: CBTRN03C.cbl:495-502

### 1500-C-LOOKUP-TRANCATG // source: CBTRN03C.cbl:504-512
`READ TRANCATG-FILE INTO TRAN-CAT-RECORD` with INVALID KEY →
`DISPLAY 'INVALID TRAN CATG KEY : ' FD-TRAN-CAT-KEY`; MOVE 23→IO-STATUS; helper; abend; EXIT.
On success TRAN-CAT-TYPE-DESC is set. // source: CBTRN03C.cbl:505-512

### 9000-TRANFILE-CLOSE // source: CBTRN03C.cbl:514-530
1. `ADD 8 TO ZERO GIVING APPL-RESULT` (→8). // source: CBTRN03C.cbl:515
2. `CLOSE TRANSACT-FILE`. // source: CBTRN03C.cbl:516
3. IF `TRANFILE-STATUS = '00'` → `SUBTRACT APPL-RESULT FROM APPL-RESULT` (→0); ELSE → `ADD 12 TO ZERO
   GIVING APPL-RESULT`. // source: CBTRN03C.cbl:517-521
4. IF `APPL-AOK` → CONTINUE; ELSE → `DISPLAY 'ERROR CLOSING POSTED TRANSACTION FILE'`, MOVE
   TRANFILE-STATUS→IO-STATUS, helper, abend. // source: CBTRN03C.cbl:522-529
5. `EXIT`. // source: CBTRN03C.cbl:530

### 9100-REPTFILE-CLOSE // source: CBTRN03C.cbl:532-548
`ADD 8 TO ZERO GIVING APPL-RESULT`; `CLOSE REPORT-FILE`; status `'00'` → `SUBTRACT APPL-RESULT FROM
APPL-RESULT` else `ADD 12 TO ZERO GIVING APPL-RESULT`; on error `DISPLAY 'ERROR CLOSING REPORT FILE'`,
MOVE TRANREPT-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:533-548

### 9200-CARDXREF-CLOSE // source: CBTRN03C.cbl:551-567
`MOVE 8 TO APPL-RESULT`; `CLOSE XREF-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING CROSS REF FILE'`, MOVE CARDXREF-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:552-567

### 9300-TRANTYPE-CLOSE // source: CBTRN03C.cbl:569-585
`MOVE 8 TO APPL-RESULT`; `CLOSE TRANTYPE-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING TRANSACTION TYPE FILE'`, MOVE TRANTYPE-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:570-585

### 9400-TRANCATG-CLOSE // source: CBTRN03C.cbl:587-603
`MOVE 8 TO APPL-RESULT`; `CLOSE TRANCATG-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING TRANSACTION CATG FILE'`, MOVE TRANCATG-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:588-603

### 9500-DATEPARM-CLOSE // source: CBTRN03C.cbl:605-621
`MOVE 8 TO APPL-RESULT`; `CLOSE DATE-PARMS-FILE`; status `'00'`→0 else 12; on error
`DISPLAY 'ERROR CLOSING DATE PARM FILE'`, MOVE DATEPARM-STATUS→IO-STATUS, helper, abend; EXIT. // source: CBTRN03C.cbl:606-621

### 9999-ABEND-PROGRAM // source: CBTRN03C.cbl:626-630
`DISPLAY 'ABENDING PROGRAM'`; `MOVE 0 TO TIMING`; `MOVE 999 TO ABCODE`; `CALL 'CEE3ABD' USING ABCODE,
TIMING`. Port: throw `Runtime.Abend(999)` (terminates the batch run, no return). // source: CBTRN03C.cbl:627-630

### 9910-DISPLAY-IO-STATUS // source: CBTRN03C.cbl:633-646
Formats the 2-char status into `'FILE STATUS IS: NNNN'`:
- IF `IO-STATUS NOT NUMERIC` **OR** `IO-STAT1 = '9'`: `MOVE IO-STAT1 TO IO-STATUS-04(1:1)`;
  `MOVE 0 TO TWO-BYTES-BINARY`; `MOVE IO-STAT2 TO TWO-BYTES-RIGHT`; `MOVE TWO-BYTES-BINARY TO
  IO-STATUS-0403`; `DISPLAY 'FILE STATUS IS: NNNN' IO-STATUS-04`. // source: CBTRN03C.cbl:634-640
- ELSE: `MOVE '0000' TO IO-STATUS-04`; `MOVE IO-STATUS TO IO-STATUS-04(3:2)`; `DISPLAY 'FILE STATUS
  IS: NNNN' IO-STATUS-04`. // source: CBTRN03C.cbl:641-644
- `EXIT`. // source: CBTRN03C.cbl:646

**Byte-order caveat:** `TWO-BYTES-RIGHT` is the rightmost (low-order) byte of the big-endian halfword
`TWO-BYTES-BINARY`, so the rendered number = `(int)(unsigned byte)IO-STAT2`. The .NET port must
reproduce big-endian semantics (character code 0..255 of `IO-STAT2`, rendered `%03d`) regardless of
host endianness. Note: when the helper is invoked from a keyed-lookup INVALID-KEY path,
`IO-STATUS` was set to numeric **23** (the literal `MOVE 23 TO IO-STATUS` moves a numeric to the
2-char group), so the ELSE branch runs and renders `'0023'`. // source: CBTRN03C.cbl:142-145, 488, 634-644

---

## 5. VALIDATION RULES & exact literal messages

**Business "validation" is minimal:** the only field test is the date-window filter; all other checks
are file-status / INVALID-KEY presence checks that abend on failure.

- **Date-window filter** (per transaction): include the record iff
  `TRAN-PROC-TS (1:10) >= WS-START-DATE` AND `TRAN-PROC-TS (1:10) <= WS-END-DATE`. Comparison is an
  alphanumeric (string) compare of the 10-char date substrings (`CCYY-MM-DD` form sorts correctly
  lexicographically). Out-of-range → record skipped (NEXT SENTENCE). // source: CBTRN03C.cbl:173-178
- **XREF presence:** card must exist in CARD_XREF or → abend with `'INVALID CARD NUMBER : '` + card num. // source: CBTRN03C.cbl:486-490
- **TRANTYPE presence:** type code must exist in TRAN_TYPE or → abend with `'INVALID TRANSACTION TYPE : '` + type. // source: CBTRN03C.cbl:496-500
- **TRANCATG presence:** (type,cat) must exist in TRAN_CATEGORY or → abend with `'INVALID TRAN CATG KEY : '` + key. // source: CBTRN03C.cbl:506-510

Exact literal strings to reproduce verbatim (SYSOUT / at abend):
- `'START OF EXECUTION OF PROGRAM CBTRN03C'` // source: CBTRN03C.cbl:160
- `'END OF EXECUTION OF PROGRAM CBTRN03C'` // source: CBTRN03C.cbl:215
- `'Reporting from ' WS-START-DATE ' to ' WS-END-DATE` // source: CBTRN03C.cbl:232-233
- `'ERROR READING DATEPARM FILE'` // source: CBTRN03C.cbl:238
- `'ERROR READING TRANSACTION FILE'` // source: CBTRN03C.cbl:266
- `DISPLAY TRAN-RECORD` (the raw 350-byte record image, once per in-range record AND once per
  out-of-range? — **no**: DISPLAY TRAN-RECORD is at line 180, *inside* the `IF END-OF-FILE='N'` block,
  reached for every record that survived the date filter's CONTINUE branch). // source: CBTRN03C.cbl:180
- `'TRAN-AMT ' TRAN-AMT` and `'WS-PAGE-TOTAL' WS-PAGE-TOTAL` (EOF diagnostics). // source: CBTRN03C.cbl:198-199
- `'INVALID CARD NUMBER : ' FD-XREF-CARD-NUM` // source: CBTRN03C.cbl:487
- `'INVALID TRANSACTION TYPE : ' FD-TRAN-TYPE` // source: CBTRN03C.cbl:497
- `'INVALID TRAN CATG KEY : ' FD-TRAN-CAT-KEY` // source: CBTRN03C.cbl:507
- `'ERROR WRITING REPTFILE'` // source: CBTRN03C.cbl:354
- Open errors: `'ERROR OPENING TRANFILE'` (387), `'ERROR OPENING REPTFILE'` (405),
  `'ERROR OPENING CROSS REF FILE'` (423), `'ERROR OPENING TRANSACTION TYPE FILE'` (441),
  `'ERROR OPENING TRANSACTION CATG FILE'` (459), `'ERROR OPENING DATE PARM FILE'` (477).
- Close errors: `'ERROR CLOSING POSTED TRANSACTION FILE'` (525), `'ERROR CLOSING REPORT FILE'` (543),
  `'ERROR CLOSING CROSS REF FILE'` (562), `'ERROR CLOSING TRANSACTION TYPE FILE'` (580),
  `'ERROR CLOSING TRANSACTION CATG FILE'` (598), `'ERROR CLOSING DATE PARM FILE'` (616).
- `'ABENDING PROGRAM'` // source: CBTRN03C.cbl:627
- `'FILE STATUS IS: NNNN'` (literal prefix; followed by the 4-char formatted `IO-STATUS-04`). // source: CBTRN03C.cbl:640,644

File-status accept rules: OPEN/CLOSE/WRITE accept `'00'` (else 12 → abend); the DATEPARM and TRANSACT
reads accept `'00'` and treat `'10'` as EOF; the three keyed lookups use INVALID KEY (no row) → abend.
// source: CBTRN03C.cbl:222-229, 251-258, 346-350, 486-490

---

## 6. ARITHMETIC / COMPUTE / counter notes

There are **no `COMPUTE` statements**; arithmetic is `ADD`/`SUBTRACT` plus one `FUNCTION MOD`.

**Money accumulation (S9(9)V99, signed, 2 implied decimals):**
- `ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL` — fired once per **in-range** record in
  `1100-WRITE-TRANSACTION-REPORT`, and once more at EOF in MAIN (the stale last record; see §7 #2).
  // source: CBTRN03C.cbl:287-288, 200-201
- `ADD WS-PAGE-TOTAL TO WS-GRAND-TOTAL` — in `1110-WRITE-PAGE-TOTALS` (rolls page into grand). // source: CBTRN03C.cbl:297
- `MOVE 0 TO WS-PAGE-TOTAL` (reset after each page total) and `MOVE 0 TO WS-ACCOUNT-TOTAL` (reset after
  each account total). // source: CBTRN03C.cbl:298, 310
- All three totals are PIC S9(9)V99 (9 integer digits + 2 decimals, signed). Per ARCHITECTURE.md money
  uses `decimal`, **truncate toward zero, silent overflow** — but here only ADD is used, so the
  concern is silent overflow if the running total exceeds 999,999,999.99 (then the COBOL field wraps /
  truncates high-order digits silently). Reproduce with `CobolDecimal` (no exception on overflow).
  // source: CBTRN03C.cbl:134-136; ARCHITECTURE.md §type map (S9(p)V(s) → decimal, truncate/silent-overflow)

**Line counter (WS-LINE-COUNTER PIC 9(09) COMP-3, unsigned packed):**
- `ADD 1 TO WS-LINE-COUNTER` after each report WRITE **except** in `1110-WRITE-GRAND-TOTALS` (no
  increment) and except the page-total *amount* line / header lines which DO increment. Precise
  increments: 1120-WRITE-HEADERS +4 (one per line), 1110-WRITE-PAGE-TOTALS +2, 1120-WRITE-ACCOUNT-TOTALS
  +2, 1120-WRITE-DETAIL +1, 1110-WRITE-GRAND-TOTALS **+0**. // source: CBTRN03C.cbl:299,302,311,314,327,331,335,339,373; 318-322 (no inc)
- **Page-break test:** `IF FUNCTION MOD(WS-LINE-COUNTER, WS-PAGE-SIZE) = 0` in
  `1100-WRITE-TRANSACTION-REPORT` (WS-PAGE-SIZE=20). Because the first header block sets the counter to
  4 before the first detail, MOD is evaluated against the *current accumulated line count*; the page
  break is **counter-driven, not detail-count-driven**, and a page total + new header are emitted
  whenever the counter is a multiple of 20 at the top of `1100`. Port with integer modulo on a `long`/
  `int` line counter. PIC 9(9) COMP-3 holds up to 999,999,999 — never overflows in practice. // source: CBTRN03C.cbl:282-285, 131-132
- `SUBTRACT APPL-RESULT FROM APPL-RESULT` (in 9000/9100 close) yields 0 — a verbose "set to zero"
  idiom; `ADD 8 TO ZERO GIVING APPL-RESULT` yields 8 (priming). `ADD 12 TO ZERO GIVING APPL-RESULT`
  yields 12. These are flag values only; no truncation possible. // source: CBTRN03C.cbl:515,518,520,533,536,538

**FUNCTION MOD semantics:** `FUNCTION MOD(a,b)` = `a - b * FUNCTION INTEGER(a / b)` (non-negative for
non-negative operands here). For `WS-LINE-COUNTER` ≥ 0 and `WS-PAGE-SIZE`=20 this equals C# `a % 20`.
// source: CBTRN03C.cbl:282

---

## 7. FAITHFUL BUGS (reproduce verbatim — do NOT fix)

1. **Inverted-looking date-filter with `NEXT SENTENCE` skip.** The filter is written as
   `IF in-range CONTINUE ELSE NEXT SENTENCE END-IF`. `NEXT SENTENCE` jumps past the period that ends
   the *entire* `PERFORM UNTIL … END-PERFORM.` sentence, so an **out-of-range** record skips the rest
   of the loop body (no DISPLAY, no lookups, no detail line, no accumulation) and the loop simply
   iterates. The in-range record falls through `CONTINUE` to be processed. This is the intended effect
   but the use of `NEXT SENTENCE` (rather than e.g. structured `IF in-range … END-IF`) is fragile: any
   added statement after `END-PERFORM` would change where `NEXT SENTENCE` lands. Reproduce the exact
   semantics: out-of-range ⇒ continue the loop without processing the record. // source: CBTRN03C.cbl:173-178, 206

2. **At EOF the program accumulates the STALE last transaction's amount into page & account totals.**
   When `1000-TRANFILE-GET-NEXT` hits EOF it sets `END-OF-FILE='Y'` but `READ … INTO` leaves
   `TRAN-RECORD` holding the **previous** record. The ELSE branch (line 197) then does
   `ADD TRAN-AMT TO WS-PAGE-TOTAL WS-ACCOUNT-TOTAL` (line 200-201) using that stale `TRAN-AMT` — i.e.
   the **last real transaction's amount is added twice** (once when it was processed normally, once at
   EOF) into the page total and account total before `1110-WRITE-PAGE-TOTALS` and grand totals run.
   This inflates the final page total and grand total by the last transaction's amount. Reproduce
   verbatim (do not guard the EOF accumulation). // source: CBTRN03C.cbl:197-203, 249 (READ INTO stale at '10')

3. **The final card's ACCOUNT total is never written.** `1120-WRITE-ACCOUNT-TOTALS` is called only on a
   *card control break* (when a new, different card arrives — line 182-184). At EOF the MAIN ELSE branch
   writes page total + grand total but **never** flushes the last card's `WS-ACCOUNT-TOTAL`. So the
   report omits the account-total line for the final card group, and that account total (which still
   holds the last card's sum, plus the stale double-add from #2) is silently discarded. Reproduce
   verbatim (do not add a final account-total flush). // source: CBTRN03C.cbl:181-187, 197-203

4. **`WS-LINE-COUNTER` counts EVERY report line (headers, blanks, totals, dashes), so the MOD-20
   "page break" is not a clean 20-detail page.** Because headers (+4), page-total lines (+2),
   account-total lines (+2) and detail lines (+1) all bump the same counter, the page break at
   `MOD(counter,20)=0` fires after a *mix* of line types — not after 20 detail rows. The first header
   block sets the counter to 4 before the first detail, so detail rows land on counter values 5,6,7,…
   and a page total/new-header is emitted only when the counter is exactly a multiple of 20. This is
   the program's actual (quirky) pagination; reproduce the counter increments exactly as listed in §6
   so the page-break positions match byte-for-byte. // source: CBTRN03C.cbl:282-285, 299,302,311,314,327,331,335,339,373

5. **`1110-WRITE-GRAND-TOTALS` does not increment `WS-LINE-COUNTER`** while every other write paragraph
   does. Harmless at EOF (no further MOD test), but it means the grand-total line is "uncounted".
   Reproduce as-is (no increment). // source: CBTRN03C.cbl:318-322

6. **Page-total at top of `1100` can fire before any detail on the first page only if counter hits 0.**
   The MOD test runs *before* accumulating/writing the current detail. On the very first detail the
   counter is 4 (not 0), so no spurious page total. But note the test order means a page total is
   written *ahead of* the header on subsequent pages, then `1120-WRITE-HEADERS` re-emits the 4-line
   header. This ordering (page-total then header) is intentional but easy to invert; reproduce the
   exact statement order `1110-WRITE-PAGE-TOTALS` then `1120-WRITE-HEADERS`. // source: CBTRN03C.cbl:282-285

7. **Redundant inner `IF END-OF-FILE = 'N'` guard** at line 171 duplicates the `PERFORM UNTIL … = 'Y'`
   condition. Harmless; reproduce the control structure as-is. // source: CBTRN03C.cbl:170-171

8. **`9910-DISPLAY-IO-STATUS` second-byte rendering depends on raw byte value / big-endian halfword**
   (same idiom as the rest of the CardDemo batch suite). On the non-numeric / `IO-STAT1='9'` branch the
   2nd status char is reinterpreted as the low byte of a halfword binary and printed as 0..255.
   Reproduce the big-endian result, not a little-endian misread. // source: CBTRN03C.cbl:142-145, 634-640

9. **`MOVE 23 TO IO-STATUS` on INVALID-KEY paths puts a numeric literal into a 2-char alphanumeric
   group**, yielding the 2 characters `'23'` (right-justified into the 2-byte field as `'23'`), which
   the helper then renders as `'0023'`. This is intentional but mixes numeric/alphanumeric usage;
   reproduce the `'0023'` output. // source: CBTRN03C.cbl:488,498,508, 641-644

> The behaviorally significant faithful bugs are **#2** (stale last-amount double-add inflating page &
> grand totals) and **#3** (missing final account-total line). Both materially change the report output
> and MUST be pinned by a characterization test diffing the 133-byte report dataset.

---

## 8. PORT NOTES (relational-access + tricky COBOL semantics)

**TRANSACTION read (sequential, card-sorted):** `OPEN INPUT` + `READ TRANSACT-FILE INTO TRAN-RECORD`
over the QSAM file `…TRANSACT.DALY(+1)` = a forward read cursor over the TRANSACTION table **in the
upstream-SORT order (ascending TRAN-CARD-NUM)**. The port must feed rows in card-number order for the
control-break grouping to work — either by replicating the SORT (filter by proc-date in `[start,end]`
then `ORDER BY card_num`) in the runner, or by pre-seeding the input in that order and pinning it.
Each `1000-TRANFILE-GET-NEXT` = `ReadNext()`; exhausted → status `'10'` → APPL-EOF → END-OF-FILE='Y'.
// source: CBTRN03C.cbl:29-31, 249; jcl/TRANREPT.jcl:37-55; ARCHITECTURE.md §"VSAM-semantics" (sequential=forward cursor)

> **Important:** CBTRN03C also re-applies the date filter internally (line 173-174), so even if the
> port feeds the full TRANSACTION table (unfiltered), only rows with `proc_ts[0..10] ∈ [start,end]` are
> reported. But the **control-break and totals correctness depends on card-number ordering**; if the
> input is not card-sorted, a card can appear in multiple non-adjacent groups, producing multiple
> account-total breaks for the same card (faithful to COBOL's purely-positional control break — do not
> "fix" by grouping). Recommended: replicate the SORT (range-include + ORDER BY card_num) for parity.

**Keyed lookups → SELECT:**
- XREF: `READ XREF-FILE … (RECORD KEY FD-XREF-CARD-NUM)` → `SELECT xref_card_num,cust_id,acct_id FROM
  CARD_XREF WHERE xref_card_num=@k`; row → use `XREF-ACCT-ID`; no row → INVALID KEY → abend. Key is
  X(16); compare with ordinal (culture-invariant) string equality, full 16-char width incl. trailing
  spaces. // source: CBTRN03C.cbl:185-187,484-492; ARCHITECTURE.md §"VSAM-semantics" (READ key → SELECT by PK; '00'/'23')
- TRANTYPE: `… (RECORD KEY FD-TRAN-TYPE X2)` → `SELECT tran_type,tran_type_desc FROM TRAN_TYPE WHERE
  tran_type=@k`. // source: CBTRN03C.cbl:189-190,494-502
- TRANCATG: `… (RECORD KEY FD-TRAN-CAT-KEY = X2 + 9(4))` → `SELECT … FROM TRAN_CATEGORY WHERE
  tran_type_cd=@t AND tran_cat_cd=@c`. The 6-byte composite key is built from the **transaction
  record's** TYPE-CD and CAT-CD (qualified `OF TRAN-RECORD`). `FD-TRAN-CAT-CD` is PIC 9(4) numeric;
  ensure the composite SELECT binds the integer category code, not the zoned-display string.
  // source: CBTRN03C.cbl:191-195,504-512

**DATEPARM read:** model the 1-record parameter dataset as either a tiny single-row config table or a
direct read of a `DATEPARM`-equivalent input. Parse the 21 used bytes: `WS-START-DATE` = chars 1-10,
FILLER char 11, `WS-END-DATE` = chars 12-21. The remaining 59 bytes of the X(80) record are unused.
Status `'00'`→proceed; `'10'`(empty file)→END-OF-FILE='Y' (no transactions processed). No repo data
file `DATEPARM` was found (grep returned none) — see §9. // source: CBTRN03C.cbl:122-125, 88, 220-243

**REPORT WRITE (fixed-width 133):** every report line is a `MOVE <template> TO FD-REPTFILE-REC` (X(133))
then `WRITE`. Build each 133-char line by serializing the CVTRA07Y group into a fixed-width buffer
(left-justified text, literal fillers, edited numerics), **right-padded with spaces to 133**. The
templates are 111-115 chars; the WRITE pads to LRECL 133 (the FD record is X(133)). Use the Runtime
fixed-width serializer; the report dataset is the **golden-fixture diff target** (mask nothing here —
there are no timestamps in the report body; only `WS-START-DATE`/`WS-END-DATE` from DATEPARM appear).
// source: CBTRN03C.cbl:84-85, 295,300,308,312,320,325,329,333,337,371; cpy/CVTRA07Y.cpy:1-66

**Edited PIC formatting (`CobolEditedNumeric`):**
- Detail amount `-ZZZ,ZZZ,ZZZ.ZZ`: floating leading minus; zero-suppress with comma grouping; 2
  decimals; positive/zero ⇒ leading space where `-` would be. // source: cpy/CVTRA07Y.cpy:30
- Page/Account/Grand totals `+ZZZ,ZZZ,ZZZ.ZZ`: floating leading plus; positive/zero ⇒ `+`, negative ⇒
  `-`. // source: cpy/CVTRA07Y.cpy:54,60,66
- Category code `9(4)` in the detail line is an **unsigned 4-digit zero-padded** field (not edited). // source: cpy/CVTRA07Y.cpy:24

**`INITIALIZE TRANSACTION-DETAIL-REPORT`:** sets each elementary **data** item to its type default
(alphanumeric→spaces, numeric→zeros) but **does NOT touch FILLER** items. The CVTRA07Y detail layout's
FILLERs carry VALUE clauses (`'-'`, spaces); however INITIALIZE leaves FILLER bytes **as they currently
are in memory**, NOT re-applying their VALUEs. Because TRANSACTION-DETAIL-REPORT is in WORKING-STORAGE
and its FILLER VALUEs were set once at program load and never overwritten, the `'-'` separators and
spaces persist across iterations — so reproducing INITIALIZE as "clear data items to space/zero, leave
FILLER bytes untouched (which still hold their initial VALUE)" yields the correct line. In the port,
model the detail line as a struct whose FILLER positions are constants (`'-'` at the two separator
slots, spaces elsewhere) and only the data fields are re-assigned each detail. // source: CBTRN03C.cbl:362; cpy/CVTRA07Y.cpy:15-31; ARCHITECTURE.md §type map (FILLER reconstructed on serialize)

**MOVE truncations to reproduce:** `MOVE TRAN-TYPE-DESC (X50) TO TRAN-REPORT-TYPE-DESC (X15)` truncates
to 15 chars (left-justified); `MOVE TRAN-CAT-TYPE-DESC (X50) TO TRAN-REPORT-CAT-DESC (X29)` truncates
to 29. `MOVE XREF-ACCT-ID (9(11) numeric) TO TRAN-REPORT-ACCOUNT-ID (X(11) alphanumeric)` moves the
11 digit characters left-justified into the 11-char field (a numeric→alphanumeric move with equal
display width = the 11 zoned digits, no sign since XREF-ACCT-ID is unsigned). // source: CBTRN03C.cbl:364,366,368; cpy/CVTRA07Y.cpy:18,22,26

**Substring reference modification:** `TRAN-PROC-TS (1:10)` = the first 10 chars of the 26-char
processing timestamp (`CCYY-MM-DD`). In the port, `proc_ts.Substring(0,10)`. The comparison operands
`WS-START-DATE`/`WS-END-DATE` are X(10). Use ordinal string comparison. // source: CBTRN03C.cbl:173-174

**REDEFINES:** `TWO-BYTES-ALPHA` over `TWO-BYTES-BINARY` — model as a 2-byte backing buffer with a
numeric (halfword, big-endian) view and a 2-char view; only the right byte is written (status helper).
No table impact. // source: CBTRN03C.cbl:142-145

**Abend mapping:** `CALL 'CEE3ABD' USING ABCODE, TIMING` with ABCODE=999, TIMING=0 → terminate with
abend code 999 via `Runtime.Abend`. No graceful return. // source: CBTRN03C.cbl:626-630

**DISPLAY TRAN-RECORD / diagnostics:** the per-record `DISPLAY TRAN-RECORD` (350-byte image) and the
EOF `DISPLAY 'TRAN-AMT ' TRAN-AMT` / `'WS-PAGE-TOTAL' WS-PAGE-TOTAL` go to SYSOUT, not the report
dataset. If SYSOUT is part of the golden fixture, render `TRAN-AMT` (S9(9)V99 DISPLAY) as 11 zoned
digits with overpunched sign on the last digit and no decimal point; otherwise treat as informational.
// source: CBTRN03C.cbl:180,198-199

**No OCCURS, no STRING/UNSTRING.** The only `FUNCTION` is `MOD`. The only signed arithmetic is on the
three V99 money totals (silent-overflow `CobolDecimal`). // source: CBTRN03C.cbl:282, 287, 297

---

## 9. OPEN QUESTIONS / RISKS

1. **No `DATEPARM` data file in the repo.** Glob for `**/DATEPARM*` / `**/data/**/*PARM*` returns
   nothing; the JCL DSN `AWS.M2.CARDDEMO.DATEPARM` is an external dataset. The port/harness must supply
   a start/end date pair (e.g. `2022-01-01` … `2022-07-06`, matching the SORT step's literal parms) as
   the DATEPARM input. Decide the canonical test parm dates and pin them. // source: jcl/TRANREPT.jcl:43-44,73-74; CBTRN03C.cbl:122-125
2. **Input ordering for the control break.** Faithful output requires the TRANSACTION input be
   card-number-sorted (per the upstream SORT). If the relational runner pulls rows in PK (`tran_id`)
   order instead of card order, account-total breaks will differ. Replicate the SORT (range-include by
   proc-date + ORDER BY card_num) and pin the order. // source: jcl/TRANREPT.jcl:37-48; CBTRN03C.cbl:181
3. **Faithful bugs #2 and #3 change totals.** A characterization test must use a fixture with ≥2 cards
   and a non-zero last-card amount to expose the stale double-add (#2) and the missing final
   account-total line (#3). Without such a fixture these bugs are invisible. // source: CBTRN03C.cbl:197-203
4. **Page-break parity (#4).** The MOD-20 break depends on the exact line-counter increments across all
   write paragraphs (incl. the grand-total non-increment, #5). Any deviation shifts page boundaries and
   breaks the byte diff. Encode the increments exactly per §6. // source: CBTRN03C.cbl:282-285, 318-322
5. **Report line length / trailing pad.** Templates are 111-115 chars but the FD record is X(133);
   confirm the WRITE emits a full 133-byte fixed-width line (space-padded) to match LRECL 133 from the
   JCL. The harness diff must compare full 133-char lines. // source: CBTRN03C.cbl:85; jcl/TRANREPT.jcl:78
6. **`MOVE 23 TO IO-STATUS` rendering** (#9): confirm the golden fixture (if SYSOUT-inclusive) expects
   `'FILE STATUS IS: NNNN0023'`-style output on a lookup-miss abend. Only matters if a test injects a
   missing key. // source: CBTRN03C.cbl:488, 641-644
7. **TRAN_CATEGORY composite-key binding.** Ensure the numeric `cat_cd` (9(4)) is bound as an integer
   in the composite SELECT and that leading-zero category codes (e.g. `0001`) match the seeded data's
   stored form. // source: CBTRN03C.cbl:191-195; ARCHITECTURE.md §schema (TRAN_CATEGORY composite PK tran_type_cd X2, tran_cat_cd 9(4))
