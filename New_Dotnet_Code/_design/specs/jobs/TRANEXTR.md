# JOB SPEC: TRANEXTR

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-transaction-type-db2/jcl/TRANEXTR.jcl`

## Overall Purpose

**Daily extract of two DB2 reference tables into flat sequential files, with GDG
backup + cleanup of the prior run.** The JCL header states: *"This JCL will extract
reference data for use in Transaction Report generation. It runs once a day. So
changes will reflect in report only after the daily batch is run."*

It is a **DB2 UNLOAD / extract + backup / file-setup** job (not posting, not online
maintenance). The two DB2 tables `CARDDEMO.TRANSACTION_TYPE` and
`CARDDEMO.TRANSACTION_TYPE_CATEGORY` are unloaded — via the `DSNTIAUL` sample
unload utility run under the TSO/E TMP (`IKJEFT01`) — into two fixed 60-byte
sequential files (`...TRANTYPE.PS` and `...TRANCATG.PS`) that downstream
transaction-report jobs consume as flat reference files. Before the fresh unload,
the previous generations of those two PS files are archived to GDGs and then
deleted.

### Sequence of operations (per the step order)

1. **STEP10** — copy the *existing* `TRANTYPE.PS` to a new GDG generation (backup).
2. **STEP20** — copy the *existing* `TRANCATG.PS` to a new GDG generation (backup).
3. **STEP30** — delete both `TRANTYPE.PS` and `TRANCATG.PS` from the prior run.
4. **STEP40** — DB2 UNLOAD `TRANSACTION_TYPE` → fresh `TRANTYPE.PS`.
5. **STEP50** — DB2 UNLOAD `TRANSACTION_TYPE_CATEGORY` → fresh `TRANCATG.PS`.

> Note: the SET on line 25 fixes `HLQ=AWS.M2.CARDDEMO`, so every `&HLQ..` below
> expands to `AWS.M2.CARDDEMO.` (e.g. `AWS.M2.CARDDEMO.TRANTYPE.PS`).

## Programs / Utilities Invoked

| Step | PGM | Role |
|------|-----|------|
| STEP10 | `IEBGENER` | Sequential copy utility — backup `TRANTYPE.PS` → GDG `(+1)` |
| STEP20 | `IEBGENER` | Sequential copy utility — backup `TRANCATG.PS` → GDG `(+1)` |
| STEP30 | `IEFBR14` | No-op program used only to allocate/`DELETE` the two PS datasets |
| STEP40 | `IKJEFT01` | TSO/E batch TMP; runs DB2 `DSNTIAUL` unload utility (plan `DSNTIAUL`) |
| STEP50 | `IKJEFT01` | TSO/E batch TMP; runs DB2 `DSNTIAUL` unload utility (plan `DSNTIAUL`) |

No CardDemo `CB*` COBOL program and no `SORT` step is present in this job. The
real "extract" work is done by the DB2 sample utility **`DSNTIAUL`** invoked from
`SYSTSIN` under `IKJEFT01`.

## JOB Card

```
//TRANEXTR JOB 'EXTRACT TRAN TYPE',
// CLASS=A,MSGCLASS=0,NOTIFY=&SYSUID
```

| Attribute | Value |
|-----------|-------|
| Job name | `TRANEXTR` |
| Accounting / desc | `'EXTRACT TRAN TYPE'` |
| CLASS | `A` |
| MSGCLASS | `0` |
| NOTIFY | `&SYSUID` (submitting TSO user) |

### Symbolics

```
//   SET HLQ=AWS.M2.CARDDEMO
```

| Symbol | Value |
|--------|-------|
| `HLQ` | `AWS.M2.CARDDEMO` |

## Datasets / Tables Involved (resolved)

| Dataset (DSN) | Type | Maps to |
|---|---|---|
| `AWS.M2.CARDDEMO.TRANTYPE.PS` | Sequential, LRECL=60 FB | Flat reference extract of DB2 table `CARDDEMO.TRANSACTION_TYPE`. Read by STEP10 (backup) + STEP30 (delete); (re)written by STEP40. |
| `AWS.M2.CARDDEMO.TRANCATG.PS` | Sequential, LRECL=60 FB | Flat reference extract of DB2 table `CARDDEMO.TRANSACTION_TYPE_CATEGORY`. Read by STEP20 (backup) + STEP30 (delete); (re)written by STEP50. |
| `AWS.M2.CARDDEMO.TRANTYPE.BKUP(+1)` | Sequential GDG, new gen, LRECL=60 RECFM=FB BLKSIZE=600 | Backup/archive generation of `TRANTYPE.PS`. |
| `AWS.M2.CARDDEMO.TRANCATG.PS.BKUP(+1)` | Sequential GDG, new gen, LRECL=60 RECFM=FB BLKSIZE=600 | Backup/archive generation of `TRANCATG.PS`. |
| `OEM.DB2.DAZ1.RUNLIB.LOAD` | Load library (SHR) | DB2 DAZ1 runtime library (STEPLIB for the unloads). |
| `OEMA.DB2.VERSIONA.SDSNLOAD` | Load library (SHR) | DB2 base load library (`DSN`/`DSNTIAUL` modules). |

**GDG usage:** both backup steps write **relative generation `(+1)`** of their GDG
bases (`...TRANTYPE.BKUP` and `...TRANCATG.PS.BKUP`) with `DISP=(NEW,CATLG)` and
`SPACE=(TRK,(1,1),RLSE)` — a new generation is cataloged each daily run. (The GDG
base/limit definitions live in a separate DEFGDG-style job, not here.)

**Relational tables (DB2 subsystem `DAZ1`, schema `CARDDEMO`):**

- `CARDDEMO.TRANSACTION_TYPE` — DDL `ddl/TRNTYPE.ddl`, DCLGEN `dcl/DCLTRTYP.dcl`:
  | Column | Type | Notes |
  |--------|------|-------|
  | `TR_TYPE` | `CHAR(2)` NOT NULL | Primary key (transaction-type code) |
  | `TR_DESCRIPTION` | `VARCHAR(50)` NOT NULL | Description |
- `CARDDEMO.TRANSACTION_TYPE_CATEGORY` — DDL `ddl/TRNTYCAT.ddl`, DCLGEN
  `dcl/DCLTRCAT.dcl`:
  | Column | Type | Notes |
  |--------|------|-------|
  | `TRC_TYPE_CODE` | `CHAR(2)` NOT NULL | PK part 1; FK → `TRANSACTION_TYPE.TR_TYPE` (ON DELETE RESTRICT) |
  | `TRC_TYPE_CATEGORY` | `CHAR(4)` NOT NULL | PK part 2 |
  | `TRC_CAT_DATA` | `VARCHAR(50)` NOT NULL | Category description/data |

---

## Steps (in execution order)

### STEP10 — EXEC PGM=IEBGENER (backup current TRANTYPE.PS → GDG)

- **PGM:** `IEBGENER` (sequential copy utility).
- **PARM:** none.
- **COND/RC gating:** none (runs first, unconditionally).
- **DDs:**
  | DD | DSN / target | DISP | R/W | Notes |
  |----|--------------|------|-----|-------|
  | `SYSPRINT` | `SYSOUT=*` | — | write | utility messages |
  | `SYSIN` | `DUMMY` | — | — | no edit control statements → straight copy |
  | `SYSUT1` | `AWS.M2.CARDDEMO.TRANTYPE.PS` | SHR | read | INPUT = current TRANTYPE extract |
  | `SYSUT2` | `AWS.M2.CARDDEMO.TRANTYPE.BKUP(+1)` | NEW,CATLG | write | OUTPUT = new GDG backup gen; `DCB=(LRECL=60,RECFM=FB,BLKSIZE=600)`, `SPACE=(TRK,(1,1),RLSE)` |
- **Effect:** archives the prior `TRANTYPE.PS` to a new GDG generation.

### STEP20 — EXEC PGM=IEBGENER,COND=(0,NE) (backup current TRANCATG.PS → GDG)

- **PGM:** `IEBGENER`.
- **PARM:** none.
- **COND/RC gating:** `COND=(0,NE)` — *bypass this step if any prior step's RC ≠ 0*.
  i.e. it runs only when STEP10 ended RC=0.
- **DDs:**
  | DD | DSN / target | DISP | R/W | Notes |
  |----|--------------|------|-----|-------|
  | `SYSPRINT` | `SYSOUT=*` | — | write | utility messages |
  | `SYSIN` | `DUMMY` | — | — | straight copy (no edit) |
  | `SYSUT1` | `AWS.M2.CARDDEMO.TRANCATG.PS` | SHR | read | INPUT = current TRANCATG extract |
  | `SYSUT2` | `AWS.M2.CARDDEMO.TRANCATG.PS.BKUP(+1)` | NEW,CATLG | write | OUTPUT = new GDG backup gen; `DCB=(LRECL=60,RECFM=FB,BLKSIZE=600)`, `SPACE=(TRK,(1,1),RLSE)` |
- **Effect:** archives the prior `TRANCATG.PS` to a new GDG generation.

### STEP30 — EXEC PGM=IEFBR14,COND=(0,NE) (delete prior-run PS files)

- **PGM:** `IEFBR14` (do-nothing program; the DD `DISP` does the work).
- **PARM:** none.
- **COND/RC gating:** `COND=(0,NE)` — runs only if all prior steps ended RC=0.
- **DDs (both use `DISP=(MOD,DELETE,DELETE)`, `UNIT=SYSDA`, `SPACE=(TRK,(1,1))`):**
  | DD | DSN | DISP | Effect |
  |----|-----|------|--------|
  | `DD01` | `AWS.M2.CARDDEMO.TRANTYPE.PS` | `(MOD,DELETE,DELETE)` | (re)allocate then delete — removes the old TRANTYPE.PS so STEP40 can create it fresh |
  | `DD02` | `AWS.M2.CARDDEMO.TRANCATG.PS` | `(MOD,DELETE,DELETE)` | (re)allocate then delete — removes the old TRANCATG.PS so STEP50 can create it fresh |
- **Effect:** deletes both flat extract datasets left over from the previous run.
  (`MOD` allocates if absent, so this is tolerant of a missing dataset.)

### STEP40 — EXEC PGM=IKJEFT01,COND=(0,NE) (DB2 UNLOAD TRANSACTION_TYPE → TRANTYPE.PS)

- **PGM:** `IKJEFT01` (TSO/E batch terminal monitor program). The actual work is the
  DB2 utility `DSNTIAUL` driven from `SYSTSIN`.
- **PARM:** none on EXEC. Utility parm is `PARMS('SQL')` (DSNTIAUL "free-form SQL"
  mode — unload the result of the `SYSIN` SELECT rather than a whole tablespace).
- **COND/RC gating:** `COND=(0,NE)` — runs only if all prior steps ended RC=0.
- **GDG usage:** none in this step.
- **DDs:**
  | DD | DSN / target | DISP | R/W | Notes |
  |----|--------------|------|-----|-------|
  | `STEPLIB` | `OEM.DB2.DAZ1.RUNLIB.LOAD` + `OEMA.DB2.VERSIONA.SDSNLOAD` | SHR | read | DB2 runtime + base load libs (concatenated) |
  | `SYSTSPRT` | `SYSOUT=*` | — | write | TSO/DSN run log |
  | `SYSPRINT` | `SYSOUT=*` | — | write | DSNTIAUL messages |
  | `SYSUDUMP` | `SYSOUT=*` | — | write | dump on abend |
  | `SYSPUNCH` | `DUMMY` | — | — | discarded LOAD-control output (not used) |
  | `SYSREC00` | `AWS.M2.CARDDEMO.TRANTYPE.PS` | `(NEW,CATLG,DELETE)` | write | OUTPUT = unloaded rows; `SPACE=(TRK,(1,1),RLSE)` |
  | `SYSIN` | inline `DD *` | — | read | the SELECT to unload (below) |
  | `SYSTSIN` | inline `DD *` | — | read | TSO/DSN commands launching DSNTIAUL (below) |
- **SYSIN (the unload SELECT):**
  ```sql
  SELECT CAST(CONCAT(CONCAT(
    TR_TYPE
   ,CAST(TR_DESCRIPTION AS CHAR(50))
    )
   ,REPEAT('0',8)
  ) AS CHAR(60))
   FROM
   CARDDEMO.TRANSACTION_TYPE
   ORDER BY TR_TYPE;
  ```
  → Builds one **fixed 60-byte** output column per row: `TR_TYPE` (2) ‖
  `TR_DESCRIPTION` right-padded to CHAR(50) ‖ 8 ASCII/EBCDIC `'0'` filler bytes =
  2+50+8 = 60. Rows ordered by `TR_TYPE`. This is the record layout of
  `TRANTYPE.PS` (LRECL 60).
- **SYSTSIN (launch DSNTIAUL):**
  ```
  DSN SYSTEM(DAZ1)
  RUN PROGRAM(DSNTIAUL) -
  PLAN(DSNTIAUL) -
  PARMS('SQL')
  ```
  → Connect to DB2 subsystem `DAZ1`; run the `DSNTIAUL` load module under plan
  `DSNTIAUL`; `PARMS('SQL')` selects free-form-SQL unload of the `SYSIN` query
  into `SYSREC00`.

### STEP50 — EXEC PGM=IKJEFT01,COND=(4,LT) (DB2 UNLOAD TRANSACTION_TYPE_CATEGORY → TRANCATG.PS)

> The step banner comment says "EXTRACT DATA FROM TRANSACTION TYPE TABLE" — this is a
> copy/paste of STEP40's banner; the SQL actually unloads the **category** table.

- **PGM:** `IKJEFT01`; runs DB2 `DSNTIAUL` from `SYSTSIN`.
- **PARM:** none on EXEC; utility `PARMS('SQL')` (free-form SQL unload).
- **COND/RC gating:** `COND=(4,LT)` — *bypass this step if 4 is LESS THAN a prior
  step's RC*, i.e. **skip if any preceding step ended with RC > 4** (RC ≥ 5). With
  normal RC=0/RC=4 upstream the step runs. (Note STEP40 uses the stricter
  `COND=(0,NE)`; STEP50 tolerates RCs of 1–4 from earlier steps.)
- **GDG usage:** none in this step.
- **DDs:**
  | DD | DSN / target | DISP | R/W | Notes |
  |----|--------------|------|-----|-------|
  | `STEPLIB` | `OEM.DB2.DAZ1.RUNLIB.LOAD` + `OEMA.DB2.VERSIONA.SDSNLOAD` | SHR | read | DB2 runtime + base load libs |
  | `SYSTSPRT` | `SYSOUT=*` | — | write | TSO/DSN run log |
  | `SYSPRINT` | `SYSOUT=*` | — | write | DSNTIAUL messages |
  | `SYSUDUMP` | `SYSOUT=*` | — | write | dump on abend |
  | `SYSPUNCH` | `DUMMY` | — | — | discarded LOAD-control output |
  | `SYSREC00` | `AWS.M2.CARDDEMO.TRANCATG.PS` | `(NEW,CATLG,DELETE)` | write | OUTPUT = unloaded rows; `SPACE=(TRK,(1,1),RLSE)` |
  | `SYSIN` | inline `DD *` | — | read | the SELECT to unload (below) |
  | `SYSTSIN` | inline `DD *` | — | read | DSN/RUN commands (below) |
- **SYSIN (the unload SELECT):**
  ```sql
  SELECT CAST(
        TRC_TYPE_CODE
     || TRC_TYPE_CATEGORY
     || CAST(TRC_CAT_DATA AS CHAR(50))
     || REPEAT('0',4)
                      AS CHAR(60))
  FROM  CARDDEMO.TRANSACTION_TYPE_CATEGORY
  ORDER BY
        TRC_TYPE_CODE
      , TRC_TYPE_CATEGORY;
  ```
  → Builds one **fixed 60-byte** column per row: `TRC_TYPE_CODE` (2) ‖
  `TRC_TYPE_CATEGORY` (4) ‖ `TRC_CAT_DATA` right-padded to CHAR(50) ‖ 4 `'0'`
  filler bytes = 2+4+50+4 = 60. Rows ordered by (`TRC_TYPE_CODE`,
  `TRC_TYPE_CATEGORY`). This is the record layout of `TRANCATG.PS` (LRECL 60).
- **SYSTSIN (launch DSNTIAUL):**
  ```
  DSN SYSTEM(DAZ1)
  RUN PROGRAM(DSNTIAUL) -
  PLAN(DSNTIAUL) -
  PARMS('SQL')
  ```

---

## Control statements summary (IDCAMS / SORT)

- **No IDCAMS and no SORT** in this job — so no `DEFINE`/`REPRO`/`DELETE` IDCAMS
  cards and no `SORT FIELDS`. Record ordering is done by the DB2 `ORDER BY` inside
  each unload SELECT, not by a SORT utility.
- The only "utilities with control input" are the two `DSNTIAUL` unloads (control =
  the `SYSIN` SELECT + `SYSTSIN` `DSN`/`RUN` command, both shown per step above).
- `IEBGENER` STEP10/STEP20 use `SYSIN DD DUMMY` → no IEBGENER edit/control
  statements; they are plain record-for-record copies.

## .NET JobControl mapping notes

- 5 steps → 5 runner actions, executed in order, with the COND gating preserved:
  - STEP10: unconditional.
  - STEP20, STEP30, STEP40: run only if all prior steps RC=0 (`COND=(0,NE)`).
  - STEP50: run unless a prior step RC > 4 (`COND=(4,LT)`).
- STEP10/STEP20 (`IEBGENER` + `SYSIN DUMMY`) → a straight sequential file copy of
  the existing `*.PS` into a new GDG generation `(+1)` (LRECL 60, FB, BLKSIZE 600);
  resolve `(+1)` against the GDG base catalog and catalog the new generation.
- STEP30 (`IEFBR14` + `DISP=(MOD,DELETE,DELETE)`) → delete the two `*.PS` datasets,
  tolerant of "not found" (MOD allocates if absent before deleting).
- STEP40/STEP50 (`IKJEFT01` + `DSN ... RUN PROGRAM(DSNTIAUL) PLAN(DSNTIAUL)
  PARMS('SQL')`) → "run a DB2 free-form-SQL unload": execute the `SYSIN` SELECT
  against subsystem `DAZ1` and stream each result row (already a single CHAR(60)
  value) as a fixed 60-byte record into the `SYSREC00` output file
  (`TRANTYPE.PS` / `TRANCATG.PS`, `DISP=(NEW,CATLG,DELETE)`).
  - In the ported world the "tables" are the same logical reference data the online
    CTTU/CTLI/category screens maintain; the extract produces the flat 60-byte
    reference files used by the transaction-report jobs.
  - Output record layouts are encoded by the SELECTs above:
    - TRANTYPE.PS: `TR_TYPE`(2) + `TR_DESCRIPTION→CHAR(50)` + `'00000000'`(8) = 60.
    - TRANCATG.PS: `TRC_TYPE_CODE`(2) + `TRC_TYPE_CATEGORY`(4) +
      `TRC_CAT_DATA→CHAR(50)` + `'0000'`(4) = 60.
  - Preserve the `ORDER BY` so the flat files are key-ordered.
- No COBOL `CB*` program, no SORT, no IDCAMS to emulate.
