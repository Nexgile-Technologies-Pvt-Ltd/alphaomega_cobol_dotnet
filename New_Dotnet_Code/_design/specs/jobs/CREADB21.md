# JOB SPEC: CREADB21 (JOB name `CREADB2`)

## Overview

| Attribute | Value |
|-----------|-------|
| **Member / file** | `CREADB21.jcl` |
| **JOB name (on the JOB card)** | `CREADB2` |
| **Source JCL** | `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/jcl/CREADB21.jcl` |
| **JOB accounting / programmer** | `(DB2CR)`, `'SNJARAO'` |
| **CLASS** | `A` |
| **MSGCLASS** | `A` |
| **TIME** | `1440` (no CPU-time limit) |
| **NOTIFY** | `&SYSUID` |
| **TYPRUN** | `SCAN` — **the job is set to syntax-scan only; it is NOT actually executed as written.** Remove/override `TYPRUN=SCAN` to run it for real. |
| **Step count** | 4 EXEC steps (`FREEPLN`, `CRCRDDB`, `LDTTYPE`+`RUNTEP2`, `LDTCCAT`) — see note below on the two-EXEC STEP 20 |
| **Programs / utilities invoked** | `IKJEFT01` (TSO/E batch driver — runs DB2 `DSN ... RUN` of `DSNTIAD` and `DSNTEP4`), `IEFBR14` (no-op gate) |
| **DB2 subsystem (SSID)** | `DAZ1` (set via `SET DB2S=DAZ1`; also hard-coded `DSN SYSTEM(DAZ1)` in the control members) |

### Symbolic parameters (SET)

| Symbol | Value | Used for |
|--------|-------|----------|
| `CODER` | `AWS` | High-level qualifier root |
| `LBNM` | `&CODER..M2.CARDDEMO` → `AWS.M2.CARDDEMO` | HLQ of the `CNTL` PDS holding the DSN/RUN driver members and SQL control members |
| `DB2S` | `DAZ1` | DB2 subsystem id, plugged into load-library DSNs |

### JOBLIB

`//JOBLIB DD` concatenation supplies the DB2 / LE runtime load libraries for the whole job:
`OEM.DB2.DAZ1.SDSNLOAD` (listed twice), `CEE.SCEERUN`.
Individual steps additionally supply `STEPLIB` (DB2 `SDSNEXIT`, `RUNLIB.LOAD`, `SDSNLOAD`).

### Purpose

This is a **DB2 relational database setup / DDL + reference-data load job** — the
DB2 equivalent of the VSAM "delete / define / load" file-setup jobs. It creates the
CardDemo **transaction-type** relational schema in DB2 subsystem `DAZ1` and seeds the two
lookup tables. It performs, in order:

1. **FREEPLN** — Free the existing DB2 plan/packages (clean-up so they can be re-bound).
2. **CRCRDDB** — Run `DSNTIAD` to execute the DDL that **creates the database, table spaces,
   the two tables, their unique indexes, the foreign key, and the GRANTs**.
3. **STEP 20 (LDTTYPE gate + RUNTEP2)** — Run `DSNTEP4` to **INSERT the seed rows into
   `TRANSACTION_TYPE`** (7 transaction-type codes).
4. **LDTCCAT** — Run `DSNTEP4` to **INSERT the seed rows into `TRANSACTION_TYPE_CATEGORY`**
   (18 type/category rows).

There is **no posting/transaction processing, no report, no backup, and no GDG usage**.
It is purely DDL + static reference-data population for the transaction-type tables used by
the Transaction-Type-DB2 variant of CardDemo (online programs `COTRTLIC` / `COTRTUPC`, etc.).

> **Note (TYPRUN=SCAN):** Because the JOB card carries `TYPRUN=SCAN`, MVS only checks the JCL
> syntax and does not run any step. This is shipped as a guarded template; the operator must
> remove `TYPRUN=SCAN` (and is reminded to BIND the DB2 utilities first — see
> `AWS.M2.CARDDEMO.JCL.UTIL(BINDTIAD)`) before it has any effect.

---

