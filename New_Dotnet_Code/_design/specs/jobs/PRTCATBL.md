# JOB SPEC: PRTCATBL

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/PRTCATBL.jcl`
Invoked PROC: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/proc/REPROC.prc`
Invoked control member: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/ctl/REPROCT.ctl`

## Overall Purpose

PRTCATBL ("Print Transaction Category Balance File") is a **report / extract pipeline** that
produces a human-readable, sorted, edited dump of the **Transaction Category Balance** master file.

It does three things in sequence:
1. Clean up any prior run's report output dataset (idempotent setup / delete).
2. Back up (unload) the live VSAM KSDS Transaction Category Balance file to a new GDG sequential
   generation via IDCAMS REPRO.
3. Sort that backup by account/type/category and reformat the balance with a decimal-edited
   picture, writing a fixed-length report (extract) dataset.

This is **not** a posting job and it does not update any master/relational data — it is read-only
against the live KSDS and produces a backup + a formatted report extract.

## JOB Card

- Job name: `PRTCATBL`
- Accounting / description: `'Print Trasaction Category Balance File'` (typo in source preserved)
- `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- `JCLLIB ORDER=('AWS.M2.CARDDEMO.PROC')` — resolves the `REPROC` PROC from the CardDemo PROC library.

## Logical / Relational Mapping

| Mainframe dataset | Kind | Maps to (.NET) |
|---|---|---|
| `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` | VSAM KSDS (live master) | Transaction Category Balance table / `TCATBAL` entity (50-byte record, key = account id + type cd + category cd) |
| `AWS.M2.CARDDEMO.TCATBALF.BKUP(+1)` | GDG sequential, FB/LRECL=50 | Backup unload of the TCATBAL master (one new generation per run) |
| `AWS.M2.CARDDEMO.TCATBALF.REPT` | Sequential, FB/LRECL=40 | Formatted report / extract output (sorted, edited balances) |

Record layout (from the SORT `SYMNAMES`, a 50-byte FB record):

| Field | Position (1-based) | Length | Type |
|---|---|---|---|
| `TRANCAT-ACCT-ID` | 1 | 11 | ZD (zoned decimal) |
| `TRANCAT-TYPE-CD` | 12 | 2 | CH (character) |
| `TRANCAT-CD` | 14 | 4 | ZD (zoned decimal) |
| `TRAN-CAT-BAL` | 18 | 11 | ZD (zoned decimal, signed balance) |

(Positions 29–50 of the 50-byte record are filler / unused by the sort.)

---

## Step 1 — `DELDEF` (EXEC PGM=IEFBR14)

- **Program / utility:** `IEFBR14` (do-nothing program; used purely for dataset disposition processing).
- **PARM:** none.
- **COND/RC gating:** none (always runs first; no dependency guard).
- **GDG:** none.
- **DD statements:**
  - `THEFILE` — `DISP=(MOD,DELETE)`, `UNIT=SYSDA`, `SPACE=(TRK,(1,1),RLSE)`,
    `DSN=AWS.M2.CARDDEMO.TCATBALF.REPT`.
    - Purpose: allocate-if-missing (MOD) then **delete at step end**, guaranteeing the report
      output dataset does not already exist before Step 3 re-creates it `NEW`. This is the standard
      "delete/define" idempotency pattern so reruns don't fail on a pre-existing dataset.
- **Reads:** none. **Writes/deletes:** `AWS.M2.CARDDEMO.TCATBALF.REPT` (report dataset).

---

## Step 2 — `STEP05R` (EXEC PROC=REPROC) → expands to `PRC001` (EXEC PGM=IDCAMS)

Invoked as `EXEC PROC=REPROC, CNTLLIB=AWS.M2.CARDDEMO.CNTL`. The PROC's single step `PRC001`
runs IDCAMS; DD overrides are supplied by the job as `PRC001.FILEIN` and `PRC001.FILEOUT`.

- **Program / utility:** `IDCAMS` (Access Method Services) — performs a `REPRO` (unload/copy).
- **PARM:** none (PROC passes only `CNTLLIB` symbolic to locate `SYSIN`).
- **COND/RC gating:** none explicitly coded.
- **GDG:** **Yes** — `FILEOUT` writes `AWS.M2.CARDDEMO.TCATBALF.BKUP(+1)`, i.e. creates a **new
  relative generation (+1)** of the `...TCATBALF.BKUP` GDG base.
