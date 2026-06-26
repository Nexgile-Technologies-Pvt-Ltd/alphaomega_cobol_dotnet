# Job Spec: DEFGDGD

## Overview

- **Job name:** `DEFGDGD`
- **Source JCL:** `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/DEFGDGD.jcl`
- **JOB card:** `//DEFGDGD JOB 'DEF DB2 GDG',CLASS=A,MSGCLASS=0,NOTIFY=&SYSUID`
- **Restart hint (commented out):** `//* RESTART=STEP30` — operators may restart from STEP30 if the earlier reference-data backups already exist.
- **Purpose (file setup / GDG bootstrap):** Defines three Generation Data Groups (GDGs) for CardDemo **transaction reference data** and loads the **first generation (+1)** of each GDG from an existing flat (PS) file. This is a one-time setup / initial-load job that establishes the backup GDG bases for three reference tables (Transaction Type, Transaction Category Type, Disclosure Group) and seeds them with a copy of the current reference data.
- **Step count:** 6 (STEP10–STEP60).
- **Programs/utilities invoked:** `IDCAMS` (×3), `IEBGENER` (×3).

Despite the job title "DEF DB2 GDG", no DB2 access occurs in this member. The GDGs back the **reference data** that elsewhere in CardDemo populates the relational/VSAM reference stores:
- `TRANTYPE` → Transaction Type reference table (`CardDemo.TransactionType`).
- `TRANCATG` → Transaction Category Type reference table (`CardDemo.TransactionCategory` / transaction category balance reference).
- `DISCGRP` → Disclosure Group reference table (`CardDemo.DisclosureGroup`).

The GDG generations themselves are sequential backup datasets (not relational tables); they hold point-in-time copies of the corresponding reference data.

---

## STEP10 — Define GDG base for Transaction Type

- **EXEC:** `PGM=IDCAMS`
- **COND/RC gating:** none (always runs).
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — IDCAMS message/listing output.
  - `SYSIN DD *` — inline control statements (below).
- **GDG usage:** Defines the GDG **base** (no data written yet).
- **IDCAMS control statements (exact):**
  ```
  DEFINE GENERATIONDATAGROUP -
  (NAME(AWS.M2.CARDDEMO.TRANTYPE.BKUP) -
   LIMIT(5) -
   SCRATCH -
  )
  ```
  - `NAME` = `AWS.M2.CARDDEMO.TRANTYPE.BKUP` (GDG base for Transaction Type backups).
  - `LIMIT(5)` = retain at most 5 generations.
  - `SCRATCH` = when a generation rolls off, physically scratch (delete) it.
- **Relational/file mapping:** GDG base for the **Transaction Type** reference table (`CardDemo.TransactionType`). No row/record I/O in this step.

---

## STEP20 — Load first generation of Transaction Type GDG

- **EXEC:** `PGM=IEBGENER`
- **COND/RC gating:** `COND=(0,NE)` — skip this step if **any** prior step ended with RC not equal to 0 (i.e., run only if all previous steps returned RC=0). Effectively gates on STEP10 succeeding.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*` — utility messages.
  - `SYSIN DD DUMMY` — no IEBGENER editing control statements; straight copy (input → output unchanged).
  - `SYSUT1 DD DISP=SHR,DSN=AWS.M2.CARDDEMO.TRANTYPE.PS` — **input** (read): existing Transaction Type flat file (PS).
  - `SYSUT2 DD DSN=AWS.M2.CARDDEMO.TRANTYPE.BKUP(+1),DISP=(NEW,CATLG)` — **output** (write): new generation `(+1)` of the GDG.
    - `DCB=(LRECL=60,RECFM=FB,BLKSIZE=600)`
    - `SPACE=(TRK,(1,1),RLSE)`
- **GDG usage:** Creates relative generation `(+1)` → first absolute generation `G0001V00`.
- **Mapping:**
  - Reads `AWS.M2.CARDDEMO.TRANTYPE.PS` (Transaction Type sequential reference file → `CardDemo.TransactionType` table source; LRECL 60).
  - Writes the Transaction Type backup GDG generation defined in STEP10.

---

## STEP30 — Define GDG base for Transaction Category Type

- **EXEC:** `PGM=IDCAMS`
- **COND/RC gating:** `COND=(0,NE)` — run only if all prior steps returned RC=0. (Also the documented restart point: `RESTART=STEP30`.)
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`
  - `SYSIN DD *` — inline control statements (below).
- **GDG usage:** Defines GDG **base** (no data written).
- **IDCAMS control statements (exact):**
  ```
  DEFINE GENERATIONDATAGROUP -
  (NAME(AWS.M2.CARDDEMO.TRANCATG.PS.BKUP) -
   LIMIT(5) -
   SCRATCH -
  )
  ```
  - `NAME` = `AWS.M2.CARDDEMO.TRANCATG.PS.BKUP`.
  - `LIMIT(5)`, `SCRATCH` as above.
- **Relational/file mapping:** GDG base for the **Transaction Category Type** reference table (`CardDemo.TransactionCategory`).

---

## STEP40 — Load first generation of Transaction Category Type GDG