## DB2 objects created / populated (relational targets)

| DB2 object | Type | Created in step | Notes |
|------------|------|-----------------|-------|
| `CARDDEMO` | DATABASE | CRCRDDB | STOGROUP `AWST1STG`, BUFFERPOOL `BP0`, CCSID EBCDIC |
| `CARDDEMO.CARDSPC1` | TABLESPACE | CRCRDDB | SEGSIZE 4, LOCKSIZE TABLE, BP0 — holds `TRANSACTION_TYPE` |
| `CARDDEMO.CARDSTTC` | TABLESPACE | CRCRDDB | SEGSIZE 4, LOCKSIZE TABLE, BP0 — holds `TRANSACTION_TYPE_CATEGORY` |
| `CARDDEMO.TRANSACTION_TYPE` | TABLE | CRCRDDB (DDL) / RUNTEP2 (rows) | PK `TR_TYPE` CHAR(2); col `TR_DESCRIPTION` VARCHAR(50) |
| `CARDDEMO.XTRAN_TYPE` | UNIQUE INDEX | CRCRDDB | On `TRANSACTION_TYPE(TR_TYPE ASC)` |
| `CARDDEMO.TRANSACTION_TYPE_CATEGORY` | TABLE | CRCRDDB (DDL) / LDTCCAT (rows) | PK `(TRC_TYPE_CODE CHAR(2), TRC_TYPE_CATEGORY CHAR(4))`; col `TRC_CAT_DATA` VARCHAR(50) |
| `CARDDEMO.X_TRAN_TYPE_CATG` | UNIQUE INDEX | CRCRDDB | On `TRANSACTION_TYPE_CATEGORY(TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC)` |
| FK on `TRANSACTION_TYPE_CATEGORY` | FOREIGN KEY | CRCRDDB | `TRC_TYPE_CODE` → `TRANSACTION_TYPE(TR_TYPE)` `ON DELETE RESTRICT` |
| Existing plans/packages | (freed) | FREEPLN | `PLAN(CARDDEMO)`, `PLAN(COTRTLIC)`, `PACKAGE(COTRTLIC.*)` |

**No sequential / VSAM datasets carry user data in this job.** Unlike the IDCAMS file-setup
jobs, there is no flat seed file: the seed rows are embedded as literal `INSERT ... SELECT ...
FROM SYSIBM.SYSDUMMY1 UNION ALL` SQL in the control members. All "DD/dataset" references below
are control-input (`SYSTSIN`/`SYSIN` pointing at PDS members) and SYSOUT message destinations.

---

## Datasets / Files involved (control input + load libs)

| Logical DD/dataset | Type | Role |
|--------------------|------|------|
| `OEM.DB2.DAZ1.SDSNLOAD` | Load library (JOBLIB) | DB2 load modules (DSNLOAD) for the whole job |
| `CEE.SCEERUN` | Load library (JOBLIB) | Language Environment runtime |
| `OEM.DB2.DAZ1.SDSNEXIT` | Load library (STEPLIB, FREEPLN) | DB2 subsystem exit/parm load lib |
| `OEMA.DB2.VERSIONA.SDSNLOAD` | Load library (STEPLIB, all DSN steps) | DB2 base load lib |
| `OEM.DB2.DAZ1.RUNLIB.LOAD` | Load library (STEPLIB, CRCRDDB/RUNTEP2/LDTCCAT) | DB2 run-time/utility plan load lib (DSNTIAD/DSNTEP4) |
| `AWS.M2.CARDDEMO.CNTL(DB2FREE)` | PDS member (`SYSTSIN`, FREEPLN) | `DSN SYSTEM(DAZ1)` + `FREE PLAN/PACKAGE` commands |
| `AWS.M2.CARDDEMO.CNTL(DB2TIAD1)` | PDS member (`SYSTSIN`, CRCRDDB) | `DSN ... RUN PROGRAM(DSNTIAD) PARMS('RC0')` driver |
| `AWS.M2.CARDDEMO.CNTL(DB2CREAT)` | PDS member (`SYSIN`, CRCRDDB) | The DDL: CREATE DATABASE/TABLESPACE/TABLE/INDEX, ALTER FK, GRANTs |
| `AWS.M2.CARDDEMO.CNTL(DB2TEP41)` | PDS member (`SYSTSIN`, RUNTEP2 & LDTCCAT) | `DSN ... RUN PROGRAM(DSNTEP4) PLAN(DSNTEP4) PARMS('/ALIGN(LHS) MIXED')` driver |
| `AWS.M2.CARDDEMO.CNTL(DB2LTTYP)` | PDS member (`SYSIN`, RUNTEP2) | `INSERT INTO CARDDEMO.TRANSACTION_TYPE` seed rows |
| `AWS.M2.CARDDEMO.CNTL(DB2LTCAT)` | PDS member (`SYSIN`, LDTCCAT) | `INSERT INTO CARDDEMO.TRANSACTION_TYPE_CATEGORY` seed rows |

