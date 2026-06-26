# PORT SPEC — CBIMPORT (Branch Migration Import)

Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBIMPORT.cbl`
JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/CBIMPORT.jcl`
Copybooks: `CVEXPORT.cpy` (input layout), `CVCUS01Y.cpy`, `CVACT01Y.cpy`, `CVACT02Y.cpy`, `CVACT03Y.cpy`, `CVTRA05Y.cpy` (output layouts)
Program kind: **batch** (BATCH COBOL). No CICS, no BMS map, no screen, no COMMAREA.
Target spec consumer: C#/.NET 10 over relational SQLite per `_design/ARCHITECTURE.md`.

---

## 1. Purpose

CBIMPORT is the **branch-migration import** batch program. It reads a single multi-record-type
sequential export file (one logical "branch export" feed, 500-byte fixed records, with binary
COMP/COMP-3 numeric fields), classifies each input record by a one-byte record-type discriminator,
maps the type-specific export fields to the corresponding normalized target record layout, and
**writes (appends) one row per input record to the matching target file**: CUSTOMER, ACCOUNT,
CARD-XREF, TRANSACTION, or CARD. Unknown record types are counted and written to a pipe-delimited
error report file. The program keeps per-target import counters and prints an end-of-run summary.
There is no de-duplication, no key validation, no balancing/cross-checking — every recognized input
record produces exactly one output record (faithful, see §6). // source: CBIMPORT.cbl:5-12, 28-33

**Invocation:** JCL job `CBIMPORT`, single step `STEP01 EXEC PGM=CBIMPORT`. It is a standalone main
program (`GOBACK` at end of `0000-MAIN-PROCESSING`); it is not called as a subroutine and calls no
external CardDemo modules (only `CEE3ABD` for abend). // source: CBIMPORT.jcl:22; CBIMPORT.cbl:171,484

---

## 2. FILE / TABLE access

### 2.1 DD-to-file map (from JCL + SELECTs)

| Logical file (COBOL) | DDNAME | SELECT line | JCL DD | LRECL | Direction |
|---|---|---|---|---|---|
| EXPORT-INPUT | EXPFILE | :37 | yes (:28) DSN=...EXPORT.DATA | 500 | INPUT |
| CUSTOMER-OUTPUT | CUSTOUT | :43 | yes (:33) | 500 | OUTPUT |
| ACCOUNT-OUTPUT | ACCTOUT | :48 | yes (:38) | 300 | OUTPUT |
| XREF-OUTPUT | XREFOUT | :53 | yes (:43) | 50 | OUTPUT |
| TRANSACTION-OUTPUT | TRNXOUT | :58 | yes (:48) | 350 | OUTPUT |
| CARD-OUTPUT | CARDOUT | :63 | **NO JCL DD** | 150 | OUTPUT |
| ERROR-OUTPUT | ERROUT | :68 | yes (:56) | 132 | OUTPUT |

> **`CARDOUT` has no DD in the JCL** even though the program `SELECT`s it, `OPEN OUTPUT`s it (:233),
> and writes `'D'` records to it. See FAITHFUL BUG FB-1. // source: CBIMPORT.cbl:63-66,233-238; CBIMPORT.jcl:33-60

### 2.2 EXPORT-INPUT access semantics (note the unusual SELECT)

EXPORT-INPUT is declared `ORGANIZATION IS INDEXED`, `ACCESS MODE IS SEQUENTIAL`,
`RECORD KEY IS EXPORT-SEQUENCE-NUM`. // source: CBIMPORT.cbl:37-41
It is only ever `OPEN INPUT` (:198) and read with `READ EXPORT-INPUT INTO EXPORT-RECORD` (:261) in a
plain forward loop until EOF — i.e. it is used as a **sequential read of a keyed (KSDS) dataset in key
order**, never keyed/random reads, never STARTBR/READPREV. The `RECORD KEY` clause only governs the
ascending order records are returned in. // source: CBIMPORT.cbl:250-256,261

> The 88-level `WS-EXPORT-EOF VALUE '10'` is used as the loop terminator. For an indexed/sequential
> read at end of file, the returned file status is `'10'`. // source: CBIMPORT.cbl:118,252,261

### 2.3 Relational mapping (per ARCHITECTURE.md)

