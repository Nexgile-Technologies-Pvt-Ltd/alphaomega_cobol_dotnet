# JOB SPEC: XREFFILE

## Overview

- **JCL member**: `XREFFILE.jcl`
- **JOB name**: `XREFFILE`
- **JOB description**: `'Delete define cross ref file'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **File setup / initial load of the Card Cross-Reference (CARDXREF) file, including an alternate index.** This job (re)builds the Card-to-Account cross-reference KSDS VSAM dataset from scratch and additionally builds an **alternate index (AIX) on the Account-ID** so the file can be browsed/keyed by account instead of only by card number. The sequence is: delete any pre-existing base cluster + AIX, define a fresh KSDS cluster, load it from the supplied flat seed file (REPRO), define the alternate index, define the path that ties the AIX to the base cluster, and finally BLDINDEX to populate the alternate index from the loaded base data. It is a one-shot bootstrap/refresh job (file setup), **not** a posting / report / backup job. It is **destructive**: existing CARDXREF VSAM and its AIX are erased and replaced from the PS seed file.

## Step summary

| Step | PGM | Action |
|------|-----|--------|
| STEP05 | IDCAMS | DELETE existing CARDXREF base KSDS cluster **and** its alternate index (idempotent) |
| STEP10 | IDCAMS | DEFINE new CARDXREF base VSAM KSDS cluster (data + index) |
| STEP15 | IDCAMS | REPRO flat seed file into the base VSAM KSDS (load) |
| STEP20 | IDCAMS | DEFINE ALTERNATEINDEX on Account-ID, related to the base cluster |
| STEP25 | IDCAMS | DEFINE PATH linking the alternate index to the base cluster |
| STEP30 | IDCAMS | BLDINDEX — build/populate the alternate index from the loaded base data |

There are **6 EXEC steps**, all invoking the **IDCAMS** utility. No COBOL `CB*` program, no SORT, no IEFBR14, no GDG usage in this job.

---

## STEP05 — Delete existing CARDXREF base cluster and alternate index

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded on the EXEC. Failure tolerance is handled internally by the control statements (`IF MAXCC LE 08 THEN SET MAXCC = 0` after each DELETE), which force the step return code back to 0 even when a DELETE fails because the object does not yet exist (RC=8, "entry not found"). This makes the step safe on a first-time setup.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DELETE AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  DELETE  AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX  -
         ALTERNATEINDEX
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
- **Datasets**:
  - Deletes VSAM base cluster `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` (the Card cross-reference master file). Deleting the cluster also removes its `.DATA` and `.INDEX` components.
  - Deletes alternate index `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX` (the Account-ID AIX over the same data).
- **Relational/file mapping**: target is the **Card Cross-Reference** store (CARDXREF / CXACAIX). Maps to the relational table **`CARD_XREF`** (the card-to-account cross reference: card number ↔ account id ↔ customer id) in the .NET conversion. In z/OS this is the indexed VSAM KSDS that online (e.g. CXACAIX-based lookups) and batch programs read to resolve a card number to its owning account/customer. The AIX has no separate table — it is a secondary (account-id) index over the same `CARD_XREF` rows.

## STEP10 — Define new CARDXREF base VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none. (The job does not gate STEP10 on STEP05's RC; STEP05 is normalized to RC=0 so STEP10 always runs.)
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(16 0) -
         RECORDSIZE(50 50) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS.INDEX) -
         )
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(16 0)**: primary key is **16 bytes long, starting at offset 0** of the record (the leading 16 bytes = the **Card Number** / `XREF-CARD-NUM`, a 16-digit card number). This 16-byte leading key becomes the **primary key of the `CARD_XREF` table**.
  - **RECORDSIZE(50 50)**: fixed-length 50-byte records (avg = max = 50), matching the cross-reference copybook (`CVACT03Y` / CARD-XREF-RECORD layout: 16-byte card number + 11-byte account id + customer id + filler = 50 bytes).
  - **CYLINDERS(1 5)**: primary allocation 1 cylinder, secondary 5 cylinders.
  - **VOLUMES(AWSHJ1)**: placed on volume `AWSHJ1`.
  - **SHAREOPTIONS(2 3)**: cross-region share option 2, cross-system 3.
  - **ERASE**: data is physically erased on deletion (security/overwrite).
  - **DATA component**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS.INDEX`