- **DD statements (after job overrides):**
  - `SYSPRINT` — `SYSOUT=*` (IDCAMS messages).
  - `FILEIN` (override `PRC001.FILEIN`) — `DISP=SHR`,
    `DSN=AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS`. **Input:** live VSAM KSDS master (TCATBAL table).
  - `FILEOUT` (override `PRC001.FILEOUT`) — `DISP=(NEW,CATLG,DELETE)`, `UNIT=SYSDA`,
    `DCB=(LRECL=50,RECFM=FB,BLKSIZE=0)`, `SPACE=(CYL,(1,1),RLSE)`,
    `DSN=AWS.M2.CARDDEMO.TCATBALF.BKUP(+1)`. **Output:** new GDG generation, FB 50-byte sequential
    backup of the master.
  - `SYSIN` — `DISP=SHR, DSN=&CNTLLIB(REPROCT)` → resolves to `AWS.M2.CARDDEMO.CNTL(REPROCT)`.

- **IDCAMS control statements (member `REPROCT`):**

  ```
  REPRO INFILE(FILEIN) OUTFILE(FILEOUT)
  ```

  Plain unload: copy every record from the VSAM KSDS (`FILEIN`) to the sequential GDG generation
  (`FILEOUT`). No `DELETE`/`DEFINE` here — `REPROCT` is a generic reusable REPRO member.

---

## Step 3 — `STEP10R` (EXEC PGM=SORT)

- **Program / utility:** `SORT` (DFSORT / equivalent).
- **PARM:** none.
- **COND/RC gating:** none explicitly coded.
- **GDG:** **Yes (read)** — `SORTIN` reads `AWS.M2.CARDDEMO.TCATBALF.BKUP(+1)`, the **same +1
  generation just created in Step 2** (within one job, `(+1)` resolves to the same new generation).
- **DD statements:**
  - `SORTIN` — `DISP=SHR`, `DSN=AWS.M2.CARDDEMO.TCATBALF.BKUP(+1)`. Input = the backup just made.
  - `SYMNAMES` (inline `DD *`) — symbolic field names for the sort (see record-layout table above):
    ```
    TRANCAT-ACCT-ID,1,11,ZD
    TRANCAT-TYPE-CD,12,2,CH
    TRANCAT-CD,14,4,ZD
    TRAN-CAT-BAL,18,11,ZD
    ```
  - `SYSIN` (inline `DD *`) — sort/reformat control statements (see below).
  - `SYSOUT` — `SYSOUT=*` (sort messages).
  - `SORTOUT` — `DISP=(NEW,CATLG,DELETE)`, `UNIT=SYSDA`,
    `DCB=(LRECL=40,RECFM=FB,BLKSIZE=0)`, `SPACE=(CYL,(1,1),RLSE)`,
    `DSN=AWS.M2.CARDDEMO.TCATBALF.REPT`. Output = the formatted report extract (FB, 40 bytes),
    the same DSN that Step 1 pre-deleted.

- **Exact SORT control statements (`SYSIN`):**

  ```
  SORT FIELDS=(TRANCAT-ACCT-ID,A,TRANCAT-TYPE-CD,A,TRANCAT-CD,A)
  OUTREC FIELDS=(TRANCAT-ACCT-ID,X,
      TRANCAT-TYPE-CD,X,
      TRANCAT-CD,X,
      TRAN-CAT-BAL,EDIT=(TTTTTTTTT.TT),9X)
  ```

  - **SORT FIELDS:** ascending by `TRANCAT-ACCT-ID` (acct id), then `TRANCAT-TYPE-CD` (type code),
    then `TRANCAT-CD` (category code) — the composite key of the balance file, all ascending.
  - **OUTREC (reformat for the report record):**
    - `TRANCAT-ACCT-ID` (11) then `X` (1 blank separator)
    - `TRANCAT-TYPE-CD` (2) then `X`
    - `TRANCAT-CD` (4) then `X`
    - `TRAN-CAT-BAL` rendered with `EDIT=(TTTTTTTTT.TT)` — a decimal-edited mask producing 9 integer
      digit positions, a literal `.`, and 2 fractional digits (i.e. inserts the implied decimal
      point into the 11-digit zoned balance), followed by `9X` (9 trailing blanks).
  - Resulting record width = 11 + 1 + 2 + 1 + 4 + 1 + 12 + 9 = **41**... note `SORTOUT` LRECL is
    declared as **40**; the edited/blank layout is what the job ships (DFSORT pads/truncates to the
    output LRECL of 40). The intent is a fixed-format, decimal-pointed, sorted listing of every
    category balance.

---

## Step Dependency / Flow Summary

```
DELDEF (IEFBR14)        delete old REPT report dataset
   |
STEP05R (REPROC→IDCAMS) REPRO: VSAM KSDS master  ->  BKUP(+1)  (new GDG gen, FB 50)
   |
STEP10R (SORT)          BKUP(+1) -> sort by acct/type/cat, edit balance -> REPT (FB 40 report)
```

No COND/RC gating is coded between steps; steps run sequentially and each later step depends on the
prior step's output dataset existing (handled by normal abnormal-termination flushing on the
mainframe rather than explicit COND checks).
