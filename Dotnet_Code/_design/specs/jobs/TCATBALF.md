# JOB SPEC: TCATBALF

## Overview

- **JCL member**: `TCATBALF.jcl`
- **JOB name**: `TCATBALF`
- **JOB description**: `'DEFINE TRANCAT BAL'` (define Transaction Category Balance)
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **File setup / initial load of the Transaction Category Balance file.** This job (re)builds the Transaction Category Balance KSDS VSAM dataset from scratch: it deletes any pre-existing TCATBAL VSAM cluster, defines a fresh KSDS cluster, and then loads it by copying records from the supplied flat (sequential) seed file. It is a one-shot bootstrap/refresh job — not a posting, report, or backup job. It is destructive: the existing TCATBAL VSAM is erased and replaced with the contents of the `.PS` seed file. The Transaction Category Balance file is the running per-account/per-category balance store that the posting program (CBTRN02C) updates as transactions are posted; this job initializes that store.

## Step summary

| Step | PGM | Action |
|------|-----|--------|
| STEP05 | IDCAMS | DELETE existing Transaction Category Balance VSAM KSDS cluster (idempotent) |
| STEP10 | IDCAMS | DEFINE new Transaction Category Balance VSAM KSDS cluster (data + index) |
| STEP15 | IDCAMS | REPRO flat seed file into the VSAM KSDS (load) |

There are **3 EXEC steps**, all invoking the **IDCAMS** utility. No COBOL `CB*` program, no SORT, no IEFBR14, no GDG usage in this job.

---

