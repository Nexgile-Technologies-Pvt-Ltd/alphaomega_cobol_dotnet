# JOB SPEC: DUSRSECJ

## Overview

| Attribute | Value |
|-----------|-------|
| **Job name** | `DUSRSECJ` |
| **Job title** | `DEF USRSEC FILE` (Define User Security File) |
| **Source** | `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/DUSRSECJ.jcl` |
| **JOB card** | `REGION=8M, CLASS=A, MSGCLASS=H, NOTIFY=&SYSUID` |
| **Step count** | 4 (`PREDEL`, `STEP01`, `STEP02`, `STEP03`) |
| **Programs/utilities** | `IEFBR14`, `IEBGENER`, `IDCAMS` |
| **Purpose** | **File setup / seeding job.** Initializes the User Security (USRSEC) data store. It deletes any prior sequential copy, generates a fresh flat (PS) file of 10 seed users (5 admins + 5 regular users) from in-stream data, (re)defines the USRSEC VSAM KSDS, and loads the VSAM file from the PS file. This is a one-time / re-runnable bootstrap of the security/login credential table, not a posting or reporting job. |

### Version stamp
`Ver: CardDemo_v1.0-15-g27d6c6f-68  Date: 2022-07-19 23:23:06 CDT`

### Datasets touched (summary)

| Dataset | Type | Logical meaning / target entity |
|---------|------|---------------------------------|
| `AWS.M2.CARDDEMO.USRSEC.PS` | Sequential (FB, LRECL=80) | Flat seed file of user-security records (the source for the VSAM load) |
| `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS` | VSAM KSDS (key 8, RECSZ 80) | **User Security file** — the relational equivalent is the `USRSEC` / users table (login id, first name, last name, password, user type). Key = 8-byte User ID. |
| `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS.DAT` | VSAM data component | Data component of the KSDS above |
| `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS.IDX` | VSAM index component | Index component of the KSDS above |

No GDG (generation data group) usage in this job — all datasets are fixed names.

There is **no explicit COND / RC gating** between steps on the EXEC cards (every step runs unconditionally subject to a normal abend; default z/OS behavior is that a preceding abend would flush later steps). The only return-code management is internal to `STEP02` via `SET MAXCC=0` (see below).

---

## Step 1 — `PREDEL`  (pre-delete / cleanup)

| Field | Value |
|-------|-------|
| **EXEC** | `PGM=IEFBR14` |
| **Type** | Utility (no-op program used purely for dataset disposition) |
| **PARM** | none |
| **COND/RC** | none |

### DD / datasets
| DD | DSN | DISP | I/O | Notes |
|----|-----|------|-----|-------|
| `DD01` | `AWS.M2.CARDDEMO.USRSEC.PS` | `(MOD,DELETE,DELETE)` | delete | Allocated `MOD` then deleted on both normal and abnormal end, so any leftover sequential USRSEC file from a prior run is removed. `IEFBR14` does nothing itself; the deletion is a side effect of the DISP. |

**Effect:** Ensures a clean start by removing the prior `USRSEC.PS` sequential file before it is regenerated in STEP01.

---

## Step 2 — `STEP01`  (create the PS seed file from in-stream data)

| Field | Value |
|-------|-------|
| **EXEC** | `PGM=IEBGENER` |
| **Type** | Utility (sequential copy / generate) |
| **PARM** | none |
| **COND/RC** | none |

### DD / datasets
| DD | DSN / Source | DISP / Attributes | I/O | Notes |
|----|--------------|-------------------|-----|-------|
| `SYSUT1` | In-stream data (`DD *`) | — | input | 10 fixed-format records (the seed user list, see below). Terminated by `/*`. |
| `SYSUT2` | `AWS.M2.CARDDEMO.USRSEC.PS` | `DISP=(NEW,CATLG,DELETE)`, `DCB=(LRECL=80,RECFM=FB,DSORG=PS,BLKSIZE=0)`, `UNIT=SYSAD`, `SPACE=(TRK,(10,5),RLSE)` | output | Newly created, cataloged on success / deleted on failure. System-determined blocksize. |
| `SYSPRINT` | `SYSOUT=*` | — | output | Utility messages |
| `SYSIN` | `DUMMY` | — | — | No control statements → straight copy SYSUT1 → SYSUT2 |

**Effect:** Generates the `USRSEC.PS` flat file containing the seed security records.

### In-stream seed data (`SYSUT1`)
Record layout (80-byte FB; columns inferred from data alignment): `User-ID (8)`, `First-Name (20)`, `Last-Name (20)`, `Password (8)`, plus a 1-byte user-type code embedded as the last character of the password field (`A`=Admin, `U`=User). This corresponds to the USRSEC record / users table.

```
ADMIN001MARGARET            GOLD                PASSWORDA
ADMIN002RUSSELL             RUSSELL             PASSWORDA
ADMIN003RAYMOND             WHITMORE            PASSWORDA
ADMIN004EMMANUEL            CASGRAIN            PASSWORDA
ADMIN005GRANVILLE           LACHAPELLE          PASSWORDA
USER0001LAWRENCE            THOMAS              PASSWORDU
USER0002AJITH               KUMAR               PASSWORDU
USER0003LAURITZ             ALME                PASSWORDU
USER0004AVERARDO            MAZZI               PASSWORDU
USER0005LEE                 TING                PASSWORDU
```

