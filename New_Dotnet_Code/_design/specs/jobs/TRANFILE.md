# JOB SPEC: TRANFILE

## Overview

- **JOB name:** `TRANFILE`
- **JOB description (positional):** `'DEFINE TRANSACTION MASTER'`
- **Class / MsgClass:** `CLASS=A`, `MSGCLASS=0`
- **Notify:** `&SYSUID`
- **Source member:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/TRANFILE.jcl`
- **Version stamp:** `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

### Purpose

This is a **file-setup / VSAM bootstrap job** for the **Transaction Master** file. It (re)builds the
indexed transaction file used by CICS and the daily-transaction posting cycle. End to end it:

1. Quiesces (closes) the relevant files in the live CICS region so the datasets can be reorganized.
2. Deletes any pre-existing Transaction Master KSDS cluster and its alternate index.
3. Defines a fresh KSDS cluster for the Transaction Master.
4. Loads (REPRO) the KSDS from an initial flat (sequential) seed file.
5. Defines an alternate index (AIX) keyed on the processed-transaction timestamp, plus a PATH, and
   builds (BLDINDEX) the AIX from the base cluster.
6. Re-opens (enables) the files in CICS so online processing can resume.

This is **infrastructure / initialization**, not business posting logic. It corresponds to the
relational **TRANSACTION** master table (the VSAM KSDS) and its secondary access path
(the alternate index → a secondary index / lookup on the transaction processing timestamp).

### Relational / file mapping (target .NET model)

| Mainframe dataset | Kind | Maps to (target) |
|---|---|---|
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | VSAM KSDS, key 16 bytes @ off 0, LRECL 350 | **TRANSACTION** master table (primary key = 16-char transaction ID) |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX` | VSAM alternate index, key 26 bytes @ off 304, non-unique | Secondary index on TRANSACTION (processed/original timestamp), non-unique |
| `AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.PATH` | VSAM PATH | Alternate access path linking AIX → base table (CICS file `CXACAIX`) |
| `AWS.M2.CARDDEMO.DALYTRAN.PS.INIT` | Sequential (flat) seed file | Initial/seed load data for the TRANSACTION table |

### COND / RC gating, GDG

- No `COND=` parameters on any EXEC step; steps run unconditionally in sequence.
- RC gating is done **inside IDCAMS** via `IF MAXCC LE 08 THEN SET MAXCC = 0` after each DELETE in STEP05
  (so a not-found DELETE, max RC 8, is reset to 0 and does not fail the job).
- **No GDG** datasets are used; all datasets are fixed-name (no `(+1)`/`(0)` generations).

---

## Steps (in order)

### Step 1 — `CLCIFIL` : `EXEC PGM=SDSF`
- **Program/utility:** SDSF (batch interface), used to issue MVS console/CICS modify commands.
- **Purpose:** Close the files in the live CICS region `CICSAWSA` before the dataset is deleted/rebuilt.
- **DD / datasets:**
  - `ISFOUT DD SYSOUT=*` — SDSF panel output.
  - `CMDOUT DD SYSOUT=*` — command response output.
  - `ISFIN DD *` — in-stream SDSF command input.
- **Control statements (CICS CEMT via /F modify):**
  ```
  /F CICSAWSA,'CEMT SET FIL(TRANSACT ) CLO'
  /F CICSAWSA,'CEMT SET FIL(CXACAIX ) CLO'
  ```
  Closes CICS file `TRANSACT` (base Transaction Master) and `CXACAIX` (alternate-index access).
- **PARM / COND:** none.

### Step 2 — `STEP05` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS (Access Method Services).
- **Purpose:** Delete the existing Transaction Master KSDS cluster and its alternate index if they already exist (idempotent teardown).
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS messages.
  - `SYSIN DD *` — control statements.
- **Control statements:**
  ```
  DELETE AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  DELETE AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX -
         ALTERNATEINDEX
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
- **RC gating:** `IF MAXCC LE 08 THEN SET MAXCC = 0` after each DELETE — tolerates "not found" so the job proceeds on a clean system. Target: drop table/index if exists; ignore not-found.

### Step 3 — `STEP10` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS.
- **Purpose:** Define the new Transaction Master KSDS cluster (the TRANSACTION table).
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*`.
  - `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 ) -
         KEYS(16 0) -
         RECORDSIZE(350 350) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS.DATA) ) -
         INDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS.INDEX) )
  ```
- **Key attributes (for target schema):**
  - Primary key: 16 bytes at offset 0 (`KEYS(16 0)`) → 16-char transaction ID primary key.
  - Record length: fixed 350 bytes (`RECORDSIZE(350 350)`).
  - INDEXED (KSDS), ERASE on delete, SHAREOPTIONS(2 3), primary alloc 1 cyl / secondary 5 cyl on volume AWSHJ1.

### Step 4 — `STEP15` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS (REPRO).
- **Purpose:** Load the KSDS from the initial flat seed file (populate the TRANSACTION table).
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*`.
  - `TRANSACT DD DISP=SHR, DSN=AWS.M2.CARDDEMO.DALYTRAN.PS.INIT` — **input**: sequential seed file (read).
  - `TRANVSAM DD DISP=SHR, DSN=AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` — **output**: the KSDS just defined (write).
  - `SYSIN DD *`.
