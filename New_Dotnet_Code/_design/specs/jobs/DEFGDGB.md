# JOB SPEC: DEFGDGB

## Overview

- **JCL member**: `DEFGDGB.jcl`
- **JOB name**: `DEFGDGB`
- **JOB description**: `'DEF GDG BASES'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **One-time infrastructure / file-setup job — "DEFINE GDG BASES NEEDED BY CARDDEMO PROJECT".** A single IDCAMS step that defines (creates the catalog base entry for) **six Generation Data Groups (GDGs)** used by the batch transaction/reporting/backup cycle. It creates **no data** and loads nothing; it only establishes the empty GDG base catalog entries so that later jobs can roll generations `(+1)` / read `(0)`. Each DEFINE is made **idempotent** (re-runnable) by swallowing the "already exists" condition (`IF LASTCC=12 THEN SET MAXCC=0`). This is a **bootstrap / environment-setup** job, not a posting, report, or backup job — it is the prerequisite that those jobs depend on.

## Step summary

| Step | PGM | Type | Action |
|------|-----|------|--------|
| STEP05 | IDCAMS | Utility (Access Method Services) | `DEFINE GENERATIONDATAGROUP` x6 — create the 6 GDG base catalog entries (each `LIMIT(5)`, `SCRATCH`); after each DEFINE, normalize RC so an already-existing GDG does not fail the job (`IF LASTCC=12 THEN SET MAXCC=0`) |

There is **1 EXEC step** invoking **1 utility program: `IDCAMS`**. No COBOL `CB*` program, no `SORT`, no `IEFBR14`. **GDG is the entire subject of this job** (it *defines* the GDG bases — it does not roll or read generations). **No `PARM=` and no `COND=`** are coded; failure tolerance is handled inside the IDCAMS control stream via `IF LASTCC=12 THEN SET MAXCC=0`.

---

## STEP05 — Define the 6 CardDemo GDG bases (PGM=IDCAMS)

- **EXEC**: `PGM=IDCAMS` (Access Method Services).
- **PARM=**: none.
- **COND/RC gating**: none coded on the EXEC. Re-run safety is handled **inside** the control statements: after every `DEFINE GENERATIONDATAGROUP`, the statement `IF LASTCC=12 THEN SET MAXCC=0` resets the maximum condition code to 0 whenever the immediately-preceding DEFINE returned **LASTCC=12** (the IDCAMS "duplicate name / GDG base already exists" return code). This makes the step **idempotent** — running it a second time on an already-set-up system completes RC=0 instead of failing.
- **Purpose**: create the catalog base (alias) entry for each of the six GDGs the CardDemo batch suite rolls and reads. No generation datasets are created here (that happens later when a job allocates `(+1)`).

### DD statements / datasets

| DD | Disposition | DSN | I/O | Maps to (file / relational table) |
|----|-------------|-----|-----|-----------------------------------|
| `SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | IDCAMS print / message listing. No data mapping. |
| `SYSIN` | `DD *` (instream) | (instream control) | INPUT | IDCAMS control statements (the six DEFINEs + IF/SET). No data mapping. |

- This step has **no input/output dataset DDs** beyond `SYSPRINT`/`SYSIN`; it operates purely on the **catalog** (it creates GDG base entries), not on any data records.

### Control statements (exact)

The `SYSIN` stream issues six `DEFINE GENERATIONDATAGROUP` commands, each immediately followed by the idempotency guard. All six share the same options: `LIMIT(5)` and `SCRATCH`.

```
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.TRANSACT.BKUP) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.TRANSACT.DALY) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.TRANREPT) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.TCATBALF.BKUP) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.SYSTRAN) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
   DEFINE GENERATIONDATAGROUP -
   (NAME(AWS.M2.CARDDEMO.TRANSACT.COMBINED) -
    LIMIT(5) -
    SCRATCH -
   )
   IF LASTCC=12 THEN SET MAXCC=0
```

### DEFINE option semantics (apply to all six)

- **`DEFINE GENERATIONDATAGROUP`** (a.k.a. `DEFINE GDG`): creates the GDG **base** catalog entry only. It does **not** allocate any generation; the first real generation is created later when some job allocates `(+1)` against the base.
- **`LIMIT(5)`**: the GDG retains at most **5** generations. When a 6th generation is rolled in, the oldest is rolled off.
- **`SCRATCH`**: generations rolled off the limit are **physically deleted (scratched)** from the volume and uncataloged — not merely uncataloged (the opposite would be `NOSCRATCH`). So only the most recent 5 generations physically survive.
- (No `EMPTY`/`NOEMPTY` coded → default **`NOEMPTY`**: when the limit is exceeded only the single oldest generation is rolled off, not all of them.)
- (No `OWNER`, `TO`/`FOR` retention, `BUFND/BUFNI`, etc.)

### The six GDG bases — names, role, and file/table mapping

