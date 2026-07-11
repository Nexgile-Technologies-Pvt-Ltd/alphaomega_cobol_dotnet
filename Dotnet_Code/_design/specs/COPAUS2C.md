# PORT SPEC — COPAUS2C (Mark Authorization Message Fraud, CICS + DB2 subprogram)

> Source program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/cbl/COPAUS2C.cbl`
> Copybooks: `CIPAUDTY.cpy` (pending-authorization detail layout), `AUTHFRDS` SQL include (`dcl/AUTHFRDS.dcl` DCLGEN), `SQLCA`.
> Target table: `AUTHFRDS` → C# entity **`AuthFraud`** (see `_design/specs/optional/DB2_SCHEMA.md` "Table 3 — AUTHFRDS", and `ARCHITECTURE.md` "Optional-module tables / AUTHFRDS").
> Alt access path: unique index `XAUTHFRD` on `(CARD_NUM ASC, AUTH_TS DESC)` (`ddl/XAUTHFRD.ddl`).
> Type hint: **ims** module, but COPAUS2C itself is a **CICS + DB2 called subprogram** — it has **no BMS map, no screen, no IMS DL/I calls**. It is a pure DB2 INSERT/UPDATE worker `LINK`ed from the online detail program.

---

## 1. Purpose & Invocation

COPAUS2C is a small CICS/DB2 subprogram whose single job is to **persist a fraud-marking action against
one pending-authorization record into the DB2 table `CARDDEMO.AUTHFRDS`**. It receives, through the
COMMAREA, the full pending-authorization detail record (the `CIPAUDTY` layout), the owning account id and
customer id, and a one-byte fraud action (`F` = report fraud / `R` = remove fraud). It derives the
`AUTH_TS` timestamp from the authorization key fields, maps every COMMAREA field onto the DCLGEN host
variables, and attempts an `INSERT` into `AUTHFRDS`. If the row already exists (DB2 duplicate-key SQLCODE
`-803`) it instead performs an `UPDATE` of just the fraud flag and report date. It writes the outcome
(success/failure flag + a human-readable message) back into the COMMAREA for the caller to display.
// source: COPAUS2C.cbl:2-6, 88-220, 221-244

**Invocation**
- **Not** a top-level transaction — there is **no `EXEC CICS RECEIVE`/`SEND`**, no TRANSID, no BMS map, no
  PFKey handling. The procedure division begins at `MAIN-PARA` and ends with a bare
  `EXEC CICS RETURN` (no `TRANSID`, no `COMMAREA` clause). // source: COPAUS2C.cbl:88-90, 218-220
- **Called via `EXEC CICS LINK`** from the online detail program **COPAUS1C**, in paragraph
  `MARK-AUTH-FRAUD`, as: `EXEC CICS LINK PROGRAM(WS-PGM-AUTH-FRAUD) COMMAREA(WS-FRAUD-DATA) NOHANDLE`
  where `WS-PGM-AUTH-FRAUD PIC X(08) VALUE 'COPAUS2C'`. // source: COPAUS1C.cbl:35, 248-252
- Defined in CICS CSD as `DEFINE PROGRAM(COPAUS2C) GROUP(CARDDEMO)`. // source: csd/CRDDEMO2.csd:32
- README role: "COPAUS2C | CICS | Fraud marking and DB2 update | (Called)". // source: README.md:209
- The caller's COMMAREA group `WS-FRAUD-DATA` is laid out: `WS-FRD-ACCT-ID 9(11)`, `WS-FRD-CUST-ID 9(9)`,
  `WS-FRAUD-AUTH-RECORD PIC X(200)` (the detail record image), then `WS-FRAUD-STATUS-RECORD`
  (`WS-FRD-ACTION X(1)`, `WS-FRD-UPDATE-STATUS X(1)`, `WS-FRD-ACT-MSG X(50)`). The callee redefines the
  same bytes with the typed `CIPAUDTY` layout for the auth record. // source: COPAUS1C.cbl:93-104, COPAUS2C.cbl:73-86

> NOTE — DB2 schema/qualifier: the program hard-codes `CARDDEMO.AUTHFRDS`; the README warns to update the
> qualifier per environment. The port uses the unqualified table name `AUTHFRDS` in the shared SQLite
> context. // source: COPAUS2C.cbl:142, 223; README.md:150

---

## 2. LINKAGE / COMMAREA layout (DFHCOMMAREA)

The callee's `01 DFHCOMMAREA` (the data it both reads and writes). Note the field names here **shadow**
WORKING-STORAGE / DCLGEN names of the same spelling in other modules; within COPAUS2C they are the COMMAREA
copies. // source: COPAUS2C.cbl:73-86

| COMMAREA field | PIC | Role in COPAUS2C | // source |
|---|---|---|---|
| `WS-ACCT-ID` | `9(11)` | input — owning account id → `ACCT-ID` host var | COPAUS2C.cbl:75, 138 |
| `WS-CUST-ID` | `9(9)` | input — owning customer id → `CUST-ID` host var | COPAUS2C.cbl:76, 139 |
| `WS-FRAUD-AUTH-RECORD` (`COPY CIPAUDTY`) | group, 200 bytes | input — pending-auth detail; all `PA-*` fields read from here | COPAUS2C.cbl:77-78, 101-136 |
| `WS-FRD-ACTION` | `X(1)`; 88 `WS-REPORT-FRAUD 'F'`, `WS-REMOVE-FRAUD 'R'` | input — fraud action → `AUTH-FRAUD` host var | COPAUS2C.cbl:80-82, 137 |
| `WS-FRD-UPDATE-STATUS` | `X(1)`; 88 `WS-FRD-UPDT-SUCCESS 'S'`, `WS-FRD-UPDT-FAILED 'F'` | **output** — set per SQL outcome | COPAUS2C.cbl:83-85, 200,206,231,234 |
| `WS-FRD-ACT-MSG` | `X(50)` | **output** — result/error text | COPAUS2C.cbl:86, 201,211-214,232,239-242 |

### 2a. `CIPAUDTY` pending-authorization detail (source of the `PA-*` inputs)
// source: CIPAUDTY.cpy:19-54

| Field | PIC | Notes |
|---|---|---|
| `PA-AUTHORIZATION-KEY` → `PA-AUTH-DATE-9C` | `S9(05) COMP-3` | packed 9's-complement date part of key |
| &nbsp;&nbsp; `PA-AUTH-TIME-9C` | `S9(09) COMP-3` | packed 9's-complement time part of key |
| `PA-AUTH-ORIG-DATE` | `X(06)` | YYMMDD (source for `WS-AUTH-YY/MM/DD`) |
| `PA-AUTH-ORIG-TIME` | `X(06)` | HHMMSS (not used by COPAUS2C) |
| `PA-CARD-NUM` | `X(16)` | → `CARD-NUM` |
| `PA-AUTH-TYPE` | `X(04)` | → `AUTH-TYPE` |
| `PA-CARD-EXPIRY-DATE` | `X(04)` | → `CARD-EXPIRY-DATE` |
| `PA-MESSAGE-TYPE` | `X(06)` | → `MESSAGE-TYPE` |
| `PA-MESSAGE-SOURCE` | `X(06)` | → `MESSAGE-SOURCE` |
| `PA-AUTH-ID-CODE` | `X(06)` | → `AUTH-ID-CODE` |
| `PA-AUTH-RESP-CODE` | `X(02)` (88 `PA-AUTH-APPROVED '00'`) | → `AUTH-RESP-CODE` |
| `PA-AUTH-RESP-REASON` | `X(04)` | → `AUTH-RESP-REASON` |
| `PA-PROCESSING-CODE` | `9(06)` | → `PROCESSING-CODE` (DCLGEN host var is `X(6)`) |
| `PA-TRANSACTION-AMT` | `S9(10)V99 COMP-3` | → `TRANSACTION-AMT` |
| `PA-APPROVED-AMT` | `S9(10)V99 COMP-3` | → `APPROVED-AMT` |
| `PA-MERCHANT-CATAGORY-CODE` | `X(04)` | → `MERCHANT-CATAGORY-CODE` (sic, "CATAGORY") |
| `PA-ACQR-COUNTRY-CODE` | `X(03)` | → `ACQR-COUNTRY-CODE` |
| `PA-POS-ENTRY-MODE` | `9(02)` | → `POS-ENTRY-MODE` (DCLGEN host var is `S9(4) COMP`/SMALLINT) |
| `PA-MERCHANT-ID` | `X(15)` | → `MERCHANT-ID` |
| `PA-MERCHANT-NAME` | `X(22)` | → `MERCHANT-NAME-TEXT` (VARCHAR body) |
| `PA-MERCHANT-CITY` | `X(13)` | → `MERCHANT-CITY` |
| `PA-MERCHANT-STATE` | `X(02)` | → `MERCHANT-STATE` |
| `PA-MERCHANT-ZIP` | `X(09)` | → `MERCHANT-ZIP` |
| `PA-TRANSACTION-ID` | `X(15)` | → `TRANSACTION-ID` |
| `PA-MATCH-STATUS` | `X(01)` (88 P/D/E/M) | → `MATCH-STATUS` |
| `PA-AUTH-FRAUD` | `X(01)` (88 `PA-FRAUD-CONFIRMED 'F'`, `PA-FRAUD-REMOVED 'R'`) | **not** copied to host var; `AUTH-FRAUD` host var comes from `WS-FRD-ACTION` instead |
| `PA-FRAUD-RPT-DATE` | `X(08)` | set by caller from `WS-CUR-DATE`; **not** used in SQL (SQL uses `CURRENT DATE`) |
| `FILLER` | `X(17)` | padding to 200 |

> NOTE: COPAUS2C writes `WS-CUR-DATE` into `PA-FRAUD-RPT-DATE` (COPAUS2C.cbl:101) but the INSERT/UPDATE
> SQL uses `CURRENT DATE` for the `FRAUD_RPT_DATE` column, **not** the host var. The `PA-FRAUD-RPT-DATE`
> mutation is effectively dead for the DB2 row, but it does mutate the COMMAREA detail image that flows
> back to the caller (COPAUS1C then copies it into its IMS segment). Reproduce both writes faithfully.
> // source: COPAUS2C.cbl:101, 166, 194, 224-225; COPAUS1C.cbl:244, 522-528

---

## 3. FILE / TABLE access

COPAUS2C touches **exactly one** table via embedded SQL (DB2). No VSAM, no IMS DL/I, no sequential files.

| Table | Operation | DB2 statement (lines) | Relational target (SQLite/EF) |
|---|---|---|---|
| `CARDDEMO.AUTHFRDS` | **INSERT** (26 columns) | `INSERT INTO CARDDEMO.AUTHFRDS (...) VALUES (...)` | `INSERT INTO AUTHFRDS (...) VALUES (...)`; PK `(CARD_NUM, AUTH_TS)` // source: COPAUS2C.cbl:141-198 |
| `CARDDEMO.AUTHFRDS` | **UPDATE** (on dup key) | `UPDATE CARDDEMO.AUTHFRDS SET AUTH_FRAUD=:AUTH-FRAUD, FRAUD_RPT_DATE=CURRENT DATE WHERE CARD_NUM=:CARD-NUM AND AUTH_TS=TIMESTAMP_FORMAT(:AUTH-TS,...)` | `UPDATE AUTHFRDS SET AUTH_FRAUD=@authFraud, FRAUD_RPT_DATE=@today WHERE CARD_NUM=@cardNum AND AUTH_TS=@authTs` // source: COPAUS2C.cbl:222-229 |

### 3a. INSERT column → value map (exact, in source order)
// source: COPAUS2C.cbl:142-197

| Column | VALUES expression | Host var origin |
|---|---|---|
| `CARD_NUM` | `:CARD-NUM` | `PA-CARD-NUM` (line 113) |
| `AUTH_TS` | `TIMESTAMP_FORMAT(:AUTH-TS, 'YY-MM-DD HH24.MI.SSNNNNNN')` | `WS-AUTH-TS` (lines 38-51, 114) |
| `AUTH_TYPE` | `:AUTH-TYPE` | `PA-AUTH-TYPE` (115) |
| `CARD_EXPIRY_DATE` | `:CARD-EXPIRY-DATE` | `PA-CARD-EXPIRY-DATE` (116) |
| `MESSAGE_TYPE` | `:MESSAGE-TYPE` | `PA-MESSAGE-TYPE` (117) |
| `MESSAGE_SOURCE` | `:MESSAGE-SOURCE` | `PA-MESSAGE-SOURCE` (118) |
| `AUTH_ID_CODE` | `:AUTH-ID-CODE` | `PA-AUTH-ID-CODE` (119) |
| `AUTH_RESP_CODE` | `:AUTH-RESP-CODE` | `PA-AUTH-RESP-CODE` (120) |
| `AUTH_RESP_REASON` | `:AUTH-RESP-REASON` | `PA-AUTH-RESP-REASON` (121) |
| `PROCESSING_CODE` | `:PROCESSING-CODE` | `PA-PROCESSING-CODE` (122) |
| `TRANSACTION_AMT` | `:TRANSACTION-AMT` | `PA-TRANSACTION-AMT` (123) |
| `APPROVED_AMT` | `:APPROVED-AMT` | `PA-APPROVED-AMT` (124) |
| `MERCHANT_CATAGORY_CODE` | `:MERCHANT-CATAGORY-CODE` | `PA-MERCHANT-CATAGORY-CODE` (125-126) |
| `ACQR_COUNTRY_CODE` | `:ACQR-COUNTRY-CODE` | `PA-ACQR-COUNTRY-CODE` (127) |
| `POS_ENTRY_MODE` | `:POS-ENTRY-MODE` | `PA-POS-ENTRY-MODE` (128) |
| `MERCHANT_ID` | `:MERCHANT-ID` | `PA-MERCHANT-ID` (129) |
| `MERCHANT_NAME` | `:MERCHANT-NAME` (VARCHAR host group: len+text) | `MERCHANT-NAME-LEN`=`LENGTH OF PA-MERCHANT-NAME`(=22), `MERCHANT-NAME-TEXT`=`PA-MERCHANT-NAME` (130-131) |
| `MERCHANT_CITY` | `:MERCHANT-CITY` | `PA-MERCHANT-CITY` (132) |
| `MERCHANT_STATE` | `:MERCHANT-STATE` | `PA-MERCHANT-STATE` (133) |
| `MERCHANT_ZIP` | `:MERCHANT-ZIP` | `PA-MERCHANT-ZIP` (134) |
| `TRANSACTION_ID` | `:TRANSACTION-ID` | `PA-TRANSACTION-ID` (135) |
| `MATCH_STATUS` | `:MATCH-STATUS` | `PA-MATCH-STATUS` (136) |
| `AUTH_FRAUD` | `:AUTH-FRAUD` | `WS-FRD-ACTION` (137) — **the COMMAREA action, not `PA-AUTH-FRAUD`** |
| `FRAUD_RPT_DATE` | `CURRENT DATE` | DB server date (194) |
| `ACCT_ID` | `:ACCT-ID` | `WS-ACCT-ID` (138) |
| `CUST_ID` | `:CUST-ID` | `WS-CUST-ID` (139) |

DCLGEN host-variable types for the table columns (`AUTHFRDS.dcl`): all `X(n)` except
`TRANSACTION-AMT`/`APPROVED-AMT` `S9(10)V99 COMP-3`, `POS-ENTRY-MODE` `S9(4) COMP`,
`MERCHANT-NAME` VARCHAR (`MERCHANT-NAME-LEN S9(4) COMP` + `MERCHANT-NAME-TEXT X(22)`),
`AUTH-TS X(26)`, `ACCT-ID S9(11) COMP-3`, `CUST-ID S9(9) COMP-3`. // source: dcl/AUTHFRDS.dcl:55-86

### 3b. Table DDL (authoritative)
PK `(CARD_NUM, AUTH_TS)`; columns per `ddl/AUTHFRDS.ddl:1-28` / `dcl/AUTHFRDS.dcl:24-51`. DB2 native types:
`CARD_NUM CHAR(16) NOT NULL`, `AUTH_TS TIMESTAMP NOT NULL`, amounts `DECIMAL(12,2)`,
`POS_ENTRY_MODE SMALLINT`, `MERCHANT_NAME VARCHAR(22)`, `FRAUD_RPT_DATE DATE`, `ACCT_ID DECIMAL(11,0)`,
`CUST_ID DECIMAL(9,0)`, rest `CHAR(n)`. Unique alt index `XAUTHFRD (CARD_NUM ASC, AUTH_TS DESC)`.
// source: ddl/AUTHFRDS.ddl, ddl/XAUTHFRD.ddl. Relational mapping per `DB2_SCHEMA.md:96-142`.

---

## 4. PROCEDURE DIVISION — paragraph by paragraph

Two paragraphs only. `MAIN-PARA` is the LINK entry; `FRAUD-UPDATE` is performed on duplicate key.

### `MAIN-PARA` → method `Run()` (entry point) // source: COPAUS2C.cbl:89-220
1. `EXEC CICS ASKTIME ABSTIME(WS-ABS-TIME) NOHANDLE` then `EXEC CICS FORMATTIME ABSTIME(WS-ABS-TIME) MMDDYY(WS-CUR-DATE) DATESEP NOHANDLE` → `WS-CUR-DATE` = current date `MM/DD/YY` with separators. // source: 91-100
2. `MOVE WS-CUR-DATE TO PA-FRAUD-RPT-DATE` — stamps report date into the COMMAREA detail image (8 bytes; `WS-CUR-DATE` is `X(08)` `MM/DD/YY`). // source: 101
3. Build the `AUTH_TS` timestamp from the auth key fields:
   - `MOVE PA-AUTH-ORIG-DATE(1:2) → WS-AUTH-YY`, `(3:2) → WS-AUTH-MM`, `(5:2) → WS-AUTH-DD` (YYMMDD slices). // source: 103-105
   - `COMPUTE WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C` — un-complements the 9's-complement packed time into a 9-digit `9(09)`. `WS-AUTH-TIME` is unsigned `9(09)`; result truncates to 9 digits (silent overflow drops high digits — see Faithful Bugs). // source: 35-37, 107
   - Redefine `WS-AUTH-TIME-AN X(09)` and slice: `(1:2)→WS-AUTH-HH`, `(3:2)→WS-AUTH-MI`, `(5:2)→WS-AUTH-SS`, `(7:3)→WS-AUTH-SSS`. // source: 36-37, 108-111
   - `WS-AUTH-TS` is the formatted group `YY-MM-DD HH.MI.SS SSS 000` (FILLERs supply `-`,`-`,space,`.`,`.`, and a trailing `'000'` for nanos positions), 26 chars to feed `TIMESTAMP_FORMAT` mask `'YY-MM-DD HH24.MI.SSNNNNNN'`. // source: 38-51, 114, 171-172
4. Move every `PA-*` COMMAREA field into the DCLGEN host variables (lines 113-139, see §3a map). For the VARCHAR merchant name: `MOVE LENGTH OF PA-MERCHANT-NAME TO MERCHANT-NAME-LEN` (length literal 22) then `MOVE PA-MERCHANT-NAME TO MERCHANT-NAME-TEXT`. `AUTH-FRAUD` is loaded from `WS-FRD-ACTION` (not `PA-AUTH-FRAUD`); `ACCT-ID`/`CUST-ID` from `WS-ACCT-ID`/`WS-CUST-ID`. // source: 113-139
5. `EXEC SQL INSERT INTO CARDDEMO.AUTHFRDS (...) VALUES (...)` — full 26-column insert. // source: 141-198
6. Branch on `SQLCODE`:
   - `= ZERO` → `SET WS-FRD-UPDT-SUCCESS TO TRUE` (`'S'`), `MOVE 'ADD SUCCESS' TO WS-FRD-ACT-MSG`. // source: 199-201
   - `= -803` (duplicate key) → `PERFORM FRAUD-UPDATE`. // source: 203-204
   - else → `SET WS-FRD-UPDT-FAILED TO TRUE` (`'F'`); `MOVE SQLCODE TO WS-SQLCODE`, `MOVE SQLSTATE TO WS-SQLSTATE`; `STRING ' SYSTEM ERROR DB2: CODE:' WS-SQLCODE ', STATE: ' WS-SQLSTATE DELIMITED BY SIZE INTO WS-FRD-ACT-MSG`. // source: 205-216
7. `EXEC CICS RETURN` (bare — control returns to the LINKing program; **no SYNCPOINT here**, the caller owns the unit of work / rollback). // source: 218-220

### `FRAUD-UPDATE` → method `FraudUpdate()` // source: COPAUS2C.cbl:221-244
1. `EXEC SQL UPDATE CARDDEMO.AUTHFRDS SET AUTH_FRAUD=:AUTH-FRAUD, FRAUD_RPT_DATE=CURRENT DATE WHERE CARD_NUM=:CARD-NUM AND AUTH_TS=TIMESTAMP_FORMAT(:AUTH-TS,'YY-MM-DD HH24.MI.SSNNNNNN')`. Updates only the fraud flag + report date on the existing PK row. // source: 222-229
2. Branch on `SQLCODE`:
   - `= ZERO` → `SET WS-FRD-UPDT-SUCCESS TO TRUE` (`'S'`), `MOVE 'UPDT SUCCESS' TO WS-FRD-ACT-MSG`. // source: 230-232
   - else → `SET WS-FRD-UPDT-FAILED TO TRUE` (`'F'`); `MOVE SQLCODE TO WS-SQLCODE`, `MOVE SQLSTATE TO WS-SQLSTATE`; `STRING ' UPDT ERROR DB2: CODE:' WS-SQLCODE ', STATE: ' WS-SQLSTATE DELIMITED BY SIZE INTO WS-FRD-ACT-MSG`. // source: 233-243

Control flow summary: `MARK-AUTH-FRAUD` (COPAUS1C) → `LINK COPAUS2C` → `MAIN-PARA` → INSERT;
on `-803` → `FRAUD-UPDATE` → UPDATE; always `RETURN`. Caller inspects `WS-FRD-UPDATE-STATUS`
(`S`/`F`) and `WS-FRD-ACT-MSG`. // source: COPAUS1C.cbl:248-262, COPAUS2C.cbl:199-216, 221-244

---

## 5. Timestamp / numeric semantics (load-bearing)

- **AUTH_TS construction** (lines 103-114): the DB2 row's primary-key timestamp is **not** taken directly
  from a field; it is reconstructed as `YY-MM-DD HH.MI.SS<sss>000` where:
  - `YY/MM/DD` = first/second/third 2-char slices of `PA-AUTH-ORIG-DATE` (a YYMMDD `X(6)`).
  - `HH/MI/SS/SSS` = slices 1-2 / 3-4 / 5-6 / 7-9 of `WS-AUTH-TIME-AN`, where
    `WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C` (9's-complement decode of the packed key time).
  - Nanos positions 7-12 of the mask (`NNNNNN`) are fed `WS-AUTH-SSS` (3 digits of milliseconds) + a
    literal `'000'` FILLER → `<sss>000`. // source: 50-51, 111
- **`COMPUTE WS-AUTH-TIME = 999999999 - PA-AUTH-TIME-9C`** — `WS-AUTH-TIME` is **unsigned** `PIC 9(09)`;
  the subtraction result is stored into a 9-digit unsigned field with truncation toward zero / sign
  dropped. Port with COBOL-decimal truncation semantics (no rounding). `PA-AUTH-TIME-9C` is `S9(09) COMP-3`.
  // source: 35-37, 21(CIPAUDTY), 107
- **`MERCHANT-NAME` is a DB2 VARCHAR(22)** carried as a host group `len(S9(4) COMP) + text(X(22))`; the
  program always sets `len = LENGTH OF PA-MERCHANT-NAME` (a compile-time constant **22**), so the stored
  VARCHAR always has length 22 (trailing spaces included). Port: store the 22-char merchant name verbatim
  (no trimming). // source: 130-131, dcl/AUTHFRDS.dcl:73-77
- **`FRAUD_RPT_DATE` = `CURRENT DATE`** (server date) in both INSERT and UPDATE — use `IClock.Today`
  formatted as a DB2 DATE (`YYYY-MM-DD`), NOT the `WS-CUR-DATE`/`PA-FRAUD-RPT-DATE` `MM/DD/YY` value.
  // source: 194, 224-225
- **`WS-SQLCODE PIC +9(06)`, `WS-SQLSTATE PIC +9(09)`** — signed numeric-edited; `MOVE SQLCODE` /
  `MOVE SQLSTATE` into them produces a leading `+`/`-` and zero-fill (note SQLSTATE is normally a 5-char
  alphanumeric in standards, but here it is moved to a numeric-edited field — reproduce the edited-numeric
  formatting, see Faithful Bugs). // source: 55-56, 208-209, 236-237

---

## 6. VALIDATION RULES & exact literal messages

COPAUS2C performs **no input validation** — it trusts the COMMAREA and only branches on SQLCODE. The exact
output strings written to `WS-FRD-ACT-MSG` (the only "messages"):

| Condition | Exact literal | // source |
|---|---|---|
| INSERT SQLCODE = 0 | `ADD SUCCESS` | COPAUS2C.cbl:201 |
| INSERT SQLCODE other than 0/-803 | `' SYSTEM ERROR DB2: CODE:' <WS-SQLCODE> ', STATE: ' <WS-SQLSTATE>` (concatenated DELIMITED BY SIZE) | COPAUS2C.cbl:211-214 |
| UPDATE SQLCODE = 0 | `UPDT SUCCESS` | COPAUS2C.cbl:232 |
| UPDATE SQLCODE != 0 | `' UPDT ERROR DB2: CODE:' <WS-SQLCODE> ', STATE: ' <WS-SQLSTATE>` | COPAUS2C.cbl:239-242 |

Note the leading space in both error literals and that the success literals have **no** leading space.
`WS-FRD-ACT-MSG` is `X(50)`; the STRING with `DELIMITED BY SIZE` writes
`' SYSTEM ERROR DB2: CODE:'`(24) + `WS-SQLCODE`(7) + `', STATE: '`(9) + `WS-SQLSTATE`(10) = 50 chars exactly,
filling the field. The UPDT error literal is `' UPDT ERROR DB2: CODE:'`(22)+7+9+10 = 48, leaving 2 trailing
chars unchanged. // source: 86, 208-214, 236-242

The caller maps `WS-FRD-UPDATE-STATUS` to its own screen messages (`AUTH MARKED FRAUD...` /
`AUTH FRAUD REMOVED...` on success, or echoes `WS-FRD-ACT-MSG` on failure) — that logic lives in COPAUS1C,
not here. // source: COPAUS1C.cbl:253-262, 534-538

---

## 7. FAITHFUL BUGS (reproduce verbatim; do NOT fix)

1. **9's-complement time decode truncates into unsigned 9(09).** `COMPUTE WS-AUTH-TIME = 999999999 -
   PA-AUTH-TIME-9C` stores into `WS-AUTH-TIME PIC 9(09)` (unsigned, 9 digits). `PA-AUTH-TIME-9C` is
   `S9(09) COMP-3`; if it is ever negative or the subtraction yields a value outside `0..999999999`, the
   result silently truncates high-order digits / drops the sign with no ON SIZE ERROR. Reproduce with
   CobolDecimal truncate-toward-zero + silent overflow. // source: COPAUS2C.cbl:35-37, 107
2. **`SQLSTATE` moved into a numeric-edited field.** `MOVE SQLSTATE TO WS-SQLSTATE` where `WS-SQLSTATE PIC
   +9(09)`. SQLSTATE in the SQLCA is a 5-character class/subclass code (e.g. `'02000'`, can contain
   letters). Moving a (possibly alphanumeric) SQLSTATE into a signed numeric-edited PIC is technically
   ill-typed; the program treats SQLSTATE as numeric. Reproduce the edited-numeric conversion exactly as
   COBOL would render it (leading sign + 9-digit zero-fill of the numeric interpretation), even though it
   loses any alphabetic SQLSTATE content. // source: COPAUS2C.cbl:56, 209, 237
3. **Dead `PA-FRAUD-RPT-DATE` for the DB2 row.** `MOVE WS-CUR-DATE TO PA-FRAUD-RPT-DATE` (line 101) is
   never used as a SQL host variable — the SQL writes `FRAUD_RPT_DATE = CURRENT DATE`. Keep the COMMAREA
   mutation (it flows back to the caller) but the DB2 column always reflects server date, not `WS-CUR-DATE`.
   // source: COPAUS2C.cbl:101, 194, 224-225
4. **`AUTH_FRAUD` sourced from `WS-FRD-ACTION`, not `PA-AUTH-FRAUD`.** The stored fraud flag is the action
   code passed in the status record (`F`/`R`), even though the detail record carries its own
   `PA-AUTH-FRAUD`. Not a defect per se, but a non-obvious mapping to preserve. // source: COPAUS2C.cbl:80-82, 137, 165, 193
5. **"CATAGORY" misspelling** is part of the contract: column `MERCHANT_CATAGORY_CODE` and field
   `PA-MERCHANT-CATAGORY-CODE`/`MERCHANT-CATAGORY-CODE`. Keep the misspelling in the entity/column name.
   // source: COPAUS2C.cbl:125-126, 155, 183; CIPAUDTY.cpy:36; dcl/AUTHFRDS.dcl:37,68
6. **No SYNCPOINT/COMMIT in COPAUS2C.** The bare `EXEC CICS RETURN` returns to the LINKing program leaving
   the DB2 change uncommitted; the caller (COPAUS1C) decides COMMIT (`EXEC CICS SYNCPOINT`) vs ROLLBACK.
   The port must NOT auto-commit inside COPAUS2C — commit/rollback is the caller's responsibility (the
   `AuthFraud` write participates in the caller's transaction). // source: COPAUS2C.cbl:218-220; COPAUS1C.cbl:253-262

---

## 8. PORT NOTES (relational-access translation plan)

- Implement as a C# class `Copaus2c` (in `CardDemo.Db2` or `CardDemo.Online` shim) with method
  `Run(AuthFraudCommarea ca)` taking a COMMAREA DTO mirroring §2 (acct id, cust id, the `CIPAUDTY` detail
  record, action `F`/`R`, plus out fields update-status + message). Returns by mutating the DTO (LINK
  COMMAREA semantics). No screen, no dispatcher entry.
- **`AuthFraud` entity** = the `AUTHFRDS` table (DB2_SCHEMA.md). PK `(CardNum, AuthTs)`. Repository ops:
  - INSERT → `repository.Add(authFraud)` then `SaveChanges`/equivalent; map duplicate-key to **SQLCODE
    -803** equivalent (SQLite `UNIQUE constraint`/`PRIMARY KEY` violation → trigger the update path).
    Any other failure → failure flag + system-error message.
  - On -803 → `UPDATE AUTHFRDS SET AUTH_FRAUD=@a, FRAUD_RPT_DATE=@today WHERE CARD_NUM=@c AND AUTH_TS=@ts`.
    Missing row on UPDATE → SQLCODE != 0 → failure message (matches COBOL else branch).
- **AUTH_TS / TIMESTAMP_FORMAT:** the value bound is the 26-char string `YY-MM-DD HH.MI.SS<sss>000`. The
  DB2 `TIMESTAMP_FORMAT(...,'YY-MM-DD HH24.MI.SSNNNNNN')` parses it into a TIMESTAMP. For SQLite store
  `AUTH_TS` as TEXT; build the canonical timestamp string from the parsed parts so INSERT and the UPDATE
  `WHERE AUTH_TS = ...` produce the **identical** string (critical: the PK lookup must match byte-for-byte
  what the INSERT stored). Decide a single canonical TEXT form for `AUTH_TS` and use it in both paths
  (e.g. `20YY-MM-DD HH:MM:SS.ssssss` or keep the literal `YY-MM-DD HH.MI.SS<sss>000`); pin with a test.
  Watch the 2-digit year (`YY`, no century) — replicate DB2's `TIMESTAMP_FORMAT` century pivot or keep the
  raw 2-digit string consistently on both sides.
- **`CURRENT DATE`** → `IClock.Today` rendered as the DB2 DATE TEXT form used elsewhere (e.g. `YYYY-MM-DD`);
  apply identically in INSERT and UPDATE.
- **Numeric host vars:** `TRANSACTION_AMT`/`APPROVED_AMT` = `decimal` (DECIMAL(12,2) col, COMP-3 host
  S9(10)V99). `POS_ENTRY_MODE` = SMALLINT from a `9(02)` source → small int. `ACCT_ID`/`CUST_ID` =
  `long`/`decimal(11/9,0)`. Truncate toward zero, never round.
- **VARCHAR merchant name:** persist exactly 22 chars (with trailing spaces) since `len` is always 22.
- **`STRING ... DELIMITED BY SIZE` error messages:** build by fixed-width concatenation of the literal +
  `CobolEditedNumeric` rendering of `WS-SQLCODE (+9(06))` and `WS-SQLSTATE (+9(09))`, padded/truncated into
  a 50-char field. Reproduce the leading `+`/`-` sign character and zero fill.
- **No COMMIT inside the method** — return without committing; the caller's transaction scope decides
  (see Faithful Bug #6). In the .NET online shim, the LINK target shares the caller's DbContext/transaction.
- **REDEFINES:** `WS-AUTH-TIME-AN` redefines `WS-AUTH-TIME` (numeric ↔ X(9) view) — model as reading the
  9-digit zero-padded string of the computed time. `WS-AUTH-TIME-AN` requires the time formatted to exactly
  9 digits with leading zeros before slicing.

---

## 9. OPEN QUESTIONS / RISKS

1. **2-digit year in AUTH_TS PK.** `WS-AUTH-YY` is the YY from `PA-AUTH-ORIG-DATE`; the timestamp mask is
   `YY-MM-DD` (no century). DB2 `TIMESTAMP_FORMAT` applies a century window. The port must choose a single
   canonical century-resolution rule and apply it consistently so INSERT and UPDATE PK keys match. Risk:
   year-2000 boundary mismatch between insert/update if rule differs. Needs a pinning test.
2. **SQLite has no native `-803`/`SQLCODE`/`SQLSTATE`.** The duplicate-key detection and the
   error-message rendering must be synthesized from the SQLite exception (constraint violation → -803;
   other → map an SQLCODE/SQLSTATE pair) to keep `WS-FRD-ACT-MSG` text plausible. Exact code/state values
   on the unhappy path are unverifiable against a DB2 oracle — characterization test only (per
   ARCHITECTURE.md verification §4).
3. **SQLSTATE→numeric coercion (Faithful Bug #2)** behavior for alphabetic SQLSTATE codes is
   implementation-defined; pin to whatever the chosen CobolDecimal/edited-numeric path yields and document.
4. **`PA-AUTH-TIME-9C` sign/range** feeding the unsigned `WS-AUTH-TIME` (Faithful Bug #1) — confirm sample
   data never relies on the truncation, but reproduce the truncation regardless.
