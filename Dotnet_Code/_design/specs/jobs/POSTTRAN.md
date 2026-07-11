# JOB SPEC: POSTTRAN

Source JCL: `Old_Cobol_Code/aws-mainframe-modernization-carddemo/app/jcl/POSTTRAN.jcl`
Version stamp: `CardDemo_v1.0-15-g27d6c6f-68` (2022-07-19)

## Overview

- **JCL member**: `POSTTRAN.jcl`
- **JOB name**: `POSTTRAN`
- **JOB description**: `'POSTTRAN'`
- **JOB params**: `CLASS=A`, `MSGCLASS=0`, `NOTIFY=&SYSUID`
- **Purpose**: **Daily transaction posting (the core posting-sequence step).** A single-step batch job that runs the COBOL program **`CBTRN02C`** to **process the daily transaction input file, validate each transaction, post valid ones to the transaction master, the per-account/per-category balance file, and the account master, and write rejected transactions to a new daily-rejects generation.** This is the "engine" of the daily cycle: it reads already-prepared inputs (transaction master, card cross-reference, account master, category-balance master) and the day's incoming transactions, and **updates balances and appends posted transactions** while routing failures to a rejects file.

This job is the consumer side of the daily transaction pipeline. Upstream jobs build/refresh the transaction master KSDS (e.g. `COMBTRAN` REPRO-loads `TRANSACT.VSAM.KSDS`) and define the rejects GDG base (`DALYREJS.jcl` defines `AWS.M2.CARDDEMO.DALYREJS`). `POSTTRAN` then posts the day's `DALYTRAN.PS` file against those masters.

## Step summary

| Step | PGM | Type | Action |
|------|-----|------|--------|
| STEP15 | `CBTRN02C` | Custom batch COBOL (`CB*` application program) | Read daily transaction file; validate each tran against card-xref and account master; post valid trans to transaction master KSDS, category-balance KSDS, and account master KSDS; write rejects to new `DALYREJS(+1)` GDG generation |

There is **1 EXEC step** invoking **1 program: `CBTRN02C`** (a custom CardDemo batch COBOL program — no IDCAMS, no SORT, no IEFBR14, no utility). **GDG is used** for the rejects output only. **No `PARM=` and no `COND=`/`IF` gating** are coded on the EXEC.

---

## STEP15 — Post daily transactions (PGM=CBTRN02C)

- **EXEC**: `PGM=CBTRN02C` — the CardDemo daily transaction posting program (`app/cbl/CBTRN02C.cbl`, "Post the records from daily transaction file").
- **PARM=**: none.
- **COND/RC gating**: none coded (single step). Note: the program itself sets a non-zero **RETURN-CODE = 4** at end of run if any transaction was rejected (`WS-REJECT-COUNT > 0`); it **ABENDs (CEE3ABD, code 999)** on any unexpected file I/O error. In the .NET runner, treat RC=4 as a "completed with rejects" warning (not a failure) and treat the ABEND path as a hard step failure.
- **STEPLIB**: `DISP=SHR, DSN=AWS.M2.CARDDEMO.LOADLIB` — load library containing the `CBTRN02C` executable (maps to the .NET program registry / assembly, no data mapping).

### Processing logic (from CBTRN02C, for the step runner)

For each record read sequentially from `DALYTRAN`:
1. **Validate (1500)**:
   - Look up card number (`DALYTRAN-CARD-NUM`) in the **card cross-reference** (`XREFFILE`). Missing -> reject reason **100** "INVALID CARD NUMBER FOUND".
   - Look up the cross-referenced account id (`XREF-ACCT-ID`) in the **account master** (`ACCTFILE`). Missing -> reject reason **101** "ACCOUNT RECORD NOT FOUND".
   - Credit-limit check: `ACCT-CURR-CYC-CREDIT - ACCT-CURR-CYC-DEBIT + DALYTRAN-AMT` must be `<= ACCT-CREDIT-LIMIT`, else reject reason **102** "OVERLIMIT TRANSACTION".
   - Expiry check: `ACCT-EXPIRAION-DATE >= DALYTRAN-ORIG-TS(1:10)`, else reject reason **103** "TRANSACTION RECEIVED AFTER ACCT EXPIRATION".
