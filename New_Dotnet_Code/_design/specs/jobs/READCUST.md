# JOB SPEC: READCUST

## Overview

- **Job name:** `READCUST`
- **JOB card:** `JOB 'Read Customer Data file',CLASS=A,MSGCLASS=0,NOTIFY=&SYUID`
  - (Note the literal source has `&SYUID`; this is the standard `&SYSUID` notify
    symbolic — a typo in the member, harmless to the run.)
- **Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/READCUST.jcl`
- **Version stamp:** `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19 23:23:07 CDT)
- **Step count:** 1 EXEC step (`STEP05`)
- **Programs/utilities invoked:** `CBCUS01C` (batch COBOL application program)

### Purpose

This is a **read / report (dump) job** for the CardDemo *Customer* master file.
Its sole function is to run the batch COBOL program **CBCUS01C**, which opens the
Customer master VSAM KSDS sequentially, reads every record from first to last, and
`DISPLAY`s (prints to SYSOUT) each customer record. It is a **read-only diagnostic /
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
| `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` | `CUSTFILE` | **Read** (input) | VSAM KSDS (base cluster) | **Customer** table / Customer master file (RECLEN 500, primary key = Customer ID, 9 bytes at offset 0) |
| `AWS.M2.CARDDEMO.LOADLIB` | `STEPLIB` | Read (load) | PDS load library | Program library that contains the `CBCUS01C` load module (no .NET equivalent — resolved at link/deploy time) |
| (spool) | `SYSOUT` | Write | SYSOUT print | Program runtime `DISPLAY` messages (start/end banners, customer records, errors) |
| (spool) | `SYSPRINT` | Write | SYSOUT print | System/runtime print stream |

The Customer record layout corresponds to the CardDemo customer record copybook
`CVCUS01Y` (`CUSTOMER-RECORD`): a 9-byte Customer ID primary key
(`FD-CUST-ID` / `CUST-ID`) followed by 491 bytes of customer data, total record
length **500** (FD `FD-CUSTFILE-REC` = `PIC 9(09)` + `PIC X(491)`). The program
reads via the primary key in **sequential** access mode (it walks the whole file;
it is not a keyed/random lookup).

---

## Step-by-step detail

### Step 1 — `STEP05` (EXEC PGM=CBCUS01C) — Read and print the Customer master file

- **Program:** `CBCUS01C` — a CardDemo BATCH COBOL program. Function (per its
  prologue): "Read and print customer data file." It is **not** a utility
  (not IDCAMS/SORT/IEFBR14); it is an application `CB*`-class program.
- **Purpose:** Sequentially read every record in the Customer master VSAM KSDS and
  `DISPLAY` it to SYSOUT. Effectively a full dump / listing of the customer file.
- **DD statements:**
  - `STEPLIB  DD DISP=SHR,DSN=AWS.M2.CARDDEMO.LOADLIB` — load library holding the
    `CBCUS01C` executable module (program resolution; not application data).
  - `CUSTFILE DD DISP=SHR,DSN=AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` — **input**
    Customer master VSAM KSDS. This is the only application dataset, and it is
    **read only** (program issues `OPEN INPUT`). Maps to the program's
    `SELECT CUSTFILE-FILE ASSIGN TO CUSTFILE` (ORGANIZATION INDEXED, ACCESS
    SEQUENTIAL, RECORD KEY `FD-CUST-ID`).
  - `SYSOUT   DD SYSOUT=*` — spool; captures the program's `DISPLAY` output
    (record contents, start/end-of-execution banners, and any I/O-error /
    file-status diagnostics).
  - `SYSPRINT DD SYSOUT=*` — spool; runtime/system print stream.
- **Relational/file correspondence:**
  - `CUSTFILE` (DD) → `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` → the **Customer**
    entity (customer master table). In the .NET model this is a read-only
    sequential scan of the Customer store, printing each row.
- **Program I/O behavior (from `CBCUS01C.cbl`):**
  - `0000-CUSTFILE-OPEN` → `OPEN INPUT CUSTFILE-FILE`; abends (CEE3ABD,
    ABCODE 999) on a non-`'00'` file status.
  - Loop `1000-CUSTFILE-GET-NEXT` → `READ CUSTFILE-FILE INTO CUSTOMER-RECORD`; on
    status `'00'` continue (and `DISPLAY` the record), on `'10'` (EOF) stop the
    loop, on anything else display the file status and abend.
  - Each successfully read record is `DISPLAY CUSTOMER-RECORD` (the printed
    output). (Note: the program displays the record both inside
    `1000-CUSTFILE-GET-NEXT` and again in the mainline loop after a successful
    read, so each record can appear twice in SYSOUT.)
  - `9000-CUSTFILE-CLOSE` → `CLOSE` then `GOBACK`.
  - **No output dataset is opened or written** — the program is purely a reader.
- **PARM:** none.
- **COND / RC gating:** none (single-step job). The program's own error path is to
  **abend** (RC via `CEE3ABD` with code 999) on any unexpected VSAM file status;
  normal completion is RC=0.
- **GDG:** none.
- **.NET runner note:** Model as a single read-only step that opens the Customer
  store, iterates all records in key order, and writes each record to the job log
  / SYSOUT-equivalent. Failure to open or an I/O error should terminate the step
  with a non-zero (abend-equivalent) code, matching the COBOL `CEE3ABD` behavior.

---

## Step-runner sequencing summary

| # | Step | PGM | Type | Action | Reads | Writes |
|---|------|-----|------|--------|-------|--------|
| 1 | STEP05 | CBCUS01C | Application COBOL | Sequentially read & DISPLAY every customer record | `CUSTFILE` = `AWS.M2.CARDDEMO.CUSTDATA.VSAM.KSDS` (Customer master KSDS) | `SYSOUT`, `SYSPRINT` (print only) |

- **Overall classification:** read-only report / file-dump job (no setup, no load,
  no sort, no posting, no backup).
- **GDG:** none.
- **JCL-level COND/RC gating:** none.
- **PARM=:** none.
- **IDCAMS / SORT control statements:** none (no utility steps in this job).
- **Abend semantics:** program self-abends (`CEE3ABD`, code 999) on VSAM
  open/read/close error; otherwise RC=0.
