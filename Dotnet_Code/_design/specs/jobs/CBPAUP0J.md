# JOB SPEC: CBPAUP0J

Source: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/jcl/CBPAUP0J.jcl`
Application: CardDemo — Authorization Module (IMS / DB2 / MQ variant)

## Overall Purpose

This is an **IMS BMP (Batch Message Processing) database-maintenance / housekeeping job**. It runs a single
batch IMS program (`CBPAUP0C`) under the IMS region controller `DFSRRC00` to **purge expired pending
authorization records** from the IMS Pending-Authorization hierarchical database (`DBPAUTP0`).

It is NOT a file-setup, posting, sort, report, or backup job. It does **not** use IDCAMS, SORT, IEFBR14,
sequential/QSAM files, VSAM clusters as application data, GDGs, or any COND/RC gating. Its only application
input is a one-line inline `SYSIN` control card that supplies run parameters (expiry days, checkpoint
frequencies, debug flag). All "data" I/O is performed via DL/I calls against the IMS database, not via DD
datasets — the only application dataset touched is the underlying IMS database (defined by the IMS DBD, not
by DD cards in this JCL).

The program walks the database root-by-root (Pending Authorization Summary segments) and, for each, walks
its child detail segments (Pending Authorization Details); any detail older than the expiry threshold is
deleted, the parent summary's running approved/declined counts and amounts are decremented, and when a
summary has no remaining approved/declined authorizations it is itself deleted. Work is committed
periodically via IMS `CHKP` (checkpoint) calls for restartability.

In the .NET target this maps to a **scheduled "expire/purge pending authorizations" batch step** operating
on whatever relational/document store replaces the IMS `DBPAUTP0` hierarchy (a parent
`PendingAuthSummary` table/collection and a child `PendingAuthDetail` table/collection keyed by account,
with a date/time key). There are no flat-file or VSAM DD mappings to carry over; the JobControl step-runner
just needs the run parameters from the `SYSIN` card and the delete/decrement logic.

## JOB Card

| Attribute | Value |
|-----------|-------|
| JOB name | `CBPAUP0J` |
| Accounting / comment | `'CARDDEMO'` |
| CLASS | `A` |
| MSGCLASS | `H` |
| MSGLEVEL | `(1,1)` |
| REGION | `0M` (no region limit) |
| NOTIFY | `&SYSUID` |

No JCL `SET` symbols, no `COND` on the JOB card.

## Steps

| # | Step | PGM / Utility | Invoked program | Purpose |
|---|------|---------------|-----------------|---------|
| 1 | `STEP01` | `DFSRRC00` (IMS region controller) | `CBPAUP0C` (COBOL IMS BMP) via PSB `PSBPAUTB` | Delete expired pending authorization detail/summary segments from IMS DB `DBPAUTP0` |

This job has exactly **one EXEC step**.

### STEP01 — `EXEC PGM=DFSRRC00`

- **Program / utility:** `DFSRRC00` — the IMS region controller (batch/BMP bootstrap). It is the EXEC `PGM=`;
  the actual application program is named in the `PARM`.
- **PARM:** `'BMP,CBPAUP0C,PSBPAUTB'`
  - `BMP` — region type = Batch Message Processing (runs against online IMS databases, can take checkpoints,
    participates in IMS logging/recovery).
  - `CBPAUP0C` — the application program to run (the COBOL IMS batch program;
    `Old_Cobol_Code/.../cbl/CBPAUP0C.cbl`).
  - `PSBPAUTB` — the PSB (Program Specification Block) that defines the program's DB views
    (`Old_Cobol_Code/.../ims/PSBPAUTB.psb`).
- **COND / RC gating:** none (single step; no `COND=` on JOB or EXEC). The program itself sets `RETURN-CODE`
  16 and GOBACKs on any DL/I error (its `9999-ABEND` path).
- **GDG usage:** none.
- **Note:** `XXXXXXXX` in `STEPLIB` DSN `XXXXXXXX.PROD.LOADLIB` is a site high-level-qualifier placeholder to
  be customized at install; it is the load library containing the `CBPAUP0C` load module.

#### DD / dataset statements

None of these are application business-data files — they are IMS/region infrastructure DDs plus the inline
parameter card. The application data lives in the IMS database `DBPAUTP0`, accessed only through DL/I (no
DD for it in this JCL).

| DD | DSN / Target | DISP / Type | Role | Corresponds to |
|----|--------------|-------------|------|----------------|
| `STEPLIB` | `IMS.SDFSRESL` | `DISP=SHR` | IMS SVC/region load library (contains `DFSRRC00`) | IMS runtime libs (infra) |
| `STEPLIB` (concat) | `XXXXXXXX.PROD.LOADLIB` | `DISP=SHR` | Application load library containing `CBPAUP0C` | App load module (infra) |
| `DFSRESLB` | `IMS.SDFSRESL` | `DISP=SHR` | IMS resident load library (DL/I modules) | IMS runtime libs (infra) |
| `PROCLIB` | `IMS.PROCLIB` | `DISP=SHR` | IMS procedure library (DFSVSMxx etc.) | IMS config (infra) |
| `DFSSEL` | `IMS.SDFSRESL` | `DISP=SHR` | IMS load library (select/modstat) | IMS runtime libs (infra) |
| `IMS` | `IMS.PSBLIB` + `IMS.DBDLIB` (concat) | `DISP=SHR` | PSB and DBD libraries — resolves `PSBPAUTB` and DBD `DBPAUTP0` | IMS DB/program definitions (metadata) |
| `SYSIN` | inline `DD *` | input | **Application run-parameter card** (see below) | control-statement deck (not data) |
| `SYSOUX` | `SYSOUT=*` | output | IMS/program message output | spool/log |
| `SYSOUT` | `SYSOUT=*` | output | Program `DISPLAY` output / messages | spool/log |
| `SYSABOUT` | `SYSOUT=*` | output | Abend trace output | spool/log |
| `ABENDAID` | `SYSOUT=*` | output | Abend-Aid diagnostic output | spool/log |
| `IEFRDER` | `DUMMY` | n/a | IMS log DD (dummied — no separate log dataset for this run) | infra (none) |
| `IMSLOGR` | `DUMMY` | n/a | IMS log read DD (dummied) | infra (none) |
| `SYSPRINT` | `SYSOUT=*` | output | System print | spool/log |
| `SYSUDUMP` | `SYSOUT=*` | output | System dump on abend | spool/log |
| `IMSERR` | `SYSOUT=*` | output | IMS error messages | spool/log |

There are **no IDCAMS, SORT, or IEFBR14 steps**, so there are no `DEFINE`/`REPRO`/`DELETE`/`SORT FIELDS`
control statements anywhere in this job.

## Inline SYSIN Control Card (run parameters)

```
00,00001,00001,Y
```

This card is read by `CBPAUP0C` via `ACCEPT PRM-INFO FROM SYSIN` and parsed positionally (`01 PRM-INFO`):

| Field | PIC | Value here | Meaning |
|-------|-----|-----------|---------|
| `P-EXPIRY-DAYS` | `9(02)` | `00` | Age threshold in days. `00` is non-zero-meaningful but numeric → used as-is (= 0 days, i.e. effectively expire everything dated on/before today; if non-numeric the program defaults to `5`). |
| (delimiter) | `X(01)` | `,` | comma separator (FILLER) |
| `P-CHKP-FREQ` | `X(05)` | `00001` | Checkpoint frequency — take an IMS `CHKP` after this many summary roots processed (defaults to `5` if blank/zero/low-values). |
| (delimiter) | `X(01)` | `,` | comma separator (FILLER) |
| `P-CHKP-DIS-FREQ` | `X(05)` | `00001` | Checkpoint-display frequency — emit a "CHKP SUCCESS" progress line every this-many checkpoints (defaults to `10` if blank/zero/low-values). |
| (delimiter) | `X(01)` | `,` | comma separator (FILLER) |
| `P-DEBUG-FLAG` | `X(01)` | `Y` | Debug flag — `Y` turns on per-call `DEBUG:` `DISPLAY` tracing (`N` otherwise). |

There is also a `SYSIN` literal `00,00001,00001,Y` only — a single record. The earlier `SYSIN DD *` in the
JCL listing (`00,00001,00001,Y`) is this same card.

## IMS Database / Segment Mapping (the "tables/files" this job touches)

The job has no DD-level dataset for application data; all access is through DL/I against the IMS database
defined by the program's PSB/DBD. For the .NET migration, these are the relational/store equivalents.

- **PSB `PSBPAUTB`** (`ims/PSBPAUTB.psb`): one DB PCB `PAUTBPCB`, `DBDNAME=DBPAUTP0`, `PROCOPT=AP`
  (get/insert/replace/delete + path), `KEYLEN=14`. Sensitive to segments `PAUTSUM0` (root) and `PAUTDTL1`
  (child of `PAUTSUM0`). The program references this PCB as `PCB(PAUT-PCB-NUM)` with `PAUT-PCB-NUM = +2`
  (the I/O-PCB is the implicit first PCB; the DB PCB is the 2nd).
- **DBD `DBPAUTP0`** (`ims/DBPAUTP0.dbd`): `ACCESS=(HIDAM,VSAM)` hierarchical DB, dataset DD `DDPAUTP0`,
  with a secondary index DB `DBPAUTX0` (LCHILD `PAUTINDX`) — none of which appear as DDs in this JCL because
  the BMP attaches to the online IMS databases.

| IMS segment | Role | Key | Copybook | .NET / relational equivalent |
|-------------|------|-----|----------|------------------------------|
| `PAUTSUM0` (`PENDING-AUTH-SUMMARY`) | Root | `ACCNTID` SEQ,U — START=1 BYTES=6 TYPE=P (packed `PA-ACCT-ID PIC S9(11) COMP-3`); BYTES=100 | `CIPAUSMY.cpy` | `PendingAuthSummary` table/entity keyed by account id; holds running approved/declined auth counts and amounts (`PA-APPROVED-AUTH-CNT/AMT`, `PA-DECLINED-AUTH-CNT/AMT`), credit/cash limits & balances. |
| `PAUTDTL1` (`PENDING-AUTH-DETAILS`) | Child of `PAUTSUM0` | `PAUT9CTS` SEQ,U — START=1 BYTES=8 TYPE=C (the 8-byte `PA-AUTHORIZATION-KEY` = `PA-AUTH-DATE-9C` + `PA-AUTH-TIME-9C`, both COMP-3); BYTES=200 | `CIPAUDTY.cpy` | `PendingAuthDetail` table/entity, child of the summary, keyed by (account id, auth date/time); one row per pending authorization with response code, amounts, card/merchant data. |

## Program Processing Logic (CBPAUP0C) — for the step-runner

DL/I call sequence (`EXEC DLI`), main driver in `MAIN-PARA`:

1. **1000-INITIALIZE** — `ACCEPT CURRENT-DATE FROM DATE`, `ACCEPT CURRENT-YYDDD FROM DAY`, `ACCEPT PRM-INFO
   FROM SYSIN`; apply parameter defaults (expiry days → 5 if non-numeric; chkp freq → 5; chkp-display freq
   → 10; debug → N).
2. **2000-FIND-NEXT-AUTH-SUMMARY** — `GN SEGMENT(PAUTSUM0)` (get-next root). `DIBSTAT`: `'  '` ok, `'GB'`
   end-of-DB, otherwise abend.
3. For each summary, loop **3000-FIND-NEXT-AUTH-DTL** — `GNP SEGMENT(PAUTDTL1)` (get-next-within-parent).
   `'  '` more details, `'GE'`/`'GB'` no more, otherwise abend.
4. **4000-CHECK-IF-EXPIRED** — compute auth date as `99999 - PA-AUTH-DATE-9C` (stored as 9's-complement
   Julian `YYDDD`), `WS-DAY-DIFF = CURRENT-YYDDD - WS-AUTH-DATE`; if `WS-DAY-DIFF >= expiry-days` mark for
   delete and decrement the parent summary's approved or declined count/amount (approved when
   `PA-AUTH-RESP-CODE = '00'`, else declined).
5. **5000-DELETE-AUTH-DTL** — `DLET SEGMENT(PAUTDTL1)` for qualifying detail.
6. **6000-DELETE-AUTH-SUMMARY** — after all details, if the summary's approved-auth count `<= 0`, `DLET
   SEGMENT(PAUTSUM0)` (delete the now-empty root).
7. **9000-TAKE-CHECKPOINT** — `CHKP ID('RMAD'+counter)` every `P-CHKP-FREQ` summaries (and a final
   checkpoint at end) for restart/commit; progress line every `P-CHKP-DIS-FREQ` checkpoints.
8. On completion, `DISPLAY` totals: summaries read/deleted, details read/deleted; `GOBACK`.

Error handling: any unexpected `DIBSTAT` → `9999-ABEND` → `MOVE 16 TO RETURN-CODE`, `GOBACK`.

## Summary Notes for the .NET JobControl Step-Runner

- **1 EXEC step**; `PGM=DFSRRC00` is the IMS region controller, and the real program is `CBPAUP0C`
  (a `CB*`-style COBOL IMS BMP), scheduled with PSB `PSBPAUTB`.
- **No IDCAMS / SORT / IEFBR14**, so no DEFINE/REPRO/DELETE or SORT FIELDS control statements.
- **No VSAM/sequential DD application data, no GDGs, no COND/RC gating.** All other DDs are IMS/region
  infrastructure or SYSOUT spool.
- **Single application input** is the inline `SYSIN` card `00,00001,00001,Y` (expiry-days, chkp-freq,
  chkp-display-freq, debug-flag) — carry these as the step's run parameters.
- **Function:** purge/expire pending authorizations — delete child `PAUTDTL1` (PendingAuthDetail) rows older
  than the expiry threshold, decrement parent `PAUTSUM0` (PendingAuthSummary) running totals, and delete the
  summary when it has no remaining approved authorizations. Periodic IMS checkpoints provide commit/restart
  granularity (in .NET: batched transaction commits).
- **Migration target:** a "expire pending authorizations" maintenance batch step over the relational/store
  replacements for the `DBPAUTP0` hierarchy (`PendingAuthSummary` parent + `PendingAuthDetail` child).
