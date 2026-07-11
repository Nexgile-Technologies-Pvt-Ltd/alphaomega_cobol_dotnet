# PORT SPEC — CBEXPORT (Customer Data Export for Branch Migration)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBEXPORT.cbl`
Export layout copybook: `app/cpy/CVEXPORT.cpy` (EXPORT-RECORD, 500 bytes, multi-record-type REDEFINES)
Input copybooks: `CVCUS01Y` (CUSTOMER 500), `CVACT01Y` (ACCOUNT 300), `CVACT03Y` (CARD-XREF 50),
  `CVTRA05Y` (TRAN-RECORD 350), `CVACT02Y` (CARD 150)
JCL: `app/jcl/CBEXPORT.jcl`
Target: `New_Dotnet_Code/src/CardDemo.Batch/CBEXPORT.cs` (one class over repositories), per `_design/ARCHITECTURE.md`.
Kind: **BATCH**. No screen, no CICS, no COMMAREA, no BMS map.

---

## 1. Purpose

CBEXPORT is a stand-alone **BATCH** program that performs a one-shot "branch migration" export.
It reads **five** CardDemo master/detail files in full (CUSTOMER, ACCOUNT, CARD-XREF, TRANSACTION,
CARD), and for every input record emits one fixed-length **500-byte** record into a single
multi-record-type **export dataset** (`EXPFILE`). Each emitted record is tagged with a 1-byte
record-type code (`C`/`A`/`X`/`T`/`D`), a shared run timestamp, a monotonically increasing sequence
number, a hard-coded branch id (`'0001'`) and region code (`'NORTH'`), followed by the type-specific
payload mapped field-by-field from the source record. The five file phases run strictly in sequence
(customers, then accounts, then cross-refs, then transactions, then cards); within each phase the
input file is read sequentially low-key to high-key. The program tracks per-type and grand-total
counters and prints them, then closes everything and ends. It does **no** updates, joins, filtering,
or arithmetic on business data — it is a pure flatten-and-dump migration extractor.
// source: CBEXPORT.cbl:5-11, 27-31 (header: "Export Customer Data for Branch Migration")
// source: CBEXPORT.cbl:149-158 (mainline orchestration)

**How it is invoked:** JCL job `CBEXPORT`, step **`STEP02  EXEC PGM=CBEXPORT`**.
A preceding step **`STEP01  EXEC PGM=IDCAMS`** DELETEs and re-DEFINEs the output VSAM cluster
`AWS.M2.CARDDEMO.EXPORT.DATA` as an **INDEXED** KSDS with `KEYS(4 28)` and `RECORDSIZE(500 500)`
before the program runs. It is not a called subprogram and not a CICS transaction; it ends with
`GOBACK` to the operating system.
// source: jcl/CBEXPORT.jcl:24-39 (STEP01 IDCAMS DEFINE CLUSTER, KEYS(4 28), RECORDSIZE(500 500))
// source: jcl/CBEXPORT.jcl:43 (//STEP02 EXEC PGM=CBEXPORT)
// source: CBEXPORT.cbl:158 (GOBACK)

DD-to-file bindings (from JCL):
- `CUSTFILE` → `CUSTDATA.VSAM.KSDS` (input)  // source: jcl/CBEXPORT.jcl:49-50
- `ACCTFILE` → `ACCTDATA.VSAM.KSDS` (input)  // source: jcl/CBEXPORT.jcl:51-52
- `XREFFILE` → `CARDXREF.VSAM.KSDS` (input)  // source: jcl/CBEXPORT.jcl:53-54
- `TRANSACT` → `TRANSACT.VSAM.KSDS` (input)  // source: jcl/CBEXPORT.jcl:55-56
- `CARDFILE` → `CARDDATA.VSAM.KSDS` (input)  // source: jcl/CBEXPORT.jcl:57-58
- `EXPFILE`  → `EXPORT.DATA` (output KSDS)   // source: jcl/CBEXPORT.jcl:62-63

This is an **online program: NO**.

---

## 2. FILE / TABLE access

All five inputs are declared `ORGANIZATION INDEXED, ACCESS MODE SEQUENTIAL` (KSDS read sequentially
low→high key). The output is declared `ORGANIZATION INDEXED, ACCESS MODE SEQUENTIAL` but written with
strictly ascending keys (the sequence counter), so it behaves as an append-ordered output dataset.