- 5 admin users: `ADMIN001`..`ADMIN005`, password `PASSWORD`, type `A`.
- 5 standard users: `USER0001`..`USER0005`, password `PASSWORD`, type `U`.

---

## Step 3 — `STEP02`  (define the VSAM KSDS)

| Field | Value |
|-------|-------|
| **EXEC** | `PGM=IDCAMS` |
| **Type** | Access Method Services (VSAM define/delete) |
| **PARM** | none |
| **COND/RC** | Internal: `SET MAXCC = 0` after the DELETE so a "dataset not found" on first run does not fail the step |

### DD / datasets
| DD | DSN / Source | I/O | Notes |
|----|--------------|-----|-------|
| `SYSPRINT` | `SYSOUT=*` | output | IDCAMS messages |
| `SYSIN` | in-stream control statements | input | DELETE + DEFINE (see below) |

### Exact IDCAMS control statements (`SYSIN`)
```
 DELETE                  AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS
 SET       MAXCC = 0
 DEFINE    CLUSTER (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS)    -
                    KEYS(8,0)                                 -
                    RECORDSIZE(80,80)                         -
                    REUSE                                     -
                    INDEXED                                   -
                    TRACKS(45,15)                             -
                    FREESPACE(10,15)                          -
                    CISZ(8192))                               -
           DATA    (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS.DAT)) -
           INDEX   (NAME(AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS.IDX))
```

**Cluster definition details (target = USRSEC table):**
| Parameter | Value | Meaning |
|-----------|-------|---------|
| `NAME` | `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS` | Cluster name (USRSEC file) |
| `KEYS(8,0)` | 8-byte key at offset 0 | Primary key = **User ID** (first 8 bytes) |
| `RECORDSIZE(80,80)` | fixed 80 | Avg=max=80, matches the PS LRECL |
| `REUSE` | — | Reusable cluster (can be re-opened/reset; supports re-running this load) |
| `INDEXED` | — | KSDS (key-sequenced) |
| `TRACKS(45,15)` | primary 45 / secondary 15 | Space allocation |
| `FREESPACE(10,15)` | CI 10% / CA 15% | Free space for inserts |
| `CISZ(8192)` | 8 KB | Control interval size |
| `DATA NAME` | `...KSDS.DAT` | Data component |
| `INDEX NAME` | `...KSDS.IDX` | Index component |

**Effect:** Deletes any existing USRSEC KSDS (ignoring not-found via `MAXCC=0`) and (re)defines an empty key-sequenced VSAM file keyed on the 8-byte User ID.

---

## Step 4 — `STEP03`  (load VSAM from PS)

| Field | Value |
|-------|-------|
| **EXEC** | `PGM=IDCAMS` |
| **Type** | Access Method Services (REPRO copy) |
| **PARM** | none |
| **COND/RC** | none |

### DD / datasets
| DD | DSN | DISP | I/O | Maps to |
|----|-----|------|-----|---------|
| `IN` | `AWS.M2.CARDDEMO.USRSEC.PS` | `SHR` | input | The PS seed file built in STEP01 |
| `OUT` | `AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS` | `SHR` | output | The USRSEC VSAM KSDS defined in STEP02 (the user-security table) |
| `SYSOUT` | `SYSOUT=*` | — | output | REPRO messages |
| `SYSPRINT` | `SYSOUT=*` | — | output | IDCAMS messages |
| `SYSIN` | in-stream | — | input | REPRO control statement (below) |

### Exact IDCAMS control statement (`SYSIN`)
```
  REPRO INFILE(IN) OUTFILE(OUT)
```

**Effect:** Copies (loads) all 10 records from the sequential `USRSEC.PS` file into the VSAM KSDS, populating the User Security file. After this step the USRSEC VSAM file is ready for online security/login lookups by key (User ID).

---

## Conversion notes for the .NET JobControl step-runner

- **Posting/relational mapping:** `USRSEC.VSAM.KSDS` → the **users / security** relational table (`USRSEC`). Columns: `USER_ID` (PK, 8), `FIRST_NAME` (20), `LAST_NAME` (20), `PASSWORD` (8), `USER_TYPE` (1, `A`/`U`). The PS file `USRSEC.PS` is just an intermediate flat-file representation of the same rows.
- **Step semantics for the runner:**
  1. `PREDEL` → drop/clear prior flat artifact (idempotent cleanup).
  2. `STEP01` → seed 10 rows from the in-stream literal data (embed the seed list as data, not C# logic).
  3. `STEP02` → "create/replace table" (define empty KSDS); the `DELETE` + `SET MAXCC=0` pattern = "drop if exists, ignore not-found".
  4. `STEP03` → bulk load seed rows into the table (REPRO = load).
- **Idempotency:** Job is designed to be safely re-runnable — `PREDEL` (DISP DELETE on MOD), `DELETE … SET MAXCC=0`, and `REUSE` on the cluster all support repeated execution that resets the USRSEC store to the 10 seed users.
- **No COND= chaining and no GDG;** ordering is purely sequential and each step depends on the prior step's output dataset.