2. **If valid -> post (2000)**:
   - Build a `TRAN-RECORD` from the daily-tran fields; set `TRAN-PROC-TS` to current DB2-format timestamp.
   - **Update category balance (2700)**: read `TCATBALF` by key (acct-id + type-cd + cat-cd). If not found, **create** a new category-balance record (status 23/INVALID KEY tolerated) seeded with `DALYTRAN-AMT`; if found, **add** `DALYTRAN-AMT` to `TRAN-CAT-BAL` and rewrite.
   - **Update account (2800)**: add `DALYTRAN-AMT` to `ACCT-CURR-BAL`; if amount >= 0 add to `ACCT-CURR-CYC-CREDIT` else to `ACCT-CURR-CYC-DEBIT`; rewrite account master.
   - **Write transaction (2900)**: `WRITE` the posted `TRAN-RECORD` to the transaction master KSDS (`TRANFILE`, opened `OUTPUT`).
3. **If invalid -> reject (2500)**: write the original daily-tran data plus an 80-byte validation trailer (reason code + description) to the **daily rejects** file (`DALYREJS`).
4. At EOF: display transaction/reject counts; set RETURN-CODE=4 if any rejects.

> Conversion note: the COBOL opens `TRANSACT-FILE` with `OPEN OUTPUT` (load mode) yet `ACCESS MODE IS RANDOM` keyed on `FD-TRANS-ID`. Functionally it appends posted transactions keyed by `TRAN-ID`. In the relational/.NET model, treat each post as an insert (keyed by transaction id) into the `TRANSACTION` store. The `(+1)` rejects dataset is freshly created each run.

### DD statements / datasets

