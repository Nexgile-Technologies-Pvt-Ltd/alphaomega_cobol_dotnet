# JOB SPEC: ESDSRRDS

## Overview

| Attribute | Value |
|-----------|-------|
| **Job name** | `ESDSRRDS` |
| **JOB card description** | `'DEF ESDS RRDS  '` |
| **Region** | `8M` |
| **Class** | `A` |
| **MsgClass** | `H` |
| **Notify** | `&SYSUID` |
| **Source** | `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/ESDSRRDS.jcl` |
| **Version stamp** | `CardDemo_v1.0-15-g27d6c6f-68  Date: 2022-07-19 23:23:04 CDT` |

### Purpose

This is a **file-setup / VSAM build (load) job** for the CardDemo **User Security** data. It demonstrates and provisions two alternative VSAM organizations of the same User Security dataset:

1. An **ESDS** (Entry-Sequenced Data Set — `NONINDEXED`) cluster.
2. An **RRDS** (Relative-Record Data Set — `NUMBERED`) cluster.

The job does the following end-to-end:
- Pre-deletes any leftover flat (PS) work file.
- Builds a fixed-length (LRECL=80) sequential **User Security** file from **in-stream (instream) card data** (10 hard-coded users: 5 admins + 5 regular users).
- Defines the **ESDS** VSAM cluster and loads (REPROs) the PS data into it.
- Defines the **RRDS** VSAM cluster and loads (REPROs) the same PS data into it.

It is a one-time / re-runnable bootstrap job (every DEFINE is preceded by a DELETE + `SET MAXCC=0`), not part of a daily posting/report/backup cycle. It seeds the security/sign-on reference data that the online (CICS) and batch programs read for user authentication.

### Programs / Utilities invoked

| Step | PGM | Type |
|------|-----|------|
| PREDEL | `IEFBR14` | No-op utility (used only to allocate/DELETE the DD dataset) |
| STEP01 | `IEBGENER` | Sequential copy/generate utility (instream -> PS dataset) |
| STEP02 | `IDCAMS` | VSAM Access Method Services — DELETE + DEFINE CLUSTER (ESDS) |
| STEP03 | `IDCAMS` | VSAM Access Method Services — REPRO load (PS -> ESDS) |
| STEP04 | `IDCAMS` | VSAM Access Method Services — DELETE + DEFINE CLUSTER (RRDS) |
| STEP05 | `IDCAMS` | VSAM Access Method Services — REPRO load (PS -> RRDS) |

### Datasets touched

| Dataset (DSN) | Org | Role | Logical entity |
|---------------|-----|------|----------------|
| `AWS.M2.CARDDEMO.ESDSRRDS.PS` | PS (FB, LRECL=80) | Flat staging file built from instream data; source for both VSAM loads | User Security records (USRSEC) — staging |
| `AWS.M2.CARDDEMO.USRSEC.VSAM.ESDS` (+ `.DAT`) | VSAM ESDS (NONINDEXED) | Target VSAM cluster | User Security table (USRSEC) — ESDS variant |
| `AWS.M2.CARDDEMO.USRSEC.VSAM.RRDS` (+ `.DAT`) | VSAM RRDS (NUMBERED) | Target VSAM cluster | User Security table (USRSEC) — RRDS variant |

**Relational mapping:** All three datasets carry the same 80-byte **User Security** record (USRSEC). In the .NET target this maps to a single **User Security** table (e.g. `SEC_USER_DATA` / user-security entity): User ID (8), First name (20), Last name (20), Password (8), and a type/role flag. The job's flat PS file is the load source; the ESDS and RRDS clusters are two physical representations of the same table content. The record layout corresponds to the COBOL copybook `CSUSR01Y` (SEC-USER-DATA: SEC-USR-ID, SEC-USR-FNAME, SEC-USR-LNAME, SEC-USR-PWD, SEC-USR-TYPE, SEC-USR-FILLER).

### GDG usage

**None.** No generation data groups are referenced; all datasets are cataloged by absolute name with `DISP=(NEW,CATLG,DELETE)` / `DISP=SHR` / `DISP=(MOD,DELETE,DELETE)`.

### COND / RC gating

**No `COND=` or `IF/THEN` gating** is coded at the JCL step level — every step runs unconditionally (subject to normal abend propagation). Return-code control is instead done **inside the IDCAMS steps** via `SET MAXCC = 0`, which resets the condition code to 0 after the leading `DELETE` so that a "dataset not found" (RC=8) on first run does not fail the subsequent `DEFINE`.

---

## Step-by-step detail (in order)

### Step 1 — `PREDEL` (PGM=IEFBR14)

