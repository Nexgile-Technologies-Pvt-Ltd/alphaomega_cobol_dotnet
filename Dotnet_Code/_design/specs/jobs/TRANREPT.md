# JOB SPEC: TRANREPT

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/TRANREPT.jcl`
Invoked PROC: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/proc/REPROC.prc`
Report program: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/cbl/CBTRN03C.cbl`
Version stamp: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## Overview

- **JCL member**: `TRANREPT.jcl`
- **JOB name**: `TRANREPT`
- **JOB description**: `'TRANSACTION REPORT'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **JCLLIB**: `JOBLIB JCLLIB ORDER=('AWS.M2.CARDDEMO.PROC')` — PROC search library so `EXEC PROC=REPROC` resolves to `REPROC.prc`.
- **Purpose**: **End-to-end transaction reporting pipeline.** A three-step batch job that (1) **unloads/backs up** the transaction master VSAM KSDS to a new sequential GDG generation via IDCAMS REPRO, (2) **filters that backup by processing-date window and sorts it by card number** via DFSORT, then (3) runs the COBOL report program **`CBTRN03C`** to **produce a formatted, paginated transaction detail report** with per-card / page / grand totals, decoding each transaction's type and category from reference KSDS files.
- **Pipeline role**: This is a **reporting (read/extract) job**, not a posting job. It does not update any master. It snapshots the transaction master, narrows it to a date range, sorts it, and prints. The date window comes both from the SORT SYMNAMES (hard-coded `2022-01-01`..`2022-07-06`) for the filter, and from the `DATEPARM` dataset read by `CBTRN03C` for the report header/range check.

## Step summary

| Step | Step name | PGM / Utility | Type | Action |
|------|-----------|---------------|------|--------|
| 1 | `STEP05R` (PROC `REPROC`, inner step `PRC001`) | `IDCAMS` | z/OS utility (REPRO) | Unload (copy) transaction master KSDS `TRANSACT.VSAM.KSDS` to new sequential backup GDG `TRANSACT.BKUP(+1)` |
| 2 | `STEP05R` (duplicate name) | `SORT` (DFSORT) | z/OS utility | Read the backup `TRANSACT.BKUP(+1)`, INCLUDE only records whose proc-date is within the parm window, SORT ascending by card number, write `TRANSACT.DALY(+1)` |
| 3 | `STEP10R` | `CBTRN03C` | Custom CardDemo batch COBOL (`CB*` program) | Read the filtered/sorted daily file plus 3 reference KSDS files and the DATEPARM control file; write the formatted transaction report `TRANREPT(+1)` |

There are **3 EXEC steps** invoking **3 distinct programs/utilities: `IDCAMS` (via PROC REPROC), `SORT`, and `CBTRN03C`**. **GDG `(+1)` is used for 3 datasets** (`TRANSACT.BKUP`, `TRANSACT.DALY`, `TRANREPT`). **No `COND=`/`IF` gating** is coded. **`PARM=` is not coded on any EXEC**; the date "parm" is supplied as SYMNAMES literals to SORT and as the `DATEPARM` dataset to `CBTRN03C`.

> **JCL anomaly to preserve in the runner**: Steps 1 and 2 are **both named `STEP05R`** in the source JCL (a duplicate step name; line 23 = the PROC invocation, line 37 = the SORT). z/OS tolerates this but it is unusual. Treat them as two ordered, distinct steps in the .NET step-runner (e.g. `STEP05R.REPROC` then `STEP05R.SORT`), preserving execution order PROC→SORT→`STEP10R`.

---

## STEP 1 — `STEP05R` EXEC PROC=REPROC → inner step `PRC001` (PGM=IDCAMS): unload transaction master

