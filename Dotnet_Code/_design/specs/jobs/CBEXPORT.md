# JOB SPEC: CBEXPORT

## Overview

- **JCL member**: `CBEXPORT.jcl`
- **JOB name**: `CBEXPORT`
- **JOB description**: `'Export Customer Data for Migration'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v2.0-44-gb6e9c27-254` (2025-10-16)
- **Purpose**: **Data extract / migration backup.** This job exports the complete CardDemo data set (customers, accounts, card cross-references, transactions, and cards) from the five online VSAM master files into a single consolidated, multi-record-type **export VSAM file** for **branch migration / data transfer**. It is **not** a posting, interest, or report job. Step 1 (re)creates the empty export cluster (file setup); Step 2 runs the COBOL program `CBEXPORT` that reads all five source masters and writes one 500-byte export record per source record into the export file. The job is read-only against the five source masters and destructive only against the export target (which it deletes and redefines each run).

## Step summary

| Step | PGM | Type | Action |
|------|-----|------|--------|
| STEP01 | IDCAMS | Utility | DELETE (PURGE) + DEFINE the export VSAM KSDS cluster (file setup) |
| STEP02 | CBEXPORT | COBOL batch (`CB*`) | Read 5 source VSAM masters, write consolidated multi-record export file |

There are **2 EXEC steps**: one IDCAMS utility step and one COBOL `CBEXPORT` program step. No SORT, no IEFBR14, no GDG usage.

---

## STEP01 — Define the export VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **PARM**: none.
- **COND/RC gating**: none coded on the EXEC. Failure tolerance for the DELETE is handled **inside** the control stream via `SET MAXCC = 0` immediately after the DELETE, which forces the step return code to 0 even when the cluster does not yet exist (first-run safe / idempotent).
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)** — exact:
  ```
  DELETE AWS.M2.CARDDEMO.EXPORT.DATA CLUSTER PURGE
  SET MAXCC = 0

  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.EXPORT.DATA) -
                  INDEXED -
                  KEYS(4 28) -
                  RECORDSIZE(500 500) -
                  CYLINDERS(10 5) -
                  FREESPACE(10 10) -
                  SHAREOPTIONS(2 3)) -
         DATA (NAME(AWS.M2.CARDDEMO.EXPORT.DATA.DATA)) -
         INDEX (NAME(AWS.M2.CARDDEMO.EXPORT.DATA.INDEX))
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.M2.CARDDEMO.EXPORT.DATA`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(4 28)**: primary key is **4 bytes long at offset 28** (0-based) of the record. This maps to `EXPORT-SEQUENCE-NUM` in copybook `CVEXPORT` — a `PIC 9(9) COMP` (4-byte binary) field positioned after `EXPORT-REC-TYPE` (1 byte) + `EXPORT-TIMESTAMP` (26 bytes), i.e. bytes 1–27 precede it. The export sequence number is therefore the unique KSDS key; the program increments `WS-SEQUENCE-COUNTER` for every record written across all five record types, guaranteeing ascending unique keys.
  - **RECORDSIZE(500 500)**: fixed-length 500-byte records (avg = max = 500), matching the `EXPORT-RECORD` copybook (`CVEXPORT`, total 500 bytes).
  - **CYLINDERS(10 5)**: primary allocation 10 cylinders, secondary 5 cylinders.
  - **FREESPACE(10 10)**: 10% free space per control interval, 10% per control area (for the keyed inserts).
  - **SHAREOPTIONS(2 3)**: cross-region share option 2, cross-system 3.
  - **DATA component**: `AWS.M2.CARDDEMO.EXPORT.DATA.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.EXPORT.DATA.INDEX`
  - **DELETE … PURGE**: removes the prior cluster ignoring any retention/expiration date.
- **Datasets**:
  - Deletes (if present) then allocates `AWS.M2.CARDDEMO.EXPORT.DATA` and its DATA/INDEX components.
- **Relational/file mapping**: target is the **export/migration extract file** (`AWS.M2.CARDDEMO.EXPORT.DATA`). It is **not** a normalized business table — it is a denormalized multi-record-type staging/transfer dataset. In the .NET conversion it corresponds to an **EXPORT output store** (e.g. an `Export` table or a sequential/KSDS-equivalent export file) keyed by a 4-byte/9-digit `ExportSequenceNum`, with a 500-byte fixed record carrying a `RecordType` discriminator (`C`/`A`/`X`/`T`/`D`). The `.DATA` and `.INDEX` VSAM components have no separate relational equivalent (the index becomes the primary index on the sequence key).

## STEP02 — Run the CBEXPORT export program