## STEP05 — Delete existing Transaction Category Balance VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded on the EXEC. Failure tolerance is handled internally by the control statement `SET MAXCC = 0`, which unconditionally forces the step's maximum condition code back to 0 even when the preceding DELETE fails because the cluster does not yet exist (RC=8, "entry not found"). This makes the step safe to run on a first-time setup. (Note: this is the blunter `SET MAXCC = 0` form, which masks **all** prior errors in the step, not the more selective `IF MAXCC LE 08 THEN SET MAXCC = 0` form seen in some other CardDemo setup jobs.)
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DELETE AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS -
         CLUSTER
  SET    MAXCC = 0
  ```
- **Datasets**:
  - Deletes VSAM cluster `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` (the Transaction Category Balance file).
- **Relational/file mapping**: target is the **Transaction Category Balance** store. Maps to the relational table **`TRANSACTION_CATEGORY_BALANCE`** (TCATBAL / `TRAN-CAT-BAL-RECORD`, copybook `CVTRA01Y`) in the .NET conversion. On z/OS this is the indexed VSAM KSDS that batch posting (CBTRN02C) reads/updates as the per-account-per-category running balance.

## STEP10 — Define new Transaction Category Balance VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none. The job does not gate STEP10 on STEP05's RC; STEP05 is normalized to RC=0 (`SET MAXCC = 0`) so STEP10 always runs.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(17 0) -
         RECORDSIZE(50 50) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS.INDEX) -
         )
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(17 0)**: primary key is **17 bytes long, starting at offset 0** of the record. Per copybook `CVTRA01Y` (`TRAN-CAT-KEY`), this 17-byte composite key is: `TRANCAT-ACCT-ID PIC 9(11)` (11 bytes) + `TRANCAT-TYPE-CD PIC X(02)` (2 bytes) + `TRANCAT-CD PIC 9(04)` (4 bytes) = 17 bytes. This composite becomes the **composite primary key** of the `TRANSACTION_CATEGORY_BALANCE` table: **(AcctId, TypeCode, CategoryCode)**.
  - **RECORDSIZE(50 50)**: fixed-length 50-byte records (avg = max = 50), matching `TRAN-CAT-BAL-RECORD` (17-byte key + `TRAN-CAT-BAL PIC S9(09)V99` 11-byte balance + `FILLER PIC X(22)` = 50 bytes).
  - **CYLINDERS(1 5)**: primary allocation 1 cylinder, secondary 5 cylinders.
  - **VOLUMES(AWSHJ1)**: placed on volume `AWSHJ1`.
  - **SHAREOPTIONS(2 3)**: cross-region share option 2, cross-system 3.
  - **ERASE**: data is physically erased on deletion.
  - **DATA component**: `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS.INDEX`
- **Datasets**: defines (allocates) the cluster and its DATA and INDEX components named above.
- **Relational/file mapping**: defines the storage for the **`TRANSACTION_CATEGORY_BALANCE`** table. In .NET, this step corresponds to ensuring the TCATBAL table/store exists with the 17-byte composite primary key (AcctId + TypeCode + CategoryCode) and a 50-byte fixed record schema. The DATA/INDEX VSAM components have no separate relational equivalent (the index becomes the table's clustered/primary index).

## STEP15 — Load (REPRO) flat seed file into the VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `TCATBAL DD DISP=SHR,DSN=AWS.M2.CARDDEMO.TCATBALF.PS` — **input** flat/sequential (PS) seed file containing the transaction-category-balance records to load. (DISP=SHR — shared read.)
  - `TCATBALV DD DISP=OLD,DSN=AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` — **output** target = the VSAM KSDS cluster defined in STEP10. (DISP=OLD — exclusive control for the load.)
  - `SYSIN DD *` — inline control statement (below).
- **Control statements (IDCAMS)**:
  ```
  REPRO INFILE(TCATBAL) OUTFILE(TCATBALV)
  ```
- **Datasets**:
  - **Reads (input)**: `AWS.M2.CARDDEMO.TCATBALF.PS` — sequential seed file of transaction-category-balance records (the supplied initial data), DD name `TCATBAL`.
  - **Writes (output)**: `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` — the TCATBAL KSDS, loaded key-sequenced by the 17-byte composite key, DD name `TCATBALV`. REPRO inserts each PS record as a VSAM record keyed on its leading 17 bytes.
- **Relational/file mapping**:
  - Input PS file `...TCATBALF.PS` corresponds to the **seed/import dataset** for the `TRANSACTION_CATEGORY_BALANCE` table (a flat extract used to populate it).
  - Output VSAM corresponds to the **`TRANSACTION_CATEGORY_BALANCE`** table itself.
  - In .NET terms this step = "bulk load the TRANSACTION_CATEGORY_BALANCE table from the seed file"; each 50-byte fixed record is parsed per copybook `CVTRA01Y` and inserted with the 17-byte composite key as primary key. The seed records must already be in ascending key order for the KSDS load to succeed.

---

## PARM / GDG / SORT notes

- **PARM=**: none on any EXEC step.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)` generation references).
- **SORT**: not used; no SORT FIELDS statements. (No sorting is performed here — REPRO loads records in the order they appear in the seed file, and a KSDS load expects them pre-sorted in ascending key order.)
- **IEFBR14**: not used.
- **COND on JOB card**: none.

## Conversion notes for the .NET JobControl step-runner

- Implement as a **3-step job**. Steps 1 and 2 are DDL-equivalent (drop-if-exists / create) for the `TRANSACTION_CATEGORY_BALANCE` store; step 3 is a bulk import from the `TCATBALF.PS` seed file.
- Preserve the **idempotent delete** semantic of STEP05: a "table/store does not exist" condition must be swallowed (equivalent to `SET MAXCC = 0`) so a clean first run does not fail. Note STEP05 masks *all* errors in the step, so the .NET equivalent should at minimum not fail the job when the store is absent.
- Honor the **17-byte composite primary key** — `(TRANCAT-ACCT-ID 9(11), TRANCAT-TYPE-CD X(02), TRANCAT-CD 9(04))` — and the **50-byte fixed record** layout (copybook `CVTRA01Y`, `TRAN-CAT-BAL-RECORD`) when defining the schema and when parsing the seed file. The balance field is `TRAN-CAT-BAL PIC S9(09)V99`.
- This job is destructive/refresh-style: running it discards existing TCATBAL data and reloads from the seed file. Guard accordingly in any environment with live data.
- Related: this same VSAM file (`AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS`) is the `TCATBAL-FILE`/`TCATBALF` updated by the posting batch program (CBTRN02C) and read by the category-balance report job (PRTCATBL); this job seeds it before those run.