All "output" files in this program are **append-only inserts** into the canonical relational tables.
The program OPENs them `OUTPUT` (create/replace) and only ever `WRITE`s. There are no keyed reads,
rewrites, deletes, or browses on the outputs. Translation: each `WRITE xxx-RECORD` ⇒ INSERT one row
into the mapped table; OPEN OUTPUT ⇒ truncate/recreate the table for this run (faithful to
`DISP=(NEW,CATLG,DELETE)`).

| COBOL file | Relational table (ARCHITECTURE.md §"Base-app schema") | Operation in program | SQL |
|---|---|---|---|
| EXPORT-INPUT | *(no base table)* — a migration **import feed**, not a base file. Source by the EBCDIC import seeder / a staging table. | sequential READ NEXT in key order until EOF | `SELECT ... FROM export_input ORDER BY export_sequence_num` (forward cursor) |
| CUSTOMER-OUTPUT | **CUSTOMER** (PK cust_id 9(9)) | WRITE (append) | `INSERT INTO CUSTOMER (...) VALUES (...)` |
| ACCOUNT-OUTPUT | **ACCOUNT** (PK acct_id 9(11)) | WRITE (append) | `INSERT INTO ACCOUNT (...) VALUES (...)` |
| XREF-OUTPUT | **CARD_XREF** (PK xref_card_num X16) | WRITE (append) | `INSERT INTO CARD_XREF (...) VALUES (...)` |
| TRANSACTION-OUTPUT | **TRANSACTION** (PK tran_id X16) | WRITE (append) | `INSERT INTO TRANSACTION (...) VALUES (...)` |
| CARD-OUTPUT | **CARD** (PK card_num X16) | WRITE (append) | `INSERT INTO CARD (...) VALUES (...)` |
| ERROR-OUTPUT | *(no base table)* — flat report file, 132-byte pipe-delimited lines | WRITE | append to an error report sink (text rows) |

> The export feed is not one of the 11 base tables. Model it as an **import-staging source**
> (either a dedicated `EXPORT_INPUT` staging table populated by the EBCDIC import seeder, or an
> in-memory record stream produced by the Runtime fixed-width/EBCDIC decoder). It carries the binary
> COMP/COMP-3 fields, so its decode happens at the Runtime codec boundary, **not** as plain TEXT
> columns. See §7 PORT NOTES.

---

## 3. Record layouts

### 3.1 EXPORT-RECORD (input, 500 bytes) — `CVEXPORT.cpy`
Common header (all record types), then a `REDEFINES` overlay per type over the 460-byte data area.
// source: CVEXPORT.cpy:9-19

| Field | PIC | Offset (1-rel) | Notes |
|---|---|---|---|
| EXPORT-REC-TYPE | X(1) | 1 | discriminator: C / A / X / T / D |
| EXPORT-TIMESTAMP | X(26) | 2–27 | redefined as DATE X(10) + sep X(1) + TIME X(15) (:12-15) |
| EXPORT-SEQUENCE-NUM | 9(9) COMP | 28–31 | **binary fullword** (4 bytes), also the KSDS RECORD KEY |
| EXPORT-BRANCH-ID | X(4) | 32–35 | not used by program logic |
| EXPORT-REGION-CODE | X(5) | 36–40 | not used by program logic |
| EXPORT-RECORD-DATA | X(460) | 41–500 | overlaid by the 5 REDEFINES below |

**Note:** `EXPORT-SEQUENCE-NUM` is `9(9) COMP` (binary), but the header above it
(`EXPORT-REC-TYPE` 1 + `EXPORT-TIMESTAMP` 26 = 27 bytes) leaves it on an odd byte boundary at offset
28. The full common header is 1+26+4+4+5 = 40 bytes, so the data overlay starts at byte 41.
// source: CVEXPORT.cpy:10-19

#### EXPORT-CUSTOMER-DATA (REDEFINES, type 'C') // source: CVEXPORT.cpy:24-42
EXP-CUST-ID 9(09) **COMP**; FIRST/MIDDLE/LAST-NAME X(25) ×3; ADDR-LINE X(50) OCCURS 3;
ADDR-STATE-CD X(02); ADDR-COUNTRY-CD X(03); ADDR-ZIP X(10); PHONE-NUM X(15) OCCURS 2;
SSN 9(09) (display); GOVT-ISSUED-ID X(20); DOB-YYYY-MM-DD X(10); EFT-ACCOUNT-ID X(10);
PRI-CARD-HOLDER-IND X(01); FICO-CREDIT-SCORE 9(03) **COMP-3**; FILLER X(134).

