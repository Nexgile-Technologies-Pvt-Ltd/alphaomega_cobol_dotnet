# JOB SPEC: ACCTFILE

## Overview

- **JCL member**: `ACCTFILE.jcl`
- **JOB name**: `ACCTFILE`
- **JOB description**: `'Delete define Account Data'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **File setup / initial load of the Account master file.** This job (re)builds the Account KSDS VSAM dataset from scratch: it deletes any pre-existing Account VSAM cluster, defines a fresh KSDS cluster, and then loads it by copying records from the supplied flat (sequential) seed file. It is a one-shot bootstrap/refresh job, not a posting/report/backup job. It is destructive: the existing account VSAM is erased and replaced with the contents of the PS seed file.

## Step summary

| Step | PGM | Action |
|------|-----|--------|
| STEP05 | IDCAMS | DELETE existing Account VSAM KSDS cluster (idempotent) |
| STEP10 | IDCAMS | DEFINE new Account VSAM KSDS cluster (data + index) |
| STEP15 | IDCAMS | REPRO flat seed file into the VSAM KSDS (load) |

There are **3 EXEC steps**, all invoking the IDCAMS utility. No COBOL `CB*` program, no SORT, no IEFBR14, no GDG usage in this job.

---

## STEP05 — Delete existing Account VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded on the EXEC. Failure tolerance is handled internally by the control statement (`IF MAXCC LE 08 THEN SET MAXCC = 0`), which forces the step return code to 0 even when the DELETE fails because the cluster does not yet exist (RC=8 from "entry not found"). This makes the step safe to run on a first-time setup.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DELETE AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
- **Datasets**:
  - Deletes VSAM cluster `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` (the Account master file).
- **Relational/file mapping**: target is the **Account master** store. Maps to the relational table **`ACCOUNT`** (the ACCTDAT/account-master entity) in the .NET conversion. In z/OS this is the indexed VSAM KSDS that the online and batch programs read/update as the account record.

## STEP10 — Define new Account VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none. (Note: the job does not gate STEP10 on STEP05's RC; STEP05 is normalized to RC=0 so STEP10 always runs.)
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(11 0) -
         RECORDSIZE(300 300) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS.INDEX) -
         )
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(11 0)**: primary key is **11 bytes long, starting at offset 0** of the record (i.e. the leading 11 bytes = the Account ID / `ACCT-ID`, an 11-digit account number). This 11-byte leading key becomes the **primary key of the `ACCOUNT` table**.
  - **RECORDSIZE(300 300)**: fixed-length 300-byte records (avg = max = 300), matching the account-record copybook (e.g. `CVACT01Y`, ACCOUNT-RECORD layout).
  - **CYLINDERS(1 5)**: primary allocation 1 cylinder, secondary 5 cylinders.
  - **VOLUMES(AWSHJ1)**: placed on volume `AWSHJ1`.
  - **SHAREOPTIONS(2 3)**: cross-region share option 2, cross-system 3.
  - **ERASE**: data is physically erased on deletion (security/overwrite).
  - **DATA component**: `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS.INDEX`
- **Datasets**: defines (allocates) the cluster and its DATA and INDEX components named above.
- **Relational/file mapping**: defines the storage for the **`ACCOUNT`** table. In .NET, this step corresponds to ensuring the Account table/store exists with the 11-char account-id primary key and a 300-byte fixed record schema; the DATA/INDEX VSAM components have no separate relational equivalent (the index becomes the table's clustered/primary index).

## STEP15 — Load (REPRO) flat seed file into the VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `ACCTDATA DD DISP=SHR,DSN=AWS.M2.CARDDEMO.ACCTDATA.PS` — **input** flat/sequential (PS) seed file containing the account records to load.
  - `ACCTVSAM DD DISP=SHR,DSN=AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` — **output** target = the VSAM KSDS cluster defined in STEP10.
  - `SYSIN DD *` — inline control statement (below).
- **Control statements (IDCAMS)**:
  ```
  REPRO INFILE(ACCTDATA) OUTFILE(ACCTVSAM)
  ```
- **Datasets**:
  - **Reads (input)**: `AWS.M2.CARDDEMO.ACCTDATA.PS` — sequential seed file of account records (the supplied initial data).
  - **Writes (output)**: `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` — the Account KSDS, loaded key-sequenced by the 11-byte account-id key. REPRO inserts each PS record as a VSAM record keyed on its leading 11 bytes.
- **Relational/file mapping**:
  - Input PS file `...ACCTDATA.PS` corresponds to the **seed/import dataset** for the `ACCOUNT` table (a flat extract used to populate it).
  - Output VSAM corresponds to the **`ACCOUNT`** table itself.
  - In .NET terms this step = "bulk load the ACCOUNT table from the seed file"; each 300-byte fixed record is parsed per the account copybook and inserted with the 11-digit account id as primary key.

---

## PARM / GDG / SORT notes

- **PARM=**: none on any EXEC step.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)` generation references).
- **SORT**: not used; no SORT FIELDS statements. (No sorting is needed — REPRO loads records in the order they appear in the seed file, and KSDS load expects them in ascending key order.)
- **IEFBR14**: not used.
- **COND on JOB card**: none.

## Conversion notes for the .NET JobControl step-runner

- Implement as a 3-step job. Steps 1 and 2 are DDL-equivalent (drop-if-exists / create) for the `ACCOUNT` store; step 3 is a bulk import from the `ACCTDATA.PS` seed file.
- Preserve the **idempotent delete** semantic of STEP05: a "table/store does not exist" condition must be swallowed (equivalent to `IF MAXCC LE 08 THEN SET MAXCC = 0`) so a clean first run does not fail.
- Honor the **11-byte leading primary key** and **300-byte fixed record** layout when defining the ACCOUNT schema and when parsing the seed file.
- This job is destructive/refresh-style: running it discards existing ACCOUNT data and reloads from the seed file. Guard accordingly in any environment with live data.
