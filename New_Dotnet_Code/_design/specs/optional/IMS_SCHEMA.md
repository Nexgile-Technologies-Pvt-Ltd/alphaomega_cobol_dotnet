# IMS_SCHEMA — Relational Re-host of the CardDemo Authorization IMS DB

Source root: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/`

This spec maps the **IMS hierarchical (HIDAM) "Pending Authorization" database** used by the
CardDemo authorization module onto a **relational (SQLite/SQL) schema**, and maps every DL/I
call pattern issued by the COBOL programs (`COPAUS0C`, `COPAUS1C`, `CBPAUP0C`, `COPAUA0C`, plus
the load/unload utilities `PAUDBLOD`, `PAUDBUNL`, `DBUNLDGS`) onto the SQL statement(s) that
reproduce the same behavior. No C# is written here — this is design only.

> Scope note: this covers the **IMS DL/I** pieces only. The DB2 fraud table `AUTHFRDS`
> (`ddl/AUTHFRDS.ddl`, written by `COPAUS2C`) is already relational and is documented at the end
> for completeness / FK context, but is not an IMS segment.

---

## 1. IMS physical structure (what we are re-hosting)

### DBDs

| DBD | File | ACCESS | Role |
|-----|------|--------|------|
| `DBPAUTP0` | `ims/DBPAUTP0.dbd` | `(HIDAM,VSAM)` | Primary database holding the two data segments. |
| `DBPAUTX0` | `ims/DBPAUTX0.dbd` | `(INDEX,VSAM,PROT)` | HIDAM **primary index** over the root key `ACCNTID`. Pure access path — NOT a data table. |
| `PASFLDBD` | `ims/PASFLDBD.DBD` | `(GSAM,BSAM)` | GSAM flat file, RECFM=F, RECORD=100 — summary-segment **export** stream. Not a DB segment. |
| `PADFLDBD` | `ims/PADFLDBD.DBD` | `(GSAM,BSAM)` | GSAM flat file, RECFM=F, RECORD=200 — detail-segment **export** stream. Not a DB segment. |

### Hierarchy (DBPAUTP0)

```
PAUTSUM0  (root,  100 bytes)  key = ACCNTID  (START=1 BYTES=6 TYPE=P, packed 6-byte = S9(11) COMP-3)
   |
   +-- PAUTDTL1 (child, 200 bytes) key = PAUT9CTS (START=1 BYTES=8 TYPE=C, char 8)