#### EXPORT-ACCOUNT-DATA (REDEFINES, type 'A') // source: CVEXPORT.cpy:47-60
EXP-ACCT-ID 9(11) (display); ACTIVE-STATUS X(01); CURR-BAL S9(10)V99 **COMP-3**;
CREDIT-LIMIT S9(10)V99 (display); CASH-CREDIT-LIMIT S9(10)V99 **COMP-3**; OPEN-DATE X(10);
EXPIRAION-DATE X(10); REISSUE-DATE X(10); CURR-CYC-CREDIT S9(10)V99 (display);
CURR-CYC-DEBIT S9(10)V99 **COMP**; ADDR-ZIP X(10); GROUP-ID X(10); FILLER X(352).

> Mixed encodings within one record: CURR-BAL and CASH-CREDIT-LIMIT are COMP-3 (packed),
> CURR-CYC-DEBIT is COMP (binary), CREDIT-LIMIT and CURR-CYC-CREDIT are zoned-display. The Runtime
> decoder must honor each field's encoding individually. // source: CVEXPORT.cpy:50,52,56,57

#### EXPORT-TRANSACTION-DATA (REDEFINES, type 'T') // source: CVEXPORT.cpy:65-79
EXP-TRAN-ID X(16); TYPE-CD X(02); CAT-CD 9(04) (display); SOURCE X(10); DESC X(100);
AMT S9(09)V99 **COMP-3**; MERCHANT-ID 9(09) **COMP**; MERCHANT-NAME X(50); MERCHANT-CITY X(50);
MERCHANT-ZIP X(10); CARD-NUM X(16); ORIG-TS X(26); PROC-TS X(26); FILLER X(140).

#### EXPORT-CARD-XREF-DATA (REDEFINES, type 'X') // source: CVEXPORT.cpy:84-88
EXP-XREF-CARD-NUM X(16); CUST-ID 9(09) (display); ACCT-ID 9(11) **COMP**; FILLER X(427).

#### EXPORT-CARD-DATA (REDEFINES, type 'D') // source: CVEXPORT.cpy:93-100
EXP-CARD-NUM X(16); ACCT-ID 9(11) **COMP**; CVV-CD 9(03) **COMP**; EMBOSSED-NAME X(50);
EXPIRAION-DATE X(10); ACTIVE-STATUS X(01); FILLER X(373).

### 3.2 Output record layouts (target tables)
- CUSTOMER-RECORD 500B `CVCUS01Y.cpy`: CUST-ID 9(9), names X(25)×3, ADDR-LINE-1/2/3 X(50),
  STATE X(2), COUNTRY X(3), ZIP X(10), PHONE-1/2 X(15), SSN 9(9), GOVT-ID X(20), DOB X(10),
  EFT-ACCT-ID X(10), PRI-CARD-HOLDER-IND X(1), FICO 9(3), FILLER X(168). // source: CVCUS01Y.cpy:4-23
- ACCOUNT-RECORD 300B `CVACT01Y.cpy`: ACCT-ID 9(11), ACTIVE-STATUS X(1), CURR-BAL/CREDIT-LIMIT/
  CASH-CREDIT-LIMIT S9(10)V99, OPEN/EXPIRAION/REISSUE-DATE X(10), CURR-CYC-CREDIT/DEBIT S9(10)V99,
  ADDR-ZIP X(10), GROUP-ID X(10), FILLER X(178). // source: CVACT01Y.cpy:4-17
- CARD-XREF-RECORD 50B `CVACT03Y.cpy`: XREF-CARD-NUM X(16), XREF-CUST-ID 9(9), XREF-ACCT-ID 9(11),
  FILLER X(14). // source: CVACT03Y.cpy:4-8
- TRAN-RECORD 350B `CVTRA05Y.cpy`: TRAN-ID X(16), TYPE-CD X(2), CAT-CD 9(4), SOURCE X(10),
  DESC X(100), AMT S9(9)V99, MERCHANT-ID 9(9), MERCHANT-NAME X(50), MERCHANT-CITY X(50),
  MERCHANT-ZIP X(10), CARD-NUM X(16), ORIG-TS X(26), PROC-TS X(26), FILLER X(20).
  // source: CVTRA05Y.cpy:4-18
- CARD-RECORD 150B `CVACT02Y.cpy`: CARD-NUM X(16), ACCT-ID 9(11), CVV-CD 9(3), EMBOSSED-NAME X(50),
  EXPIRAION-DATE X(10), ACTIVE-STATUS X(1), FILLER X(59). // source: CVACT02Y.cpy:4-11