| COBOL file (SELECT) | DD | ORG / ACCESS | RECORD KEY | Relational target (ARCHITECTURE.md) | Operations in this program | Maps to (relational repository / SQL) |
|---|---|---|---|---|---|---|
| `CUSTOMER-INPUT` | CUSTFILE | INDEXED, SEQUENTIAL | `CUST-ID` 9(09) | **CUSTOMER** (PK `cust_id`) | OPEN INPUT; sequential READ (READNEXT semantics) to EOF; CLOSE | `SELECT * FROM CUSTOMER ORDER BY cust_id` forward cursor; each READ = next row; EOF → status `'10'`. // source: CBEXPORT.cbl:35-39, 200, 260, 556 |
| `ACCOUNT-INPUT` | ACCTFILE | INDEXED, SEQUENTIAL | `ACCT-ID` 9(11) | **ACCOUNT** (PK `acct_id`) | OPEN INPUT; sequential READ; CLOSE | `SELECT * FROM ACCOUNT ORDER BY acct_id` forward cursor. // source: CBEXPORT.cbl:41-45, 207, 329, 557 |
| `XREF-INPUT` | XREFFILE | INDEXED, SEQUENTIAL | `XREF-CARD-NUM` X(16) | **CARD_XREF** (PK `xref_card_num`) | OPEN INPUT; sequential READ; CLOSE | `SELECT * FROM CARD_XREF ORDER BY xref_card_num` forward cursor. // source: CBEXPORT.cbl:47-51, 214, 393, 558 |
| `TRANSACTION-INPUT` | TRANSACT | INDEXED, SEQUENTIAL | `TRAN-ID` X(16) | **TRANSACTION** (PK `tran_id`) | OPEN INPUT; sequential READ; CLOSE | `SELECT * FROM TRANSACTION ORDER BY tran_id` forward cursor. // source: CBEXPORT.cbl:53-57, 221, 448, 559 |
| `CARD-INPUT` | CARDFILE | INDEXED, SEQUENTIAL | `CARD-NUM` X(16) | **CARD** (PK `card_num`) | OPEN INPUT; sequential READ; CLOSE | `SELECT * FROM CARD ORDER BY card_num` forward cursor. // source: CBEXPORT.cbl:59-63, 228, 513, 560 |
| `EXPORT-OUTPUT` | EXPFILE | INDEXED, SEQUENTIAL | `EXPORT-SEQUENCE-NUM` 9(9) COMP | **derived EXPORT dataset — NOT a base table** | OPEN OUTPUT; WRITE (5× across phases); CLOSE | append fixed-width 500-byte record to the EXPORT output dataset, in ascending sequence-number order. // source: CBEXPORT.cbl:65-69, 89-92, 235, 301, 561 |

**Important:** Only the five INPUT files correspond to base relational tables (CUSTOMER, ACCOUNT,
CARD_XREF, TRANSACTION, CARD). `EXPFILE` is the program's **product** — a multi-record-type export
file. Per ARCHITECTURE.md it is a derived dataset, not a base table; the .NET port must emit it as a
byte-faithful fixed-width 500-byte record stream (this is exactly what the batch-characterization
golden harness diffs, with timestamps masked). Do **not** model EXPFILE as a DB table.

### Operation → SQL / repository mapping
- All five inputs: `OPEN INPUT` → open a forward cursor `ORDER BY <PK>`; `READ` (no `INTO`,
  reads into the FD record) → `cursor.ReadNext()`; the 88-level `*-EOF VALUE '10'` flag → cursor
  exhausted. `CLOSE` → dispose cursor. // source: CBEXPORT.cbl:101,104,107,110,113 (EOF 88s = '10')
- Output: `OPEN OUTPUT` → create/truncate the export dataset; `WRITE EXPORT-OUTPUT-RECORD FROM
  EXPORT-RECORD` → append one serialized 500-byte record; `CLOSE` → flush/close.
  // source: CBEXPORT.cbl:301, 364, 419, 484, 542

---

## 3. EXPORT-RECORD layout (CVEXPORT.cpy) — the serialized 500-byte output

The export record is a fixed 500-byte image with a common 50-byte header and a 460-byte payload area
(`EXPORT-RECORD-DATA`) that is REDEFINED five ways (one per record type). **Only one redefinition is
populated per WRITE**, selected by `EXPORT-REC-TYPE`. The payload encodings include COMP (binary) and
COMP-3 (packed) fields — these are **file-format concerns only**; the relational input columns stay
typed per ARCHITECTURE.md, and the Runtime fixed-width serializer reproduces the on-disk encoding.

### Common header (offsets within the 500-byte record)
| Field | PIC | Bytes | Off | Notes |
|---|---|---|---|---|
| `EXPORT-REC-TYPE` | X(1) | 1 | 0 | `C`/`A`/`X`/`T`/`D` |
| `EXPORT-TIMESTAMP` | X(26) | 26 | 1 | run timestamp `YYYY-MM-DD HH:MM:SS.00`; REDEFINES as DATE X(10)+SEP X(1)+TIME X(15) |
| `EXPORT-SEQUENCE-NUM` | 9(9) **COMP** | 4 | 27 | binary fullword; ascending counter |
| `EXPORT-BRANCH-ID` | X(4) | 4 | 31 | constant `'0001'` |
| `EXPORT-REGION-CODE` | X(5) | 5 | 35 | constant `'NORTH'` |
| `EXPORT-RECORD-DATA` | X(460) | 460 | 40 | type-specific payload (see below) |
// source: CVEXPORT.cpy:9-19

