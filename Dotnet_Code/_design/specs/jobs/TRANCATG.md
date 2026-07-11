# JOB SPEC: TRANCATG

## Overview

- **Member:** `TRANCATG.jcl`
- **JOB name:** `TRANCATG`
- **JOB accounting / description:** `'DEFINE TRAN CATEGORY'`
- **JOB params:** `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Purpose:** **File setup / initial load** for the Transaction Category reference data. The job (re)creates the Transaction Category VSAM KSDS from a flat (sequential) seed file. It is a classic three-step IDCAMS pattern: **DELETE** any prior cluster, **DEFINE** a fresh empty KSDS, then **REPRO** (load) the sequential seed data into the new VSAM file.
- **Source version stamp:** `CardDemo_v1.0-15-g27d6c6f-68  Date: 2022-07-19`
- **Step count:** 3 (STEP05, STEP10, STEP15)
- **Programs/utilities invoked:** `IDCAMS` (all three steps)

### Datasets / corresponding files & tables

| Dataset (DSN) | Type | Role | Maps to |
|---|---|---|---|
| `AWS.M2.CARDDEMO.TRANCATG.PS` | Sequential (PS) flat file | Input seed data | Transaction Category seed/import file |
| `AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS` | VSAM KSDS (cluster) | Target keyed file | **Transaction Category** relational table (e.g. `TRAN_CATEGORY` / `CARD_XREF`-style reference table) — the application's transaction-category type store |
| `AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS.DATA` | VSAM data component | KSDS data part | (component of above) |
| `AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS.INDEX` | VSAM index component | KSDS index part | (component of above) |

- **Record layout:** RECORDSIZE 60/60 (fixed 60-byte records); KEY length 6 at offset 0 (the leading 6 bytes are the primary key — the transaction category type code). This 6-byte / 60-byte layout matches the COBOL Transaction Category Type record (e.g. `CVTRA04Y` copybook: 2-digit type code + 4-digit category code = 6-byte key, plus description filler to 60 bytes).
- **GDG usage:** None. All datasets are fixed-name (no `(+1)` / `(0)` generations).

---

## STEP05 — DELETE existing Transaction Category VSAM cluster

- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Idempotent cleanup — delete the KSDS cluster if it already exists so the subsequent DEFINE does not fail on a duplicate. `SET MAXCC = 0` forces the step return code to 0 even when the DELETE fails because the cluster does not yet exist (first-ever run), so the job continues.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message output.
  - `SYSIN DD *` — inline control statements (below).
- **PARM:** none.
- **COND / RC gating:** none on the EXEC itself; gating is handled internally via `SET MAXCC = 0` (suppresses non-zero RC from a missing-cluster DELETE).
- **IDCAMS control statements (exact):**
  ```
  DELETE AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS -
         CLUSTER
  SET    MAXCC = 0
  ```

---

## STEP10 — DEFINE Transaction Category VSAM KSDS cluster

- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Create a fresh, empty indexed VSAM KSDS cluster (data + index components) to hold transaction category records.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message output.
  - `SYSIN DD *` — inline control statements (below).
- **PARM:** none.
- **COND / RC gating:** none.
- **Allocation / cluster attributes:**
  - `CYLINDERS(1 5)` — primary 1 cyl, secondary 5 cyl.
  - `VOLUMES(AWSHJ1)` — placed on volume AWSHJ1.
  - `KEYS(6 0)` — 6-byte key starting at offset 0.
  - `RECORDSIZE(60 60)` — fixed-length 60-byte records (avg=max=60).
  - `SHAREOPTIONS(2 3)` — cross-region 2, cross-system 3.
  - `ERASE` — overwrite data on delete.
  - `INDEXED` — KSDS (key-sequenced).
- **IDCAMS control statements (exact):**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(6 0) -
         RECORDSIZE(60 60) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS.INDEX) -
         )
  ```

---

## STEP15 — REPRO (load) flat file into VSAM KSDS

- **EXEC:** `PGM=IDCAMS`
- **Purpose:** Copy/load all records from the sequential seed file into the newly defined VSAM KSDS, populating the transaction-category reference data.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message output.
  - `TRANCATG DD DISP=SHR, DSN=AWS.M2.CARDDEMO.TRANCATG.PS` — **INPUT** sequential flat file (seed data).
  - `TCATVSAM DD DISP=OLD, DSN=AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS` — **OUTPUT** VSAM KSDS (just created in STEP10).
  - `SYSIN DD *` — inline control statement (below).
- **PARM:** none.
- **COND / RC gating:** none. (Implicitly depends on STEP10 having created the cluster; no explicit COND parameter is coded.)
- **IDCAMS control statements (exact):**
  ```
  REPRO INFILE(TRANCATG) OUTFILE(TCATVSAM)
  ```
- **Data flow:** `AWS.M2.CARDDEMO.TRANCATG.PS` (sequential) → `AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS` (KSDS, keyed on first 6 bytes).

---

## .NET JobControl step-runner notes

- No SORT step is present; no SORT FIELDS to translate.
- Translate to a 3-step job:
  1. **DELETE/DROP** target table/file if exists, ignore "not found" (mirrors `SET MAXCC = 0`).
  2. **CREATE** the Transaction Category store (empty), 6-byte key, 60-byte fixed record.
  3. **LOAD/REPRO** sequential seed file into the store (upsert by 6-byte key).
- Suggested relational mapping: a `TransactionCategory` table keyed on the 6-byte composite code; the `.PS` file is the import source. The `.DATA` / `.INDEX` components are VSAM internals with no separate relational equivalent.
- Job is **rerunnable/idempotent**: STEP05 makes repeated runs safe (drop-then-create-then-load).