> All output records' numeric target fields are **zoned-display / signed-display** (no COMP/COMP-3 in
> the output copybooks). The MOVEs from COMP/COMP-3 export fields to display output fields are
> ordinary numeric MOVEs (value-preserving, with COBOL truncation/sign rules). In the relational
> model the column is typed (`long`/`int`/`decimal`) and these become straight value assignments.

---

## 4. PARAGRAPH-BY-PARAGRAPH outline (each = one C# method)

### 0000-MAIN-PROCESSING // source: CBIMPORT.cbl:165-171
PERFORM 1000-INITIALIZE → 2000-PROCESS-EXPORT-FILE → 3000-VALIDATE-IMPORT → 4000-FINALIZE → GOBACK.
Straight-line driver, no conditionals.

### 1000-INITIALIZE // source: CBIMPORT.cbl:174-193
1. DISPLAY start banner. (:176)
2. Build `WS-IMPORT-DATE` X(10) = `CCYY-MM-DD` from `FUNCTION CURRENT-DATE` substrings (1:4)/(5:2)/(7:2)
   with literal `'-'` separators placed at positions 5 and 8. (:178-182)
3. Build `WS-IMPORT-TIME` X(8) = `HH:MM:SS` from CURRENT-DATE (9:2)/(11:2)/(13:2) with `':'` at 3,6.
   (:184-188)
4. PERFORM 1100-OPEN-FILES. (:190)
5. DISPLAY import date and import time. (:192-193)
> `WS-IMPORT-DATE`/`WS-IMPORT-TIME` are computed and displayed but **never used** in any record write
> (cosmetic only). // source: CBIMPORT.cbl:135-136,178-193

### 1100-OPEN-FILES // source: CBIMPORT.cbl:196-245
OPEN INPUT EXPORT-INPUT, then OPEN OUTPUT for CUSTOMER, ACCOUNT, XREF, TRANSACTION, CARD, ERROR — in
that order. After each OPEN, test the corresponding `*-OK` 88 (value '00'); if not OK, DISPLAY a
file-specific `'ERROR: Cannot open <file>, Status: ' <status>` and PERFORM 9999-ABEND-PROGRAM.
// source: CBIMPORT.cbl:198-245
- C# mapping: "open input" = open the export source stream/staging query; "open output" = begin a
  fresh load of each target table (truncate/recreate for DISP=NEW). The CARDOUT open has no JCL DD
  (FB-1) — in .NET, this corresponds to opening the CARD target which exists in the schema, so it
  succeeds; preserve the abend-on-failure structure regardless.

### 2000-PROCESS-EXPORT-FILE // source: CBIMPORT.cbl:248-256
1. PERFORM 2100-READ-EXPORT-RECORD (priming read). (:250)
2. PERFORM UNTIL WS-EXPORT-EOF: ADD 1 TO WS-TOTAL-RECORDS-READ; PERFORM 2200-PROCESS-RECORD-BY-TYPE;
   PERFORM 2100-READ-EXPORT-RECORD. (:252-256)
> Standard priming-read loop. The counter increments **once per record actually read before EOF**,
> including unknown types. // source: CBIMPORT.cbl:253

### 2100-READ-EXPORT-RECORD // source: CBIMPORT.cbl:259-267
`READ EXPORT-INPUT INTO EXPORT-RECORD`. If status is NOT '00' AND NOT '10' (EOF) ⇒ DISPLAY
`'ERROR: Reading EXPORT-INPUT, Status: ' <status>` and PERFORM 9999-ABEND-PROGRAM.
- C# mapping: advance the forward cursor over the import source. End-of-stream sets the EOF flag
  ('10'); any other failure abends. // source: CBIMPORT.cbl:261-267

### 2200-PROCESS-RECORD-BY-TYPE // source: CBIMPORT.cbl:270-285
EVALUATE EXPORT-REC-TYPE:
- 'C' → 2300-PROCESS-CUSTOMER-RECORD
- 'A' → 2400-PROCESS-ACCOUNT-RECORD
- 'X' → 2500-PROCESS-XREF-RECORD
- 'T' → 2600-PROCESS-TRAN-RECORD
- 'D' → 2650-PROCESS-CARD-RECORD
- OTHER → 2700-PROCESS-UNKNOWN-RECORD
> Note: type `'D'` maps to the **CARD** record, not type `'C'` (which is CUSTOMER). The card
> discriminator is intentionally 'D', not 'K'/'C'. // source: CBIMPORT.cbl:272-285