| # | GDG base name | Role in CardDemo | Maps to (sequential file / relational table) |
|---|---------------|------------------|-----------------------------------------------|
| 1 | `AWS.M2.CARDDEMO.TRANSACT.BKUP` | **Backup of the transaction master.** New generation written by the transaction-backup job; read (`(0)`) by `COMBTRAN` as one of the two SORT inputs. | Sequential backup image of the **TRANSACTION** master (`TRAN-RECORD`, copybook `CVTRA05Y`). Logically a point-in-time snapshot of the **`TRANSACTION`** table. |
| 2 | `AWS.M2.CARDDEMO.TRANSACT.DALY` | **Daily transactions feed.** Day's posting input — daily transaction file consumed by the posting job (`CBTRN02C`/`POSTTRAN`); the `TRANFILE`/daily transaction generation. | Daily sequential transaction file (`DALYTRAN`, `TRAN-RECORD` / `CVTRA05Y`). New rows to be posted into the **`TRANSACTION`** table. |
| 3 | `AWS.M2.CARDDEMO.TRANREPT` | **Transaction report output.** Generation holding the printed transaction report produced by the transaction-report job (`CBTRN03C`). | Sequential **report print file** (133-col report lines). Not a data table — it is the formatted report extract of the **`TRANSACTION`** data. |
| 4 | `AWS.M2.CARDDEMO.TCATBALF.BKUP` | **Backup of the Transaction-Category-Balance file.** Snapshot of the TCATBAL file taken around the posting cycle. | Sequential backup image of the **Transaction Category Balance** file (`TRAN-CAT-BAL-RECORD`, copybook `CVTRA01Y`). Snapshot of the **`TRAN_CAT_BAL`** (transaction-category-balance) table. |
| 5 | `AWS.M2.CARDDEMO.SYSTRAN` | **System-generated transactions.** Transactions created by online/system activity awaiting consolidation; read (`(0)`) by `COMBTRAN` as the second SORT input. | Sequential system-transaction file (`TRAN-RECORD` / `CVTRA05Y`). New rows destined for the **`TRANSACTION`** table. |
| 6 | `AWS.M2.CARDDEMO.TRANSACT.COMBINED` | **Combined/sorted transaction file.** Produced by `COMBTRAN` STEP05R (SORT of `TRANSACT.BKUP(0)` + `SYSTRAN(0)`); consumed by `COMBTRAN` STEP10 (IDCAMS REPRO) to reload the transaction master KSDS. | Sorted union sequential file of the two transaction inputs (`TRAN-RECORD` / `CVTRA05Y`). Staging extract feeding the **`TRANSACTION`** master/table. |

- **Cross-reference**: bases #1 (`TRANSACT.BKUP`), #5 (`SYSTRAN`), and #6 (`TRANSACT.COMBINED`) are exactly the three GDGs used by `COMBTRAN.jcl` (see `COMBTRAN.md`). Base #2 (`TRANSACT.DALY`) is the daily posting feed; #3 (`TRANREPT`) is the report output; #4 (`TCATBALF.BKUP`) is the category-balance backup.

---

## PARM / COND / GDG / SORT / IDCAMS summary

- **PARM=**: none on the EXEC.
- **COND/RC gating**: no `COND=` on the JOB or EXEC. RC handling is **inside IDCAMS** — `IF LASTCC=12 THEN SET MAXCC=0` after each DEFINE (idempotent "already exists" swallow). Net step RC is 0 on both first run (all six DEFINEs succeed, LASTCC=0) and re-run (DEFINEs return LASTCC=12 → reset to MAXCC=0).
- **GDG**: this job **defines six GDG bases** (it is the GDG-bootstrap job). It does **not** roll `(+1)` or read `(0)` — that is done by the downstream jobs that use these bases. Each base: `LIMIT(5)`, `SCRATCH`, default `NOEMPTY`.
- **SORT**: not used; no `SORT FIELDS`.
- **IDCAMS**: the only program. Six `DEFINE GENERATIONDATAGROUP` commands (no `DELETE`, no `REPRO`, no `DEFINE CLUSTER`) plus six `IF LASTCC=12 THEN SET MAXCC=0` guards.
- **IEFBR14**: not used.

## Conversion notes for the .NET JobControl step-runner

- Model this as a **single bootstrap step** that **registers/creates six GDG base definitions** in the .NET equivalent of the catalog. In the SQLite-backed model there is no physical VSAM catalog, so a GDG base becomes a small **catalog/metadata record** describing a generation series: `{ baseName, limit=5, scratchOnRolloff=true }`. Generations themselves are created later by the jobs that allocate `(+1)`.
- **Idempotent create** is required: creating a base that already exists must be a no-op returning success (the `IF LASTCC=12 THEN SET MAXCC=0` semantic). Do not error on "already defined."
- Honor **`LIMIT(5)` + `SCRATCH`** retention in the GDG abstraction: keep only the latest 5 generations of each base; on rollover, **physically delete** the rolled-off oldest generation (SCRATCH), and roll off **one at a time** (NOEMPTY default), not the whole group.
- This job must run **before** any job that references these GDGs (`COMBTRAN`, daily posting / `CBTRN02C`, transaction-backup, TCATBAL-backup, transaction-report). Treat it as a one-time environment-setup prerequisite in the run sequence.
- The six bases and their `(0)`/`(+1)` usage are the integration contract: `TRANSACT.BKUP`, `TRANSACT.DALY`, `TRANREPT`, `TCATBALF.BKUP`, `SYSTRAN`, `TRANSACT.COMBINED`. Keep the exact names so downstream specs resolve.
- No data is read or written here — purely catalog/metadata setup. There is no relational table populated by this job; it only enables the generation series that later feed the **`TRANSACTION`** and **`TRAN_CAT_BAL`** tables and the transaction report.
