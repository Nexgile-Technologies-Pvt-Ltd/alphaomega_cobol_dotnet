# JOB SPEC: READCARD

## Overview

- **Job name:** `READCARD`
- **JOB card:** `JOB 'READCARD',CLASS=A,MSGCLASS=0,NOTIFY=&SYSUID`
- **Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/READCARD.jcl`
- **Version stamp:** `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)
- **Step count:** 1 EXEC step (`STEP05`)
- **Programs/utilities invoked:** `CBACT02C` (batch COBOL application program)

### Purpose

This is a **read / report (dump) job** for the CardDemo *Card Data* master file.
Its sole function is to run the batch COBOL program **CBACT02C**, which opens the
Card master VSAM KSDS sequentially, reads every record from first to last, and
`DISPLAY`s (prints to SYSOUT) each card record. It is a **read-only diagnostic /
listing job** — it does **not** define, load, sort, update, post, or back up
anything. No dataset is written; the only output is the printed record stream on
SYSOUT/SYSPRINT.

There is **no COND/RC gating** in this JCL (no `COND=` parameters, no `IF`
logic — there is only one step, so there is nothing to gate). There is **no GDG
usage** — the single input dataset is a fixed (non-generation) VSAM cluster name.
There is **no PARM=** on the EXEC. There is no IDCAMS or SORT utility in this job,
so there are no `DEFINE`/`REPRO`/`DELETE` or `SORT FIELDS` control statements.

### Data domain mapping

| Mainframe dataset | DD name | I/O | Type | Logical entity / .NET target |
|---|---|---|---|---|
| `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` | `CARDFILE` | **Read** (input) | VSAM KSDS (base cluster) | **Card** table / Card master file (RECLEN 150, primary key = Card Number, 16 bytes at offset 0) |
| `AWS.M2.CARDDEMO.LOADLIB` | `STEPLIB` | Read (load) | PDS load library | Program library that contains the `CBACT02C` load module (no .NET equivalent — resolved at link/deploy time) |
| (spool) | `SYSOUT` | Write | SYSOUT print | Program runtime `DISPLAY` messages (start/end banners, errors) |
| (spool) | `SYSPRINT` | Write | SYSOUT print | System/runtime print stream |

The Card record layout corresponds to the CardDemo card record copybook
`CVACT02Y` (`CARD-RECORD`): a 16-byte Card Number primary key (`FD-CARD-NUM` /
`CARD-NUM`) followed by 134 bytes of card data, total record length 150. The
program reads via the primary key in **sequential** access mode (it walks the
whole file; it is not a keyed/random lookup).

---

## Step-by-step detail

### Step 1 — `STEP05` (EXEC PGM=CBACT02C) — Read and print the Card master file

- **Program:** `CBACT02C` — a CardDemo BATCH COBOL program. Function (per its
  prologue): "Read and print card data file." It is **not** a utility
  (not IDCAMS/SORT/IEFBR14); it is an application `CB*`-class program.
- **Purpose:** Sequentially read every record in the Card master VSAM KSDS and
  `DISPLAY` it to SYSOUT. Effectively a full dump / listing of the card file.
- **DD statements:**
  - `STEPLIB  DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` — load library holding the
    `CBACT02C` executable module (program resolution; not application data).
  - `CARDFILE DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` — **input** Card
    master VSAM KSDS. This is the only application dataset, and it is **read
    only** (program issues `OPEN INPUT`). Maps to the program's
    `SELECT CARDFILE-FILE ASSIGN TO CARDFILE` (ORGANIZATION INDEXED, ACCESS
    SEQUENTIAL, RECORD KEY `FD-CARD-NUM`).
  - `SYSOUT   DD SYSOUT=*` — spool; captures the program's `DISPLAY` output
    (record contents, start/end-of-execution banners, and any I/O-error /
    file-status diagnostics).
  - `SYSPRINT DD SYSOUT=*` — spool; runtime/system print stream.
- **Relational/file correspondence:**
  - `CARDFILE` (DD) → `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` → the **Card** entity
    (card master table). In the .NET model this is a read-only sequential scan of
    the Card store, printing each row.
- **Program I/O behavior (from `CBACT02C.cbl`):**
  - `0000-CARDFILE-OPEN` → `OPEN INPUT CARDFILE-FILE`; abends (CEE3ABD,
    ABCODE 999) on a non-`'00'` file status.
  - Loop `1000-CARDFILE-GET-NEXT` → `READ CARDFILE-FILE INTO CARD-RECORD`; on
    status `'00'` continue, on `'10'` (EOF) stop the loop, on anything else
    display the file status and abend.
  - Each successfully read record is `DISPLAY CARD-RECORD` (the printed output).
  - `9000-CARDFILE-CLOSE` → `CLOSE` then `GOBACK`.
  - **No output dataset is opened or written** — the program is purely a reader.
- **PARM:** none.
- **COND / RC gating:** none (single-step job). The program's own error path is to
  **abend** (RC via `CEE3ABD` with code 999) on any unexpected VSAM file status;
  normal completion is RC=0.
- **GDG:** none.
- **.NET runner note:** Model as a single read-only step that opens the Card
  store, iterates all records in key order, and writes each record to the job log
  / SYSOUT-equivalent. Failure to open or an I/O error should terminate the step
  with a non-zero (abend-equivalent) code, matching the COBOL `CEE3ABD` behavior.

---

## Step-runner sequencing summary

| # | Step | PGM | Type | Action | Reads | Writes |
|---|------|-----|------|--------|-------|--------|
| 1 | STEP05 | CBACT02C | Application COBOL | Sequentially read & DISPLAY every card record | `CARDFILE` = `AWS.M2.CARDDEMO.CARDDATA.VSAM.KSDS` (Card master KSDS) | `SYSOUT`, `SYSPRINT` (print only) |

- **Overall classification:** read-only report / file-dump job (no setup, no load,
  no sort, no posting, no backup).
- **GDG:** none.
- **JCL-level COND/RC gating:** none.
- **PARM=:** none.
- **IDCAMS / SORT control statements:** none (no utility steps in this job).
- **Abend semantics:** program self-abends (`CEE3ABD`, code 999) on VSAM
  open/read/close error; otherwise RC=0.