| DD | Disposition | DSN | I/O (COBOL OPEN) | GDG | Copybook / record | Maps to (file / relational table) |
|----|-------------|-----|------------------|-----|-------------------|-----------------------------------|
| `STEPLIB` | `DISP=SHR` | `AWS.M2.CARDDEMO.LOADLIB` | (load lib) | n/a | n/a | Program load library (executable lookup). No data mapping. |
| `SYSPRINT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | Program print / job log. No data mapping. |
| `SYSOUT` | `SYSOUT=*` | (spool) | OUTPUT | n/a | n/a | COBOL `DISPLAY` / runtime messages. No data mapping. |
| `TRANFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.TRANSACT.VSAM.KSDS` | **OUTPUT** (load/append, keyed) | n/a (fixed VSAM KSDS) | `CVTRA05Y` `TRAN-RECORD`, key `TRAN-ID` X(16) | **Transaction master** KSDS = relational **`TRANSACTION`** table. Posted transactions are written here. |
| `DALYTRAN` | `DISP=SHR` | `AWS.M2.CARDDEMO.DALYTRAN.PS` | **INPUT** (sequential) | n/a (fixed PS) | `CVTRA06Y` `DALYTRAN-RECORD`, 350 bytes | **Daily transaction input** sequential file (the day's incoming transactions). Source rows to be posted; corresponds to the staged **DAILY-TRANSACTION** input (pre-post). |
| `XREFFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.CARDXREF.VSAM.KSDS` | **INPUT** (random, keyed) | n/a (fixed VSAM KSDS) | `CVACT03Y` `CARD-XREF-RECORD`, key card-num X(16) | **Card cross-reference** KSDS = relational **`CARD_XREF`** table (card-number -> account/customer). Read-only lookup. |
| `DALYREJS` | `DISP=(NEW,CATLG,DELETE)` | `AWS.M2.CARDDEMO.DALYREJS(+1)` | **OUTPUT** (sequential) | **GDG, `(+1)` = new generation created this run** | `FD-REJS-RECORD` 430 bytes (350 reject data + 80 validation trailer) | **Daily rejects** output sequential file (rejected transactions + reason). New GDG generation each run. GDG base defined in `DALYREJS.jcl`. Logically a **TRANSACTION-REJECTS** journal/file. |
| `ACCTFILE` | `DISP=SHR` | `AWS.M2.CARDDEMO.ACCTDATA.VSAM.KSDS` | **I-O** (random, keyed) | n/a (fixed VSAM KSDS) | `CVACT01Y` `ACCOUNT-RECORD`, key acct-id 9(11) | **Account master** KSDS = relational **`ACCOUNT`** table. Read for validation, rewritten with updated balances/cycle credit-debit. |
| `TCATBALF` | `DISP=SHR` | `AWS.M2.CARDDEMO.TCATBALF.VSAM.KSDS` | **I-O** (random, keyed) | n/a (fixed VSAM KSDS) | `CVTRA01Y` `TRAN-CAT-BAL-RECORD`, key = acct-id 9(11) + type-cd X(2) + cat-cd 9(4) | **Transaction category-balance master** KSDS = relational **`TRAN_CATEGORY_BALANCE`** (TCATBAL) table. Read; updated (add amount) or inserted when key absent. |

#### DALYREJS DCB / space (exact)

```
DCB=(RECFM=F,LRECL=430,BLKSIZE=0)
UNIT=SYSDA
SPACE=(CYL,(1,1),RLSE)
DISP=(NEW,CATLG,DELETE)
DSN=AWS.M2.CARDDEMO.DALYREJS(+1)
```

- Fixed-length 430-byte records (350 reject record + 80 validation trailer), system-determined block size, 1 primary + 1 secondary cylinder with unused space released (`RLSE`). Catalogued on normal end, deleted on abnormal end.

---

## PARM / COND / GDG / SORT / IDCAMS summary

- **PARM=**: none.
- **COND/RC gating**: none coded. Program emits **RC=4** when rejects exist (treat as warning), and **ABENDs** on I/O errors (treat as failure). No `COND=`/`IF/THEN`.
- **GDG**: **used** for `DALYREJS` only — `AWS.M2.CARDDEMO.DALYREJS(+1)` creates a **new generation** (catalogued on success). The GDG base `AWS.M2.CARDDEMO.DALYREJS` (`LIMIT(5)`, `SCRATCH`) is defined by `DALYREJS.jcl`. All other datasets are fixed-name VSAM KSDS / PS (no GDG).
- **SORT**: not used.
- **IDCAMS**: not used (no DEFINE/REPRO/DELETE in this job).
- **IEFBR14**: not used.

## Programs / Utilities Invoked

- `CBTRN02C` (STEP15) — custom CardDemo daily transaction posting batch program.

## Conversion notes for the .NET JobControl step-runner

- Implement as a **single step** that invokes the ported `CBTRN02C` posting routine. Already ported in this conversion (see Phase 4 "Port CBTRN02C (transaction posting) + golden master").
- **Inputs** (read-only): `DALYTRAN.PS` (sequential daily transactions), `CARDXREF` (CARD_XREF table), `ACCTDATA` (ACCOUNT table, also updated), `TCATBALF` (TRAN_CATEGORY_BALANCE, also updated). **Outputs**: posted rows into `TRANSACT.VSAM.KSDS` (TRANSACTION table), and a **new** `DALYREJS(+1)` rejects generation.
- **Update semantics**: account-balance and category-balance updates are read-modify-write (rewrite/insert by key); transaction posting is an insert keyed on `TRAN-ID`. Category-balance auto-creates the key if absent.
- **Return code**: surface RC=4 as "posted with N rejects" (non-fatal); surface the COBOL ABEND (CEE3ABD code 999) as a hard step failure. Do not gate downstream steps on RC=4 unless a later job explicitly checks it.
- **GDG semantics**: model `DALYREJS(+1)` as "create next generation, catalog on success" honoring the `LIMIT(5)` SCRATCH retention from `DALYREJS.jcl`.
- **Ordering in the daily cycle**: this job must run after the transaction-master refresh (`COMBTRAN`/transaction-master build) and after the `DALYREJS` GDG base exists; it produces the posted master and balances that reporting/statement jobs read.