- **EXEC:** `PGM=IEBGENER`
- **COND/RC gating:** `COND=(0,NE)` — run only if all prior steps returned RC=0.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`
  - `SYSIN DD DUMMY` — straight copy, no edits.
  - `SYSUT1 DD DISP=SHR,DSN=AWS.M2.CARDDEMO.TRANCATG.PS` — **input** (read): Transaction Category Type flat file (PS).
  - `SYSUT2 DD DSN=AWS.M2.CARDDEMO.TRANCATG.PS.BKUP(+1),DISP=(NEW,CATLG)` — **output** (write): new generation `(+1)`.
    - `DCB=(LRECL=60,RECFM=FB,BLKSIZE=600)`
    - `SPACE=(TRK,(1,1),RLSE)`
- **GDG usage:** Creates relative generation `(+1)` (first absolute generation).
- **Mapping:**
  - Reads `AWS.M2.CARDDEMO.TRANCATG.PS` (Transaction Category sequential reference file → `CardDemo.TransactionCategory`; LRECL 60).
  - Writes the Transaction Category backup GDG generation defined in STEP30.

---

## STEP50 — Define GDG base for Disclosure Group

- **EXEC:** `PGM=IDCAMS`
- **COND/RC gating:** none (always runs; note: unlike STEP30, this DEFINE has no `COND`).
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`
  - `SYSIN DD *` — inline control statements (below).
- **GDG usage:** Defines GDG **base** (no data written).
- **IDCAMS control statements (exact):**
  ```
  DEFINE GENERATIONDATAGROUP -
  (NAME(AWS.M2.CARDDEMO.DISCGRP.BKUP) -
   LIMIT(5) -
   SCRATCH -
  )
  ```
  - `NAME` = `AWS.M2.CARDDEMO.DISCGRP.BKUP`.
  - `LIMIT(5)`, `SCRATCH` as above.
- **Relational/file mapping:** GDG base for the **Disclosure Group** reference table (`CardDemo.DisclosureGroup`).

---

## STEP60 — Load first generation of Disclosure Group GDG

- **EXEC:** `PGM=IEBGENER`
- **COND/RC gating:** `COND=(0,NE)` — run only if all prior steps returned RC=0.
- **DD statements:**
  - `SYSPRINT DD SYSOUT=*`
  - `SYSIN DD DUMMY` — straight copy, no edits.
  - `SYSUT1 DD DISP=SHR,DSN=AWS.M2.CARDDEMO.DISCGRP.PS` — **input** (read): Disclosure Group flat file (PS).
  - `SYSUT2 DD DSN=AWS.M2.CARDDEMO.DISCGRP.BKUP(+1),DISP=(NEW,CATLG)` — **output** (write): new generation `(+1)`.
    - `DCB=(LRECL=50,RECFM=FB,BLKSIZE=500)` — note shorter LRECL (50) than the other two reference files.
    - `SPACE=(TRK,(1,1),RLSE)`
- **GDG usage:** Creates relative generation `(+1)` (first absolute generation).
- **Mapping:**
  - Reads `AWS.M2.CARDDEMO.DISCGRP.PS` (Disclosure Group sequential reference file → `CardDemo.DisclosureGroup`; LRECL 50).
  - Writes the Disclosure Group backup GDG generation defined in STEP50.

---

## Dataset / Mapping Summary

| Step  | Pgm      | Reads (SYSUT1 / input)              | Writes (SYSUT2 / output)                     | LRECL | Reference table / file                  |
|-------|----------|-------------------------------------|----------------------------------------------|-------|------------------------------------------|
| STEP10| IDCAMS   | — (DEFINE GDG)                      | GDG base `AWS.M2.CARDDEMO.TRANTYPE.BKUP`     | —     | Transaction Type (`CardDemo.TransactionType`) |
| STEP20| IEBGENER | `AWS.M2.CARDDEMO.TRANTYPE.PS`        | `...TRANTYPE.BKUP(+1)`                        | 60 FB | Transaction Type backup gen             |
| STEP30| IDCAMS   | — (DEFINE GDG)                      | GDG base `AWS.M2.CARDDEMO.TRANCATG.PS.BKUP` | —     | Transaction Category (`CardDemo.TransactionCategory`) |
| STEP40| IEBGENER | `AWS.M2.CARDDEMO.TRANCATG.PS`        | `...TRANCATG.PS.BKUP(+1)`                     | 60 FB | Transaction Category backup gen         |
| STEP50| IDCAMS   | — (DEFINE GDG)                      | GDG base `AWS.M2.CARDDEMO.DISCGRP.BKUP`      | —     | Disclosure Group (`CardDemo.DisclosureGroup`) |
| STEP60| IEBGENER | `AWS.M2.CARDDEMO.DISCGRP.PS`         | `...DISCGRP.BKUP(+1)`                         | 50 FB | Disclosure Group backup gen             |

## Notes for the .NET JobControl step-runner

- **No SORT step** in this job; no SORT FIELDS control statements.
- **IDCAMS action used:** only `DEFINE GENERATIONDATAGROUP` (no `REPRO`, no `DELETE` here). The .NET runner must register a GDG base concept: name, `LIMIT(5)`, and `SCRATCH` (auto-delete oldest beyond the limit).
- **IEBGENER** here is an unconditional sequential copy (`SYSIN DD DUMMY` = no edit cards). Map to: copy source PS file → newest GDG generation `(+1)`, with the given DCB (LRECL/RECFM/BLKSIZE).
- **GDG generation semantics:** `(+1)` allocates a new generation, cataloged on success (`DISP=(NEW,CATLG)`); on rollover the runner should enforce the 5-generation limit and scratch the oldest.
- **COND=(0,NE) gating chain:** STEP20, STEP30, STEP40, STEP60 each abort if any preceding step's RC ≠ 0. STEP10 and STEP50 are ungated (always attempt the DEFINE). The .NET runner should evaluate this as "run unless any prior step returned non-zero".
- **Idempotency caveat:** re-running the whole job will fail the DEFINE steps if the GDG bases already exist; use the `RESTART=STEP30` style restart, or make DEFINE tolerant of "already defined" when porting.
- The three `*.PS` inputs are pre-existing reference flat files that must exist before this job runs (produced by the data-load/refresh process for CardDemo reference data).