### 2300-PROCESS-CUSTOMER-RECORD // source: CBIMPORT.cbl:288-320
1. INITIALIZE CUSTOMER-RECORD (sets numerics→0, alphanumerics→spaces). (:290)
2. 17 field MOVEs from `EXP-CUST-*` (incl. OCCURS: ADDR-LINE(1..3)→ADDR-LINE-1/2/3,
   PHONE-NUM(1..2)→PHONE-NUM-1/2) into CUSTOMER-RECORD fields. (:293-310)
3. WRITE CUSTOMER-RECORD; if NOT WS-CUSTOMER-OK ⇒ DISPLAY error + 9999-ABEND. (:312-318)
4. ADD 1 TO WS-CUSTOMER-RECORDS-IMPORTED. (:320)
- Numeric MOVEs of note: EXP-CUST-ID 9(9) COMP → CUST-ID 9(9); EXP-CUST-FICO 9(3) COMP-3 → FICO 9(3);
  EXP-CUST-SSN 9(9) → CUST-SSN 9(9). All same precision, value-preserving.

### 2400-PROCESS-ACCOUNT-RECORD // source: CBIMPORT.cbl:323-349
1. INITIALIZE ACCOUNT-RECORD. (:325)
2. 11 MOVEs `EXP-ACCT-*` → ACCT-* fields. (:328-339)
3. WRITE ACCOUNT-RECORD; if NOT WS-ACCOUNT-OK ⇒ DISPLAY + abend. (:341-347)
4. ADD 1 TO WS-ACCOUNT-RECORDS-IMPORTED. (:349)
- Arithmetic/precision notes: the monetary MOVEs are between identical `S9(10)V99` scales (display↔
  COMP-3↔COMP), so no scaling/truncation occurs; sign preserved. EXP-ACCT-ID 9(11)→ACCT-ID 9(11).
  // source: CVEXPORT.cpy:48-59 vs CVACT01Y.cpy:5-16

### 2500-PROCESS-XREF-RECORD // source: CBIMPORT.cbl:352-369
1. INITIALIZE **CARD-XREF-RECORD**. (:354)
2. MOVE EXP-XREF-CARD-NUM→XREF-CARD-NUM, EXP-XREF-CUST-ID→XREF-CUST-ID,
   EXP-XREF-ACCT-ID (COMP)→XREF-ACCT-ID. (:357-359)
3. WRITE CARD-XREF-RECORD; if NOT WS-XREF-OK ⇒ DISPLAY + abend. (:361-367)
4. ADD 1 TO WS-XREF-RECORDS-IMPORTED. (:369)

### 2600-PROCESS-TRAN-RECORD // source: CBIMPORT.cbl:372-399
1. INITIALIZE TRAN-RECORD. (:374)
2. 13 MOVEs `EXP-TRAN-*` → TRAN-* fields, incl. EXP-TRAN-AMT S9(9)V99 COMP-3 → TRAN-AMT S9(9)V99,
   EXP-TRAN-MERCHANT-ID 9(9) COMP → TRAN-MERCHANT-ID 9(9), EXP-TRAN-CAT-CD 9(4) → TRAN-CAT-CD 9(4).
   (:377-389)
3. WRITE TRAN-RECORD; if NOT WS-TRANSACTION-OK ⇒ DISPLAY + abend. (:391-397)
4. ADD 1 TO WS-TRAN-RECORDS-IMPORTED. (:399)

### 2650-PROCESS-CARD-RECORD // source: CBIMPORT.cbl:402-422
1. INITIALIZE CARD-RECORD. (:404)
2. 6 MOVEs `EXP-CARD-*` → CARD-* fields: NUM X(16), ACCT-ID 9(11) COMP, CVV-CD 9(3) COMP,
   EMBOSSED-NAME X(50), EXPIRAION-DATE X(10), ACTIVE-STATUS X(1). (:407-412)
3. WRITE CARD-RECORD; if NOT WS-CARD-OK ⇒ DISPLAY + abend. (:414-420)
4. ADD 1 TO WS-CARD-RECORDS-IMPORTED. (:422)