- **EXEC**: `EXEC PROC=REPROC, CNTLLIB=AWS.M2.CARDDEMO.CNTL` — invokes the cataloged procedure `REPROC` (`app/proc/REPROC.prc`). Symbolic `CNTLLIB` is passed so the PROC's `SYSIN` resolves to `&CNTLLIB(REPROCT)` = `AWS.M2.CARDDEMO.CNTL(REPROCT)`.
- **Inner step**: `PRC001 EXEC PGM=IDCAMS` — generic REPRO load/unload utility step.
- **PARM=**: none.
- **COND/RC gating**: none.
- **DD overrides** (supplied by the job on the PROC step, override the PROC's `DSN=NULLFILE` placeholders):
  - `PRC001.FILEIN`  → input  = the transaction master KSDS.
  - `PRC001.FILEOUT` → output = the new backup GDG generation.
- **Control statements**: the PROC's `SYSIN` is `DISP=SHR, DSN=&CNTLLIB(REPROCT)` = member `AWS.M2.CARDDEMO.CNTL(REPROCT)`. That member is the standard CardDemo REPRO deck, i.e. effectively:
  ```
  REPRO INFILE(FILEIN) OUTFILE(FILEOUT)
  ```
  (REPRO copies every record from `FILEIN` to `FILEOUT`. No DEFINE/DELETE here — pure unload of the KSDS to a flat sequential backup. The `REPROCT` CNTL member is not present in this repo snapshot; its content is the conventional `REPRO INFILE(FILEIN) OUTFILE(FILEOUT)` used by the REPROC PROC across CardDemo jobs.)

### DD statements / datasets (effective, after override)

| DD | Disposition | DSN | I/O | GDG | DCB / record | Maps to (file / relational table) |
|----|-------------|-----|-----|-----|--------------|-----------------------------------|
| `PRC001.SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | IDCAMS messages / job log. No data mapping. |
| `PRC001.SYSIN` | `DISP=SHR` | `AWS.M2.CARDDEMO.CNTL(REPROCT)` | INPUT | n/a | control deck | IDCAMS REPRO control statements (see above). |
| `PRC001.FILEIN` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | INPUT | n/a (fixed VSAM KSDS) | `CVTRA05Y` `TRAN-RECORD`, key `TRAN-ID` X(16), 350 bytes | **Transaction master** KSDS = relational **`TRANSACTION`** table. Read-only source of the unload. |
| `PRC001.FILEOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.TRANSACT.BKUP(+1)` | OUTPUT | **GDG `(+1)` = new generation** | `LRECL=350, RECFM=FB, BLKSIZE=0`; `UNIT=SYSDA`; `SPACE=(CYL,(1,1),RLSE)` | **Sequential backup/unload** of the transaction master (flat copy). Logically a point-in-time **TRANSACTION snapshot** sequential file. New GDG generation each run. |

---

## STEP 2 — `STEP05R` EXEC PGM=SORT: date-filter + sort by card number

- **EXEC**: `EXEC PGM=SORT` — DFSORT.
- **PARM=**: none.
- **COND/RC gating**: none.
- **Purpose**: filter the just-created backup to the parm date window and order it by card number so the report (step 3) can do per-card control-break totals.

### SORT control statements (exact)

`SYMNAMES DD *` (symbol/field definitions used by the SORT control deck):
```
TRAN-CARD-NUM,263,16,ZD
TRAN-PROC-DT,305,10,CH
PARM-START-DATE,C'2022-01-01'
PARM-END-DATE,C'2022-07-06'
```
- `TRAN-CARD-NUM` = field at **position 263, length 16, format ZD** (zoned-decimal) — the card number.
- `TRAN-PROC-DT`  = field at **position 305, length 10, format CH** (character) — the processing date (`yyyy-mm-dd`).
- `PARM-START-DATE` = character constant `'2022-01-01'` (inclusive lower bound).
- `PARM-END-DATE`   = character constant `'2022-07-06'` (inclusive upper bound).

`SYSIN DD *` (the actual SORT control cards):
```
 SORT FIELDS=(TRAN-CARD-NUM,A)
 INCLUDE COND=(TRAN-PROC-DT,GE,PARM-START-DATE,AND,
         TRAN-PROC-DT,LE,PARM-END-DATE)
```
- **SORT FIELDS=(TRAN-CARD-NUM,A)** — ascending sort on the 16-byte card number at offset 263.
- **INCLUDE COND** — keep only records where `2022-01-01 <= TRAN-PROC-DT <= 2022-07-06` (date window filter on the proc-date at offset 305).

### DD statements / datasets

| DD | Disposition | DSN | I/O | GDG | DCB / record | Maps to (file / relational table) |
|----|-------------|-----|-----|-----|--------------|-----------------------------------|
| `SORTIN` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.BKUP(+1)` | INPUT | **GDG `(+1)`** — the generation **created in Step 1** (same `(+1)` resolves within the job to the just-allocated generation) | `LRECL=350, RECFM=FB` (from Step 1) | Backup/unload of transaction master (the Step-1 output). |
| `SYMNAMES` | inline `DD *` | (instream) | INPUT | n/a | symbol defs | SORT field/constant symbol table (see above). |
| `SYSIN` | inline `DD *` | (instream) | INPUT | n/a | control deck | SORT/INCLUDE control statements (see above). |
| `SYSOUT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | DFSORT messages / job log. No data mapping. |
| `SORTOUT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.TRANSACT.DALY(+1)` | OUTPUT | **GDG `(+1)` = new generation** | `DCB=(*.SORTIN)` (inherit `LRECL=350, RECFM=FB` from SORTIN); `UNIT=SYSDA`; `SPACE=(CYL,(1,1),RLSE)` | **Filtered + card-sorted "daily" transaction file** = the report input. Sequential PS; a date-windowed, card-ordered **TRANSACTION** extract. New GDG generation each run. |

---

## STEP 3 — `STEP10R` EXEC PGM=CBTRN03C: produce formatted transaction report

- **EXEC**: `EXEC PGM=CBTRN03C` — CardDemo COBOL batch program "Print the transaction detail report" (`app/cbl/CBTRN03C.cbl`).
- **PARM=**: none.
- **COND/RC gating**: none coded. Program path: opens all files; reads the `DATEPARM` record to obtain the report start/end dates (displayed as the report range); streams the (already filtered+sorted) transaction file; for each in-range record does card-number control breaks and lookups; on file errors it sets `IO-STATUS`/`APPL-RESULT` and abends via `ABEND-PROGRAM` (CEE3ABD). In the .NET runner, treat an I/O abend as a hard step failure; normal completion is RC=0.
- **STEPLIB**: `DISP=SHR, DSN=AWS.M2.CARDDEMO.LOADLIB` — load library for the `CBTRN03C` executable (maps to .NET program registry; no data mapping).

### Processing logic (from CBTRN03C, for the step runner)

1. **Open** all six files; **read** the single `DATEPARM` record → `WS-START-DATE` (X(10)), filler, `WS-END-DATE` (X(10)); display `Reporting from <start> to <end>`.
2. **Loop** reading `TRANFILE` sequentially (`1000-TRANFILE-GET-NEXT`). For each record:
   - **Date gate**: only process records where `TRAN-PROC-TS(1:10)` is between `WS-START-DATE` and `WS-END-DATE` (redundant with the SORT INCLUDE, but uses the `DATEPARM` window).
   - **Card control break**: when `TRAN-CARD-NUM` changes, write the previous card's **account totals** (`1120-WRITE-ACCOUNT-TOTALS`), then look up the new card in **CARDXREF** (`1500-A-LOOKUP-XREF`, keyed read).
   - **Type lookup**: `1500-B-LOOKUP-TRANTYPE` reads **TRANTYPE** by `TRAN-TYPE-CD` for the type description.
   - **Category lookup**: build key (`TRAN-TYPE-CD` + `TRAN-CAT-CD`) and `1500-C-LOOKUP-TRANCATG` reads **TRANCATG** for the category description.
   - **Write detail line** (`1100-WRITE-TRANSACTION-REPORT`) into `TRANREPT`; maintain `WS-PAGE-TOTAL`, `WS-ACCOUNT-TOTAL`, `WS-GRAND-TOTAL`; paginate every `WS-PAGE-SIZE`=20 lines, writing page-total lines.
3. At EOF: write final page total and **grand total** (`1110-WRITE-PAGE-TOTALS` / `1110-WRITE-GRAND-TOTALS`); **close** all files. Output is a 133-byte print report.

### DD statements / datasets

| DD | Disposition | DSN | I/O (COBOL OPEN) | GDG | Copybook / record | Maps to (file / relational table) |
|----|-------------|-----|------------------|-----|-------------------|-----------------------------------|
| `STEPLIB` | `DISP=SHR` | `AWS.M2.CARDDEMO.LOADLIB` | (load lib) | n/a | n/a | Program load library. No data mapping. |
| `SYSOUT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | COBOL `DISPLAY` / runtime messages. No data mapping. |
| `SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | Print / job log. No data mapping. |
| `TRANFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.DALY(+1)` | **INPUT** (sequential) | **GDG `(+1)`** — the Step-2 SORTOUT generation | `CVTRA05Y` `TRAN-RECORD` (FD `FD-TRANFILE-REC`, 304+26+20=350 bytes) | **Filtered + card-sorted transaction extract** (Step-2 output) = date-windowed **`TRANSACTION`** rows. The report's main driving input. |
| `CARDXREF` | `DISP=SHR` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | **INPUT** (random, keyed) | n/a (fixed VSAM KSDS) | `CVACT03Y`; FD key `FD-XREF-CARD-NUM` X(16), data X(34) | **Card cross-reference** KSDS = relational **`CARD_XREF`** table (card → account/customer). Read-only lookup at each card control break. |
| `TRANTYPE` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANTYPE.VSAM.KSDS` | **INPUT** (random, keyed) | n/a (fixed VSAM KSDS) | `CVTRA03Y`; FD key `FD-TRAN-TYPE` X(2), data X(58) | **Transaction type** reference KSDS = relational **`TRANSACTION_TYPE`** table. Read-only lookup for type description. |
| `TRANCATG` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANCATG.VSAM.KSDS` | **INPUT** (random, keyed) | n/a (fixed VSAM KSDS) | `CVTRA04Y`; FD key = `FD-TRAN-TYPE-CD` X(2) + `FD-TRAN-CAT-CD` 9(4), data X(54) | **Transaction category** reference KSDS = relational **`TRANSACTION_CATEGORY`** table. Read-only lookup for category description. |
| `DATEPARM` | `DISP=SHR` | `AWS.M2.CARDDEMO.DATEPARM` | **INPUT** (sequential) | n/a (fixed PS) | `FD-DATEPARM-REC` X(80); parsed as `WS-START-DATE` X(10) + filler X(1) + `WS-END-DATE` X(10) | **Report date-parameter control file** (sequential 80-byte). Supplies the report start/end dates. Logically a small **REPORT-PARAMETERS** control record. |
| `TRANREPT` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.TRANREPT(+1)` | **OUTPUT** (sequential) | **GDG `(+1)` = new generation** | `LRECL=133, RECFM=FB, BLKSIZE=0`; FD `FD-REPTFILE-REC` X(133); `UNIT=SYSDA`; `SPACE=(CYL,(1,1),RLSE)` | **Formatted transaction detail report** (133-byte print lines, paginated with page/account/grand totals). Output report sequential file. New GDG generation each run. |

---

## PARM / COND / GDG / SORT / IDCAMS summary

- **PARM=**: none on any EXEC. The "date parm" is delivered two ways: SORT `SYMNAMES` constants `PARM-START-DATE`/`PARM-END-DATE` (Step 2 filter) and the `DATEPARM` dataset read by `CBTRN03C` (Step 3 report range). Both are intended to carry the same window (`2022-01-01`..`2022-07-06`).
- **COND/RC gating**: **none** (`COND=`/`IF`/`THEN` not used). Steps run unconditionally in order; in the runner, abort the pipeline if an earlier step fails (e.g. IDCAMS RC>0 or SORT RC>0) since each step consumes the prior step's `(+1)` output.
- **GDG**: **used for three datasets**, each `(+1)` = a **new generation catalogued on success / deleted on abend**:
  - Step 1 creates `AWS.M2.CARDDEMO.TRANSACT.BKUP(+1)`.
  - Step 2 reads that same `(+1)` as `SORTIN` and creates `AWS.M2.CARDDEMO.TRANSACT.DALY(+1)`.
  - Step 3 reads `TRANSACT.DALY(+1)` and creates `AWS.M2.CARDDEMO.TRANREPT(+1)`.
  Within a single job, a relative `(+1)` reference resolves to the same generation allocated earlier in the job; the .NET runner must resolve `(+1)` once per GDG base per job and reuse that resolution for later steps in the same run. GDG bases (`TRANSACT.BKUP`, etc.) are defined by separate `DEFGDG*` jobs.
- **SORT**: **used (Step 2, DFSORT)** — `SORT FIELDS=(TRAN-CARD-NUM,A)` with an `INCLUDE COND` date-window filter; field/constant symbols via `SYMNAMES` (card num pos 263 len 16 ZD; proc-date pos 305 len 10 CH; date constants).
- **IDCAMS**: **used (Step 1, via PROC REPROC)** — `REPRO INFILE(FILEIN) OUTFILE(FILEOUT)` from `CNTL(REPROCT)`; no DEFINE/DELETE (pure KSDS→sequential unload).
- **IEFBR14**: not used.

## Programs / Utilities Invoked

- `IDCAMS` (Step 1, inner step `PRC001` of PROC `REPROC`) — REPRO unload of `TRANSACT.VSAM.KSDS` to `TRANSACT.BKUP(+1)`.
- `SORT` (Step 2, DFSORT) — date-filter + sort-by-card of the backup into `TRANSACT.DALY(+1)`.
- `CBTRN03C` (Step 3) — custom CardDemo COBOL transaction-detail report program → `TRANREPT(+1)`.

## Conversion notes for the .NET JobControl step-runner

- Implement as **three ordered steps**; disambiguate the two `STEP05R` names internally (PROC/IDCAMS step, then SORT step), then `STEP10R`.
- **Step 1 (REPRO)**: copy all rows of the `TRANSACTION` store to a new `TRANSACT.BKUP` GDG generation (flat 350-byte FB). This is a snapshot/backup, read-only on the master.
- **Step 2 (SORT)**: from the backup, keep rows with proc-date (chars at offset 305, len 10) within `2022-01-01`..`2022-07-06`, order ascending by card number (offset 263, len 16), write `TRANSACT.DALY` GDG generation. Implement the INCLUDE as a string/date BETWEEN filter and the SORT as an ascending order-by on card number. Honor `DCB=(*.SORTIN)` = inherit 350-byte FB.
- **Step 3 (CBTRN03C, already ported — see Phase 4 "Port CBTRN01C and CBTRN03C + golden masters")**: drive off the sorted daily extract; read `DATEPARM` for the displayed/used report window; join (keyed lookups) to `CARD_XREF`, `TRANSACTION_TYPE`, `TRANSACTION_CATEGORY` for descriptions; emit a paginated 133-byte report with per-card (account) totals, page totals (page size 20), and a grand total, into `TRANREPT` GDG generation.
- **GDG resolution**: resolve `(+1)` per base **once per job run**; Step 2 must read the exact generation Step 1 created (`TRANSACT.BKUP`), and Step 3 must read the exact generation Step 2 created (`TRANSACT.DALY`). Catalog on success, discard on failure.
- **Failure semantics**: no `COND` gating exists, but the steps are data-dependent — fail the whole job if any step errors (missing input generation would otherwise cascade). Surface `CBTRN03C` I/O abends (CEE3ABD) as a hard failure.
- **Date-window consistency**: keep the SORT `SYMNAMES` window and the `DATEPARM` dataset contents in sync; in the .NET model, prefer a single source of truth for the report window and feed both the filter and the report header from it.
