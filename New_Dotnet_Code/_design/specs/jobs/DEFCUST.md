# JOB SPEC: DEFCUST

## Overview

- **JCL member**: `DEFCUST.jcl`
- **JOB name**: `DEFCUST`
- **JOB description**: `'Define Customer Data File'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Purpose**: **File setup / VSAM (re)definition of the Customer master KSDS — DELETE-then-DEFINE only (no load).** This job is a minimal bootstrap that (1) deletes any pre-existing Customer VSAM cluster, then (2) defines a fresh empty KSDS cluster (DATA + INDEX). It does **not** load any data (there is no REPRO step and no seed/PS input). It is a one-shot "create the empty customer file" job, not a posting/report/backup job. It is destructive in that an existing cluster is dropped before the new one is defined.
- **Source quirks (IMPORTANT — flag for conversion)**: This JCL member is internally inconsistent and looks like an unfinished/early variant of the more complete `CUSTFILE.jcl`:
  1. **Both EXEC steps are named `STEP05`** (the step name is duplicated). On real z/OS this is a JCL error — duplicate step names within a job are flagged; only the first is uniquely addressable. The intended design is clearly DELETE (step 1) then DEFINE (step 2).
  2. **Mismatched dataset names between DELETE and DEFINE.** The DELETE targets `AWS.CCDA.CUSTDATA.CLUSTER`, but the DEFINE creates `AWS.CUSTDATA.CLUSTER`. So the DELETE does **not** actually delete the cluster the DEFINE creates — re-running this job would fail the DEFINE with "duplicate name" because the real cluster (`AWS.CUSTDATA.CLUSTER`) is never deleted. (Compare `CUSTFILE.jcl`, where delete and define use the same DSN `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS`.)
  3. **No CICS quiesce/resume, no REPRO load.** Unlike `CUSTFILE.jcl`, there is no SDSF CICS close/open and no data load — this job only carves out an empty VSAM shell.
  4. **DELETE has no `IF MAXCC ... SET MAXCC = 0` guard**, so on a first-ever run the DELETE returns a non-zero RC (entry not found) that is not normalized; the second step still runs because there is no COND gating.

## Step summary

| Step | PGM | Action |
|------|-----|--------|
| STEP05 (1st) | IDCAMS | DELETE existing Customer VSAM cluster `AWS.CCDA.CUSTDATA.CLUSTER` (note: different DSN than the one defined) |
| STEP05 (2nd) | IDCAMS | DEFINE new empty Customer VSAM KSDS cluster `AWS.CUSTDATA.CLUSTER` (DATA + INDEX) |

There are **2 EXEC steps**, both invoking the **IDCAMS** utility (DELETE, then DEFINE). No COBOL `CB*` program, no SORT, no IEFBR14, no SDSF/CICS step, no REPRO/load, and no GDG usage in this job.

---

## STEP05 (first occurrence) — Delete existing Customer VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded on the EXEC. **No** `IF MAXCC ... SET MAXCC = 0` normalization either, so a first-time run (cluster not present) leaves a non-zero condition code from the DELETE. There is no COND on the following step, so execution continues regardless.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DELETE AWS.CCDA.CUSTDATA.CLUSTER -
         CLUSTER
  ```
- **Datasets**:
  - Attempts to delete VSAM cluster **`AWS.CCDA.CUSTDATA.CLUSTER`** (the `CLUSTER` keyword restricts the delete to a cluster entry).
  - **Note**: this DSN (`AWS.CCDA...`) does **not** match the DSN created by the DEFINE step (`AWS.CUSTDATA...`). See "Source quirks" above.
- **Relational/file mapping**: target is the **Customer master** store. Maps to the relational table **`CUSTOMER`** (the CUSTDAT / customer-master entity) in the .NET conversion. In z/OS this is the indexed VSAM KSDS that the online (CICS file `CUSTDAT`) and batch programs read/update as the customer record.