### 2700-PROCESS-UNKNOWN-RECORD // source: CBIMPORT.cbl:425-434
1. ADD 1 TO WS-UNKNOWN-RECORD-TYPE-COUNT. (:427)
2. Build WS-ERROR-RECORD: ERR-TIMESTAMP ← `FUNCTION CURRENT-DATE` (X(26)); ERR-RECORD-TYPE ←
   EXPORT-REC-TYPE; ERR-SEQUENCE ← EXPORT-SEQUENCE-NUM (9(9) COMP → 9(7) display, see FB-2);
   ERR-MESSAGE ← `'Unknown record type encountered'`. (:429-432)
3. PERFORM 2750-WRITE-ERROR. (:434)

### 2750-WRITE-ERROR // source: CBIMPORT.cbl:437-446
1. WRITE ERROR-OUTPUT-RECORD FROM WS-ERROR-RECORD. (:439)
2. If NOT WS-ERROR-OK ⇒ DISPLAY `'ERROR: Writing error record, Status: ' <status>` — **no abend**
   (write-error on the error file is logged but tolerated). (:441-444)
3. ADD 1 TO WS-ERROR-RECORDS-WRITTEN. (:446)

### 3000-VALIDATE-IMPORT // source: CBIMPORT.cbl:449-452
DISPLAY `'CBIMPORT: Import validation completed'` then `'CBIMPORT: No validation errors detected'`.
> **No actual validation is performed** — despite the program's stated "Validate data integrity using
> checksums" purpose, this paragraph only prints two static lines. See FB-3. // source: CBIMPORT.cbl:30,449-452

### 4000-FINALIZE // source: CBIMPORT.cbl:455-478
CLOSE all 7 files (export, customer, account, xref, transaction, card, error), then DISPLAY the
end-of-run summary: import completed banner + 7 counters (total read, customers, accounts, xrefs,
transactions, cards, errors written, unknown types). // source: CBIMPORT.cbl:457-478

### 9999-ABEND-PROGRAM // source: CBIMPORT.cbl:481-484
DISPLAY `'CBIMPORT: ABENDING PROGRAM'`, then `CALL 'CEE3ABD'` (LE forced abend). In .NET map to an
`Abend` raise (see Runtime `Abend`). // source: CBIMPORT.cbl:483-484

---

## 5. VALIDATION RULES and exact literal messages

There are **no input-data validation rules** in this program — every record of a recognized type is
written unconditionally. The only branching is record-type dispatch and file-status checks.

Exact literal strings (preserve verbatim, including trailing spaces in DISPLAY concatenations):
- `'CBIMPORT: Starting Customer Data Import'` (:176)
- `'CBIMPORT: Import Date: '` + WS-IMPORT-DATE (:192)
- `'CBIMPORT: Import Time: '` + WS-IMPORT-TIME (:193)
- `'ERROR: Cannot open EXPORT-INPUT, Status: '` + status (:200-201)
- `'ERROR: Cannot open CUSTOMER-OUTPUT, Status: '` + status (:207-208)
- `'ERROR: Cannot open ACCOUNT-OUTPUT, Status: '` + status (:214-215)
- `'ERROR: Cannot open XREF-OUTPUT, Status: '` + status (:221-222)
- `'ERROR: Cannot open TRANSACTION-OUTPUT, Status: '` + status (:228-229)
- `'ERROR: Cannot open CARD-OUTPUT, Status: '` + status (:235-236)
- `'ERROR: Cannot open ERROR-OUTPUT, Status: '` + status (:242-243)
- `'ERROR: Reading EXPORT-INPUT, Status: '` + status (:264-265)
- `'ERROR: Writing customer record, Status: '` + status (:315-316)
- `'ERROR: Writing account record, Status: '` + status (:344-345)
- `'ERROR: Writing xref record, Status: '` + status (:364-365)
- `'ERROR: Writing transaction record, Status: '` + status (:394-395)
- `'ERROR: Writing card record, Status: '` + status (:417-418)
- `'ERROR: Writing error record, Status: '` + status (:442-443)
- `'Unknown record type encountered'` — written to ERR-MESSAGE X(50) field (:432)
- `'CBIMPORT: Import validation completed'` (:451)
- `'CBIMPORT: No validation errors detected'` (:452)
- `'CBIMPORT: Import completed'` (:465)
- Summary counter lines (:466-478): `'CBIMPORT: Total Records Read: '`,
  `'CBIMPORT: Customers Imported: '`, `'CBIMPORT: Accounts Imported: '`, `'CBIMPORT: XRefs Imported: '`,
  `'CBIMPORT: Transactions Imported: '`, `'CBIMPORT: Cards Imported: '`, `'CBIMPORT: Errors Written: '`,
  `'CBIMPORT: Unknown Record Types: '`.
