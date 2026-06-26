# JOB SPEC: CARDFILE

## Overview

- **Job name:** `CARDFILE`
- **JOB card:** `JOB 'Delete define card data',CLASS=A,MSGCLASS=0,NOTIFY=&SYSUID`
- **Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/CARDFILE.jcl`
- **Version stamp:** `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Step count:** 8 EXEC steps
- **Programs/utilities invoked:** `SDSF`, `IDCAMS`

### Purpose

This is a **file setup / VSAM provisioning job** for the CardDemo *Card Data*
file. It (1) quiesces the file in the running CICS region, (2) tears down and
re-builds the `CARDDATA` VSAM KSDS cluster, (3) loads it from a flat sequential
file, (4) builds an alternate index keyed on Account ID with its path, and (5)
re-opens the file in CICS. There is **no posting, reporting, or backup logic** —
it is purely an initialize-and-load (refresh) job for the card master file.

There is **no COND/RC gating at the JCL step level** (no `COND=` parameters).
RC handling exists only *inside* the IDCAMS DELETE step via `IF MAXCC` logic
(see STEP05). There is **no GDG usage** in this job — all datasets are fixed
(non-generation) names.

### Data domain mapping

| Mainframe dataset | Type | Logical entity / .NET target |
|---|---|---|
| `AWS.M2.CARDDEMO.CARDDATA.PS` | Sequential (flat) input | Source seed/extract of the Card master records |
| `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` | VSAM KSDS (base cluster) | **Card** table / Card master file (RECLEN 150, key = Card Number, 16 bytes at offset 0) |
| `AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX` | VSAM Alternate Index | Secondary index **by Account ID** (11-byte key at offset 16) — supports "cards by account" lookups |
| `AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX.PATH` | VSAM Path | Read path relating AIX to base cluster |

The base record layout corresponds to the CardDemo `CVACT02Y` / card record copybook: 16-byte Card Number primary key (`CARD-NUM`), followed by an 11-byte Account ID field at offset 16 (used as the AIX key), total record length 150.

CICS file names: base file `CARDDAT`, alternate-index file `CARDAIX`, in region `CICSAWSA`.

---

## Step-by-step detail

### Step 1 — `CLCIFIL` (EXEC PGM=SDSF) — Close files in CICS

- **Program:** `SDSF` (System Display and Search Facility, used here as an operator-command issuer).
- **Purpose:** Close the card files in the CICS region so VSAM can be deleted/redefined without enqueue conflicts.
- **DD statements:**
  - `ISFOUT  DD SYSOUT=*` — SDSF panel output.
  - `CMDOUT  DD SYSOUT=*` — issued-command responses.
  - `ISFIN   DD *` — inline SDSF command input.
- **Control / commands issued (modify CICS region `CICSAWSA`):**
  ```
  /F CICSAWSA,'CEMT SET FIL(CARDDAT ) CLO'
  /F CICSAWSA,'CEMT SET FIL(CARDAIX ) CLO'
  ```
  Closes CICS file `CARDDAT` (base) and `CARDAIX` (alternate index).
- **PARM / COND:** none.
- **.NET runner note:** No real CICS equivalent; treat as a no-op or as "ensure no open handle / lock on the Card store" guard before the rebuild.

### Step 2 — `STEP05` (EXEC PGM=IDCAMS) — Delete existing VSAM if present

- **Program:** `IDCAMS`.
- **Purpose:** Idempotent teardown — delete the base cluster and alternate index if they already exist, tolerating "not found".
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`.
  - `SYSIN    DD *` — control statements below.
- **Control statements:**
  ```
  DELETE AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS -
         CLUSTER
  IF MAXCC LE 08 THEN SET MAXCC = 0
  DELETE AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX -
         ALTERNATEINDEX
  IF MAXCC LE 08 THEN SET MAXCC = 0
  ```
- **RC gating (internal):** After each DELETE, `IF MAXCC LE 08 THEN SET MAXCC = 0` resets the condition code so a "dataset does not exist" (RC=8) does not fail the job. The step effectively always ends RC=0.
- **Datasets affected:** deletes base cluster `...CARDDATA.VSAM.KSDS` and alternate index `...CARDDATA.VSAM.AIX`.
- **PARM:** none.

### Step 3 — `STEP10` (EXEC PGM=IDCAMS) — Define VSAM KSDS cluster

- **Program:** `IDCAMS`.
- **Purpose:** Create the empty base KSDS cluster for the Card master.
- **DD statements:** `SYSPRINT DD SYSOUT=*`; `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE CLUSTER (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS) -
         CYLINDERS(1 5) -
         VOLUMES(AWSHJ1) -
         KEYS(16 0) -
         RECORDSIZE(150 150) -
         SHAREOPTIONS(2 3) -
         ERASE -
         INDEXED -
         ) -
         DATA (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS.DATA)) -
         INDEX (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS.INDEX))
  ```
- **Key cluster attributes:**
  - `KEYS(16 0)` — primary key is 16 bytes long at offset 0 (the Card Number).
  - `RECORDSIZE(150 150)` — fixed-length 150-byte records.
  - `CYLINDERS(1 5)` — 1 primary cyl + 5 secondary, on volume `AWSHJ1`.
  - `SHAREOPTIONS(2 3)`, `ERASE`, `INDEXED`.
  - Data component `...KSDS.DATA`, index component `...KSDS.INDEX`.
- **PARM / COND:** none.

### Step 4 — `STEP15` (EXEC PGM=IDCAMS) — REPRO flat file into VSAM

- **Program:** `IDCAMS`.
- **Purpose:** Load (copy) the seed records from the sequential flat file into the newly defined VSAM KSDS.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`.
  - `CARDDATA DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CARDDATA.PS` — **input** flat file (the Card data source / seed).
  - `CARDVSAM DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` — **output** VSAM base cluster.
  - `SYSIN DD *`.
