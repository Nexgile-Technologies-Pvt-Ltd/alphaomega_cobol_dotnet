# JOB SPEC: DBPAUTP0

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/app-authorization-ims-db2-mq/jcl/DBPAUTP0.jcl`
Version tag in source: `CardDemo_v2.0-35-gcfa73b2-245` (2025-04-29)

## Overall Purpose

This is an **IMS database UNLOAD (backup/extract) job**. It performs an offline
HD (Hierarchical Direct) unload of the IMS HIDAM database `DBPAUTP0` — the
**Pending Authorization** database used by the CardDemo authorization (IMS/DB2/MQ)
application. The unload produces a flat sequential dataset that can later be used
to RELOAD the database or feed downstream extract/conversion jobs (see companion
jobs `UNLDPADB.JCL` / `LOADPADB.JCL` / `UNLDGSAM.JCL`).

This is **NOT** a relational/DB2 job. The data source is an **IMS DL/I hierarchical
database** backed by VSAM datasets. There is no SQL table; the "tables" below are
described as IMS segments and their backing VSAM clusters.

Step flow:
1. `STEPDEL` — IEFBR14 utility step: delete any pre-existing unload output dataset so the run starts clean.
2. `UNLOAD` — IMS region controller (DFSRRC00) runs the IMS HD Unload utility (DFSURGU0) against DBD `DBPAUTP0`, writing the unloaded segments to a new sequential dataset.

## JOB Card

```
//DBPAUTP0 JOB 'DBPAUTP0 DB UNLOAD',CLASS=A,MSGCLASS=X,
// REGION=0K,TIME=30,NOTIFY=&SYSUID
```

| Attribute | Value |
|-----------|-------|
| Job name | DBPAUTP0 |
| Description | "DBPAUTP0 DB UNLOAD" |
| CLASS | A |
| MSGCLASS | X |
| REGION | 0K (unlimited) |
| TIME | 30 (minutes) |
| NOTIFY | &SYSUID (submitting user) |

No job-level RESTART/TYPRUN. No GDG (generation data group) usage anywhere in this job — the output is a standard cataloged sequential dataset, not a GDG.

---

## STEP 1: STEPDEL — Delete Output Dataset (IEFBR14)

```
//STEPDEL  EXEC PGM=IEFBR14
```

- **Program/Utility:** `IEFBR14` — the IBM no-op utility. It does nothing itself; all action comes from DD-statement disposition processing at allocation/deallocation. Used here purely to delete a dataset via `DISP=(MOD,DELETE)`.
- **PARM:** none.
- **COND/RC gating:** none (runs unconditionally; it is the first step).

### DD statements

| DD | DSN | DISP | Role / Corresponds to |
|----|-----|------|------------------------|
| SYSPRINT | (SYSOUT=*) | — | Message/print output to spool. |
| SYSUT1 | `AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0` | `(MOD,DELETE)`, `UNIT=SYSDA`, `SPACE=(TRK,0)` | The **unload output sequential file** that STEP 2 will create. `MOD` allocates (creating it if absent), and `DELETE` (both normal + abnormal end) removes it. Effect: scratch any leftover unload from a prior run so STEP 2 can recreate it. |

Purpose of step: idempotent cleanup of the target unload dataset before the unload runs.

---

## STEP 2: UNLOAD — IMS HD Unload of DB `DBPAUTP0` (DFSRRC00 / DFSURGU0)

```
//UNLOAD   EXEC PGM=DFSRRC00,REGION=4M,
//         PARM=(ULU,DFSURGU0,DBPAUTP0)
```

- **Program/Utility invoked:**
  - `DFSRRC00` — the IMS region controller / batch DL/I bootstrap program. It is the PGM= on the EXEC; it loads and drives the actual utility named in PARM.
  - `DFSURGU0` — the **IMS HD Unload utility** (HISAM/HD reorganization unload). This is the real workhorse: it reads the IMS database and writes the unloaded segment stream.
- **REGION:** 4M.
- **PARM:** `(ULU,DFSURGU0,DBPAUTP0)`
  - `ULU` = utility region type "Utility, DL/I, Unload" (DL/I batch utility region, no DB2/online). It tells DFSRRC00 this is a DL/I utility run.
  - `DFSURGU0` = the utility program/PSB to execute (HD Unload).
  - `DBPAUTP0` = the DBD name of the database to unload (the Pending Authorization HIDAM DB).
- **COND/RC gating:** none coded. (Note: there is no explicit `COND` ensuring STEPDEL succeeded; STEP 2 simply (re)allocates the output with `DISP=(,CATLG,DELETE)`.)

### What is being unloaded — IMS DB `DBPAUTP0` (DBD: `ims/DBPAUTP0.dbd`)

- DBD `DBPAUTP0`: `ACCESS=(HIDAM,VSAM)` — HIDAM (Hierarchical Indexed Direct Access) database stored in VSAM.
- Segments (the "tables"):
  - `PAUTSUM0` — ROOT segment, "Pending Authorization Summary". 100 bytes. Sequence/key field `ACCNTID` (START=1, BYTES=6, packed-decimal `TYPE=P`) = the account ID. This is the per-account pending-authorization summary record.
  - `PAUTDTL1` — child of `PAUTSUM0`, "Pending Authorization Details". 200 bytes. Sequence field `PAUT9CTS` (START=1, BYTES=8, char) — individual pending-authorization detail records under each account.
- Primary-index companion DBD `DBPAUTX0` (`ims/DBPAUTX0.dbd`): `ACCESS=(INDEX,VSAM,PROT)` — the HIDAM primary index over `ACCNTID`; segment `PAUTINDX` (key `INDXSEQ`, 6-byte packed), `LCHILD` points back to `PAUTSUM0`.

### DD statements

| DD | DSN | DISP | Role / Corresponds to |
|----|-----|------|------------------------|
| STEPLIB | `OEMA.IMS.IMSP.SDFSRESL` + `AWS.M2.CARDDEMO.LOADLIB` | SHR | IMS RESLIB (utility/system load modules) concatenated with the CardDemo application load library. |
| DFSRESLB | `OEMA.IMS.IMSP.SDFSRESL` | SHR | Authorized IMS RESLIB used by DFSRRC00. |
| IMS | `OEM.IMS.IMSP.PSBLIB` + `OEM.IMS.IMSP.DBDLIB` | SHR | PSB library + DBD library concatenation — supplies the DBD definition (`DBPAUTP0`/`DBPAUTX0`) and PSB used by the unload. |
| SYSPRINT | (SYSOUT=*) | — | Utility messages/statistics to spool. |
| **DFSURGU1** | `AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0` | `(,CATLG,DELETE)`, `UNIT=SYSDA`, `SPACE=(32274,(600,100),RLSE)`, `DCB=(LRECL=27990,RECFM=VB,BLKSIZE=0)` | **OUTPUT** — the HD Unload sequential output file. This is the new dataset created and cataloged (deleted on abend). It is the unloaded image of the `DBPAUTP0` IMS database. RECFM=VB, LRECL=27990 (system-determined BLKSIZE). Same DSN deleted by STEP 1. |
| **DDPAUTP0** | `OEM.IMS.IMSP.PAUTHDB` | SHR | **INPUT** — the VSAM cluster backing IMS DBD `DBPAUTP0` (DSG `DDPAUTP0`). The actual Pending Authorization HIDAM data being read. |
| **DDPAUTX0** | `OEM.IMS.IMSP.PAUTHDBX` | SHR | **INPUT** — the VSAM cluster backing the primary-index DBD `DBPAUTX0` (DSG `DDPAUTX0`). HIDAM primary index over account id. |
| DFSVSAMP | `OEMPP.IMS.V15R01MB.PROCLIB(DFSVSMDB)` | SHR | IMS DL/I VSAM/OSAM buffer pool definitions (member DFSVSMDB) for the utility. |
| DFSCTL | (inline `*`) | — | DL/I control statements (see below). |
| SYSUDUMP | (SYSOUT=*) | — | Abend dump dataset; `DCB=(RECFM=FBA,LRECL=133)`, `SPACE=(605,(500,500),RLSE,,ROUND)`. |
| RECON1 | `OEM.IMS.IMSP.RECON1` | SHR | IMS DBRC RECON dataset (copy 1). |
| RECON2 | `OEM.IMS.IMSP.RECON2` | SHR | IMS DBRC RECON dataset (copy 2). |
| RECON3 | `OEM.IMS.IMSP.RECON3` | SHR | IMS DBRC RECON dataset (copy 3 / spare). |
| DFSWRK01 | DUMMY | — | Work dataset, dummied out (no sort work needed for this unload). |
| DFSSRT01 | DUMMY | — | Sort work dataset, dummied out. |

### Control statements

There is no IDCAMS or DFSORT/SORT step in this job, so there are **no DEFINE/REPRO/DELETE (IDCAMS) and no SORT FIELDS statements**.

The only inline control input is the **DFSCTL** stream, containing one IMS DL/I sequential-buffering parameter:

```
//DFSCTL   DD *
SBPARM ACTIV=COND
```

- `SBPARM ACTIV=COND` — enable IMS **Sequential Buffering** conditionally (activated only when the access pattern makes it beneficial). This is a performance tuning directive for the unload's sequential reads, not a data-definition statement.

---

## Summary Tables

### Programs / Utilities invoked

| Step | EXEC PGM= | Effective utility | Purpose |
|------|-----------|-------------------|---------|
| STEPDEL | IEFBR14 | (none — DISP-driven delete) | Scratch prior unload output dataset. |
| UNLOAD | DFSRRC00 | DFSURGU0 (via PARM ULU) | IMS HD Unload of DBD DBPAUTP0. |

### Datasets / DD by direction

| DSN | Type | Direction | Corresponds to |
|-----|------|-----------|----------------|
| `AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0` | Sequential (VB, LRECL 27990) | Deleted (STEP1) then Created/Output (STEP2) | IMS HD Unload flat file of the Pending Authorization DB. |
| `OEM.IMS.IMSP.PAUTHDB` | VSAM | Input | IMS HIDAM data DBD `DBPAUTP0` (segments PAUTSUM0 / PAUTDTL1). |
| `OEM.IMS.IMSP.PAUTHDBX` | VSAM | Input | IMS HIDAM primary index DBD `DBPAUTX0` (segment PAUTINDX). |
| `OEM.IMS.IMSP.RECON1/2/3` | VSAM | Input/Update | IMS DBRC recovery control datasets. |
| IMS/DBD/PSB libs, RESLIB, buffer proclib | PDS/load | Input (SHR) | IMS system + CardDemo load/control libraries. |

### Gating / Special features

- **COND/RC:** none coded between steps.
- **GDG:** none.
- **PARM gating:** STEP2 PARM `(ULU,DFSURGU0,DBPAUTP0)` selects utility type, utility program, and target DBD.
- **DBRC:** RECON1/2/3 present, so the unload runs under DBRC control.
- **No IDCAMS / SORT control statements** in this job.

## Notes for the .NET JobControl step-runner

- Model this as a 2-step job: (1) a delete/cleanup of an output artifact, (2) an "IMS unload" data-extract producing a single output stream/file.
- Step 2 is an IMS DL/I hierarchical unload, not a SQL/relational extract. In the .NET target, the equivalent is reading the migrated Pending Authorization store (root + detail hierarchy keyed by 6-byte packed account id) and emitting a flat export file analogous to `AWS.M2.CARDDEMO.IMSDATA.DBPAUTP0`.
- The output file is RECFM=VB / LRECL=27990 — variable-length records carrying serialized IMS segment images; downstream reload/extract jobs depend on that layout.
- Honor the cleanup-before-create semantics: delete the prior output file first, recreate it on the unload step, and delete-on-failure (do not leave a partial unload cataloged).
