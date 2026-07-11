# JOB SPEC: TRANTYPE

## Overview

| Attribute | Value |
|-----------|-------|
| **Job name** | `TRANTYPE` |
| **Source JCL** | `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/TRANTYPE.jcl` |
| **JOB description** | `'DEFINE TRAN TYPE'` |
| **CLASS** | `A` |
| **MSGCLASS** | `0` |
| **NOTIFY** | `&SYSUID` |
| **Step count** | 3 (`STEP05`, `STEP10`, `STEP15`) |
| **Programs / utilities invoked** | `IDCAMS` (all 3 steps) |
| **Version stamp** | `CardDemo_v1.0-15-g27d6c6f-68`, Date 2022-07-19 |

### Purpose

This is a **reference-data / file-setup (load) job**. It rebuilds the Transaction Type
reference VSAM KSDS from a flat sequential seed file. The classic mainframe
"delete / define / load" pattern:

1. **STEP05** — Delete the existing Transaction Type VSAM cluster (idempotent rebuild; ignores "not found").
2. **STEP10** — Define (allocate) a fresh Transaction Type VSAM KSDS cluster.
3. **STEP15** — Load (REPRO) the records from the flat PS seed file into the new VSAM KSDS.

There is **no posting/transaction processing, no report, no backup, and no GDG usage** in this job.
It establishes the `TRANTYPE` lookup table used elsewhere by CardDemo (transaction-type code → description).

---

## Datasets / Files involved

| Logical dataset | Type | Corresponds to |
|-----------------|------|----------------|
| `AWS.M2.CARDDEMO.TRANTYPE.PS` | Sequential flat file (input seed) | Source rows for the Transaction Type reference table |
| `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS` | VSAM KSDS (cluster) | Relational table **`TRANTYPE`** (transaction-type reference / lookup table) |
| `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS.DATA` | VSAM data component | (Internal component of the `TRANTYPE` KSDS) |
| `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS.INDEX` | VSAM index component | (Internal component of the `TRANTYPE` KSDS) |

**Record layout note:** `RECORDSIZE(60 60)` (fixed 60 bytes), `KEYS(2 0)` — a **2-byte key
beginning at offset 0**, i.e. the 2-character transaction-type code is the primary key.
In the .NET target this maps to the `TRANTYPE` table with a 2-char primary key and a fixed
60-byte record (key + description fields).

---

## Step-by-step detail

### STEP05 — Delete existing Transaction Type VSAM cluster
- **EXEC:** `PGM=IDCAMS`
- **PARM:** none
- **COND / RC gating:** none (no COND on EXEC). Failure of the DELETE is neutralized inside the
  control statements via `SET MAXCC = 0`, so a missing cluster does **not** fail the step or the job.
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Role |
  |----|-----------|------|
  | `SYSPRINT` | `SYSOUT=*` | IDCAMS message/listing output |
  | `SYSIN` | inline (`DD *`) | IDCAMS control statements |
- **IDCAMS control statements (exact):**
  ```
  DELETE AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS -
         CLUSTER
  SET    MAXCC = 0
  ```
  - `DELETE ... CLUSTER` removes the `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS` cluster (and its
    DATA/INDEX components) if it exists.
  - `SET MAXCC = 0` forces the condition code back to 0 so a "not found" (first-run) does not abend.

### STEP10 — Define the Transaction Type VSAM KSDS
- **EXEC:** `PGM=IDCAMS`
- **PARM:** none
- **COND / RC gating:** none
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Role |
  |----|-----------|------|
  | `SYSPRINT` | `SYSOUT=*` | IDCAMS message/listing output |
  | `SYSIN` | inline (`DD *`) | IDCAMS control statements |
- **IDCAMS control statements (exact):**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(2 0) -
         RECORDSIZE(60 60) -
         SHAREOPTIONS(1 4) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS.INDEX) -
         )
  ```
  - **Cluster name:** `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS`
  - **Space:** `CYLINDERS(1 5)` — primary 1 cyl, secondary 5 cyl
  - **Volume:** `AWSHJ1`
  - **Key:** `KEYS(2 0)` — 2-byte key at offset 0
  - **Record size:** `RECORDSIZE(60 60)` — fixed-length 60-byte records
  - **Share options:** `SHAREOPTIONS(1 4)`
  - **Attributes:** `ERASE`, `INDEXED` (KSDS)
  - Explicitly named DATA and INDEX components.

### STEP15 — Load flat file into VSAM (REPRO)
- **EXEC:** `PGM=IDCAMS`
- **PARM:** none
- **COND / RC gating:** none
- **GDG:** none
- **DD statements:**
  | DD | Allocation | Reads/Writes | Dataset | Maps to |
  |----|-----------|--------------|---------|---------|
  | `SYSPRINT` | `SYSOUT=*` | write | IDCAMS messages | — |
  | `TRANTYPE` | `DISP=SHR` | **read** (input) | `AWS.M2.CARDDEMO.TRANTYPE.PS` | Sequential seed file |
  | `TTYPVSAM` | `DISP=OLD` | **write** (output) | `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS` | `TRANTYPE` table (KSDS) |
  | `SYSIN` | inline (`DD *`) | read | IDCAMS control statements | — |
- **IDCAMS control statements (exact):**
  ```
  REPRO INFILE(TRANTYPE) OUTFILE(TTYPVSAM)
  ```
  - Copies every record from the flat PS file (`TRANTYPE` DD →
    `AWS.M2.CARDDEMO.TRANTYPE.PS`) into the freshly-defined VSAM KSDS
    (`TTYPVSAM` DD → `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS`), keyed on the 2-byte transaction-type code.

---

## .NET JobControl mapping notes
- All three steps invoke the **IDCAMS** utility — implement via the JobControl IDCAMS-equivalent
  handlers: `DeleteCluster`, `DefineCluster`, and `Repro` (flat-file → table load).
- The `TRANTYPE` KSDS is modeled as the relational **`TRANTYPE`** table (PK = 2-char type code,
  fixed 60-byte record).
- **Idempotency:** STEP05's `SET MAXCC = 0` means "delete if exists, otherwise ignore" — the
  .NET delete step must swallow a not-found condition and continue.
- **Execution order is strictly sequential** (delete → define → load); no conditional gating,
  no parallelism, no GDG generations.
- Net effect: a clean **truncate-and-reload** of the `TRANTYPE` reference table from its seed file.