Note the VSAM key in the JCL is `KEYS(4 28)` = a **4-byte** key at **offset 28 (1-based)** = offset
27 (0-based) = exactly the 4-byte `EXPORT-SEQUENCE-NUM` COMP field. // source: jcl/CBEXPORT.jcl:32

### Customer payload (`EXPORT-CUSTOMER-DATA`, REC-TYPE `C`)  // source: CVEXPORT.cpy:24-42
`EXP-CUST-ID` 9(09) **COMP**; `EXP-CUST-FIRST-NAME` X(25); `EXP-CUST-MIDDLE-NAME` X(25);
`EXP-CUST-LAST-NAME` X(25); `EXP-CUST-ADDR-LINE` X(50) **OCCURS 3**; `EXP-CUST-ADDR-STATE-CD` X(02);
`EXP-CUST-ADDR-COUNTRY-CD` X(03); `EXP-CUST-ADDR-ZIP` X(10); `EXP-CUST-PHONE-NUM` X(15) **OCCURS 2**;
`EXP-CUST-SSN` 9(09) (zoned DISPLAY); `EXP-CUST-GOVT-ISSUED-ID` X(20); `EXP-CUST-DOB-YYYY-MM-DD` X(10);
`EXP-CUST-EFT-ACCOUNT-ID` X(10); `EXP-CUST-PRI-CARD-HOLDER-IND` X(01);
`EXP-CUST-FICO-CREDIT-SCORE` 9(03) **COMP-3**; `FILLER` X(134).

### Account payload (`EXPORT-ACCOUNT-DATA`, REC-TYPE `A`)  // source: CVEXPORT.cpy:47-60
`EXP-ACCT-ID` 9(11) (zoned); `EXP-ACCT-ACTIVE-STATUS` X(01); `EXP-ACCT-CURR-BAL` S9(10)V99 **COMP-3**;
`EXP-ACCT-CREDIT-LIMIT` S9(10)V99 (zoned); `EXP-ACCT-CASH-CREDIT-LIMIT` S9(10)V99 **COMP-3**;
`EXP-ACCT-OPEN-DATE` X(10); `EXP-ACCT-EXPIRAION-DATE` X(10); `EXP-ACCT-REISSUE-DATE` X(10);
`EXP-ACCT-CURR-CYC-CREDIT` S9(10)V99 (zoned); `EXP-ACCT-CURR-CYC-DEBIT` S9(10)V99 **COMP**;
`EXP-ACCT-ADDR-ZIP` X(10); `EXP-ACCT-GROUP-ID` X(10); `FILLER` X(352).

### Transaction payload (`EXPORT-TRANSACTION-DATA`, REC-TYPE `T`)  // source: CVEXPORT.cpy:65-79
`EXP-TRAN-ID` X(16); `EXP-TRAN-TYPE-CD` X(02); `EXP-TRAN-CAT-CD` 9(04); `EXP-TRAN-SOURCE` X(10);
`EXP-TRAN-DESC` X(100); `EXP-TRAN-AMT` S9(09)V99 **COMP-3**; `EXP-TRAN-MERCHANT-ID` 9(09) **COMP**;
`EXP-TRAN-MERCHANT-NAME` X(50); `EXP-TRAN-MERCHANT-CITY` X(50); `EXP-TRAN-MERCHANT-ZIP` X(10);
`EXP-TRAN-CARD-NUM` X(16); `EXP-TRAN-ORIG-TS` X(26); `EXP-TRAN-PROC-TS` X(26); `FILLER` X(140).

### Card-Xref payload (`EXPORT-CARD-XREF-DATA`, REC-TYPE `X`)  // source: CVEXPORT.cpy:84-88
`EXP-XREF-CARD-NUM` X(16); `EXP-XREF-CUST-ID` 9(09) (zoned); `EXP-XREF-ACCT-ID` 9(11) **COMP**;
`FILLER` X(427).

### Card payload (`EXPORT-CARD-DATA`, REC-TYPE `D`)  // source: CVEXPORT.cpy:93-100
`EXP-CARD-NUM` X(16); `EXP-CARD-ACCT-ID` 9(11) **COMP**; `EXP-CARD-CVV-CD` 9(03) **COMP**;
`EXP-CARD-EMBOSSED-NAME` X(50); `EXP-CARD-EXPIRAION-DATE` X(10); `EXP-CARD-ACTIVE-STATUS` X(01);
`FILLER` X(373).

