# JOB SPEC: CLOSEFIL

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/CLOSEFIL.jcl`
Version tag: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## Overall Purpose

This is a **CICS file-control operations job**, NOT a batch data-processing job. It uses the z/OS spool
display/operations utility **SDSF** to issue an MVS **MODIFY (`/F`) console command** to the running CICS
region `CICSAWSA`, telling CICS (via the `CEMT SET FILE(...) CLOSED` master-terminal command) to **CLOSE**
the application's VSAM files so they are no longer open to the online CICS region.

The typical reason to close these files is to release the CICS enqueue/ownership of the underlying VSAM
clusters so that **batch jobs can read/update them exclusively** (batch posting, backups, IDCAMS
REPRO/DEFINE, etc.). After batch completes, the companion job **`OPENFIL.jcl`** re-opens (`CEMT SET
FILE(...) OPEN`) the same five files. CLOSEFIL/OPENFIL are a matched bracket around the batch window.

It does **not** itself read or write any application data, sequential files, GDGs, or relational tables. It
issues control commands only. The "datasets" referenced are CICS **FCT file names** (logical file handles),
each of which maps to an underlying VSAM cluster / logical table (see mapping table below).

### Relevance to the .NET JobControl step-runner

In the .NET target there are no CICS-owned VSAM enqueues to release: the online app and batch share the same
relational/SQLite data layer with normal transactional locking. So this job maps to an **operational
no-op / advisory step** (optionally a "quiesce online access to these tables" gate before a batch window,
paired with a re-enable step modeled on `OPENFIL`). No data movement, no posting, no report.

## JOB Card

| Attribute | Value |
|-----------|-------|
| JOB name | `CLOSEFIL` |
| Description | `'Close files in CICS'` |
| CLASS | `A` |
| MSGCLASS | `0` |
| NOTIFY | `&SYSUID` |

## Steps

| # | Step | PGM / Utility | Purpose |
|---|------|---------------|---------|
| 1 | `CLCIFIL` | `SDSF` | Issue MVS `/F CICSAWSA,'CEMT SET FILE(...) CLOSED'` MODIFY commands to close 5 CICS VSAM files |

### STEP1 — `CLCIFIL EXEC PGM=SDSF`

- **Program / utility:** `SDSF` (IBM Spool Display and Search Facility, run in batch). Used here as a vehicle
  to submit operator console commands via its `ISFIN` command input stream.
- **PARM:** none.
- **COND / RC gating:** none (no `COND=` on JOB or EXEC; single step).
- **GDG usage:** none.

#### DD / dataset statements

| DD | DSN / Target | DISP / Type | Role | Corresponds to |
|----|--------------|-------------|------|----------------|
| `ISFOUT` | `SYSOUT=*` | output | SDSF panel/output messages | spool/log |
| `CMDOUT` | `SYSOUT=*` | output | Issued-command responses/results | spool/log |
| `ISFIN` | inline (`DD *`) | input | SDSF command input stream — the `/F` MODIFY commands listed below | control-statement deck |

No application data DDs. No VSAM, sequential, GDG, or relational-table I/O is performed by this job itself.

## Exact Control Statements (ISFIN command stream)

These are MVS **MODIFY** (`/F`) console commands routed to CICS region **`CICSAWSA`**, each invoking the CICS
master-terminal transaction **`CEMT SET FILE(name) CLOSED`** (abbreviated `FIL`/`CLO`). They are not
IDCAMS or SORT statements; there are no `DEFINE`/`REPRO`/`DELETE` or `SORT FIELDS` here.

```
/F CICSAWSA,'CEMT SET FIL(TRANSACT) CLO'
/F CICSAWSA,'CEMT SET FIL(CCXREF)   CLO'
/F CICSAWSA,'CEMT SET FIL(ACCTDAT)  CLO'
/F CICSAWSA,'CEMT SET FIL(CXACAIX)  CLO'
/F CICSAWSA,'CEMT SET FIL(USRSEC)   CLO'
```

### CICS file → VSAM cluster → logical table mapping

| CICS FCT file | Action | Underlying VSAM cluster (DSN) | Logical table / file in .NET model |
|---------------|--------|-------------------------------|------------------------------------|
| `TRANSACT` | CLOSE | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` (KSDS) | Transaction file → `TRANSACTION` table |
| `CCXREF` | CLOSE | Card cross-reference KSDS (`AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS`) | Card/Account cross-reference → `CARD_XREF` table |
| `ACCTDAT` | CLOSE | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` (KSDS) | Account master → `ACCOUNT` table |
| `CXACAIX` | CLOSE | Card-xref **alternate index** over the card-xref cluster (account-ID AIX path) | Alternate-index/lookup over `CARD_XREF` (account-ID access path) |
| `USRSEC` | CLOSE | User-security KSDS (`AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS`) | User security / sign-on credentials → `USER_SECURITY` table |

Notes:
- `CXACAIX` is an **alternate index / path** (card cross-reference by account id), not a separate base
  cluster; closing it quiesces the alternate access path to the card-xref data.
- The same five files (in the same order) are re-opened by `OPENFIL.jcl` with `CEMT SET FIL(...) OPE`.

## Summary Notes for the .NET JobControl Step-Runner

- **1 EXEC step**, utility `SDSF` (not a CB* COBOL program, not IDCAMS/SORT/IEFBR14/DFHCSDUP).
- **No business data I/O**: no VSAM/sequential reads or writes, no relational-table updates, no GDGs, no
  PARM/COND gating, no DEFINE/REPRO/DELETE, no SORT FIELDS.
- The job's effect is purely **operational control of CICS**: close 5 application files (`TRANSACT`,
  `CCXREF`, `ACCTDAT`, `CXACAIX`, `USRSEC`) so a batch window can own them.
- Treat in the modernization as an **advisory "quiesce online access" gate**, paired with the `OPENFIL`
  re-enable step. It is not a runnable batch step that moves or posts data.
