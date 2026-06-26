# JOB SPEC: CUSTFILE

## Overview

- **JCL member**: `CUSTFILE.jcl`
- **JOB name**: `CUSTFILE`
- **JOB description**: `'DEFINE CUSTOMER FILE'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Source version tag**: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Purpose**: **File setup / initial load of the Customer master file (with CICS quiesce/resume).** This job (re)builds the Customer KSDS VSAM dataset from scratch and refreshes the CICS view of it. It (1) closes the `CUSTDAT` file in the running CICS region so the batch job can take the dataset, (2) deletes any pre-existing Customer VSAM cluster, (3) defines a fresh KSDS cluster, (4) loads it by copying records from the supplied flat (sequential) seed file, and (5) re-opens `CUSTDAT` in CICS so the online application sees the reloaded data. It is a one-shot bootstrap/refresh job, not a posting/report/backup job. It is destructive: the existing Customer VSAM is erased and replaced with the contents of the PS seed file.

## Step summary

| Step | PGM | Action |
|------|-----|--------|
| CLCIFIL | SDSF | Issue CICS console command to **CLOSE** the `CUSTDAT` file in region `CICSAWSA` (quiesce online access) |
| STEP05 | IDCAMS | DELETE existing Customer VSAM KSDS cluster (idempotent) |
| STEP10 | IDCAMS | DEFINE new Customer VSAM KSDS cluster (data + index) |
| STEP15 | IDCAMS | REPRO flat seed file into the VSAM KSDS (load) |
| OPCIFIL | SDSF | Issue CICS console command to **OPEN** the `CUSTDAT` file in region `CICSAWSA` (resume online access) |

There are **5 EXEC steps**: two SDSF utility steps (CICS file close/open via the modify/`/F` operator command) bracketing three IDCAMS utility steps (delete / define / load). No COBOL `CB*` program, no SORT, no IEFBR14, no GDG usage in this job.

---

## CLCIFIL — Close CUSTDAT file in CICS region

- **EXEC**: `PGM=SDSF` (Spool Display and Search Facility, used in batch to issue an MVS operator/console command).
- **COND/RC gating**: none coded.
- **DD statements**:
  - `ISFOUT DD SYSOUT=*` — SDSF panel/output listing to spool.
  - `CMDOUT DD SYSOUT=*` — command response output to spool.
  - `ISFIN DD *` — inline SDSF command input (below).
- **Command issued (SDSF / MVS modify)**:
  ```
  /F CICSAWSA,'CEMT SET FIL(CUSTDAT ) CLO'
  ```
  - `/F CICSAWSA,...` is an MVS `MODIFY` (F) command sent to the started task / CICS region named **`CICSAWSA`**.
  - `CEMT SET FIL(CUSTDAT) CLO` is the CICS master-terminal command that sets the CICS file resource **`CUSTDAT`** to **CLOSED**, releasing CICS's hold on the underlying VSAM so the batch IDCAMS steps can delete/define/load it.
- **Datasets**: none of its own; it acts on the CICS-managed file `CUSTDAT`, which maps to the same VSAM cluster the IDCAMS steps rebuild (`AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS`).
- **Relational/file mapping**: targets the **Customer master** as seen by the online CICS application (file resource `CUSTDAT`). In the .NET conversion this has no data effect; it corresponds to taking the **`CUSTOMER`** store offline / acquiring an exclusive lock before the refresh (e.g. quiescing readers, or a no-op if the .NET runtime does not keep an open online handle).

## STEP05 — Delete existing Customer VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded on the EXEC. Failure tolerance is handled internally by the control statement (`IF MAXCC LE 08 THEN SET MAXCC = 0`), which forces the step return code to 0 even when the DELETE fails because the cluster does not yet exist (RC=8 from "entry not found"). This makes the step safe to run on a first-time setup.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output to spool.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DELETE AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
- **Datasets**:
  - Deletes VSAM cluster `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` (the Customer master file).
- **Relational/file mapping**: target is the **Customer master** store. Maps to the relational table **`CUSTOMER`** (the CUSTDAT/customer-master entity) in the .NET conversion. In z/OS this is the indexed VSAM KSDS that the online (CICS file `CUSTDAT`) and batch programs read/update as the customer record.

## STEP10 — Define new Customer VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none. (The job does not gate STEP10 on STEP05's RC; STEP05 is normalized to RC=0 so STEP10 always runs.)
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `SYSIN DD *` — inline control statements (below).
- **Control statements (IDCAMS)**:
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1 -
         ) -
         KEYS(9 0) -
         RECORDSIZE(500 500) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS.DATA) -
         ) -
         INDEX (NAME(AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS.INDEX) -
         )
  ```
- **Cluster attributes (key facts for the .NET model)**:
  - **Cluster name**: `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS`
  - **Type**: `INDEXED` (KSDS — Key-Sequenced Data Set).
  - **KEYS(9 0)**: primary key is **9 bytes long, starting at offset 0** of the record (i.e. the leading 9 bytes = the Customer ID / `CUST-ID`, a 9-digit customer number). This 9-byte leading key becomes the **primary key of the `CUSTOMER` table**.
  - **RECORDSIZE(500 500)**: fixed-length 500-byte records (avg = max = 500), matching the customer-record copybook (e.g. `CVCUS01Y`, CUSTOMER-RECORD layout).
  - **CYLINDERS(1 5)**: primary allocation 1 cylinder, secondary 5 cylinders.
  - **VOLUMES(AWSHJ1)**: placed on volume `AWSHJ1`.
  - **SHAREOPTIONS(2 3)**: cross-region share option 2, cross-system 3.
  - **ERASE**: data is physically erased on deletion (security/overwrite).
  - **DATA component**: `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS.DATA`
  - **INDEX component**: `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS.INDEX`