> NOTE: each REDEFINES branch declares its own trailing `FILLER` so all five payloads occupy exactly
> 460 bytes. The .NET serializer must reconstruct FILLER as spaces (X fillers) / appropriate zero/low
> fill so the record is exactly 500 bytes regardless of record type.

---

## 4. WORKING-STORAGE of interest

- `WS-FILE-STATUS-AREA` — six PIC X(02) status fields, one per file, each with `*-OK VALUE '00'` and
  (inputs only) `*-EOF VALUE '10'` 88-levels. The export status has only `WS-EXPORT-OK VALUE '00'`
  (no EOF). // source: CBEXPORT.cbl:99-116
- `WS-EXPORT-CONTROL`: `WS-EXPORT-DATE` X(10), `WS-EXPORT-TIME` X(08), `WS-FORMATTED-TIMESTAMP` X(26),
  `WS-SEQUENCE-COUNTER` 9(09) **DISPLAY** VALUE 0. // source: CBEXPORT.cbl:119-123
- `WS-TIMESTAMP-FIELDS`: `WS-CURRENT-DATE` (YEAR 9(4)/MONTH 9(2)/DAY 9(2)); `WS-CURRENT-TIME`
  (HOUR/MINUTE/SECOND/HUNDREDTH each 9(2)). // source: CBEXPORT.cbl:126-135
- `WS-EXPORT-STATISTICS`: six 9(09) counters — customer, account, xref, tran, card, total — all
  VALUE 0. // source: CBEXPORT.cbl:138-144

---

## 5. PARAGRAPH-BY-PARAGRAPH outline (each = a method)

**`0000-MAIN-PROCESSING`** — orchestrator. PERFORM in order: `1000-INITIALIZE`,
`2000-EXPORT-CUSTOMERS`, `3000-EXPORT-ACCOUNTS`, `4000-EXPORT-XREFS`, `5000-EXPORT-TRANSACTIONS`,
`5500-EXPORT-CARDS`, `6000-FINALIZE`; then `GOBACK`.
// source: CBEXPORT.cbl:149-158

**`1000-INITIALIZE`** — DISPLAY start banner; PERFORM `1050-GENERATE-TIMESTAMP`; PERFORM
`1100-OPEN-FILES`; DISPLAY export date and export time.
// source: CBEXPORT.cbl:161-169

**`1050-GENERATE-TIMESTAMP`** — `ACCEPT WS-CURRENT-DATE FROM DATE YYYYMMDD`; `ACCEPT WS-CURRENT-TIME
FROM TIME`. STRING `YEAR '-' MONTH '-' DAY` (DELIMITED BY SIZE) INTO `WS-EXPORT-DATE` (→ `YYYY-MM-DD`).
STRING `HOUR ':' MINUTE ':' SECOND` INTO `WS-EXPORT-TIME` (→ `HH:MM:SS`). STRING
`WS-EXPORT-DATE ' ' WS-EXPORT-TIME '.00'` INTO `WS-FORMATTED-TIMESTAMP` (→ 26-char
`YYYY-MM-DD HH:MM:SS.00`). The `.00` hundredths are **hard-coded constant**, NOT the real
`WS-CURR-HUNDREDTH`. // source: CBEXPORT.cbl:172-195

**`1100-OPEN-FILES`** — OPEN INPUT each of CUSTOMER, ACCOUNT, XREF, TRANSACTION, CARD; OPEN OUTPUT
EXPORT-OUTPUT. After each OPEN, if NOT `*-OK` (i.e. status ≠ '00'), DISPLAY a file-specific error with
the status code and PERFORM `9999-ABEND-PROGRAM`. // source: CBEXPORT.cbl:198-240

**`2000-EXPORT-CUSTOMERS`** — DISPLAY banner; priming PERFORM `2100-READ-CUSTOMER-RECORD`; PERFORM
UNTIL `WS-CUSTOMER-EOF` { `2200-CREATE-CUSTOMER-EXP-REC`; `2100-READ-CUSTOMER-RECORD` }; DISPLAY
customers-exported count. (Read-ahead loop: prime, then process-then-read.)
// source: CBEXPORT.cbl:243-255

**`2100-READ-CUSTOMER-RECORD`** — READ CUSTOMER-INPUT. If NOT OK AND NOT EOF, DISPLAY read error +
status, PERFORM `9999-ABEND-PROGRAM`. (Status '10' = EOF is tolerated and ends the loop.)
// source: CBEXPORT.cbl:258-266