- **Purpose:** Pre-delete any pre-existing PS staging file so STEP01 can re-create it cleanly. `IEFBR14` does nothing itself; the DELETE happens via the DD disposition at step-end.
- **DD statements:**

  | DD | DSN | DISP | Notes |
  |----|-----|------|-------|
  | `DD01` | `AWS.M2.CARDDEMO.ESDSRRDS.PS` | `(MOD,DELETE,DELETE)` | Allocated then deleted whether the step ends normally or abnormally |

- **PARM:** none.
- **COND/RC:** none.

### Step 2 — `STEP01` (PGM=IEBGENER) — Create User Security PS file from instream data

- **Purpose:** Generate the fixed-length User Security sequential file from hard-coded instream "card" records.
- **DD statements:**

  | DD | DSN / source | DISP / attributes | Read/Write |
  |----|--------------|-------------------|------------|
  | `SYSUT1` | `DD *` (instream data) | 10 records, 80-byte cards | Read (input) |
  | `SYSUT2` | `AWS.M2.CARDDEMO.ESDSRRDS.PS` | `DISP=(NEW,CATLG,DELETE)`, `DCB=(LRECL=80,RECFM=FB,DSORG=PS,BLKSIZE=0)`, `UNIT=SYSAD`, `SPACE=(TRK,(10,5),RLSE)` | Write (output) |
  | `SYSPRINT` | `SYSOUT=*` | utility messages | Write |
  | `SYSIN` | `DUMMY` | no edit/reformat control records (straight copy) | — |

- **Instream User Security data (10 records, each laid out as ID(8) + FirstName(20) + LastName(20) + ...Password):**

  | User ID | First name | Last name | Password | Type implied |
  |---------|-----------|-----------|----------|--------------|
  | `ADMIN001` | MARGARET | GOLD | PASSWORDA | Admin (A) |
  | `ADMIN002` | RUSSELL | RUSSELL | PASSWORDA | Admin (A) |
  | `ADMIN003` | RAYMOND | WHITMORE | PASSWORDA | Admin (A) |
  | `ADMIN004` | EMMANUEL | CASGRAIN | PASSWORDA | Admin (A) |
  | `ADMIN005` | GRANVILLE | LACHAPELLE | PASSWORDA | Admin (A) |
  | `USER0001` | LAWRENCE | THOMAS | PASSWORDU | User (U) |
  | `USER0002` | AJITH | KUMAR | PASSWORDU | User (U) |
  | `USER0003` | LAURITZ | ALME | PASSWORDU | User (U) |
  | `USER0004` | AVERARDO | MAZZI | PASSWORDU | User (U) |
  | `USER0005` | LEE | TING | PASSWORDU | User (U) |

  > Note: the trailing token (`PASSWORDA` / `PASSWORDU`) begins at the password field; the final letter (`A`/`U`) doubles as the user-type indicator (Admin vs. regular User).

- **PARM:** none.
- **COND/RC:** none.

### Step 3 — `STEP02` (PGM=IDCAMS) — DEFINE ESDS cluster (with pre-DELETE)

- **Purpose:** Define the VSAM **ESDS** (Entry-Sequenced, `NONINDEXED`) cluster for User Security, deleting any prior copy first.
- **DD statements:**

  | DD | Value | Role |
  |----|-------|------|
  | `SYSPRINT` | `SYSOUT=*` | IDCAMS messages |
  | `SYSIN` | `DD *` | control statements (below) |

- **IDCAMS control statements (exact):**

  ```idcams
   DELETE                  AWS.M2.CARDDEMO.USRSEC.VSAM.ESDS
   SET       MAXCC = 0
   DEFINE    CLUSTER (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.ESDS)    -
                      RECORDSIZE(80,80)                         -
                      REUSE                                     -
                      NONINDEXED                                -
                      TRACKS(45,15)                             -
                      FREESPACE(10,15)                          -
                      CISZ(8192))                               -
             DATA    (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.ESDS.DAT))
  ```

- **Cluster attributes:** ESDS (`NONINDEXED`), fixed 80-byte records (`RECORDSIZE(80,80)`), `REUSE` (re-loadable as a work file), primary/secondary `TRACKS(45,15)`, `FREESPACE(10,15)`, control-interval size `CISZ(8192)`. Data component named `...ESDS.DAT`.
- **PARM:** none.
- **COND/RC:** `SET MAXCC = 0` after DELETE so a not-found condition (first-run RC=8) does not block the DEFINE.

### Step 4 — `STEP03` (PGM=IDCAMS) — REPRO load PS -> ESDS

