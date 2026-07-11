# JOB SPEC: CBADMCDJ

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/CBADMCDJ.jcl`
Version tag: `CardDemo_v1.0-70-g193b394-123` (2022-08-22)

## Overall Purpose

This is a **CICS resource-definition / installation job**, NOT a batch data-processing job. It runs the
CICS offline utility **DFHCSDUP** against the CICS System Definition (CSD) file to **DEFINE the CardDemo
CICS resource group `CARDDEMO`** — the load LIBRARY, BMS MAPSETs (screens), PROGRAMs, and TRANSACTIONs that
make up the online (CICS) side of the CardDemo application.

It does **not** read or write any application data, VSAM clusters, sequential files, GDGs, or relational
tables. There is no posting sequence, sort, backup, report, or IDCAMS file setup here. Its single output is
updates to the CICS CSD metadata catalog so the online transactions/programs/maps can be installed
(via `CEDA INSTALL GROUP(CARDDEMO)`) and run under CICS.

In the .NET target, there is no direct runtime equivalent: CICS resource definitions correspond to the
application's online program/transaction/screen registration (routing/menu wiring + UI mapset → screen
model mappings). This job is informational for the JobControl step-runner: it defines metadata, so for a
.NET migration it maps to **configuration/registration of online endpoints**, not a runnable batch step that
moves business data.

## JOB Card

| Attribute | Value |
|-----------|-------|
| JOB name | `CBADMCDJ` |
| Accounting | `(COBOL),'AWSCODR'` |
| CLASS | `A` |
| MSGCLASS | `H` |
| MSGLEVEL | `(1,1)` |
| NOTIFY | `&SYSUID` |
| TIME | `1440` (effectively unlimited) |

### JCL SET symbol
- `SET HLQ=AWS.M2.CARDDEMO` — high-level qualifier substituted into the DEFINE LIBRARY DSNAME (`&HLQ..LOADLIB` → `AWS.M2.CARDDEMO.LOADLIB`).

## Steps

| # | Step | PGM / Utility | Purpose |
|---|------|---------------|---------|
| 1 | `STEP1` | `DFHCSDUP` | CICS offline CSD utility — DEFINE the `CARDDEMO` resource group (library, mapsets, programs, transactions) into the CSD |

### STEP1 — `EXEC PGM=DFHCSDUP`

- **Program / utility:** `DFHCSDUP` (IBM CICS System Definition offline batch utility).
- **REGION:** `0M` (no region limit).
- **PARM:** `'CSD(READWRITE),PAGESIZE(60),NOCOMPAT'`
  - `CSD(READWRITE)` — open the CSD for update (definitions will be written).
  - `PAGESIZE(60)` — listing page size.
  - `NOCOMPAT` — run in non-compatibility (modern) mode.
- **COND / RC gating:** none (no COND= on JOB or EXEC; this is the only step).
- **GDG usage:** none.

#### DD / dataset statements

| DD | DSN / Target | DISP / Type | Role | Corresponds to |
|----|--------------|-------------|------|----------------|
| `STEPLIB` | `OEM.CICSTS.V05R06M0.CICS.SDFHLOAD` | `DISP=SHR` | CICS TS 5.6 load library containing `DFHCSDUP` | CICS runtime libs (not app data) |
| `DFHCSD` | `OEM.CICSTS.DFHCSD` | `UNIT=SYSDA,DISP=SHR` | **The CSD file being updated** (input/output) | CICS System Definition catalog (metadata, not a relational table or app sequential file) |
| `OUTDD` | `SYSOUT=*` | output | DFHCSDUP messages/listing | spool/log |
| `SYSPRINT` | `SYSOUT=*` | output | Utility print output | spool/log |
| `SYSIN` | inline (`DD *,SYMBOLS=JCLONLY`) | input | DFHCSDUP control statements; `SYMBOLS=JCLONLY` lets JCL symbol `&HLQ` be substituted in the in-stream control cards | control-statement deck |

No application reads/writes. The only updated dataset is the CSD (`DFHCSD`).

## Exact DFHCSDUP Control Statements (SYSIN)

These are CICS resource definition commands (analogous in spirit to IDCAMS DEFINE, but for CICS resources,
not VSAM). `&HLQ` resolves to `AWS.M2.CARDDEMO`.

### Group / housekeeping
- `* DELETE GROUP(CARDDEMO)` — commented out; to be uncommented on rerun to wipe and redefine the group.
- `LIST GROUP(CARDDEMO)` — at the end, list the resulting group contents.

### LIBRARY
```
DEFINE LIBRARY(COM2DOLL) GROUP(CARDDEMO)
       DSNAME01(AWS.M2.CARDDEMO.LOADLIB)
