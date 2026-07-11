# JOB SPEC: DALYREJS

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/DALYREJS.jcl`
Version stamp: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## Overall Purpose

**Setup / GDG provisioning job.** This is a one-time (or as-needed) setup job that
**defines the Generation Data Group (GDG) base** used to hold the *daily rejects*
output. Despite the inline comment referring to a "transaction master VSAM file",
the job does **not** delete or build any VSAM data set — it only runs a single
IDCAMS `DEFINE GENERATIONDATAGROUP` to register the GDG base
`AWS.M2.CARDDEMO.DALYREJS`. After this base exists, other CardDemo posting/processing
jobs (e.g. the daily transaction posting job) can write rejected-transaction
generations as `AWS.M2.CARDDEMO.DALYREJS(+1)`.

The comment block in the JCL ("DELETE TRANSACTION MASTER VSAM FILE IF ONE ALREADY
EXISTS") is a copy/paste artifact and does **not** match the actual step content;
the real action is GDG base creation only.

## JOB Card

| Attribute | Value |
|-----------|-------|
| Job name | `DALYREJS` |
| Description | `DEF GDG FOR REJS` |
| CLASS | `A` |
| MSGCLASS | `0` |
| NOTIFY | `&SYSUID` |

## Steps (in order)

### STEP05 — `EXEC PGM=IDCAMS`

| Item | Value |
|------|-------|
| Program / utility | **IDCAMS** (Access Method Services utility) |
| PARM | none |
| COND / RC gating | none (single step; no conditional execution) |

#### DD statements

| DD name | Disposition / target | Meaning |
|---------|----------------------|---------|
| `SYSPRINT` | `SYSOUT=*` | IDCAMS message / listing output (spool) |
| `SYSIN`    | instream (`DD *`) | IDCAMS control statements (see below) |

No application input/output data sets are read or written by this step. There is
**no relational table or sequential file** consumed here — the step only manipulates
catalog metadata (it creates a GDG base entry in the ICF catalog).

#### IDCAMS control statements (exact)

```
DEFINE GENERATIONDATAGROUP -
(NAME(AWS.M2.CARDDEMO.DALYREJS) -
 LIMIT(5) -
 SCRATCH -
)
```

Statement-by-statement:

- **`DEFINE GENERATIONDATAGROUP`** — creates a new GDG base (no REPRO, no DELETE,
  no PRINT in this job).
- **`NAME(AWS.M2.CARDDEMO.DALYREJS)`** — the GDG base name. This is the *daily
  rejects* GDG. Individual generations written later are referenced as
  `AWS.M2.CARDDEMO.DALYREJS(+1)` (new), `(0)` (current), `(-1)` (prior), etc.
- **`LIMIT(5)`** — retains at most 5 generations; the 6th rolls the oldest off.
- **`SCRATCH`** — when a generation rolls off the limit, its data set is
  **physically deleted (uncataloged and scratched)**, not merely uncataloged.

There is **no SORT step**, no `REPRO`, no `DELETE`, and no other program in this job.

## GDG Usage Summary

| GDG base | Defined here | LIMIT | Roll-off behavior | Corresponds to |
|----------|-------------|-------|-------------------|----------------|
| `AWS.M2.CARDDEMO.DALYREJS` | Yes (this job) | 5 | SCRATCH (physical delete) | Daily rejected-transactions output file (sequential generations) |

## Programs / Utilities Invoked

- `IDCAMS` (STEP05)

## .NET JobControl Notes

- This maps to a **GDG-base registration** action in the step runner: ensure a
  managed "generation group" named `AWS.M2.CARDDEMO.DALYREJS` exists, configured
  with a maximum of 5 retained generations and a "delete on roll-off" (scratch)
  policy.
- No file I/O, no DB table access, no record processing — purely a catalog/setup
  operation. Idempotency note: on the mainframe a second `DEFINE` of an existing
  GDG fails (RC≠0); the .NET equivalent should treat "already exists" as success
  or guard with an existence check.