> The `.ctl` files in `app-transaction-type-db2/ctl/` are the source equivalents of these
> `CNTL` PDS members: `DB2FREE.ctl`, `DB2TIAD1.ctl`, `DB2CREAT.ctl`, `DB2TEP41.ctl`,
> `DB2LTTYP.ctl`, `DB2LTCAT.ctl`.

---

## Step-by-step detail

### STEP 00 — `FREEPLN`: Free existing plans and packages
- **EXEC:** `PGM=IKJEFT01,DYNAMNBR=20` (TSO/E batch terminal monitor — drives the DB2 `DSN` subcommand)
- **PARM:** none on EXEC (`DYNAMNBR=20` allocates 20 dynamic DD slots)
- **COND / RC gating:** none. Header comment notes this step **ends with RC 8 if the plans
  do not exist**, and advises *not* to run it when creating a brand-new database.
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Role |
  |----|-----------|------|
  | `STEPLIB` | `OEM.DB2.DAZ1.SDSNEXIT` + `OEMA.DB2.VERSIONA.SDSNLOAD` (`DISP=SHR`) | DB2 load libs |
  | `SYSPRINT` | `SYSOUT=*` | Utility messages |
  | `SYSTSPRT` | `SYSOUT=*` | TSO/DSN messages |
  | `SYSUDUMP` | `SYSOUT=*` | Dump output |
  | `SYSTSIN` | `DISP=SHR,DSN=AWS.M2.CARDDEMO.CNTL(DB2FREE)` | TSO/DSN command stream |
- **Control statements (exact, `DB2FREE`):**
  ```
  DSN SYSTEM(DAZ1)
   FREE PLAN(CARDDEMO)
   FREE PLAN(COTRTLIC)
   FREE PACKAGE(COTRTLIC.*)
  END
  ```
  - Frees the DB2 application plans `CARDDEMO` and `COTRTLIC` and all packages in collection
    `COTRTLIC.*` so they can be re-bound. Idempotent in intent but **RC 8** when objects are absent.

### STEP 10 — `CRCRDDB`: Create the database / tablespaces / tables (DSNTIAD)
- **EXEC:** `PGM=IKJEFT01,DYNAMNBR=20`
- **PARM:** none on EXEC. The **DSN driver** in `DB2TIAD1` runs `DSNTIAD` with `PARMS('RC0')`
  (the SQL processor `DSNTIAD`, RC0 option).
- **COND / RC gating:** none on this EXEC.
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Reads/Writes | Role |
  |----|-----------|--------------|------|
  | `STEPLIB` | `OEM.DB2.DAZ1.RUNLIB.LOAD` + `OEMA.DB2.VERSIONA.SDSNLOAD` (`DISP=SHR`) | — | DB2 load libs incl. DSNTIAD |
  | `SYSTSPRT` | `SYSOUT=*` | write | TSO/DSN messages |
  | `SYSUDUMP` | `SYSOUT=*` | write | Dump output |
  | `SYSPRINT` | `SYSOUT=*` | write | DSNTIAD listing |
  | `SYSTSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2TIAD1)` (`DISP=SHR`) | read | DSN RUN driver |
  | `SYSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2CREAT)` (`DISP=SHR`) | read | DDL executed by DSNTIAD |