```

- `PAUTSUM0` = **Pending Authorization Summary**, one per account (copybook `cpy/CIPAUSMY.cpy`,
  COBOL group `PENDING-AUTH-SUMMARY`).
- `PAUTDTL1` = **Pending Authorization Detail**, many per summary (copybook `cpy/CIPAUDTY.cpy`,
  COBOL group `PENDING-AUTH-DETAILS`). Its 8-byte char sequence key `PAUT9CTS` overlays the
  group `PA-AUTHORIZATION-KEY = PA-AUTH-DATE-9C S9(05) COMP-3 (3 bytes) + PA-AUTH-TIME-9C
  S9(09) COMP-3 (5 bytes) = 8 bytes`. Both are stored as **9s-complement descending** values
  (`99999 - yyddd`, `999999999 - hhmmssmmm`) so a forward GN/GNP scan returns **newest first**.
- `PAUTINDX` (in `DBPAUTX0`) is the index segment (key `INDXSEQ`, 6-byte packed) whose `LCHILD`
  points at `PAUTSUM0 INDEX=ACCNTID`. In the relational model this collapses into the **primary
  key / unique index on `PAUT_SUMMARY.ACCT_ID`** — no separate table.

### PSBs (sensitivity + PROCOPT)

| PSB | File | PCB → DBD | PROCOPT | Used by |
|-----|------|-----------|---------|---------|
| `PSBPAUTB` | `ims/PSBPAUTB.psb` | `DBPAUTP0` (KEYLEN 14) | `AP` (all + path) | Online `COPAUS0C/1C`, MQ `COPAUA0C` (PCB #1), batch `CBPAUP0C` (PCB #2). SENSEG PAUTSUM0, PAUTDTL1. |
| `PSBPAUTL` | `ims/PSBPAUTL.psb` | `DBPAUTP0` (KEYLEN 14) | `L` (load) | Initial DB load. |
| `PAUTBUNL` | `ims/PAUTBUNL.PSB` | `DBPAUTP0` | `GOTP` (get-only + path) | `PAUDBUNL` unload. |
| `DLIGSAMP` | `ims/DLIGSAMP.PSB` | `DBPAUTP0` + 2× GSAM (`PASFLDBD`,`PADFLDBD`) | `GOTP` / `LS` | `DBUNLDGS` GSAM unload. |

KEYLEN 14 = 6-byte root key (`ACCNTID`) concatenated with 8-byte child key (`PAUT9CTS`) =
the fully-qualified concatenated key the relational composite PK reproduces.

---

## 2. Relational schema (target)

Two segments → two tables, parent/child modeled as a FK. The HIDAM primary index becomes the
PK on the parent; the child's own sequence field plus the parent FK form the child PK.

### Table `PAUT_SUMMARY`  (← IMS root segment `PAUTSUM0`, copybook `CIPAUSMY`)

| Column | SQL type | Source field (PIC) | Key / Notes |
|--------|----------|--------------------|-------------|
| `ACCT_ID` | `DECIMAL(11,0)` (INTEGER ok) | `PA-ACCT-ID` S9(11) COMP-3 | **PRIMARY KEY**. = IMS root key `ACCNTID` (6-byte packed). Replaces DBPAUTX0/PAUTINDX index. |
| `CUST_ID` | `DECIMAL(9,0)` | `PA-CUST-ID` 9(09) | |
| `AUTH_STATUS` | `CHAR(1)` | `PA-AUTH-STATUS` X(01) | |
| `ACCOUNT_STATUS_1..5` | `CHAR(2)` ×5 | `PA-ACCOUNT-STATUS` X(02) OCCURS 5 | OCCURS 5 → 5 columns `ACCOUNT_STATUS_1`…`_5` (or a child `PAUT_SUMMARY_STATUS(ACCT_ID, SEQ, STATUS)`; flatten preferred — fixed small occurs). |
| `CREDIT_LIMIT` | `DECIMAL(11,2)` | `PA-CREDIT-LIMIT` S9(09)V99 COMP-3 | |
| `CASH_LIMIT` | `DECIMAL(11,2)` | `PA-CASH-LIMIT` S9(09)V99 COMP-3 | |
| `CREDIT_BALANCE` | `DECIMAL(11,2)` | `PA-CREDIT-BALANCE` S9(09)V99 COMP-3 | Running pending credit balance. |
| `CASH_BALANCE` | `DECIMAL(11,2)` | `PA-CASH-BALANCE` S9(09)V99 COMP-3 | |
| `APPROVED_AUTH_CNT` | `SMALLINT` | `PA-APPROVED-AUTH-CNT` S9(04) COMP | |
| `DECLINED_AUTH_CNT` | `SMALLINT` | `PA-DECLINED-AUTH-CNT` S9(04) COMP | |
| `APPROVED_AUTH_AMT` | `DECIMAL(11,2)` | `PA-APPROVED-AUTH-AMT` S9(09)V99 COMP-3 | |
| `DECLINED_AUTH_AMT` | `DECIMAL(11,2)` | `PA-DECLINED-AUTH-AMT` S9(09)V99 COMP-3 | |
| (`FILLER` X(34)) | — | trailing filler to 100 bytes | Not modeled as a column; preserve only if byte-exact round-trip needed. |

```sql
CREATE TABLE PAUT_SUMMARY (
  ACCT_ID            DECIMAL(11,0) NOT NULL,
  CUST_ID            DECIMAL(9,0),
  AUTH_STATUS        CHAR(1),
  ACCOUNT_STATUS_1   CHAR(2), ACCOUNT_STATUS_2 CHAR(2), ACCOUNT_STATUS_3 CHAR(2),
  ACCOUNT_STATUS_4   CHAR(2), ACCOUNT_STATUS_5 CHAR(2),
  CREDIT_LIMIT       DECIMAL(11,2), CASH_LIMIT      DECIMAL(11,2),
  CREDIT_BALANCE     DECIMAL(11,2), CASH_BALANCE    DECIMAL(11,2),
  APPROVED_AUTH_CNT  SMALLINT,      DECLINED_AUTH_CNT SMALLINT,
  APPROVED_AUTH_AMT  DECIMAL(11,2), DECLINED_AUTH_AMT DECIMAL(11,2),
  CONSTRAINT PK_PAUT_SUMMARY PRIMARY KEY (ACCT_ID)
);
```

### Table `PAUT_DETAIL`  (← IMS child segment `PAUTDTL1`, copybook `CIPAUDTY`)

| Column | SQL type | Source field (PIC) | Key / Notes |
|--------|----------|--------------------|-------------|
| `ACCT_ID` | `DECIMAL(11,0)` | parent `PA-ACCT-ID` | **FK → PAUT_SUMMARY.ACCT_ID**, part of PK. Supplies IMS parentage. |
| `AUTH_KEY` | `CHAR(8)` (BINARY(8)) | `PAUT9CTS` / `PA-AUTHORIZATION-KEY` | **PK part 2**. = child sequence key (date-9C + time-9C, 9s-complement). |
| `AUTH_DATE_9C` | `DECIMAL(5,0)` | `PA-AUTH-DATE-9C` S9(05) COMP-3 | Sort component (descending). `yyddd = 99999 − value`. |
| `AUTH_TIME_9C` | `DECIMAL(9,0)` | `PA-AUTH-TIME-9C` S9(09) COMP-3 | Sort component (descending). |
| `AUTH_ORIG_DATE` | `CHAR(6)` | `PA-AUTH-ORIG-DATE` X(06) | YYMMDD original request date. |
| `AUTH_ORIG_TIME` | `CHAR(6)` | `PA-AUTH-ORIG-TIME` X(06) | HHMMSS original request time. |
| `CARD_NUM` | `CHAR(16)` | `PA-CARD-NUM` X(16) | |
| `AUTH_TYPE` | `CHAR(4)` | `PA-AUTH-TYPE` X(04) | |
| `CARD_EXPIRY_DATE` | `CHAR(4)` | `PA-CARD-EXPIRY-DATE` X(04) | |
| `MESSAGE_TYPE` | `CHAR(6)` | `PA-MESSAGE-TYPE` X(06) | |
| `MESSAGE_SOURCE` | `CHAR(6)` | `PA-MESSAGE-SOURCE` X(06) | |
| `AUTH_ID_CODE` | `CHAR(6)` | `PA-AUTH-ID-CODE` X(06) | |
| `AUTH_RESP_CODE` | `CHAR(2)` | `PA-AUTH-RESP-CODE` X(02) | `'00'` = approved (88 `PA-AUTH-APPROVED`). |
| `AUTH_RESP_REASON` | `CHAR(4)` | `PA-AUTH-RESP-REASON` X(04) | Decline reason code. |
| `PROCESSING_CODE` | `DECIMAL(6,0)` | `PA-PROCESSING-CODE` 9(06) | |
| `TRANSACTION_AMT` | `DECIMAL(12,2)` | `PA-TRANSACTION-AMT` S9(10)V99 COMP-3 | |
| `APPROVED_AMT` | `DECIMAL(12,2)` | `PA-APPROVED-AMT` S9(10)V99 COMP-3 | |
| `MERCHANT_CATAGORY_CODE` | `CHAR(4)` | `PA-MERCHANT-CATAGORY-CODE` X(04) | (sic — spelled "CATAGORY" in source.) |
| `ACQR_COUNTRY_CODE` | `CHAR(3)` | `PA-ACQR-COUNTRY-CODE` X(03) | |
| `POS_ENTRY_MODE` | `SMALLINT` | `PA-POS-ENTRY-MODE` 9(02) | |
| `MERCHANT_ID` | `CHAR(15)` | `PA-MERCHANT-ID` X(15) | |
| `MERCHANT_NAME` | `CHAR(22)` | `PA-MERCHANT-NAME` X(22) | |
| `MERCHANT_CITY` | `CHAR(13)` | `PA-MERCHANT-CITY` X(13) | |
| `MERCHANT_STATE` | `CHAR(2)` | `PA-MERCHANT-STATE` X(02) | |
| `MERCHANT_ZIP` | `CHAR(9)` | `PA-MERCHANT-ZIP` X(09) | |
| `TRANSACTION_ID` | `CHAR(15)` | `PA-TRANSACTION-ID` X(15) | |
| `MATCH_STATUS` | `CHAR(1)` | `PA-MATCH-STATUS` X(01) | 88s: `P`=pending, `D`=declined, `E`=pending-expired, `M`=matched. |
| `AUTH_FRAUD` | `CHAR(1)` | `PA-AUTH-FRAUD` X(01) | 88s: `F`=confirmed, `R`=removed. Updated by fraud REPL. |
| `FRAUD_RPT_DATE` | `CHAR(8)` | `PA-FRAUD-RPT-DATE` X(08) | |
| (`FILLER` X(17)) | — | trailing filler to 200 bytes | Not a column. |

```sql
CREATE TABLE PAUT_DETAIL (
  ACCT_ID                 DECIMAL(11,0) NOT NULL,
  AUTH_KEY                CHAR(8)       NOT NULL,
  AUTH_DATE_9C            DECIMAL(5,0),
  AUTH_TIME_9C            DECIMAL(9,0),
  AUTH_ORIG_DATE          CHAR(6),  AUTH_ORIG_TIME CHAR(6),
  CARD_NUM                CHAR(16), AUTH_TYPE CHAR(4), CARD_EXPIRY_DATE CHAR(4),
  MESSAGE_TYPE            CHAR(6),  MESSAGE_SOURCE CHAR(6),
  AUTH_ID_CODE            CHAR(6),  AUTH_RESP_CODE CHAR(2), AUTH_RESP_REASON CHAR(4),
  PROCESSING_CODE         DECIMAL(6,0),
  TRANSACTION_AMT         DECIMAL(12,2), APPROVED_AMT DECIMAL(12,2),
  MERCHANT_CATAGORY_CODE  CHAR(4), ACQR_COUNTRY_CODE CHAR(3), POS_ENTRY_MODE SMALLINT,
  MERCHANT_ID             CHAR(15), MERCHANT_NAME CHAR(22), MERCHANT_CITY CHAR(13),
  MERCHANT_STATE          CHAR(2),  MERCHANT_ZIP CHAR(9), TRANSACTION_ID CHAR(15),
  MATCH_STATUS            CHAR(1),  AUTH_FRAUD CHAR(1), FRAUD_RPT_DATE CHAR(8),
  CONSTRAINT PK_PAUT_DETAIL PRIMARY KEY (ACCT_ID, AUTH_KEY),
  CONSTRAINT FK_PAUT_DETAIL_SUMMARY
      FOREIGN KEY (ACCT_ID) REFERENCES PAUT_SUMMARY (ACCT_ID)
      ON DELETE CASCADE
);
-- Hierarchical-scan order = newest first. Recreate IMS GN/GNP order with this index:
CREATE INDEX IX_PAUT_DETAIL_SEQ
  ON PAUT_DETAIL (ACCT_ID, AUTH_DATE_9C ASC, AUTH_TIME_9C ASC);