- **Datasets**: defines (allocates) the base cluster and its DATA and INDEX components named above.
- **Relational/file mapping**: defines the storage for the **`CARD_XREF`** table. In .NET this step corresponds to ensuring the Card-Xref table/store exists with the 16-char card-number primary key and a 50-byte fixed record schema; the DATA/INDEX VSAM components have no separate relational equivalent (the primary index becomes the table's clustered/primary index).

## STEP15 — Load (REPRO) flat seed file into the base VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `XREFDATA DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CARDXREF.PS` — **input** flat/sequential (PS) seed file containing the cross-reference records to load.
  - `XREFVSAM DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` — **output** target = the base VSAM KSDS cluster defined in STEP10.
  - `SYSIN DD *` — inline control statement (below).
- **Control statements (IDCAMS)**:
  ```
  REPRO INFILE(XREFDATA) OUTFILE(XREFVSAM)
  ```
- **Datasets**:
  - **Reads (input)**: `AWS.M2.CARDDEMO.CARDXREF.PS` — sequential seed file of card cross-reference records (the supplied initial data).
  - **Writes (output)**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` — the CARDXREF KSDS, loaded key-sequenced by the 16-byte card-number key. REPRO inserts each PS record as a VSAM record keyed on its leading 16 bytes.
- **Relational/file mapping**:
  - Input PS file `...CARDXREF.PS` corresponds to the **seed/import dataset** for the `CARD_XREF` table (a flat extract used to populate it).
  - Output VSAM corresponds to the **`CARD_XREF`** table itself.
  - In .NET terms this step = "bulk load the CARD_XREF table from the seed file"; each 50-byte fixed record is parsed per the cross-reference copybook (card number, account id, customer id) and inserted with the 16-digit card number as primary key.

## STEP20 — Define alternate index on Account-ID

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE ALTERNATEINDEX (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX)-
  RELATE(AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS)                    -
  KEYS(11,25)                                                   -
  NONUNIQUEKEY                                                  -
  UPGRADE                                                       -
  RECORDSIZE(50,50)                                             -
  FREESPACE(10,20)                                              -
  VOLUMES(AWSHJ1)                                               -
  CYLINDERS(5,1))                                               -
  DATA (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.DATA))           -
  INDEX (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.INDEX))
  ```
- **Alternate index attributes (key facts for the .NET model)**:
  - **AIX name**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX`
  - **RELATE**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` — the AIX is built over the base CARDXREF cluster (same rows).
  - **KEYS(11,25)**: the alternate key is **11 bytes long starting at offset 25** (0-based) of the base record. In the 50-byte cross-reference layout this is the **Account-ID** field (`XREF-ACCT-ID`, 11 digits) that follows the 16-byte card number (and any intervening customer-id field). So the AIX provides **lookup by account id**.
  - **NONUNIQUEKEY**: account id is **not unique** across the file — one account can own multiple cards, so multiple base records can share the same account-id alternate key.
  - **UPGRADE**: the AIX is part of the base cluster's upgrade set; VSAM keeps it automatically in sync when the base cluster is updated (inserts/updates/deletes maintain the AIX).
  - **RECORDSIZE(50,50)**: AIX record size 50 (avg/max) — sizing for the alternate-index records (alt key + pointer list).
  - **FREESPACE(10,20)**: 10% free space within control intervals, 20% within control areas (to accommodate added duplicate pointers for non-unique keys).
  - **VOLUMES(AWSHJ1)** / **CYLINDERS(5,1)**: placed on volume `AWSHJ1`; primary 5 cylinders, secondary 1 cylinder.
  - **DATA component**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.INDEX`
- **Datasets**: defines (allocates) the alternate index and its DATA and INDEX components. At this point the AIX is **defined but empty** (not yet populated — see STEP30).
- **Relational/file mapping**: the AIX maps to a **secondary index on `CARD_XREF.account_id`** in the .NET model (a non-unique index supporting "find all cards for an account"). It is not a separate table. The `UPGRADE` attribute corresponds to the index being maintained automatically on every write to the `CARD_XREF` table.

## STEP25 — Define PATH relating the alternate index to the base cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE PATH                                           -
   (NAME(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.PATH)        -
    PATHENTRY(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX))
  ```
- **Datasets**:
  - Defines path object `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX.PATH` with `PATHENTRY` = the alternate index `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX`.
- **Purpose / mapping**: the **PATH** is the named object that applications open to read the base cluster **through** the alternate index (i.e. it ties the AIX to its base cluster so reads via the path return base-cluster records ordered by the alternate key). In the online CardDemo programs, the AIX path is the DD/file used for account-keyed access to the cross-reference (e.g. the `CXACAIX`-style alternate access). In the .NET model this has no separate table; it corresponds to exposing/querying the `CARD_XREF` rows ordered/filtered by the `account_id` secondary index defined in STEP20.

## STEP30 — Build (populate) the alternate index

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statement (below).
- **Control statements (IDCAMS)**:
  ```
  BLDINDEX                                                      -
  INDATASET(AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS)                 -
  OUTDATASET(AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX)
  ```
- **Datasets**:
  - **Reads (input)**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` — the loaded base cluster (the source of records to index).
  - **Writes (output)**: `AWS.M2.CARDDEMO.CARDXREF.VSAM.AIX` — the previously-defined alternate index, now populated.
- **Purpose / mapping**: `BLDINDEX` reads every base record, extracts the 11-byte account-id alternate key (offset 25), and builds the AIX entries (one alternate key value pointing to all base records sharing it, since the key is non-unique). This is the step that makes the account-id index usable. In the .NET model this corresponds to **building/refreshing the secondary index on `CARD_XREF.account_id`** after the bulk load in STEP15 — for a relational engine that maintains indexes automatically this is implicit, but the logical equivalent is "ensure the account_id index is populated and consistent with the loaded data."

---

## PARM / GDG / SORT / COND notes

- **PARM=**: none on any EXEC step.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)` generation references).
- **SORT**: not used; no SORT FIELDS statements. (No sorting is needed — REPRO loads the base in seed-file order, and BLDINDEX performs any internal sort it requires to construct the alternate index.)
- **IEFBR14**: not used.
- **COND on JOB card / EXEC steps**: none. There is **no inter-step COND/RC gating** — every step runs regardless of prior step RC. The only return-code management is the internal `IF MAXCC LE 08 THEN SET MAXCC = 0` in STEP05 (delete-if-exists idempotency).

