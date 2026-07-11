# JOB SPEC: OPENFIL

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/OPENFIL.jcl`
Version tag: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## Overall Purpose

This is a **CICS file-control operations job**, NOT a batch data-processing job. It uses the z/OS spool
display/operations utility **SDSF** to issue an MVS **MODIFY (`/F`) console command** to the running CICS
region `CICSAWSA`, telling CICS (via the `CEMT SET FILE(...) OPEN` master-terminal command) to **OPEN**
the application's VSAM files so they become available again to the online CICS region.

It is the matched counterpart of **`CLOSEFIL.jcl`**. The typical sequence is: `CLOSEFIL` closes (quiesces)
the VSAM files so a **batch window can own them exclusively** (batch posting, backups, IDCAMS
REPRO/DEFINE, etc.), and after batch completes `OPENFIL` **re-opens** (`CEMT SET FILE(...) OPEN`) the same
five files in the same order, restoring online access. CLOSEFIL/OPENFIL are a matched bracket around the
batch window.

It does **not** itself read or write any application data, sequential files, GDGs, or relational tables. It
issues control commands only. The "datasets" referenced are CICS **FCT file names** (logical file handles),
each of which maps to an underlying VSAM cluster / logical table (see mapping table below).

### Relevance to the .NET JobControl step-runner

In the .NET target there are no CICS-owned VSAM enqueues to acquire/release: the online app and batch share
the same relational/SQLite data layer with normal transactional locking. So this job maps to an
**operational no-op / advisory step** (optionally a "re-enable online access to these tables" gate that ends
a batch window, paired with the `CLOSEFIL`-modeled "quiesce" step that begins it). No data movement, no
posting, no report.

## JOB Card

| Attribute | Value |
|-----------|-------|
| JOB name | `OPENFIL` |
| Description | `'Open files in CICS'` |
| CLASS | `A` |
| MSGCLASS | `0` |
| NOTIFY | `&SYSUID` |

## Steps

| # | Step | PGM / Utility | Purpose |
|---|------|---------------|---------|
| 1 | `OPCIFIL` | `SDSF` | Issue MVS `/F CICSAWSA,'CEMT SET FIL(...) OPE'` MODIFY commands to open 5 CICS VSAM files |

### STEP1 — `OPCIFIL EXEC PGM=SDSF`

- **Program / utility:** `SDSF` (IBM Spool Display and Search Facility, run in batch). Used here as a vehicle
  to submit operator console commands via its `ISFIN` command input stream. It is **not** a CB* COBOL
  program, and not IDCAMS / SORT / IEFBR14 / DFHCSDUP.
- **PARM:** none.
- **COND / RC gating:** none (no `COND=` on the JOB card or on the EXEC; single step, so no inter-step gating).
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
master-terminal transaction **`CEMT SET FILE(name) OPEN`** (abbreviated `FIL`/`OPE`). They are not
IDCAMS or SORT statements; there are no `DEFINE`/`REPRO`/`DELETE` or `SORT FIELDS` here. Reproduced exactly
as coded in the member (note the trailing blank inside each `FIL(...)` and the `OPE` open keyword):

```
/F CICSAWSA,'CEMT SET FIL(TRANSACT ) OPE'
/F CICSAWSA,'CEMT SET FIL(CCXREF ) OPE'
/F CICSAWSA,'CEMT SET FIL(ACCTDAT ) OPE'
/F CICSAWSA,'CEMT SET FIL(CXACAIX ) OPE'
/F CICSAWSA,'CEMT SET FIL(USRSEC ) OPE'
```

### CICS file → VSAM cluster → logical table mapping

| CICS FCT file | Action | Underlying VSAM cluster (DSN) | Logical table / file in .NET model |
|---------------|--------|-------------------------------|------------------------------------|
| `TRANSACT` | OPEN | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` (KSDS) | Transaction file → `TRANSACTION` table |
| `CCXREF` | OPEN | Card cross-reference KSDS (`AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS`) | Card/Account cross-reference → `CARD_XREF` table |
| `ACCTDAT` | OPEN | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` (KSDS) | Account master → `ACCOUNT` table |
| `CXACAIX` | OPEN | Card-xref **alternate index** over the card-xref cluster (account-ID AIX path) | Alternate-index/lookup over `CARD_XREF` (account-ID access path) |
| `USRSEC` | OPEN | User-security KSDS (`AWS.M2.CARDDEMO.USRSEC.VSAM.KSDS`) | User security / sign-on credentials → `USER_SECURITY` table |

Notes:
- `CXACAIX` is an **alternate index / path** (card cross-reference by account id), not a separate base
  cluster; opening it re-enables the alternate access path to the card-xref data.
- The same five files (in the same order) are closed by `CLOSEFIL.jcl` with `CEMT SET FIL(...) CLO`.

## Summary Notes for the .NET JobControl Step-Runner

- **1 EXEC step**, utility `SDSF` (not a CB* COBOL program, not IDCAMS/SORT/IEFBR14/DFHCSDUP).
- **No business data I/O**: no VSAM/sequential reads or writes, no relational-table updates, no GDGs, no
  PARM/COND gating, no DEFINE/REPRO/DELETE, no SORT FIELDS.
- The job's effect is purely **operational control of CICS**: open (re-enable) 5 application files
  (`TRANSACT`, `CCXREF`, `ACCTDAT`, `CXACAIX`, `USRSEC`) so the online region can use them after a batch
  window.
- Treat in the modernization as an **advisory "re-enable online access" gate** that ends a batch window,
  paired with the `CLOSEFIL`-modeled "quiesce" step that begins it. It is not a runnable batch step that
  moves or posts data.