-- (ASC on 9s-complement values == DESC on real date/time == IMS twin order.)
```

**Ordering rule (critical):** IMS returns root segments by ascending `ACCNTID`, and child
segments by ascending `PAUT9CTS`. Because `PAUT9CTS` is a 9s-complement value, ascending byte
order = **most-recent-authorization first**. Every relational `SELECT … ORDER BY` that emulates
a GN/GNP scan MUST order `PAUT_DETAIL` by `(ACCT_ID, AUTH_KEY)` ascending (equivalently
`AUTH_DATE_9C, AUTH_TIME_9C` ascending) to preserve the screen paging behavior in `COPAUS0C`.

### Parent/child relationship summary

| IMS relationship | Relational equivalent |
|------------------|-----------------------|
| `PAUTSUM0` root, keyed by `ACCNTID` | `PAUT_SUMMARY` PK `ACCT_ID` |
| `DBPAUTX0` / `PAUTINDX` HIDAM primary index on `ACCNTID` | (absorbed) unique PK index on `PAUT_SUMMARY.ACCT_ID` |
| `PAUTDTL1 PARENT=PAUTSUM0` | `PAUT_DETAIL.ACCT_ID` **FK** → `PAUT_SUMMARY.ACCT_ID`, `ON DELETE CASCADE` |
| Twin chain order under a parent (`PAUT9CTS` ascending) | `ORDER BY ACCT_ID, AUTH_KEY` |
| Concatenated key KEYLEN=14 (6+8) | composite `(ACCT_ID, AUTH_KEY)` |

---

## 3. DL/I call → SQL mapping

Two call styles appear in the code:

1. **EXEC DLI** (CICS / DLI-batch macro form) — used by `COPAUS0C`, `COPAUS1C`, `COPAUA0C`,
   `CBPAUP0C`. Status in `DIBSTAT`.
2. **CALL 'CBLTDLI' USING FUNC-xxx, PCB, io-area, SSA** (assembler interface) — used by the
   load/unload utilities `PAUDBLOD`, `PAUDBUNL`, `DBUNLDGS`. Status in the PCB
   (`PAUT-PCB-STATUS`), function codes from `cpy/IMSFUNCS.cpy` (`FUNC-GU/GHU/GN/GHN/GNP/GHNP/REPL/ISRT/DLET`).

DL/I status codes and their SQL meaning:

| DL/I status | Meaning | Relational equivalent |
|-------------|---------|-----------------------|
| `'  '` (spaces) / `FW` | OK | row returned / rows affected ≥ 1 |
| `GE` | segment not found | `SELECT` returns 0 rows |
| `GB` | end of database | cursor exhausted (no next row) |
| `II` | duplicate insert | `INSERT` PK/unique violation |
| `GP` | wrong parentage | parentage cursor moved past parent |

### 3.1 GU (Get Unique) — random read of root, qualified by key

**Source:** `COPAUS0C GET-AUTH-SUMMARY`, `COPAUS1C READ-AUTH-RECORD`, `COPAUA0C 5500-READ-AUTH-SUMMRY`.
```
EXEC DLI GU USING PCB(PAUT-PCB-NUM)
     SEGMENT (PAUTSUM0)
     INTO (PENDING-AUTH-SUMMARY)
     WHERE (ACCNTID = PA-ACCT-ID)