## Execution-order dependencies (important for the .NET runner)

The steps are **strictly ordered and dependent**, even though no COND is coded:
1. DELETE (STEP05) must precede DEFINE (STEP10) so a stale cluster does not cause a duplicate-name failure.
2. DEFINE base (STEP10) before REPRO load (STEP15).
3. Base must be loaded (STEP15) before the AIX is built (STEP30) — `BLDINDEX` needs base records present.
4. DEFINE ALTERNATEINDEX (STEP20) and DEFINE PATH (STEP25) must precede BLDINDEX (STEP30).

If a real failure occurs mid-job on the mainframe, later steps would still attempt to run (no COND), but functionally the file would be inconsistent; the .NET runner should treat the six steps as one atomic "rebuild CARD_XREF + account-id index" unit.

## Conversion notes for the .NET JobControl step-runner

- Implement as a 6-step job that rebuilds the `CARD_XREF` store and its account-id secondary index.
- Steps STEP05/STEP10 are DDL-equivalent (drop-if-exists / create) for the `CARD_XREF` store; STEP15 is a bulk import from the `CARDXREF.PS` seed file; STEP20/STEP25/STEP30 establish and populate the **account-id secondary index** (non-unique).
- Preserve the **idempotent delete** semantic of STEP05: "object does not exist" conditions for both the base cluster and the AIX must be swallowed (equivalent to `IF MAXCC LE 08 THEN SET MAXCC = 0`) so a clean first run does not fail.
- Honor the record layout: **16-byte leading primary key (card number)**, **50-byte fixed record**, and an **11-byte alternate key (account id) at offset 25**, **non-unique**, when defining the `CARD_XREF` schema/indexes and when parsing the seed file.
- On a relational engine the AIX/PATH/BLDINDEX trio collapses into "create a non-unique index on `account_id` and ensure it is populated"; the index maintenance (`UPGRADE`) is automatic.
- This job is destructive/refresh-style: running it discards existing CARD_XREF data and its index and reloads from the seed file. Guard accordingly in any environment with live data.