- `'CBIMPORT: ABENDING PROGRAM'` (:483).

### Error report line format (WS-ERROR-RECORD, 132 bytes) // source: CBIMPORT.cbl:152-160
`ERR-TIMESTAMP X(26)` `|` `ERR-RECORD-TYPE X(1)` `|` `ERR-SEQUENCE 9(7)` `|` `ERR-MESSAGE X(50)` then
`FILLER X(43) SPACES`. The `|` separators are literal pipe characters at fixed positions.

---

## 6. FAITHFUL BUGS to reproduce verbatim (do NOT fix)

- **FB-1 — CARDOUT has no JCL DD.** Program SELECTs `CARD-OUTPUT ASSIGN TO CARDOUT` (:63), OPENs it
  OUTPUT (:233), and writes 'D' records to it (:414), but `CBIMPORT.jcl` allocates no `CARDOUT` DD
  (only EXPFILE, CUSTOUT, ACCTOUT, XREFOUT, TRNXOUT, ERROUT). On a real run this either abends at OPEN
  (status ≠ '00' → DISPLAY 'ERROR: Cannot open CARD-OUTPUT' → CEE3ABD) or relies on a runtime default
  allocation. Reproduce the missing-DD condition in characterization; the in-spec target table CARD
  exists in the relational schema, so the .NET port's CARD insert succeeds — pin this divergence in a
  test note. // source: CBIMPORT.cbl:63,233-238,414; CBIMPORT.jcl:33-60

- **FB-2 — Sequence-number truncation in error report.** `EXPORT-SEQUENCE-NUM` is `9(9)` but
  `ERR-SEQUENCE` is `9(7)`. MOVE of a 9-digit value into a 7-digit field truncates the two
  high-order digits (right-justified numeric MOVE). Sequence numbers ≥ 10,000,000 are silently
  mis-reported in the error file. Do NOT widen. // source: CVEXPORT.cpy:16; CBIMPORT.cbl:157,431

- **FB-3 — "Validation" does nothing.** `3000-VALIDATE-IMPORT` performs zero checks (no checksum, no
  cross-file consistency) yet unconditionally prints "Import validation completed / No validation
  errors detected", contradicting the documented business case "Validate data integrity using
  checksums". Reproduce as-is (no validation logic). // source: CBIMPORT.cbl:30-31,449-452

- **FB-4 — Error-timestamp uses raw CURRENT-DATE into X(26).** `ERR-TIMESTAMP` X(26) receives
  `FUNCTION CURRENT-DATE` (which returns a 21-char `CCYYMMDDhhmmssnn±hhmm` structure). The MOVE
  pads/places the 21 chars left-justified into the 26-byte field (trailing 5 chars = spaces), giving
  a non-ISO timestamp string in the report (not the `WS-IMPORT-DATE`/`WS-IMPORT-TIME` format built in
  INITIALIZE). Reproduce verbatim. // source: CBIMPORT.cbl:429,153

- **FB-5 — No de-duplication / no key checks on output.** Outputs are OPEN OUTPUT + WRITE only;
  duplicate cust_id/acct_id/card_num/tran_id in the feed produce duplicate rows. In the relational
  port the PRIMARY KEY would reject duplicates with FileStatus '22'; CBIMPORT's COBOL sequential WRITE
  does **not** check keys (sequential PS/QSAM output), so it never sees '22'. The port must replicate
  COBOL's behavior: append rows without PK-uniqueness enforcement on this load path (or stage into a
  table without the unique constraint, then surface what COBOL would have done — see OPEN QUESTIONS).
  // source: CBIMPORT.cbl:43-66 (all OUTPUT SEQUENTIAL), 312,341,361,391,414

---

## 7. PORT NOTES (relational-access translation plan + COBOL semantics)

- **Input source = decode boundary.** EXPORT-INPUT records carry binary fields (`EXPORT-SEQUENCE-NUM`
  9(9) COMP; and per-type COMP/COMP-3 numerics). These are a *file-format* concern: decode with the
  Runtime `PackedDecimal`/binary codecs (per ARCHITECTURE.md "COMP-3/COMP = a file-format concern").
  Provide the import feed as a typed `ExportRecord` stream (one element per 500-byte image) rather
  than TEXT columns. The discriminator byte selects which overlay decoder to apply.