## STEP05 (second occurrence) — Define new (empty) Customer VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded (no COND parameter), so this DEFINE runs unconditionally after the DELETE step regardless of the DELETE's return code.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE CLUSTER (NAME(AWS.CUSTDATA.CLUSTER) -
         CYLINDERS(1 5) -
         KEYS(10 0) -
         RECORDSIZE(500 500) -
         SHAREOPTIONS(1 4) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.CUSTDATA.CLUSTER.DATA) -
         ) -
         INDEX (NAME(AWS.CUSTDATA.CLUSTER.INDEX) -
         )
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.CUSTDATA.CLUSTER`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(10 0)**: primary key is **10 bytes long, starting at offset 0** of the record (the leading 10 bytes = the customer key). This 10-byte leading key becomes the **primary key of the `CUSTOMER` table**. (Note: this differs from `CUSTFILE.jcl`, which uses `KEYS(9 0)` — a 9-byte key. Flag the discrepancy; the canonical CardDemo customer id is 9 digits, so the 10-byte key here is likely an early/inconsistent value.)
  - **RECORDSIZE(500 500)**: fixed-length 500-byte records (avg = max = 500), matching the customer-record copybook (CUSTOMER-RECORD layout, e.g. `CVCUS01Y`).
  - **CYLINDERS(1 5)**: primary allocation 1 cylinder, secondary 5 cylinders.
  - **SHAREOPTIONS(1 4)**: cross-region share option 1, cross-system 4. (Differs from `CUSTFILE.jcl`'s `SHAREOPTIONS(2 3)`.)
  - **ERASE**: data component is physically erased on deletion (security/overwrite).
  - **No VOLUMES**: unlike `CUSTFILE.jcl` (`VOLUMES(AWSHJ1)`), this DEFINE specifies no explicit volume — placement is left to SMS/system default.
  - **DATA component**: `AWS.CUSTDATA.CLUSTER.DATA`
  - **INDEX component**: `AWS.CUSTDATA.CLUSTER.INDEX`
- **Datasets**: defines (allocates) the cluster and its DATA and INDEX components named above. The cluster is created **empty** (no REPRO/load in this job).
- **Relational/file mapping**: defines the storage for the **`CUSTOMER`** table. In .NET, this step corresponds to ensuring an empty Customer table/store exists with a leading customer-id primary key and a 500-byte fixed record schema; the DATA/INDEX VSAM components have no separate relational equivalent (the index becomes the table's clustered/primary index).

---

## PARM / GDG / SORT notes

- **PARM=**: none on any EXEC step.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)` generation references).
- **SORT**: not used; no SORT FIELDS statements.
- **REPRO / load**: not present — this job creates an empty cluster and never loads data.
- **IEFBR14**: not used.
- **COND on JOB card**: none. No COND on any EXEC step either.
- **CICS coupling**: none. Unlike `CUSTFILE.jcl`, there is no SDSF/`CEMT` file close/open bracketing the IDCAMS steps.

## Conversion notes for the .NET JobControl step-runner

- Model as a 2-step job: (1) drop-if-exists the Customer store, (2) create the Customer store **empty**. Do **not** add a load step — this job intentionally (or by omission) loads no data.
- **Reconcile the dataset-name and key-length inconsistencies before implementing.** The DELETE DSN (`AWS.CCDA.CUSTDATA.CLUSTER`) does not match the DEFINE DSN (`AWS.CUSTDATA.CLUSTER`), and the key length (10) differs from the sibling job `CUSTFILE.jcl` (9). For the .NET target, prefer the canonical CardDemo `CUSTOMER` definition (9-digit customer id, 500-byte record) used elsewhere unless this member is deliberately a distinct artifact. Treat the literal DSNs/key here as suspect early-draft values.
- **Make the delete idempotent.** Even though the source lacks an `IF MAXCC ... SET MAXCC = 0` guard, the .NET "drop if exists" should swallow a "does not exist" condition so a clean first run does not fail.
- **Duplicate step name `STEP05`**: in the step-runner, give the two steps distinct ids (e.g. `STEP05-DELETE` and `STEP10-DEFINE`); do not replicate the duplicate-name JCL defect.
- This job is destructive/refresh-style for the cluster shell: running it drops and recreates the Customer VSAM (empty). Guard accordingly in any environment with live data; note that, as written, the mismatched DSNs would actually leave the previously-defined `AWS.CUSTDATA.CLUSTER` undeleted and cause the second run's DEFINE to fail with a duplicate-name error.
