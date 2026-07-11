# Job Spec: REPTFILE

## Overview

- **Job name:** `REPTFILE`
- **Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/REPTFILE.jcl`
- **JOB card:** `//REPTFILE JOB 'DEF GDG FOR REPORT FILE',CLASS=A,MSGCLASS=0,NOTIFY=&SYSUID`
  - Job title: `DEF GDG FOR REPORT FILE`.
  - `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID` (notify submitting user on completion).
- **Purpose (file setup / GDG bootstrap):** A one-time setup job that **defines a single Generation Data Group (GDG) base** for the CardDemo **transaction report** file. It allocates the GDG *base entry only* — it does **not** create any generation, load data, copy, or run a report. The actual report generations are produced later by the transaction-report process (e.g., the `CBTRN03C` transaction-report batch program / `TRANREPT.jcl`), each new run writing a new `(+1)` generation under this base.
- **Step count:** 1 (`STEP05`).
- **Programs/utilities invoked:** `IDCAMS` (×1).
- **SORT usage:** none. **No SORT step, no SORT FIELDS.**
- **COND/RC gating:** none. **No GDG generation roll** in this job (base define only — generations are created by downstream report jobs).

The GDG base defined here backs the **Transaction Report** sequential output:
- `AWS.M2.CARDDEMO.TRANREPT` → CardDemo **transaction report** dataset family (a sequential print/report file, not a relational table). Each generation is a point-in-time transaction report produced by the report batch job.

---

## STEP05 — Define GDG base for the Transaction Report file

- **EXEC:** `PGM=IDCAMS`
- **COND/RC gating:** none (always runs — this is the only step).
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output (spooled to SYSOUT).
  - `SYSIN DD *` — inline control statements (below).
- **PARM:** none.
- **GDG usage:** Defines the GDG **base** only. No generation is allocated or written in this step (no `(+1)` here).
- **IDCAMS control statements (exact):**
  ```
  DEFINE GENERATIONDATAGROUP -
  (NAME(AWS.M2.CARDDEMO.TRANREPT) -
   LIMIT(10) -
  )
  ```
  - **Action:** `DEFINE GENERATIONDATAGROUP` (only IDCAMS action in the job — no `REPRO`, no `DELETE`).
  - `NAME` = `AWS.M2.CARDDEMO.TRANREPT` — the GDG base name for transaction report generations.
  - `LIMIT(10)` = retain at most **10** generations in the group.
  - **No `SCRATCH`/`NOSCRATCH` keyword** is coded → IDCAMS default `NOSCRATCH` applies: when a generation rolls off beyond the limit, the catalog entry is removed (uncataloged) but the dataset is **not** physically scratched. (Contrast with the `*.BKUP` GDGs in `DEFGDGB`/`DEFGDGD`, which specify `SCRATCH` and `LIMIT(5)`.)
- **Relational / file mapping:**
  - This step performs **no row/record I/O**. It only creates the GDG base (a catalog structure).
  - Backing object: `AWS.M2.CARDDEMO.TRANREPT` — the **transaction report** file family. This corresponds to a **sequential report (print) file**, *not* a relational table. The report content is derived from CardDemo transaction data (Transaction master + reference tables such as Transaction Type / Transaction Category) by the downstream report program; this job only prepares the GDG container that those report runs write into.

---

## Dataset / Mapping Summary

| Step   | Pgm    | Reads | Writes (allocates) | Action | Relational table / file |
|--------|--------|-------|---------------------|--------|--------------------------|
| STEP05 | IDCAMS | —     | GDG base `AWS.M2.CARDDEMO.TRANREPT` (`LIMIT(10)`, default NOSCRATCH) | `DEFINE GENERATIONDATAGROUP` | Transaction Report sequential print file (no relational table) |

---

## Notes for the .NET JobControl step-runner

- **Single-step, idempotency caveat:** Re-running this job will **fail the DEFINE** if the GDG base `AWS.M2.CARDDEMO.TRANREPT` already exists (IDCAMS returns a non-zero RC, typically RC=12). When porting, either (a) make the GDG-base registration tolerant of "already defined," or (b) precede it with a `DELETE ... GENERATIONDATAGROUP` if a clean re-create is intended. The original JCL does neither.
- **GDG base model the runner must support:** register a named generation group with `name = AWS.M2.CARDDEMO.TRANREPT`, `limit = 10`, and rollover policy `NOSCRATCH` (default — keep the dataset, drop only the catalog entry when it rolls off). This differs from the backup GDGs which use `SCRATCH` + `LIMIT(5)`.
- **No generation written here:** unlike `DEFGDGB`/`DEFGDGD` (which DEFINE *and* IEBGENER-load a `(+1)` first generation), `REPTFILE` defines the base **only**. The first and subsequent generations come from the transaction-report job. The .NET runner should treat this as a pure "create GDG container" operation with no file copy.
- **No SORT, no REPRO, no DELETE, no COND gating, no PARM** in this member — the only behavior to implement is the single `DEFINE GENERATIONDATAGROUP`.
- **Downstream dependency:** Whatever .NET artifact emits the transaction report must target the newest generation `(+1)` of `AWS.M2.CARDDEMO.TRANREPT` and honor the 10-generation retention so consumers can reference prior reports (relative generations `(0)`, `(-1)`, …).
- **Versioning comment in source:** `Ver: CardDemo_v1.0-15-g27d6c6f-68 Date: 2022-07-19 23:23:07 CDT` (informational only).