- **REDEFINES overlay (5 types over X(460)).** Implement as a tagged union / per-type decode chosen on
  `EXPORT-REC-TYPE`. Only one overlay is valid per record; never read a 'C' record's bytes through the
  'A' layout.
- **OCCURS unrolling.** `EXP-CUST-ADDR-LINE OCCURS 3` and `EXP-CUST-PHONE-NUM OCCURS 2` are flattened
  to ADDR-LINE-1/2/3 and PHONE-NUM-1/2 by index (1→_1, 2→_2, 3→_3). // source: CBIMPORT.cbl:297-304
- **INITIALIZE before each map.** Each 2300/2400/2500/2600/2650 starts with `INITIALIZE` of the target
  record (numerics→0, alphanumeric→spaces). In the typed/relational port, construct a fresh entity
  with default values (0 / "" padded to width) before assigning mapped fields, so unmapped/FILLER
  bytes serialize back as spaces and trailing fields are clean. // source: CBIMPORT.cbl:290,325,354,374,404
- **Numeric MOVEs are all equal-scale, value-preserving.** Every monetary/ID MOVE pairs identical
  PICs across COMP/COMP-3/display variants (e.g. S9(10)V99→S9(10)V99, 9(11)→9(11)). No rescale, no
  rounding. In .NET these are direct `decimal`/`long`/`int` assignments. Use `CobolDecimal`
  truncate-toward-zero semantics only if a future scale mismatch is introduced (none today).
- **Output = INSERT into mapped relational tables** (CUSTOMER, ACCOUNT, CARD_XREF, TRANSACTION, CARD).
  OPEN OUTPUT ⇒ fresh load (truncate/recreate per `DISP=(NEW,CATLG,DELETE)`); WRITE ⇒ INSERT. No
  reads/updates/deletes/browses. ERROR-OUTPUT ⇒ append text lines to an error report sink.
- **EXPORT-INPUT ordering.** Records are returned in ascending `EXPORT-SEQUENCE-NUM` (KSDS key order).
  If the import source is a staging table, `ORDER BY export_sequence_num`. If it is a raw fixed-width
  feed read sequentially, preserve file order (which the export step presumably wrote in key order).
- **Edited-numeric / signed-display:** output copybook numerics are zoned-display; when re-serializing
  rows back to fixed-width images for the verification harness, emit signed-display per Runtime rules.
- **Counters** are `9(9)` (max 999,999,999); model as `long`/`int` and print with leading zeros
  matching `9(9)` DISPLAY behavior in the summary lines (COBOL DISPLAY of an unedited 9(9) shows all 9
  digits, zero-padded). // source: CBIMPORT.cbl:140-147,466-478
- **Abend:** map `CALL 'CEE3ABD'` to the Runtime `Abend` mechanism (non-zero exit / exception), after
  the `'CBIMPORT: ABENDING PROGRAM'` line. // source: CBIMPORT.cbl:483-484

---

## 8. OPEN QUESTIONS / risks

1. **FB-1 resolution policy:** Should the .NET port (a) faithfully simulate the missing CARDOUT DD as
   an OPEN failure → abend, or (b) treat CARD as a valid relational target and insert rows? Recommend
   (b) for the relational model + a pinning note documenting the COBOL divergence, since the schema
   defines CARD. Needs sign-off vs the faithful-bug rule.
2. **PK uniqueness vs sequential append (FB-5):** The relational tables have PRIMARY KEYs that would
   reject duplicate feed records ('22'), but COBOL's sequential WRITE never checks. Decide whether the
   import load path stages into a non-unique table or whether duplicate-feed handling is out of scope
   for characterization (the standard CardDemo EXPORT.DATA feed is presumed unique).
3. **EXPORT.DATA source for tests:** confirm whether a captured EBCDIC `EXPORT.DATA` fixture exists
   (with binary COMP/COMP-3 fields) for the verification harness, or whether the import staging table
   must be synthesized. The COMP/COMP-3 decode path is the highest-risk area.
4. **ERR-SEQUENCE truncation (FB-2)** only manifests for seq ≥ 10,000,000 — confirm the test corpus
   exercises this boundary if FB-2 is to be pinned.
5. **`FUNCTION CURRENT-DATE` length into X(26) (FB-4):** confirm exact runtime-emitted format under
   the chosen Runtime clock so the error-report timestamp characterization is deterministic (mask in
   golden diffs like other timestamps).