**`2200-CREATE-CUSTOMER-EXP-REC`** — `INITIALIZE EXPORT-RECORD`; MOVE `'C'` to REC-TYPE; MOVE
`WS-FORMATTED-TIMESTAMP` to EXPORT-TIMESTAMP; `ADD 1 TO WS-SEQUENCE-COUNTER`; MOVE counter to
EXPORT-SEQUENCE-NUM; MOVE `'0001'` to BRANCH-ID; MOVE `'NORTH'` to REGION-CODE. Then field-by-field
MOVE of the 17 customer fields into `EXPORT-CUSTOMER-DATA` (CUST-ID→EXP-CUST-ID; names; the 3
addr-lines into EXP-CUST-ADDR-LINE(1..3); state/country/zip; the 2 phones into EXP-CUST-PHONE-NUM(1..2);
SSN; govt-id; DOB; EFT-acct; pri-holder-ind; FICO). WRITE EXPORT-OUTPUT-RECORD FROM EXPORT-RECORD; if
NOT EXPORT-OK, DISPLAY write error + status, PERFORM `9999-ABEND-PROGRAM`. `ADD 1` to
customer-records-exported and to total-records-exported. // source: CBEXPORT.cbl:269-310

**`3000-EXPORT-ACCOUNTS`** — same read-ahead loop pattern for ACCOUNT; PERFORM
`3100-READ-ACCOUNT-RECORD` then UNTIL EOF { `3200-CREATE-ACCOUNT-EXP-REC`; read }; DISPLAY count.
// source: CBEXPORT.cbl:312-324

**`3100-READ-ACCOUNT-RECORD`** — READ ACCOUNT-INPUT; if NOT OK AND NOT EOF → error + ABEND.
// source: CBEXPORT.cbl:327-335

**`3200-CREATE-ACCOUNT-EXP-REC`** — INITIALIZE; header (REC-TYPE `'A'`, timestamp, ADD 1 seq,
seq→EXPORT-SEQUENCE-NUM, `'0001'`, `'NORTH'`). MOVE the 12 account fields into `EXPORT-ACCOUNT-DATA`
(ACCT-ID, active-status, curr-bal, credit-limit, cash-credit-limit, open/expir/reissue dates,
curr-cyc-credit, curr-cyc-debit, addr-zip, group-id). WRITE; on not-OK error+ABEND. ADD 1 to
account-records and total. // source: CBEXPORT.cbl:338-373

**`4000-EXPORT-XREFS`** — read-ahead loop for XREF: `4100-READ-XREF-RECORD`, UNTIL EOF {
`4200-CREATE-XREF-EXPORT-RECORD`; read }; DISPLAY count. // source: CBEXPORT.cbl:376-388

**`4100-READ-XREF-RECORD`** — READ XREF-INPUT; if NOT OK AND NOT EOF → error + ABEND.
// source: CBEXPORT.cbl:391-399

**`4200-CREATE-XREF-EXPORT-RECORD`** — INITIALIZE; header (REC-TYPE `'X'`, timestamp, ADD 1 seq, seq,
`'0001'`, `'NORTH'`). MOVE XREF-CARD-NUM, XREF-CUST-ID, XREF-ACCT-ID into `EXPORT-CARD-XREF-DATA`.
WRITE; on not-OK error+ABEND. ADD 1 to xref-records and total. // source: CBEXPORT.cbl:402-428

**`5000-EXPORT-TRANSACTIONS`** — read-ahead loop for TRANSACTION: `5100-READ-TRANSACTION-RECORD`,
UNTIL EOF { `5200-CREATE-TRAN-EXP-REC`; read }; DISPLAY count. // source: CBEXPORT.cbl:431-443

**`5100-READ-TRANSACTION-RECORD`** — READ TRANSACTION-INPUT; if NOT OK AND NOT EOF → error + ABEND.
// source: CBEXPORT.cbl:446-454

**`5200-CREATE-TRAN-EXP-REC`** — INITIALIZE; header (REC-TYPE `'T'`, timestamp, ADD 1 seq, seq,
`'0001'`, `'NORTH'`). MOVE the 13 transaction fields into `EXPORT-TRANSACTION-DATA` (TRAN-ID,
type-cd, cat-cd, source, desc, amt, merchant-id, merchant-name, merchant-city, merchant-zip, card-num,
orig-ts, proc-ts). WRITE; on not-OK error+ABEND. ADD 1 to tran-records and total.
// source: CBEXPORT.cbl:457-493

**`5500-EXPORT-CARDS`** — read-ahead loop for CARD: `5600-READ-CARD-RECORD`, UNTIL EOF {
`5700-CREATE-CARD-EXPORT-RECORD`; read }; DISPLAY count. // source: CBEXPORT.cbl:496-508

**`5600-READ-CARD-RECORD`** — READ CARD-INPUT; if NOT OK AND NOT EOF → error + ABEND.
// source: CBEXPORT.cbl:511-519

**`5700-CREATE-CARD-EXPORT-RECORD`** — INITIALIZE; header (REC-TYPE `'D'`, timestamp, ADD 1 seq, seq,
`'0001'`, `'NORTH'`). MOVE CARD-NUM, CARD-ACCT-ID, CARD-CVV-CD, CARD-EMBOSSED-NAME,
CARD-EXPIRAION-DATE, CARD-ACTIVE-STATUS into `EXPORT-CARD-DATA`. WRITE; on not-OK error+ABEND. ADD 1
to card-records and total. // source: CBEXPORT.cbl:522-551