- **EXEC**: `PGM=CBEXPORT`
- **PARM**: none.
- **COND/RC gating**: none coded on the EXEC (STEP02 is not conditioned on STEP01's RC; STEP01 is normalized to RC=0 so STEP02 always runs).
- **STEPLIB**: `DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` — load library containing the `CBEXPORT` load module.
- **DD statements** (DDNAME → dataset → direction → mapping):

  | DDNAME | Dataset (DSN) | Dir | COBOL SELECT / record type | Source file / table |
  |--------|---------------|-----|----------------------------|---------------------|
  | `CUSTFILE` | `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` | IN | `CUSTOMER-INPUT` (KSDS, key `CUST-ID`); copybook `CVCUS01Y`; export rec type `C` | **CUSTOMER** master table |
  | `ACCTFILE` | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | IN | `ACCOUNT-INPUT` (KSDS, key `ACCT-ID`); copybook `CVACT01Y`; export rec type `A` | **ACCOUNT** master table |
  | `XREFFILE` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | IN | `XREF-INPUT` (KSDS, key `XREF-CARD-NUM`); copybook `CVACT03Y`; export rec type `X` | **CARD\_XREF** (card-to-account/customer cross-reference) table |
  | `TRANSACT` | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | IN | `TRANSACTION-INPUT` (KSDS, key `TRAN-ID`); copybook `CVTRA05Y`; export rec type `T` | **TRANSACTION** table |
  | `CARDFILE` | `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` | IN | `CARD-INPUT` (KSDS, key `CARD-NUM`); copybook `CVACT02Y`; export rec type `D` | **CARD** master table |
  | `EXPFILE` | `AWS.M2.CARDDEMO.EXPORT.DATA` | OUT | `EXPORT-OUTPUT` (KSDS, key `EXPORT-SEQUENCE-NUM`); copybook `CVEXPORT` | **EXPORT** consolidated extract (defined in STEP01) |
  | `SYSOUT` | `SYSOUT=*` | OUT | program `DISPLAY` / runtime messages | spool |
  | `SYSPRINT` | `SYSOUT=*` | OUT | system print | spool |

  All six data files are `DISP=SHR`. The five input masters are opened `INPUT` and read **sequentially** (`ACCESS MODE IS SEQUENTIAL`) start-to-finish; `EXPFILE` is opened `OUTPUT` and loaded with sequentially-keyed records.

- **Program behaviour (for the step-runner)**:
  - Processing order (each phase reads its master to EOF, writing one export record per input record):
    1. `2000-EXPORT-CUSTOMERS` → rec type **`C`** (maps `CUST-*` fields).
    2. `3000-EXPORT-ACCOUNTS` → rec type **`A`** (maps `ACCT-*` fields).
    3. `4000-EXPORT-XREFS` → rec type **`X`** (maps `XREF-*` fields).
    4. `5000-EXPORT-TRANSACTIONS` → rec type **`T`** (maps `TRAN-*` fields).
    5. `5500-EXPORT-CARDS` → rec type **`D`** (maps `CARD-*` fields).
  - Every export record carries a common prefix: `EXPORT-REC-TYPE` (1 char discriminator), `EXPORT-TIMESTAMP` (26-char `YYYY-MM-DD HH:MM:SS.00` built from `DATE`/`TIME`), `EXPORT-SEQUENCE-NUM` (the KSDS key, incremented globally), and constants `EXPORT-BRANCH-ID = '0001'` and `EXPORT-REGION-CODE = 'NORTH'`. The remaining 460 bytes are a `REDEFINES` payload specific to the record type.
  - **Error handling**: any failed OPEN/READ/WRITE (file status not `00`/`10`) triggers `9999-ABEND-PROGRAM`, which `CALL 'CEE3ABD'` to force an abend. In the .NET runner this is a hard fail (non-zero RC / abort) for that step, not a tolerated condition.
  - Emits per-phase and total counts via `DISPLAY` (customers/accounts/xrefs/transactions/cards exported, plus total).
- **Datasets read**: the five source VSAM KSDS masters listed above (read-only).
- **Datasets written**: the export VSAM KSDS `AWS.M2.CARDDEMO.EXPORT.DATA` (loaded with the consolidated multi-type records).
- **Relational/file mapping**: this step is the **extract/transform** that fans the five normalized tables (`CUSTOMER`, `ACCOUNT`, `CARD_XREF`, `TRANSACTION`, `CARD`) into one denormalized `EXPORT` store, tagging each row with its source-type discriminator and a global sequence key.

---

## PARM / COND / GDG / SORT / IEFBR14 notes

- **PARM=**: none on either EXEC step.
- **COND/RC gating on EXEC**: none. STEP01 self-normalizes its DELETE RC to 0 via `SET MAXCC = 0`; STEP02 runs unconditionally.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)`/`(-1)` generation references).
- **SORT**: not used; no SORT FIELDS statements. (Records are written in the program's processing order and keyed by the monotonically increasing `EXPORT-SEQUENCE-NUM`, so the KSDS load is already in ascending key order.)
- **IEFBR14**: not used.
- **COND on JOB card**: none.

## Conversion notes for the .NET JobControl step-runner

- Implement as a **2-step job**:
  1. A setup step (IDCAMS-equivalent) that **drops-if-exists then creates** the `EXPORT` store: 500-byte fixed record, 4-byte/9-digit integer primary key at offset 28 (`ExportSequenceNum`), with the `DELETE ... PURGE` modeled as an unconditional drop and the `SET MAXCC = 0` as "ignore not-found on first run."
  2. The `CBEXPORT` program step: open 5 source stores read-only, stream each to EOF, and append one 500-byte export row per source row with the correct type discriminator (`C/A/X/T/D`), common prefix (type, 26-char timestamp, global sequence key, branch `0001`, region `NORTH`), and the type-specific 460-byte payload.
- Preserve the **single global sequence counter** across all five phases — it is both the KSDS key and the record ordering, and must be unique/ascending.
- Preserve **phase order** C → A → X → T → D; the export file's row order and sequence numbers depend on it.
- Map the export `RecordType` discriminator + `REDEFINES` payloads to a polymorphic/tagged row in the .NET `EXPORT` store (or per-type tables keyed by the shared sequence number).
- Treat any source read / export write failure as an **abend** (`CEE3ABD`) → fail the step with non-zero RC; do not silently continue.
- This job is **read-only on the five business masters** and **destructive only on the EXPORT target** (recreated each run); safe to re-run for a fresh full extract.