- **Driver (exact, `DB2TIAD1`):**
  ```
  DSN SYSTEM(DAZ1)
  RUN PROGRAM(DSNTIAD) -
  PARMS('RC0')
  ```
- **SQL/DDL executed (key statements, from `DB2CREAT`):**
  ```
  SET CURRENT SQLID = 'SYSADM';
  CREATE DATABASE CARDDEMO  STOGROUP AWST1STG BUFFERPOOL BP0 CCSID EBCDIC;
  CREATE TABLESPACE CARDSPC1 IN CARDDEMO USING STOGROUP AWST1STG
         SEGSIZE 4 LOCKSIZE TABLE BUFFERPOOL BP0 CLOSE NO CCSID EBCDIC;
  CREATE TABLE CARDDEMO.TRANSACTION_TYPE
        (TR_TYPE CHAR(2) NOT NULL,
         TR_DESCRIPTION VARCHAR(50) NOT NULL,
         PRIMARY KEY(TR_TYPE)) IN CARDDEMO.CARDSPC1 CCSID EBCDIC;
  CREATE UNIQUE INDEX CARDDEMO.XTRAN_TYPE ON CARDDEMO.TRANSACTION_TYPE (TR_TYPE ASC) ...;
  GRANT DBADM ON DATABASE CARDDEMO TO PUBLIC;
  GRANT USE OF TABLESPACE CARDDEMO.CARDSPC1 TO PUBLIC;
  GRANT DELETE,INSERT,SELECT,UPDATE ON TABLE CARDDEMO.TRANSACTION_TYPE TO PUBLIC;
  CREATE TABLESPACE CARDSTTC IN CARDDEMO USING STOGROUP AWST1STG
         SEGSIZE 4 LOCKSIZE TABLE BUFFERPOOL BP0 CLOSE NO CCSID EBCDIC;
  GRANT USE OF TABLESPACE CARDDEMO.CARDSTTC TO PUBLIC;
  CREATE TABLE CARDDEMO.TRANSACTION_TYPE_CATEGORY
        (TRC_TYPE_CODE CHAR(2) NOT NULL,
         TRC_TYPE_CATEGORY CHAR(4) NOT NULL,
         TRC_CAT_DATA VARCHAR(50) NOT NULL,
         PRIMARY KEY(TRC_TYPE_CODE, TRC_TYPE_CATEGORY)) IN CARDDEMO.CARDSTTC CCSID EBCDIC;
  CREATE UNIQUE INDEX CARDDEMO.X_TRAN_TYPE_CATG
         ON CARDDEMO.TRANSACTION_TYPE_CATEGORY (TRC_TYPE_CODE ASC, TRC_TYPE_CATEGORY ASC) ...;
  ALTER TABLE CARDDEMO.TRANSACTION_TYPE_CATEGORY
        FOREIGN KEY (TRC_TYPE_CODE)
        REFERENCES CARDDEMO.TRANSACTION_TYPE (TR_TYPE) ON DELETE RESTRICT;
  GRANT DELETE,INSERT,SELECT,UPDATE ON TABLE CARDDEMO.TRANSACTION_TYPE_CATEGORY TO PUBLIC;
  ```
  (Each logical group is followed by `COMMIT;`.) The STOGROUP `AWST1STG` must already exist.

### STEP 20 — `LDTTYPE` (gate) + `RUNTEP2`: Load `TRANSACTION_TYPE` (DSNTEP4)
This is shipped as **two EXEC cards** under one logical "STEP 20" banner:

- **`LDTTYPE` — gate step:** `EXEC PGM=IEFBR14,COND=(0,NE)`
  - **Program:** `IEFBR14` (do-nothing utility — allocates nothing here).
  - **COND / RC gating:** `COND=(0,NE)` → *bypass this step if (0 NE prior-RC)*, i.e. it is
    **skipped whenever any earlier step returned a non-zero RC**; it runs only when all prior
    steps ended RC 0. Acts as a no-op success gate guarding the load.
  - **DD / GDG:** none.