**`6000-FINALIZE`** — CLOSE all six files (in order: CUSTOMER, ACCOUNT, XREF, TRANSACTION, CARD,
EXPORT); DISPLAY "Export completed" and the six summary counts (customers, accounts, xrefs,
transactions, cards, total). No file-status checks on CLOSE. // source: CBEXPORT.cbl:554-573

**`9999-ABEND-PROGRAM`** — DISPLAY `'CBEXPORT: ABENDING PROGRAM'`; `CALL 'CEE3ABD'` (LE forced abend;
no return code argument is passed — see Faithful Bugs). // source: CBEXPORT.cbl:576-579

### Arithmetic / COMPUTE notes
- The only arithmetic is `ADD 1 TO WS-SEQUENCE-COUNTER` and the `ADD 1` count increments; all targets
  are unsigned `9(09)` DISPLAY. No truncation/sign concerns at normal volumes; wrap (silent overflow,
  drop high digit) only past 999,999,999 records — not reachable. // source: CBEXPORT.cbl:276-277, 309-310
- `WS-SEQUENCE-COUNTER` is `9(09)` **DISPLAY**; it is MOVEd into `EXPORT-SEQUENCE-NUM` which is
  `9(9)` **COMP** (binary). The MOVE converts numeric value DISPLAY→binary (value preserved); the
  on-disk image is a 4-byte big-endian binary fullword. The Runtime serializer must emit COMP, not
  zoned. // source: CBEXPORT.cbl:277, CVEXPORT.cpy:16

---

## 6. VALIDATION RULES and exact literal messages

There is **no business validation** — only I/O status checks. Exact literal text (must match):

DISPLAY banners / progress (informational):
- `'CBEXPORT: Starting Customer Data Export'` // source: CBEXPORT.cbl:163
- `'CBEXPORT: Export Date: '` + WS-EXPORT-DATE // source: CBEXPORT.cbl:168
- `'CBEXPORT: Export Time: '` + WS-EXPORT-TIME // source: CBEXPORT.cbl:169
- `'CBEXPORT: Processing customer records'` // source: CBEXPORT.cbl:245
- `'CBEXPORT: Customers exported: '` + count // source: CBEXPORT.cbl:254-255
- `'CBEXPORT: Processing account records'` // source: CBEXPORT.cbl:314
- `'CBEXPORT: Accounts exported: '` + count // source: CBEXPORT.cbl:323-324
- `'CBEXPORT: Processing cross-reference records'` // source: CBEXPORT.cbl:378
- `'CBEXPORT: Cross-references exported: '` + count // source: CBEXPORT.cbl:387-388
- `'CBEXPORT: Processing transaction records'` // source: CBEXPORT.cbl:433
- `'CBEXPORT: Transactions exported: '` + count // source: CBEXPORT.cbl:442-443
- `'CBEXPORT: Processing card records'` // source: CBEXPORT.cbl:498
- `'CBEXPORT: Cards exported: '` + count // source: CBEXPORT.cbl:507-508
- `'CBEXPORT: Export completed'` // source: CBEXPORT.cbl:563
- `'CBEXPORT: Customers Exported: '` + count // source: CBEXPORT.cbl:564-565
- `'CBEXPORT: Accounts Exported: '` + count // source: CBEXPORT.cbl:566-567
- `'CBEXPORT: XRefs Exported: '` + count // source: CBEXPORT.cbl:568
- `'CBEXPORT: Transactions Exported: '` + count // source: CBEXPORT.cbl:569-570
- `'CBEXPORT: Cards Exported: '` + count // source: CBEXPORT.cbl:571
- `'CBEXPORT: Total Records Exported: '` + count // source: CBEXPORT.cbl:572-573

Error messages (each precedes an ABEND):
- OPEN failures (status ≠ '00'):
  - `'ERROR: Cannot open CUSTOMER-INPUT, Status: '` + WS-CUSTOMER-STATUS // source: CBEXPORT.cbl:202-203
  - `'ERROR: Cannot open ACCOUNT-INPUT, Status: '` + WS-ACCOUNT-STATUS // source: CBEXPORT.cbl:209-210
  - `'ERROR: Cannot open XREF-INPUT, Status: '` + WS-XREF-STATUS // source: CBEXPORT.cbl:216-217
  - `'ERROR: Cannot open TRANSACTION-INPUT, Status: '` + WS-TRANSACTION-STATUS // source: CBEXPORT.cbl:223-224
  - `'ERROR: Cannot open CARD-INPUT, Status: '` + WS-CARD-STATUS // source: CBEXPORT.cbl:230-231
  - `'ERROR: Cannot open EXPORT-OUTPUT, Status: '` + WS-EXPORT-STATUS // source: CBEXPORT.cbl:237-238