```
Defines the application load library (DSNAME from `&HLQ..LOADLIB`) as a CICS LIBRARY resource.

### TDQUEUEs (commented out, not defined)
```
* DEFINE TDQUEUE(CSSD) GROUP(CARDDEMO) TYPE(INTRA)
* DEFINE TDQUEUE(IRDC) GROUP(CARDDEMO) TYPE(INTRA)
```

### MAPSETs (BMS screens) — DEFINE MAPSET(...) GROUP(CARDDEMO)
| MAPSET | Description |
|--------|-------------|
| `COSGN00M` | LOGIN SCREEN (defined twice — duplicate) |
| `COACT00S` | ACCOUNT MENU (also listed again as CARD MENU — duplicate name) |
| `COACTVWS` | VIEW ACCOUNT / VIEW CARD (duplicate name) |
| `COACTUPS` | UPDATE ACCOUNT / UPDATE CARD (duplicate name) |
| `COACTDES` | DEACTIVATE ACCOUNT / DEACTIVATE CARD (duplicate name) |
| `COTRN00S` | TRANSACTION |
| `COTRNVWS` | TRANSACTION REPORT |
| `COTRNVDS` | TRANSACTION DETAILS |
| `COTRNATS` | ADD TRANSACTIONS |
| `COBIL00S` | BILL PAY SETUP |
| `COADM00S` | ADMIN MENU |
| `COTSTP1S` | PGM1 TEST |
| `COTSTP2S` | PGM2 TEST |
| `COTSTP3S` | PGM3 TEST |
| `COTSTP4S` | PGM4 TEST |

Note: the deck contains duplicate MAPSET DEFINEs (`COSGN00M` twice; `COACT00S`/`COACTVWS`/`COACTUPS`/`COACTDES`
each defined once for "account" and again for "card"). DFHCSDUP would warn/replace on duplicates.

### PROGRAMs — DEFINE PROGRAM(...) GROUP(CARDDEMO) DA(ANY)
| PROGRAM | Description | TRANSID |
|---------|-------------|---------|
| `COSGN00C` | LOGIN | `CC00` |
| `COACT00C` | ACCOUNT MAIN MENU / CARD MENU (defined twice) | — |
| `COACTVWC` | VIEW ACCOUNT / VIEW CARD (twice) | — |
| `COACTUPC` | UPDATE ACCOUNT / UPDATE CARD (twice) | — |
| `COACTDEC` | DEACTIVATE ACCOUNT / DEACTIVATE CARD (twice) | — |
| `COTRN00C` | TRANSACTION | — |
| `COTRNVWC` | TRANSACTION REPORT | — |
| `COTRNVDC` | TRANSACTION DETAILS | — |
| `COTRNATC` | ADD TRANSACTIONS | — |
| `COBIL00C` | BILL PAY | — |
| `COADM00C` | ADMIN MENU | `CCAD` |
| `COTSTP1C` | PGM1 TEST | `CCT1` |
| `COTSTP2C` | PGM2 TEST | `CCT2` |
| `COTSTP3C` | PGM1 TEST (description typo; PGM3) | `CCT3` |
| `COTSTP4C` | PGM4 TEST | `CCT4` |

`DA(ANY)` = DATALOCATION(ANY). Programs defined twice (account vs card variants) are duplicates that
DFHCSDUP replaces.

### TRANSACTIONs — DEFINE TRANSACTION(...) GROUP(CARDDEMO) ... TASKDATAL(ANY)
| TRANSACTION | PROGRAM |
|-------------|---------|
| `CCDM` | `COADM00C` |
| `CCT1` | `COTSTP1C` |
| `CCT2` | `COTSTP2C` |
| `CCT3` | `COTSTP3C` |
| `CCT4` | `COTSTP4C` |

(Some TRANSIDs such as `CC00`, `CCAD` are declared inline on the PROGRAM DEFINEs rather than as standalone
TRANSACTION resources.)

## Summary Notes for the .NET JobControl Step-Runner

- **1 EXEC step**, utility `DFHCSDUP` (not a CB* COBOL program, not IDCAMS/SORT/IEFBR14).
- **No business data I/O**: no VSAM/sequential files, no relational tables, no GDGs, no PARM/COND gating
  beyond the single utility step.
- This job is **CICS online-resource registration** (library + mapsets + programs + transactions for group
  `CARDDEMO`). For the modernization, treat it as **online application registration/configuration metadata**,
  not a runnable batch step that moves or posts data.
- Idempotent rerun is enabled by uncommenting `DELETE GROUP(CARDDEMO)` before the DEFINEs.