- **Purpose:** Copy the User Security records from the PS staging file into the ESDS cluster.
- **DD statements:**

  | DD | DSN | DISP | Role |
  |----|-----|------|------|
  | `IN` | `AWS.M2.CARDDEMO.ESDSRRDS.PS` | `DISP=SHR` | Input (read) — flat PS source |
  | `OUT` | `AWS.M2.CARDDEMO.USRSEC.VSAM.ESDS` | `DISP=SHR` | Output (write) — ESDS target |
  | `SYSOUT` | `SYSOUT=*` | — | utility output |
  | `SYSPRINT` | `SYSOUT=*` | — | IDCAMS messages |
  | `SYSIN` | `DD *` | — | control statement (below) |

- **IDCAMS control statement (exact):**

  ```idcams
    REPRO INFILE(IN) OUTFILE(OUT)
  ```

- **PARM:** none.
- **COND/RC:** none.

### Step 5 — `STEP04` (PGM=IDCAMS) — DEFINE RRDS cluster (with pre-DELETE)

- **Purpose:** Define the VSAM **RRDS** (Relative-Record, `NUMBERED`) cluster for User Security, deleting any prior copy first.
- **DD statements:**

  | DD | Value | Role |
  |----|-------|------|
  | `SYSPRINT` | `SYSOUT=*` | IDCAMS messages |
  | `SYSIN` | `DD *` | control statements (below) |

- **IDCAMS control statements (exact):**

  ```idcams
   DELETE                  AWS.M2.CARDDEMO.USRSEC.VSAM.RRDS
   SET       MAXCC = 0
   DEFINE    CLUSTER (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.RRDS)    -
                      RECORDSIZE(80,80)                         -
                      REUSE                                     -
                      NUMBERED                                  -
                      TRACKS(45,15)                             -
                      FREESPACE(10,15)                          -
                      CISZ(8192))                               -
             DATA    (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.RRDS.DAT))
  ```

- **Cluster attributes:** RRDS (`NUMBERED`), fixed 80-byte records (`RECORDSIZE(80,80)`), `REUSE`, `TRACKS(45,15)`, `FREESPACE(10,15)`, `CISZ(8192)`. Data component named `...RRDS.DAT`. (Identical to the ESDS DEFINE except `NUMBERED` replaces `NONINDEXED`.)
- **PARM:** none.
- **COND/RC:** `SET MAXCC = 0` after DELETE (same pattern as STEP02).

### Step 6 — `STEP05` (PGM=IDCAMS) — REPRO load PS -> RRDS

- **Purpose:** Copy the same User Security records from the PS staging file into the RRDS cluster.
- **DD statements:**

  | DD | DSN | DISP | Role |
  |----|-----|------|------|
  | `IN` | `AWS.M2.CARDDEMO.ESDSRRDS.PS` | `DISP=SHR` | Input (read) — flat PS source |
  | `OUT` | `AWS.M2.CARDDEMO.USRSEC.VSAM.RRDS` | `DISP=SHR` | Output (write) — RRDS target |
  | `SYSOUT` | `SYSOUT=*` | — | utility output |
  | `SYSPRINT` | `SYSOUT=*` | — | IDCAMS messages |
  | `SYSIN` | `DD *` | — | control statement (below) |

- **IDCAMS control statement (exact):**

  ```idcams
    REPRO INFILE(IN) OUTFILE(OUT)
  ```

- **PARM:** none.
- **COND/RC:** none.

---

## SORT usage

**None.** No `SORT`/`DFSORT`/`ICETOOL` step appears in this job; no `SORT FIELDS` control statements exist.

## .NET JobControl step-runner notes

- 6 steps total: 1× IEFBR14 (file delete), 1× IEBGENER (instream -> flat file), 4× IDCAMS (2 DEFINE + 2 REPRO load).
- For the step-runner, model:
  - **PREDEL/IEFBR14** -> "delete file if exists" action on `AWS.M2.CARDDEMO.ESDSRRDS.PS`.
  - **STEP01/IEBGENER** -> "write embedded fixed-80 records to a staging file/table" (the 10 seed users).
  - **STEP02/STEP04 IDCAMS DEFINE** -> "create/replace User Security store" (in .NET both ESDS and RRDS collapse to the same logical User Security table; `NONINDEXED` vs `NUMBERED` is a physical VSAM distinction, not a schema difference).
  - **STEP03/STEP05 IDCAMS REPRO** -> "bulk load staging records into the User Security store".
- Re-runnable: each DEFINE is preceded by DELETE + `SET MAXCC=0`, so the step-runner should treat "target already exists" / "not found on first run" as non-fatal (idempotent create).
- Record layout per `CSUSR01Y` (SEC-USER-DATA): SEC-USR-ID PIC X(08), SEC-USR-FNAME PIC X(20), SEC-USR-LNAME PIC X(20), SEC-USR-PWD PIC X(08), SEC-USR-TYPE PIC X(01), SEC-USR-FILLER PIC X(23) = 80 bytes.