- **`RUNTEP2` — the actual load:** `EXEC PGM=IKJEFT01,DYNAMNBR=20`
  - **PARM:** none on EXEC; the DSN driver runs `DSNTEP4` with `PARMS('/ALIGN(LHS) MIXED')`,
    `PLAN(DSNTEP4)`.
  - **COND / RC gating:** none on this EXEC (no COND coded).
  - **GDG:** none.
  - **DD statements:**
    | DD | Allocation | Reads/Writes | Role |
    |----|-----------|--------------|------|
    | `STEPLIB` | `OEM.DB2.DAZ1.RUNLIB.LOAD` + `OEMA.DB2.VERSIONA.SDSNLOAD` | — | DB2 load libs incl. DSNTEP4 |
    | `SYSTSPRT` / `SYSPRINT` | `SYSOUT=*` | write | DSN / DSNTEP4 messages & SQL listing |
    | `SYSUDUMP` | `SYSOUT=*` | write | Dump output |
    | `SYSTSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2TEP41)` | read | DSN RUN driver for DSNTEP4 |
    | `SYSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2LTTYP)` | read | INSERT statements (seed rows) |
  - **Driver (exact, `DB2TEP41`):**
    ```
    DSN SYSTEM(DAZ1)
    RUN PROGRAM(DSNTEP4) -
    PLAN(DSNTEP4) -
    PARMS('/ALIGN(LHS) MIXED')
    ```
  - **SQL executed (exact, `DB2LTTYP`):** inserts **7 rows** into `CARDDEMO.TRANSACTION_TYPE`
    `(TR_TYPE, TR_DESCRIPTION)` via `SELECT ... FROM SYSIBM.SYSDUMMY1 UNION ALL ... COMMIT;`:
    ```
    '01','PURCHASE'      '02','PAYMENT'       '03','CREDIT'
    '04','AUTHORIZATION' '05','REFUND'        '06','REVERAL'
    '07','ADJUSTMENT'
    ```
    (Note the source typo `REVERAL` for code 06 — preserve verbatim for parity.)

### STEP 30 — `LDTCCAT`: Load `TRANSACTION_TYPE_CATEGORY` (DSNTEP4)
- **EXEC:** `PGM=IKJEFT01,DYNAMNBR=20,COND=(0,NE)`
- **PARM:** none on EXEC; DSN driver runs `DSNTEP4` with `PLAN(DSNTEP4) PARMS('/ALIGN(LHS) MIXED')`.
- **COND / RC gating:** `COND=(0,NE)` → **skip this step unless every prior step ended RC 0**
  (same gate semantics as `LDTTYPE`). Ensures the category load runs only after a clean
  create + type-table load.
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Reads/Writes | Role |
  |----|-----------|--------------|------|
  | `STEPLIB` | `OEM.DB2.DAZ1.RUNLIB.LOAD` + `OEMA.DB2.VERSIONA.SDSNLOAD` | — | DB2 load libs incl. DSNTEP4 |
  | `SYSTSPRT` / `SYSPRINT` | `SYSOUT=*` | write | DSN / DSNTEP4 messages & SQL listing |
  | `SYSUDUMP` | `SYSOUT=*` | write | Dump output |
  | `SYSTSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2TEP41)` | read | DSN RUN driver for DSNTEP4 |
  | `SYSIN` | `DSN=AWS.M2.CARDDEMO.CNTL(DB2LTCAT)` | read | INSERT statements (seed rows) |