END-EXEC
```
**SQL:**
```sql
SELECT * FROM PAUT_SUMMARY WHERE ACCT_ID = :PA_ACCT_ID;   -- LIMIT 1
```
- 1 row → status `'  '` → `FOUND-PAUT-SMRY-SEG`.
- 0 rows → status `GE` → `NFOUND-PAUT-SMRY-SEG`.
- GU also **establishes parentage / position** for subsequent GNP. In SQL there is no implicit
  cursor; the engine must remember the resolved `ACCT_ID` so the following GNP becomes
  `WHERE ACCT_ID = :that_id`. (See GNP below.)

**Variant — GU on detail by key** (`COPAUS1C READ-AUTH-RECORD` second call,
`COPAUS0C REPOSITION-AUTHORIZATIONS` uses GNP-with-WHERE): qualified retrieval of one detail:
```sql
SELECT * FROM PAUT_DETAIL WHERE ACCT_ID = :acct AND AUTH_KEY = :auth_key;  -- LIMIT 1
```

### 3.2 GN (Get Next) — forward scan of root segments

**Source:** `CBPAUP0C 2000-FIND-NEXT-AUTH-SUMMARY` (purge), `PAUDBUNL` / `DBUNLDGS`
`2000-FIND-NEXT-AUTH-SUMMARY` (`CALL 'CBLTDLI' USING FUNC-GN ... ROOT-UNQUAL-SSA`).
```
EXEC DLI GN USING PCB(...) SEGMENT (PAUTSUM0) INTO (PENDING-AUTH-SUMMARY)
```
**SQL — forward cursor over all roots in key order:**
```sql
-- open once:
SELECT * FROM PAUT_SUMMARY ORDER BY ACCT_ID ASC;   -- iterate with a forward cursor
-- each GN  = cursor.MoveNext():
--   row     -> status '  '
--   no row  -> status 'GB' (end of database)
```
Implementation note: model the PCB's "current position" as a cursor/iterator the program holds
across GN calls. After each root GN, GNP scans that root's children (3.3).

### 3.3 GNP (Get Next within Parent) — scan child segments under current parent

**Source:** `CBPAUP0C 3000-FIND-NEXT-AUTH-DTL`, `COPAUS0C GET-AUTHORIZATIONS`,
`COPAUS1C READ-NEXT-AUTH-RECORD`, `PAUDBUNL`/`DBUNLDGS` `3000-FIND-NEXT-AUTH-DTL`.
```
EXEC DLI GNP USING PCB(...) SEGMENT (PAUTDTL1) INTO (PENDING-AUTH-DETAILS)
```
**SQL — forward cursor over children of the parent established by the last GU/GN:**
```sql
SELECT * FROM PAUT_DETAIL
 WHERE ACCT_ID = :current_parent_acct_id
 ORDER BY AUTH_KEY ASC;        -- == ORDER BY AUTH_DATE_9C, AUTH_TIME_9C  (newest first)