- **Datasets**: defines (allocates) the cluster and its DATA and INDEX components named above.
- **Relational/file mapping**: defines the storage for the **`CUSTOMER`** table. In .NET, this step corresponds to ensuring the Customer table/store exists with the 9-char customer-id primary key and a 500-byte fixed record schema; the DATA/INDEX VSAM components have no separate relational equivalent (the index becomes the table's clustered/primary index).

## STEP15 — Load (REPRO) flat seed file into the VSAM cluster

- **EXEC**: `PGM=IDCAMS`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `SYSPRINT DD SYSOUT=*` — IDCAMS listing.
  - `CUSTDATA DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CUSTDATA.PS` — **input** flat/sequential (PS) seed file containing the customer records to load.
  - `CUSTVSAM DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` — **output** target = the VSAM KSDS cluster defined in STEP10.
  - `SYSIN DD *` — inline control statement (below).
- **Control statements (IDCAMS)**:
  ```
  REPRO INFILE(CUSTDATA) OUTFILE(CUSTVSAM)
  ```
- **Datasets**:
  - **Reads (input)**: `AWS.M2.CARDDEMO.CUSTDATA.PS` — sequential seed file of customer records (the supplied initial data).
  - **Writes (output)**: `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` — the Customer KSDS, loaded key-sequenced by the 9-byte customer-id key. REPRO inserts each PS record as a VSAM record keyed on its leading 9 bytes.
- **Relational/file mapping**:
  - Input PS file `...CUSTDATA.PS` corresponds to the **seed/import dataset** for the `CUSTOMER` table (a flat extract used to populate it).
  - Output VSAM corresponds to the **`CUSTOMER`** table itself.
  - In .NET terms this step = "bulk load the CUSTOMER table from the seed file"; each 500-byte fixed record is parsed per the customer copybook and inserted with the 9-digit customer id as primary key.

## OPCIFIL — Open CUSTDAT file in CICS region

- **EXEC**: `PGM=SDSF`
- **COND/RC gating**: none coded.
- **DD statements**:
  - `ISFOUT DD SYSOUT=*` — SDSF panel/output listing to spool.
  - `CMDOUT DD SYSOUT=*` — command response output to spool.
  - `ISFIN DD *` — inline SDSF command input (below).
- **Command issued (SDSF / MVS modify)**:
  ```
  /F CICSAWSA,'CEMT SET FIL(CUSTDAT ) OPE'
  ```
  - `/F CICSAWSA,...` — MVS `MODIFY` command to CICS region **`CICSAWSA`**.
  - `CEMT SET FIL(CUSTDAT) OPE` sets CICS file resource **`CUSTDAT`** back to **OPEN/ENABLED**, so the online application can again read the freshly reloaded VSAM cluster.
- **Datasets**: none of its own; reopens the CICS file `CUSTDAT` over `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS`.
- **Relational/file mapping**: corresponds to bringing the **`CUSTOMER`** store back online for the application after the refresh (release the offline/lock state from `CLCIFIL`). No data effect; a no-op in environments where the .NET runtime opens the store on demand.

---

## PARM / GDG / SORT notes

- **PARM=**: none on any EXEC step.
- **GDG**: not used. All datasets are fixed-name (no `(+1)`/`(0)` generation references).
- **SORT**: not used; no SORT FIELDS statements. (No sorting is needed — REPRO loads records in the order they appear in the seed file, and KSDS load expects them in ascending key order.)
- **IEFBR14**: not used.
- **COND on JOB card**: none.
- **CICS coupling**: this job differs from the pure-VSAM-setup jobs by wrapping the IDCAMS refresh in CICS file CLOSE (`CLCIFIL`) and OPEN (`OPCIFIL`) commands against region `CICSAWSA`, file `CUSTDAT`. This is required because the online CICS region holds the VSAM open; it must be closed before the dataset can be deleted/redefined and re-opened afterward.

## Conversion notes for the .NET JobControl step-runner

- Implement as a 5-step job: (1) take the Customer store offline / acquire exclusive access (maps the CICS CLOSE), (2) drop-if-exists, (3) create, (4) bulk import from `CUSTDATA.PS`, (5) bring the store back online (maps the CICS OPEN). The two CICS steps are operational/locking concerns, not data transforms; model them as quiesce/resume hooks (or no-ops) around the destructive reload.
- Preserve the **idempotent delete** semantic of STEP05: a "table/store does not exist" condition must be swallowed (equivalent to `IF MAXCC LE 08 THEN SET MAXCC = 0`) so a clean first run does not fail.
- Honor the **9-byte leading primary key** and **500-byte fixed record** layout when defining the CUSTOMER schema and when parsing the seed file. (Note this differs from ACCTFILE: customer key is 9 bytes / record is 500 bytes, vs account's 11 bytes / 300 bytes.)
- This job is destructive/refresh-style: running it discards existing CUSTOMER data and reloads from the seed file. Guard accordingly in any environment with live data.