- READ failures (status not '00' and not '10'):
  - `'ERROR: Reading CUSTOMER-INPUT, Status: '` + status // source: CBEXPORT.cbl:263-264
  - `'ERROR: Reading ACCOUNT-INPUT, Status: '` + status // source: CBEXPORT.cbl:332-333
  - `'ERROR: Reading XREF-INPUT, Status: '` + status // source: CBEXPORT.cbl:396-397
  - `'ERROR: Reading TRANSACTION-INPUT, Status: '` + status // source: CBEXPORT.cbl:451-452
  - `'ERROR: Reading CARD-INPUT, Status: '` + status // source: CBEXPORT.cbl:516-517
- WRITE failures (status ≠ '00'), identical text in all five creators:
  - `'ERROR: Writing export record, Status: '` + WS-EXPORT-STATUS // source: CBEXPORT.cbl:304-305, 367-368, 422-423, 487-488, 545-546
- ABEND banner: `'CBEXPORT: ABENDING PROGRAM'` // source: CBEXPORT.cbl:578

---

## 7. FAITHFUL BUGS / quirks to reproduce verbatim (do NOT fix)

1. **Hard-coded hundredths `.00` in timestamp.** `WS-CURR-HUNDREDTH` (from `ACCEPT ... FROM TIME`) is
   captured but **never used**; the 26-char timestamp always ends in literal `.00`. Reproduce: emit
   `YYYY-MM-DD HH:MM:SS.00` ignoring real hundredths. // source: CBEXPORT.cbl:135, 191-194

2. **Hard-coded branch/region on every record.** Every export record carries `EXPORT-BRANCH-ID =
   '0001'` and `EXPORT-REGION-CODE = 'NORTH'` regardless of the source data. Do not derive these.
   // source: CBEXPORT.cbl:278-279, 347-348, 411-412, 466-467, 531-532

3. **`CALL 'CEE3ABD'` with no arguments.** The LE abend routine is called with no return-code/cleanup
   parameters (the documented signature is `CEE3ABD(abend-code, cleanup)`). On a real LE this is a
   malformed call; the .NET port should treat `9999-ABEND-PROGRAM` as an immediate abnormal
   termination (non-zero exit / throw) after printing the ABEND banner. Reproduce the abrupt-abend
   semantics, not a graceful return. // source: CBEXPORT.cbl:579

4. **No file-status check on any CLOSE.** `6000-FINALIZE` closes all six files without inspecting
   status; a close failure is silently ignored. Preserve (do not add error handling on close).
   // source: CBEXPORT.cbl:556-561

5. **Per-type FILLER differs and is INITIALIZEd, not space-filled by MOVE.** Each WRITE first does
   `INITIALIZE EXPORT-RECORD`, which sets alphanumeric fields to spaces and numeric fields to zero
   across the *whole 500 bytes under the base (non-redefined) view*. Because the populated payload is a
   REDEFINES, the unused tail (`FILLER X(nnn)`) and any payload bytes the active type does not touch
   retain INITIALIZE's result for the base layout — i.e. spaces for the X(460) base view. Net effect:
   the trailing/unused payload bytes are **spaces**, and the COMP/COMP-3 numeric subfields of the
   active redefine are then overwritten. Reproduce exact fill: base view space-fill then overlay the
   active type's binary/packed/zoned fields. // source: CBEXPORT.cbl:271, 340, 404, 459, 524; CVEXPORT.cpy:19,42,60,79,88,100

6. **Misspelled field name `EXPIRAION` carried through.** Source copybooks and the export layout both
   spell it `EXPIRAION-DATE` (missing the second `T`). Keep the (mis)spelling in any field/column
   naming for faithfulness; it is the canonical name. // source: CVEXPORT.cpy:54,98; CVACT01Y.cpy:11; CVACT02Y.cpy:9

7. **EOF condition keyed to status literal `'10'`.** Loops end only when file status = `'10'`. Any
   other non-'00' status on READ → ABEND. The SQLite repository's "cursor exhausted" must surface as
   status `'10'`. // source: CBEXPORT.cbl:101,104,107,110,113, 262, 331, 395, 450, 515

8. **Output dataset is INDEXED but written sequentially with an ascending key.** The program relies on
   the sequence counter being unique and ascending so WRITE never hits a duplicate-key ('22'). Since
   the counter starts at 0 and is incremented *before* each WRITE, the first record's key is 1 (never
   0). No record ever has sequence number 0. // source: CBEXPORT.cbl:123, 276-277, 345-346, 409-410, 464-465, 529-530

---

## 8. PORT NOTES (relational-access translation plan)