-- each GNP = cursor.MoveNext():
--   row    -> '  '
--   no row -> 'GE' (no more children) ... 'GB' at true end-of-db
```
- `:current_parent_acct_id` comes from the most recent root GU/GN (parentage). This is the SQL
  stand-in for IMS "position".

**Qualified GNP (reposition)** — `COPAUS0C REPOSITION-AUTHORIZATIONS`, `COPAUS1C`:
```
EXEC DLI GNP ... SEGMENT(PAUTDTL1) ... WHERE (PAUT9CTS = PA-AUTHORIZATION-KEY)
```
Used for screen paging: resume the child scan **at or after** a saved key.
```sql
SELECT * FROM PAUT_DETAIL
 WHERE ACCT_ID = :acct AND AUTH_KEY >= :saved_auth_key
 ORDER BY AUTH_KEY ASC;        -- first row repositions; continue MoveNext for the page
```

### 3.4 GHU / GHN / GHNP (Get Hold variants) — read with intent to update/delete

**Source:** function codes are defined in `IMSFUNCS.cpy` (`FUNC-GHU/GHN/GHNP`). The EXEC DLI
programs here issue plain `GU/GN/GNP` and then `REPL`/`DLET` against the held position; under
EXEC DLI the macro acquires the hold implicitly. Semantically these are "get-for-update".
**SQL:** identical SELECT statements as 3.1–3.3, executed inside a transaction with row locking:
```sql
SELECT ... FROM PAUT_DETAIL WHERE ACCT_ID = :a AND AUTH_KEY = :k;   -- then UPDATE/DELETE
-- engines that support it:  ... FOR UPDATE;   (SQLite: rely on the enclosing transaction)
```
The subsequent REPL/DLET (3.5/3.6) must target the **same row** just read (optimistic or
transaction-scoped).

### 3.5 REPL (Replace) — update the held segment

**Source:** `COPAUS1C UPDATE-AUTH-DETAILS` (fraud tag on detail), `COPAUA0C 8400-UPDATE-SUMMARY`
(REPL on summary when it already exists).
```
EXEC DLI REPL USING PCB(...) SEGMENT (PAUTDTL1) FROM (PENDING-AUTH-DETAILS)
EXEC DLI REPL USING PCB(...) SEGMENT (PAUTSUM0) FROM (PENDING-AUTH-SUMMARY)
```
**SQL — UPDATE the previously-retrieved row (all non-key columns):**
```sql
-- detail (fraud tagging path):
UPDATE PAUT_DETAIL
   SET AUTH_FRAUD = :pa_auth_fraud, FRAUD_RPT_DATE = :pa_fraud_rpt_date,
       MATCH_STATUS = :pa_match_status /* …all data columns from the io-area… */
 WHERE ACCT_ID = :acct AND AUTH_KEY = :auth_key;