- **Control statements:**
  ```
  REPRO INFILE(CARDDATA) OUTFILE(CARDVSAM)
  ```
- **Mapping:** reads sequential `CARDDATA.PS` → writes base cluster `CARDDATA.VSAM.KSDS` (= load the Card master table). In .NET this corresponds to bulk-loading the Card store/table from the seed file.
- **PARM / COND:** none.

### Step 5 — `STEP40` (EXEC PGM=IDCAMS) — Define Alternate Index on Account ID

- **Program:** `IDCAMS`.
- **Purpose:** Define an alternate index over the base cluster keyed by Account ID.
- **DD statements:** `SYSPRINT DD SYSOUT=*`; `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE ALTERNATEINDEX (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX) -
   RELATE(AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS)                    -
   KEYS(11 16)                                                   -
   NONUNIQUEKEY                                                  -
   UPGRADE                                                       -
   RECORDSIZE(150,150)                                           -
   VOLUMES(AWSHJ1)                                               -
   CYLINDERS(5,1))                                               -
   DATA (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX.DATA))           -
   INDEX (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX.INDEX))
  ```
- **Key AIX attributes:**
  - `RELATE(...KSDS)` — built over the base Card cluster.
  - `KEYS(11 16)` — AIX key is 11 bytes at offset 16 = the **Account ID** field. (This is what makes "find all cards for an account" possible.)
  - `NONUNIQUEKEY` — multiple cards may share one account (one-to-many).
  - `UPGRADE` — AIX is kept in sync (upgraded) on base-cluster updates.
  - `RECORDSIZE(150,150)`, on volume `AWSHJ1`, `CYLINDERS(5,1)`.
- **.NET mapping:** equivalent to a secondary (non-unique) index on `Card.AccountId`.
- **PARM / COND:** none.

### Step 6 — `STEP50` (EXEC PGM=IDCAMS) — Define PATH for the AIX

- **Program:** `IDCAMS`.
- **Purpose:** Define a VSAM PATH that relates the alternate index back to the base cluster so applications can open the AIX as a usable access path.
- **DD statements:** `SYSPRINT DD SYSOUT=*`; `SYSIN DD *`.
- **Control statements:**
  ```
  DEFINE PATH -
   (NAME(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX.PATH) -
    PATHENTRY(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX))
  ```
- **Mapping:** path `...AIX.PATH` over alternate index `...AIX`. No direct .NET artifact (the secondary index itself is the equivalent).
- **PARM / COND:** none.

### Step 7 — `STEP60` (EXEC PGM=IDCAMS) — Build the Alternate Index

- **Program:** `IDCAMS`.
- **Purpose:** Populate (build) the alternate index from the now-loaded base cluster.
- **DD statements:** `SYSPRINT DD SYSOUT=*`; `SYSIN DD *`.
- **Control statements:**
  ```
  BLDINDEX -
   INDATASET(AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS) -
   OUTDATASET(AWS.M2.CARDDEMO.CARDDATA.VSAM.AIX)
  ```
- **Mapping:** scans base cluster `...KSDS` (IN) and populates AIX `...AIX` (OUT). In .NET this is the "rebuild the secondary index after bulk load" operation.
- **PARM / COND:** none.

### Step 8 — `OPCIFIL` (EXEC PGM=SDSF) — Open files in CICS

- **Program:** `SDSF`.
- **Purpose:** Re-open the card files in the CICS region now that the VSAM cluster and AIX have been rebuilt and loaded.
- **DD statements:**
  - `ISFOUT  DD SYSOUT=*`.
  - `CMDOUT  DD SYSOUT=*`.
  - `ISFIN   DD *` — inline SDSF commands.
- **Control / commands issued:**
  ```
  /F CICSAWSA,'CEMT SET FIL(CARDDAT ) OPE'
  /F CICSAWSA,'CEMT SET FIL(CARDAIX ) OPE'
  ```
  Re-opens CICS file `CARDDAT` (base) and `CARDAIX` (alternate index) in region `CICSAWSA`.
- **PARM / COND:** none.
- **.NET runner note:** Mirror of step 1 — no-op / "release lock" in the .NET model.

---

## Step-runner sequencing summary

| # | Step | PGM | Action | Reads | Writes |
|---|------|-----|--------|-------|--------|
| 1 | CLCIFIL | SDSF | Close CICS files (CARDDAT, CARDAIX) | — | — |
| 2 | STEP05 | IDCAMS | DELETE cluster + AIX (idempotent, MAXCC reset) | — | drops KSDS, AIX |
| 3 | STEP10 | IDCAMS | DEFINE CLUSTER (KSDS, key 16@0, reclen 150) | — | creates KSDS |
| 4 | STEP15 | IDCAMS | REPRO flat → VSAM | `CARDDATA.PS` | `CARDDATA.VSAM.KSDS` |
| 5 | STEP40 | IDCAMS | DEFINE ALTERNATEINDEX (key 11@16 = AcctID, nonunique) | — | creates AIX |
| 6 | STEP50 | IDCAMS | DEFINE PATH (AIX→base) | — | creates PATH |
| 7 | STEP60 | IDCAMS | BLDINDEX (populate AIX) | `...KSDS` | `...AIX` |
| 8 | OPCIFIL | SDSF | Open CICS files (CARDDAT, CARDAIX) | — | — |

- **GDG:** none.
- **JCL-level COND/RC gating:** none; only internal `IF MAXCC LE 08 THEN SET MAXCC = 0` in STEP05.
- **PARM=:** none on any step.