- **Five read cursors, one writer.** Implement each phase as: open an ordered forward cursor over the
  corresponding repository (`ORDER BY` the PK shown in §2), loop `ReadNext()` until exhausted ('10'),
  build the export record, append to the export writer. Preserve the read-ahead (prime-read then
  process-then-read) structure so the EOF record is not processed — this matches COBOL exactly.
- **EXPFILE is NOT a table.** Emit a 500-byte fixed-width record stream via the Runtime fixed-width
  serializer. The serializer must honor the per-type REDEFINES layout (header + active payload + space
  base-fill for the unused tail) and the COMP / COMP-3 encodings (`EXPORT-SEQUENCE-NUM` COMP fullword;
  the COMP/COMP-3 numeric payload subfields). This file is the golden-master diff target (mask the
  timestamp header bytes — offsets 1..26 — and possibly the sequence number if run order differs).
- **Timestamp.** Build `YYYY-MM-DD HH:MM:SS.00` from an injected clock (`IClock`) once at init; reuse
  the same value for every record in the run (COBOL captures it once in `1050-GENERATE-TIMESTAMP`).
  Hundredths are always `00`. The `STRING ... DELIMITED BY SIZE` concatenations are fixed-width and
  zero-pad-free; reproduce as straight string concatenation of the 2/4-digit zero-padded components.
- **INITIALIZE EXPORT-RECORD** = reset the 500-byte buffer to spaces (X base view) / zeros (numeric)
  before populating. Model as: allocate a 500-byte space-filled buffer, then write header + active
  payload fields at their fixed offsets.
- **OCCURS handling.** Customer addr-lines map 1→ADDR-LINE-1, 2→ADDR-LINE-2, 3→ADDR-LINE-3; phones
  1→PHONE-NUM-1, 2→PHONE-NUM-2. Straight positional MOVEs. // source: CBEXPORT.cbl:286-288, 292-293
- **Type mapping (numeric fields → record bytes).** Per ARCHITECTURE.md the relational input columns
  are typed (long/decimal/string); the export *file* re-encodes them. E.g. `CUST-ID` (input zoned
  9(9)) → `EXP-CUST-ID` COMP fullword; `ACCT-CURR-BAL` decimal → `EXP-ACCT-CURR-BAL` COMP-3 S9(10)V99;
  `TRAN-AMT` decimal → `EXP-TRAN-AMT` COMP-3 S9(9)V99; `EXP-CUST-SSN`/`EXP-ACCT-ID`/`EXP-XREF-CUST-ID`
  stay zoned DISPLAY. The serializer must apply the *target* PIC/USAGE from CVEXPORT, not the source's.
- **Signed-zoned / packed semantics.** S9(p)V99 COMP-3 packs `ceil((p+2+1)/2)` bytes with a sign
  nibble; COMP S9(10)V99 (e.g. `EXP-ACCT-CURR-CYC-DEBIT`) is binary scaled. Use the existing
  PackedDecimal / Binary codecs (Phase 1/3 work, tasks #3) for these; value truncation toward zero
  only matters if a source value exceeds the target precision (not expected for like-for-like fields).
- **Abend.** Map `9999-ABEND-PROGRAM` to the Runtime `Abend` helper (print banner, terminate
  abnormally). Any OPEN/READ/WRITE status check that fails triggers it.
- **Counters.** Maintain six `int`/`long` counters; final DISPLAY lines are part of the SYSOUT
  characterization (if SYSOUT is captured) — keep exact label text.

---

## 9. OPEN QUESTIONS / risks

1. **Output record ordering vs golden master.** The export record order = customer-order, then
   account-order, etc., and `EXPORT-SEQUENCE-NUM` encodes that order. If the SQLite cursors return
   rows in the same key order as the VSAM KSDS (they will, via `ORDER BY PK` with ordinal string
   collation for X keys / numeric for 9 keys), the sequence numbers and record order match the golden
   fixture deterministically. Confirm ordinal vs EBCDIC collation does not diverge for the 16-byte
   card/tran keys actually present (ARCHITECTURE.md §VSAM-semantics notes this is safe for the ASCII
   subset CardDemo uses).
2. **COMP/COMP-3 byte-exactness in EXPFILE.** The golden master must be produced from the same packed
   encoders; verify the Phase-1 PackedDecimal/Binary codecs already cover S9(10)V99 COMP, S9(9)V99
   COMP-3, 9(9) COMP, 9(3) COMP-3, 9(11) COMP for the exact byte widths CVEXPORT declares.
3. **Empty input files.** If any input table is empty, the priming READ immediately returns '10', the
   phase loop body never runs, and the count line shows 0. No records of that type are emitted. This is
   correct and must be preserved (no header/footer record per type).
4. **Timestamp masking in tests.** Bytes 1..26 (the EXPORT-TIMESTAMP header) and possibly the
   sequence-number bytes vary per run; the characterization harness should mask the timestamp region
   when diffing.