-- summary (running totals after an auth decision):
UPDATE PAUT_SUMMARY
   SET CREDIT_LIMIT=:.., CASH_LIMIT=:.., APPROVED_AUTH_CNT=:.., APPROVED_AUTH_AMT=:..,
       DECLINED_AUTH_CNT=:.., DECLINED_AUTH_AMT=:.., CREDIT_BALANCE=:.., CASH_BALANCE=:..
 WHERE ACCT_ID = :acct;
```
- rows affected = 1 → `'  '`. The REPL replaces the segment in place at the current position; SQL
  uses the PK in the WHERE. IMS forbids changing the key on REPL — so the SQL UPDATE must **not**
  touch `ACCT_ID` / `AUTH_KEY`.

### 3.6 DLET (Delete) — delete the held segment (cascades to children)

**Source:** `CBPAUP0C 5000-DELETE-AUTH-DTL` (detail) and `6000-DELETE-AUTH-SUMMARY` (summary).
```
EXEC DLI DLET USING PCB(...) SEGMENT (PAUTDTL1) FROM (PENDING-AUTH-DETAILS)
EXEC DLI DLET USING PCB(...) SEGMENT (PAUTSUM0) FROM (PENDING-AUTH-SUMMARY)
```
**SQL:**
```sql
-- delete one expired detail (held at current position):
DELETE FROM PAUT_DETAIL WHERE ACCT_ID = :acct AND AUTH_KEY = :auth_key;

-- delete the summary once all its details are gone:
DELETE FROM PAUT_SUMMARY WHERE ACCT_ID = :acct;   -- FK ON DELETE CASCADE removes any leftover details
```
- **IMS semantics:** deleting a root (`PAUTSUM0`) physically deletes **all** child
  `PAUTDTL1` under it. The FK `ON DELETE CASCADE` reproduces this exactly. (`CBPAUP0C` deletes
  the children first, then the root, but the cascade makes the relational delete safe either way.)

### 3.7 ISRT (Insert) — add segments

**Source (EXEC DLI):** `COPAUA0C 8400-UPDATE-SUMMARY` (insert summary if absent) and
`8500-INSERT-AUTH` (**path insert** of detail under its parent).
**Source (CALL CBLTDLI):** `PAUDBLOD` `2100-INSERT-ROOT-SEG` / `3200-INSERT-IMS-CALL`
(`FUNC-ISRT` with `ROOT-UNQUAL-SSA` / `CHILD-UNQUAL-SSA`).

Root insert:
```
EXEC DLI ISRT USING PCB(...) SEGMENT (PAUTSUM0) FROM (PENDING-AUTH-SUMMARY)
```
```sql
INSERT INTO PAUT_SUMMARY (ACCT_ID, CUST_ID, AUTH_STATUS, …) VALUES (:.., :.., …);
-- duplicate -> 'II'  (PK violation)
```
Child / **path** insert (parent qualified, then child inserted under it):
```
EXEC DLI ISRT USING PCB(...)
     SEGMENT (PAUTSUM0) WHERE (ACCNTID = PA-ACCT-ID)
     SEGMENT (PAUTDTL1) FROM (PENDING-AUTH-DETAILS) SEGLENGTH (...)
