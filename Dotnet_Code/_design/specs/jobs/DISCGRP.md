# JOB SPEC: DISCGRP

## Source
- JCL member: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/DISCGRP.jcl`
- Version stamp: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## JOB Card
- **JOB name:** `DISCGRP`
- **Description:** `DEFINE DISCLOSURE GROUP FILE`
- **CLASS:** `A`
- **MSGCLASS:** `0`
- **NOTIFY:** `&SYSUID`

## Overall Purpose
**File setup / VSAM reload job.** This job (re)builds the **Disclosure Group** reference file as a VSAM KSDS. It runs entirely under the IDCAMS utility across three steps:
1. Delete the existing Disclosure Group KSDS cluster (idempotent — tolerates "not found").
2. Define a fresh, empty KSDS cluster.
3. Load (REPRO) the KSDS from a flat sequential seed file.

This is an initial-load / refresh utility, not a posting, report, or backup job. It is typically run once during environment setup or whenever the disclosure group reference data needs to be reseeded. There is no COND/RC gating between steps and no GDG usage anywhere in this job.

## Logical Data Entity
- **Disclosure Group** — interest-rate / disclosure group reference data keyed by a 16-byte composite key (account group + transaction type + transaction category). Record length is fixed at 50 bytes.
- **Target relational table (in .NET model):** `DISCLOSURE_GROUP` (a.k.a. disclosure group / interest-rate lookup table).
- **Source seed file:** sequential PS dataset `AWS.M2.CARDDEMO.DISCGRP.PS` (corresponds to seed/reference data import; mapped to a flat seed file imported into the `DISCLOSURE_GROUP` table).

---

## Steps (in order)

### STEP05 — Delete existing VSAM cluster
- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Drop the Disclosure Group KSDS cluster if it already exists, so the job can be re-run cleanly.
- **PARM:** none
- **COND/RC gating:** none (always runs)
- **GDG:** none
- **DD statements:**
  | DD | Disposition / Type | Dataset | Role |
  |----|--------------------|---------|------|
  | `SYSPRINT` | `SYSOUT=*` | spool | IDCAMS messages |
  | `SYSIN` | instream `*` | — | control statements |
- **IDCAMS control statements (exact):**
  ```
  DELETE AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS -
         CLUSTER
  SET    MAXCC = 0
  ```
- **Notes:** `DELETE ... CLUSTER` removes the cluster (data + index components). `SET MAXCC = 0` forces the condition code back to 0 so a "dataset not found" (RC=8) on a fresh system does not fail the job. → .NET equivalent: drop / truncate the `DISCLOSURE_GROUP` table (or its store), ignoring "does not exist".

### STEP10 — Define new VSAM KSDS cluster
- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Create the empty Disclosure Group KSDS into which data will be loaded.
- **PARM:** none
- **COND/RC gating:** none
- **GDG:** none
- **DD statements:**
  | DD | Disposition / Type | Dataset | Role |
  |----|--------------------|---------|------|
  | `SYSPRINT` | `SYSOUT=*` | spool | IDCAMS messages |
  | `SYSIN` | instream `*` | — | control statements |
- **IDCAMS control statements (exact):**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(16 0) -
         RECORDSIZE(50 50) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS.INDEX) -
         )
  ```
- **Cluster attributes (mapping):**
  | Attribute | Value | Meaning for .NET model |
  |-----------|-------|------------------------|
  | `INDEXED` | — | Key-Sequenced Data Set (KSDS) → primary-key indexed table |
  | `KEYS(16 0)` | 16-byte key at offset 0 | Primary key = first 16 bytes of the record |
  | `RECORDSIZE(50 50)` | fixed 50 bytes | Fixed-length 50-byte record layout |
  | `CYLINDERS(1 5)` | primary 1, secondary 5 | space allocation (no .NET equivalent) |
  | `VOLUMES(AWSHJ1)` | volume | physical placement (n/a in .NET) |
  | `SHAREOPTIONS(2 3)` | cross-region/system share | concurrency (n/a in .NET) |
  | `ERASE` | — | erase-on-delete (security; n/a in .NET) |
- **Components defined:** cluster `...VSAM.KSDS`, data component `...VSAM.KSDS.DATA`, index component `...VSAM.KSDS.INDEX`.

### STEP15 — Load (REPRO) flat file into VSAM
- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Copy all records from the sequential seed file into the newly defined KSDS.
- **PARM:** none
- **COND/RC gating:** none
- **GDG:** none
- **DD statements:**
  | DD | Disposition / Type | Dataset | Logical mapping | Role |
  |----|--------------------|---------|-----------------|------|
  | `SYSPRINT` | `SYSOUT=*` | spool | — | IDCAMS messages |
  | `DISCGRP` | `DISP=SHR` | `AWS.M2.CARDDEMO.DISCGRP.PS` | sequential seed file → `DISCLOSURE_GROUP` import source | INPUT (read) |
  | `DISCVSAM` | `DISP=OLD` | `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS` | `DISCLOSURE_GROUP` table (KSDS) | OUTPUT (write) |
  | `SYSIN` | instream `*` | — | — | control statements |
- **IDCAMS control statements (exact):**
  ```
  REPRO INFILE(DISCGRP) OUTFILE(DISCVSAM)
  ```
- **Notes:** `REPRO` performs a full copy from the `DISCGRP` flat file (INFILE) to the `DISCVSAM` KSDS (OUTFILE). Records are inserted in key order (16-byte key). → .NET equivalent: bulk-load every row from the disclosure-group seed file into the `DISCLOSURE_GROUP` table.

---

## Datasets Summary
| Dataset | Type | Read/Write | Steps | Corresponds to |
|---------|------|-----------|-------|----------------|
| `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS` | VSAM KSDS (cluster) | deleted / defined / written | STEP05, STEP10, STEP15 | `DISCLOSURE_GROUP` table |
| `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS.DATA` | VSAM data component | defined | STEP10 | (data part of above) |
| `AWS.M2.CARDDEMO.DISCGRP.VSAM.KSDS.INDEX` | VSAM index component | defined | STEP10 | (index part of above) |
| `AWS.M2.CARDDEMO.DISCGRP.PS` | sequential flat file (PS) | read | STEP15 | seed/import source for `DISCLOSURE_GROUP` |

## Programs / Utilities Invoked
- `IDCAMS` (3 invocations: STEP05 DELETE, STEP10 DEFINE, STEP15 REPRO)

No application (CB*) programs, SORT, or IEFBR14 steps are present in this job.

## Step-Runner Notes (for .NET JobControl)
- Implement as a 3-step pipeline targeting the `DISCLOSURE_GROUP` store.
- STEP05 must be idempotent: "not found" on delete must NOT fail the job (mirror of `SET MAXCC = 0`).
- STEP10/STEP15 can be collapsed into "ensure empty table, then bulk import seed file" if VSAM physical attributes are not modeled; preserve the 16-byte primary key and 50-byte fixed record semantics.
- No inter-step COND/RC dependencies, no GDG generations, no PARM values to carry.
