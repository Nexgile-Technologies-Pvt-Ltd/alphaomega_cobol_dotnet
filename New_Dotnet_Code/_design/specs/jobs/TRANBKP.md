# JOB SPEC: TRANBKP

**Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/TRANBKP.jcl`
**Referenced PROC:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/proc/REPROC.prc`
**Referenced CNTL member:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/ctl/REPROCT.ctl` (= `&CNTLLIB(REPROCT)`)

## Overall Purpose

**Backup + re-create of the Transaction Master VSAM KSDS.**

The job description on the JOB card is *"REPRO and Delete Transaction Master"*. It does three things, in order:

1. **Backup** the current Transaction Master VSAM KSDS to a new sequential GDG generation (the "processed transaction file" snapshot).
2. **Delete** the existing Transaction Master VSAM cluster and its alternate index (idempotent cleanup; tolerates "not found").
3. **Re-define** an empty Transaction Master VSAM KSDS cluster so downstream posting/processing jobs can reload it.

This is a **file-setup / backup / reset** job, not a posting or reporting job. Net effect: yesterday's (or the prior run's) transaction file is archived to a GDG, then the live VSAM file is emptied/recreated as a fresh, empty KSDS.

## Environment / Setup

- `//JOBLIB JCLLIB ORDER=('AWS.M2.CARDDEMO.PROC')` — adds the CardDemo PROC library to the JCLLIB search order so `PROC=REPROC` resolves.
- JOB card: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`.

## Datasets / Tables Involved

| Dataset (DSN) | Type | Maps to |
|---|---|---|
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | VSAM KSDS, 16-byte key, RECSIZE 350 | **Transaction Master** — relational table `TRANSACTION` (a.k.a. TRANSACT). Keyed by the 16-char transaction ID. |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX` | VSAM Alternate Index over the KSDS | Alternate access path on the TRANSACTION table |
| `AWS.M2.CARDDEMO.TRANSACT.BKUP(+1)` | Sequential GDG, new generation, LRECL=350 RECFM=FB | **Backup/archive** sequential file of the TRANSACTION table contents |

GDG usage: the backup output uses **relative generation `(+1)`** of GDG base `AWS.M2.CARDDEMO.TRANSACT.BKUP`, i.e. a new generation is cataloged each run (`DISP=(NEW,CATLG,DELETE)`).

---

## Steps (in execution order)

### STEP05R — EXEC PROC=REPROC (backup via IDCAMS REPRO)

- **Step type:** `EXEC PROC=REPROC` with `CNTLLIB=AWS.M2.CARDDEMO.CNTL`.
- **Effective program (inside PROC step `PRC001`):** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** none (runs first, unconditionally).
- **DD overrides supplied by the job (override the PROC's NULLFILE placeholders):**
  - `PRC001.FILEIN  DD DISP=SHR, DSN=AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS`
    - INPUT = the live Transaction Master VSAM KSDS (TRANSACTION table).
  - `PRC001.FILEOUT DD DISP=(NEW,CATLG,DELETE), UNIT=SYSDA, DCB=(LRECL=350,RECFM=FB,BLKSIZE=0), SPACE=(CYL,(1,1),RLSE), DSN=AWS.M2.CARDDEMO.TRANSACT.BKUP(+1)`
    - OUTPUT = new GDG generation, the backup of the transaction file.
- **DDs from the PROC itself:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD DISP=SHR, DSN=&CNTLLIB(REPROCT)` — control statements from member `REPROCT`.
- **IDCAMS control statements (from `REPROCT.ctl`):**
  ```
  REPRO INFILE(FILEIN) OUTFILE(FILEOUT)
  ```
  → Copies every record from the KSDS (`FILEIN`) to the sequential GDG backup (`FILEOUT`). This is the **backup/unload** operation.

### STEP05 — EXEC PGM=IDCAMS (delete existing cluster + AIX)

- **Program/utility:** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** none (always runs). Internal cleanup is made idempotent via `IF MAXCC LE 08 THEN SET MAXCC = 0` so a "not found" (RC=8) does not fail the step.
- **DDs:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — inline control statements.
- **IDCAMS control statements (inline):**
  ```
  DELETE AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  DELETE AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX -
         ALTERNATEINDEX
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
  → Deletes the Transaction Master KSDS cluster and its alternate index if they exist; resets MAXCC to 0 after each so the step ends RC=0 even when the objects were absent.

### STEP10 — EXEC PGM=IDCAMS,COND=(4,LT) (define fresh empty cluster)

- **Program/utility:** `PGM=IDCAMS`.
- **PARM:** none.
- **COND/RC gating:** `COND=(4,LT)` — "bypass this step if 4 is LESS THAN a prior step's return code", i.e. **skip the DEFINE if any preceding step ended with RC > 4** (RC of 5+). With normal RC=0 results upstream, the step runs.
- **DDs:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — inline control statements.
- **IDCAMS control statements (inline):**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(16 0) -
         RECORDSIZE(350 350) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS.INDEX) -
         )
  ```
  → Re-creates an **empty** KSDS for the Transaction Master:
  - Primary key: 16 bytes at offset 0 (`KEYS(16 0)`).
  - Fixed record size 350 (`RECORDSIZE(350 350)`), matching the LRECL=350 backup.
  - Space: `CYLINDERS(1 5)` (1 primary, 5 secondary).
  - Volume `AWSHJ1`; `SHAREOPTIONS(2 3)`; `ERASE`; `INDEXED`.
  - Separate DATA and INDEX components named `.KSDS.DATA` / `.KSDS.INDEX`.

---

## .NET JobControl Mapping Notes

- **STEP05R** → a REPRO/unload action: stream all rows of the `TRANSACTION` table (VSAM KSDS abstraction) into a new sequential, fixed-350, FB backup file representing GDG generation `+1`. Resolve `(+1)` against the GDG base catalog and catalog the new generation.
- **STEP05** → a DELETE action against the VSAM KSDS + its alternate index; must be tolerant of "object not found" (treat RC<=8 as success, normalize to 0), mirroring `IF MAXCC LE 08 THEN SET MAXCC = 0`.
- **STEP10** → a DEFINE action that (re)creates an empty KSDS with key length 16 at offset 0, record size 350, with DATA and INDEX sub-objects; gated to skip when any prior step RC > 4.
- All three steps invoke **IDCAMS** (STEP05R indirectly via PROC REPROC). No COBOL `CB*` program and no SORT are involved in this job.