END-EXEC
```
```sql
-- the WHERE(ACCNTID=..) just locates/validates the parent; the FK enforces it:
INSERT INTO PAUT_DETAIL (ACCT_ID, AUTH_KEY, AUTH_DATE_9C, AUTH_TIME_9C, …)
VALUES (:pa_acct_id, :pa_authorization_key, :date9c, :time9c, …);
-- FK violation (no parent) is the relational analogue of a missing-parent ISRT failure;
-- PK violation -> 'II'.
```
`AUTH_KEY` for the new detail is derived exactly as the COBOL does:
`PA-AUTH-DATE-9C = 99999 − yyddd`, `PA-AUTH-TIME-9C = 999999999 − (hhmmss*1000 + ms)`,
concatenated into the 8-byte `PA-AUTHORIZATION-KEY` (= `AUTH_KEY`).

### 3.8 SCHD / TERM / CHKP / SYNCPOINT — scheduling & commit control

| DL/I op | Source | Relational equivalent |
|---------|--------|-----------------------|
| `EXEC DLI SCHD PSB(...)` | `COPAUS0C/1C SCHEDULE-PSB`, `COPAUA0C 1200-SCHEDULE-PSB` | Open a DB connection / unit of work. `TC` ("scheduled more than once") → already-open → `TERM` then re-`SCHD` = close+reopen. |
| `EXEC DLI TERM` | same | Release the connection / end unit of work. |
| `EXEC CICS SYNCPOINT` | `COPAUS1C TAKE-SYNCPOINT`, `COPAUA0C 2000` loop | `COMMIT`. |
| `EXEC CICS SYNCPOINT ROLLBACK` | `COPAUS1C ROLL-BACK` | `ROLLBACK`. |
| `EXEC DLI CHKP ID(WK-CHKPT-ID)` | `CBPAUP0C 9000-TAKE-CHECKPOINT` | `COMMIT` at the checkpoint frequency (`P-CHKP-FREQ`); persist a restart token = `WK-CHKPT-ID` (`'RMAD' + counter`). Batch restart/repositioning. |

### 3.9 GSAM ISRT (export streams, not DB)

**Source:** `DBUNLDGS 3100-INSERT-PARENT-SEG-GSAM` / `3200-INSERT-CHILD-SEG-GSAM`
(`CALL 'CBLTDLI' USING FUNC-ISRT, PASFLPCB|PADFLPCB, io-area`).
GSAM is a **sequential flat file**, not a segment. There is **no SQL** — these map to writing the
already-read summary/detail row out to a flat export file (`PASFLDBD` = 100-byte records,
`PADFLDBD` = 200-byte records). The relational equivalent of the whole `DBUNLDGS` program is:
`SELECT … FROM PAUT_SUMMARY ORDER BY ACCT_ID` and per-row `SELECT … FROM PAUT_DETAIL … ORDER BY
AUTH_KEY`, writing each fetched row to the corresponding flat export stream.

---

## 4. Program → DL/I → SQL cross-reference

| Program | File | DL/I ops issued | SQL footprint |
|---------|------|-----------------|---------------|
| `COPAUA0C` (MQ auth processor) | `cbl/COPAUA0C.cbl` | SCHD; GU(PAUTSUM0/WHERE); REPL/ISRT(PAUTSUM0); ISRT path(PAUTSUM0→PAUTDTL1); TERM | SELECT summary; UPDATE/INSERT summary; INSERT detail (FK to summary); COMMIT |
| `COPAUS0C` (summary screen) | `cbl/COPAUS0C.cbl` | SCHD; GU(PAUTSUM0/WHERE); GNP(PAUTDTL1) + GNP/WHERE(reposition); SYNCPOINT | SELECT summary; paged SELECT details `ORDER BY AUTH_KEY` with `>= :key` reposition |
| `COPAUS1C` (detail screen + fraud) | `cbl/COPAUS1C.cbl` | SCHD; GU(PAUTSUM0/WHERE); GNP(PAUTDTL1/WHERE); GNP(next); REPL(PAUTDTL1); SYNCPOINT/ROLLBACK | SELECT summary; SELECT detail by key; UPDATE detail (fraud flag); COMMIT/ROLLBACK |
| `CBPAUP0C` (batch purge) | `cbl/CBPAUP0C.cbl` | GN(PAUTSUM0); GNP(PAUTDTL1); DLET(PAUTDTL1); DLET(PAUTSUM0); CHKP | cursor scan summaries; cursor scan details; DELETE expired details; DELETE empty summary (CASCADE); periodic COMMIT |
| `PAUDBLOD` (DB load) | `cbl/PAUDBLOD.CBL` | CBLTDLI ISRT(root, unqual SSA); GU(root, qual SSA `ACCNTID EQ`); ISRT(child, unqual SSA) | INSERT summary; SELECT(verify parent); INSERT detail |
| `PAUDBUNL` (DB unload→flat) | `cbl/PAUDBUNL.CBL` | CBLTDLI GN(root); GNP(child) → WRITE flat files | SELECT all summaries `ORDER BY ACCT_ID`; per-parent SELECT details `ORDER BY AUTH_KEY`; write flat |
| `DBUNLDGS` (DB unload→GSAM) | `cbl/DBUNLDGS.CBL` | CBLTDLI GN(root); GNP(child); ISRT(GSAM summary); ISRT(GSAM detail) | same SELECTs; write to flat export streams (no SQL on the GSAM side) |

---

## 5. DB2 fraud table `AUTHFRDS` (already relational — context only)

Not an IMS segment. Defined in `ddl/AUTHFRDS.ddl` (DCL `dcl/AUTHFRDS.dcl`, index `ddl/XAUTHFRD.ddl`),
written by `COPAUS2C` (called from `COPAUS1C` when an auth is marked fraud via PF5). It is a flat
relational table that mirrors most `PAUT_DETAIL` columns plus `ACCT_ID`/`CUST_ID`, keyed by
`(CARD_NUM, AUTH_TS)`.

| Column | Type | Notes |
|--------|------|-------|
| `CARD_NUM` | `CHAR(16)` NOT NULL | PK part 1 |
| `AUTH_TS` | `TIMESTAMP` NOT NULL | PK part 2 |
| `AUTH_TYPE`…`TRANSACTION_ID` | per DDL | copies of the auth detail at time of fraud report |
| `MATCH_STATUS`,`AUTH_FRAUD` | `CHAR(1)` | |
| `FRAUD_RPT_DATE` | `DATE` | |
| `ACCT_ID` | `DECIMAL(11)` | logical link to `PAUT_SUMMARY.ACCT_ID` |
| `CUST_ID` | `DECIMAL(9)` | |

PK `(CARD_NUM, AUTH_TS)`; unique index `XAUTHFRD (CARD_NUM ASC, AUTH_TS DESC)`. In the relational
re-host this table carries over essentially as-is. There is **no enforced FK** from `AUTHFRDS` to
`PAUT_SUMMARY` in the source (the relationship is logical via `ACCT_ID`); add one only if the
authorization detail it references is guaranteed resident (it may already be purged/matched), so
keep it as an un-enforced/soft reference.

---

## 6. Re-host notes / decisions

1. **Index segment vanishes.** `DBPAUTX0`/`PAUTINDX` is purely the HIDAM primary index over
   `ACCNTID`; it becomes the PK index on `PAUT_SUMMARY.ACCT_ID`. No table.
2. **Position is a cursor.** IMS "current position" (set by GU/GN, walked by GN/GNP) has no
   relational analogue — the re-host must hold a forward cursor/iterator per logical PCB:
   one over `PAUT_SUMMARY ORDER BY ACCT_ID`, and a nested one over
   `PAUT_DETAIL WHERE ACCT_ID = :current ORDER BY AUTH_KEY`.
3. **9s-complement keys preserve order.** Do NOT "fix" the descending-date encoding; store
   `AUTH_DATE_9C`/`AUTH_TIME_9C` as-is so ascending key order == newest-first, matching screen
   paging and the purge scan.
4. **Cascade = root delete.** Model the parent delete with `ON DELETE CASCADE` to mirror IMS
   physical deletion of children when a root segment is deleted.
5. **Path ISRT / path call.** `COPAUA0C`'s `SEGMENT(PAUTSUM0) WHERE(...) SEGMENT(PAUTDTL1) FROM`
   is a single hierarchical insert that locates the parent then inserts the child — relationally
   it is just `INSERT INTO PAUT_DETAIL` with the FK already pointing at the parent row.
6. **Filler bytes.** The X(34)/X(17) fillers padding segments to 100/200 bytes are not columns;
   only retain them if byte-exact flat-file round-trip with the GSAM/unload streams is required.
7. **REPL cannot change keys.** Generated UPDATE statements must exclude `ACCT_ID`/`AUTH_KEY`
   (IMS rejects key changes on REPL).
8. **Status-code contract.** Map engine results to DL/I statuses so existing COBOL EVALUATEs
   still work: 0 rows on a get → `GE`; cursor exhausted → `GB`; PK/unique violation on insert →
   `II`; otherwise spaces (OK).