- **Control statements:**
  ```
  REPRO INFILE(TRANSACT) OUTFILE(TRANVSAM)
  ```
- **Mapping:** copy/insert every record from sequential `DALYTRAN.PS.INIT` into the TRANSACTION table keyed by the 16-byte transaction ID.

### Step 5 — `STEP20` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS.
- **Purpose:** Define the alternate index over the Transaction Master, keyed on the processed-timestamp field.
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*`.
  - `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE ALTERNATEINDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX) -
    RELATE(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS) -
    KEYS(26 304) -
    NONUNIQUEKEY -
    UPGRADE -
    RECORDSIZE(350,350) -
    VOLUMES(AWSHJ1) -
    CYLINDERS(5,1)) -
    DATA (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.DATA)) -
    INDEX (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.INDEX))
  ```
- **Key attributes (for target schema):**
  - Alternate key: 26 bytes at offset 304 (`KEYS(26 304)`) → secondary index on the 26-char timestamp field inside the 350-byte record.
  - `NONUNIQUEKEY` (multiple rows may share a timestamp), `UPGRADE` (kept in sync on base updates).
  - RELATE to base cluster `...TRANSACT.VSAM.KSDS`.

### Step 6 — `STEP25` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS.
- **Purpose:** Define a PATH that relates the alternate index to the base cluster (the CICS `CXACAIX` access path).
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*`.
  - `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE PATH -
    (NAME(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX.PATH) -
     PATHENTRY(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX))
  ```
- **Mapping:** the PATH is the named access route through the alternate index; in the target it is the secondary-index lookup linking AIX → base TRANSACTION table.

### Step 7 — `STEP30` : `EXEC PGM=IDCAMS`
- **Program/utility:** IDCAMS (BLDINDEX).
- **Purpose:** Build/populate the alternate index from the base cluster after the data load.
- **DD / datasets:**
  - `SYSPRINT DD SYSOUT=*`.
  - `SYSIN DD *`.
- **Control statements:**
  ```
  BLDINDEX -
    INDATASET(AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS) -
    OUTDATASET(AWS.M2.CARDDEMO.TRANSACT.VSAM.AIX)
  ```
- **Mapping:** read all base records and populate the secondary index entries (timestamp → primary key). In the target this is equivalent to creating/refreshing the secondary index on the now-populated TRANSACTION table.

### Step 8 — `OPCIFIL` : `EXEC PGM=SDSF`
- **Program/utility:** SDSF (batch interface) issuing CICS modify commands.
- **Purpose:** Re-open (enable) the files in CICS region `CICSAWSA` so online access resumes after the rebuild.
- **DD / datasets:**
  - `ISFOUT DD SYSOUT=*`.
  - `CMDOUT DD SYSOUT=*`.
  - `ISFIN DD *` — in-stream commands.
- **Control statements:**
  ```
  /F CICSAWSA,'CEMT SET FIL(TRANSACT ) OPE'
  /F CICSAWSA,'CEMT SET FIL(CXACAIX ) OPE'
  ```
  Re-opens CICS file `TRANSACT` (base) and `CXACAIX` (alternate-index path).
- **PARM / COND:** none.

---

## Notes for the .NET JobControl step-runner

- **No business COBOL (CB*) program is invoked** — every step is a utility: SDSF (CICS file open/close) or IDCAMS (VSAM lifecycle). No SORT step is present.
- **SDSF steps (CLCIFIL / OPCIFIL)** are CICS-region file-state management. In a relational/.NET target they translate to "no-op / take-offline" and "bring-online" guards around the table rebuild (or can be omitted if the table is not concurrently served by an online region).
- **IDCAMS steps** map to: DROP-IF-EXISTS table+index (STEP05, with not-found tolerated), CREATE table (STEP10), bulk LOAD from seed file (STEP15), CREATE secondary index (STEP20/STEP25/STEP30, with the index populated from the freshly loaded data).
- The DELETE step is intentionally idempotent (`IF MAXCC LE 08 THEN SET MAXCC = 0`); the runner should not treat a missing-object delete as a failure.
- No GDG generations and no `COND=` step gating; steps execute strictly in listed order.