- **SQL executed (exact, `DB2LTCAT`):** inserts **18 rows** into
  `CARDDEMO.TRANSACTION_TYPE_CATEGORY (TRC_TYPE_CODE, TRC_TYPE_CATEGORY, TRC_CAT_DATA)` via a
  `WITH DMY AS (SELECT * FROM SYSIBM.SYSDUMMY1)` CTE and `SELECT ... FROM DMY UNION ALL ... COMMIT;`:
  | TRC_TYPE_CODE | TRC_TYPE_CATEGORY | TRC_CAT_DATA |
  |---|---|---|
  | 01 | 0001 | REGULAR SALES DRAFT |
  | 01 | 0002 | REGULAR CASH ADVANCE |
  | 01 | 0003 | CONVENIENCE CHECK DEBIT |
  | 01 | 0004 | ATM CASH ADVANCE |
  | 01 | 0005 | INTEREST AMOUNT |
  | 02 | 0001 | CASH PAYMENT |
  | 02 | 0002 | ELECTRONIC PAYMENT |
  | 02 | 0003 | CHECK PAYMENT |
  | 03 | 0001 | CREDIT TO ACCOUNT |
  | 03 | 0002 | CREDIT TO PURCHASE BALANCE |
  | 03 | 0003 | CREDIT TO CASH BALANCE |
  | 04 | 0001 | ZERO DOLLAR AUTHORIZATION |
  | 04 | 0002 | ONLINE PURCHASE AUTHORIZATION |
  | 04 | 0003 | TRAVEL BOOKING AUTHORIZATION |
  | 05 | 0001 | REFUND CREDIT |
  | 06 | 0001 | FRAUD REVERSAL |
  | 06 | 0002 | NON FRAUD REVERSAL |
  | 07 | 0001 | SALES DRAFT CREDIT ADJUSTMENT |

  Each `TRC_TYPE_CODE` value matches an existing `TRANSACTION_TYPE.TR_TYPE` row (FK
  `ON DELETE RESTRICT`), so this step must run **after** STEP 20 has populated the parent table.

---

## .NET JobControl mapping notes
- **This is a DB2/DDL job, not an IDCAMS/SORT/file job.** There are no `DEFINE/REPRO/DELETE`
  IDCAMS statements and no `SORT FIELDS`. The step-runner equivalent is a **schema-create +
  reference-data-seed** routine for the relational store.
- Map the four logical actions to JobControl handlers:
  1. `FREEPLN` (DB2 `FREE PLAN/PACKAGE`) — a re-bind/cleanup no-op in a self-contained .NET
     target (DB2 plans/packages have no relational counterpart); may be skipped, mirroring the
     header advice "don't run it when creating a new database."
  2. `CRCRDDB` (DSNTIAD running `DB2CREAT`) — execute the DDL: create the schema and the two
     tables `TRANSACTION_TYPE` and `TRANSACTION_TYPE_CATEGORY`, their unique indexes, the FK
     (`TRC_TYPE_CODE` → `TR_TYPE`, `ON DELETE RESTRICT`), and grants. In the SQLite/.NET data
     layer this is `CREATE TABLE` + index + FK.
  3. `RUNTEP2` (DSNTEP4 running `DB2LTTYP`) — `INSERT` the 7 transaction-type seed rows
     (preserve the `REVERAL` typo for code `06`).
  4. `LDTCCAT` (DSNTEP4 running `DB2LTCAT`) — `INSERT` the 18 transaction-type-category seed rows.
- **RC gating:** `LDTTYPE` and `LDTCCAT` use `COND=(0,NE)` — semantically "run only if every
  prior step ended RC 0." The .NET runner should treat any earlier non-zero step result as a
  reason to skip the load steps. `FREEPLN` may legitimately return RC 8 (objects absent) and is
  not gated, so it must not, by itself, block subsequent steps in a real run — but be aware the
  un-gated `CRCRDDB` will still execute after it.
- **TYPRUN=SCAN:** As shipped the job only syntax-checks. A faithful port should treat the
  default state as "validate only," requiring an explicit opt-in to actually create/seed.
- **No GDG, no sequential/VSAM data, no backup, no report.** Execution is strictly sequential:
  free → create schema → seed type table → seed category table.
- Equivalent reference-data is also defined in the `.ddl` source files
  (`ddl/TRNTYPE.ddl`, `ddl/TRNTYCAT.ddl`, `ddl/XTRNTYPE.ddl`, `ddl/XTRNTYCAT.ddl`) and the
  DCLGEN copybooks (`dcl/DCLTRTYP.dcl`, `dcl/DCLTRCAT.dcl`).
